using System.Security.Cryptography;
using System.Text;
using Argos.Server.Services;
using Argos.Shared.Contracts;

namespace Argos.Server;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            await RunServerAsync(args);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task RunServerAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });

        // Secrets locaux (non versionnés) : appsettings.Local.json à côté de l'exécutable.
        builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

        builder.Services.Configure<ArgosServerOptions>(builder.Configuration.GetSection("ARGOS"));
        builder.Services.Configure<DiscordOAuthOptions>(builder.Configuration.GetSection("Discord"));
        builder.Services.AddSingleton<AuthSessionStore>();
        builder.Services.AddSingleton<PersistentDiscordSessionStore>();
        builder.Services.AddSingleton<DiscordOAuthService>();
        builder.Services.AddSingleton<RealtimeHub>();
        builder.Services.AddSingleton(_ => new OdeDatabase());
        builder.Services.AddSingleton<OdeService>();
        builder.Services.AddSingleton<RemiseService>();
        builder.Services.AddSingleton<McoService>();
        builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection("Webhooks"));
        builder.Services.AddSingleton<DiscordWebhookService>();

        var app = builder.Build();

        var accessKey = app.Configuration["ARGOS:AccessKey"];
        if (string.IsNullOrWhiteSpace(accessKey) || accessKey.StartsWith("CHANGE_ME", StringComparison.Ordinal))
            throw new InvalidOperationException("La clé du serveur (ARGOS:AccessKey) doit être configurée avant le démarrage.");

        if (!app.Services.GetRequiredService<DiscordOAuthService>().IsConfigured)
            app.Logger.LogWarning("Connexion Discord non configurée : renseigner Discord:ClientId, ClientSecret, PublicBaseUrl, GuildId et RequiredRoleId.");

        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });

        app.MapGet("/", () => Results.Ok(new { service = "ARGOS Server", status = "online" }));
        app.MapGet("/health", (RealtimeHub hub) => Results.Ok(new
        {
            status = "ok",
            version = typeof(Program).Assembly.GetName().Version?.ToString(3),
            applications = new[] { "adagio-ode", "adagio-remise", "mco" },
            utc = DateTimeOffset.UtcNow,
            connections = hub.ConnectionCount
        }));

        // ===== Connexion Discord =====

        app.MapPost("/auth/discord/session", (DiscordOAuthService oauth, AuthSessionStore sessions) =>
        {
            if (!oauth.IsConfigured)
                return Results.Problem("La connexion Discord n'est pas configurée sur le serveur ARGOS.", statusCode: 503);
            var session = sessions.CreateSession();
            return Results.Ok(new AuthSessionStartResponse(session.Id, oauth.BuildAuthorizeUrl(session.Id), session.ExpiresAt));
        });

        app.MapGet("/auth/discord/status/{sessionId}", (string sessionId, AuthSessionStore sessions) =>
        {
            if (!sessions.TryGetSession(sessionId, out var session))
                return Results.NotFound(new AuthStatusResponse("expired", "Session expirée.", null, null));
            return Results.Ok(new AuthStatusResponse(
                session.Status,
                session.Error,
                session.Status == "approved" ? session.AppToken : null,
                session.User));
        });

        app.MapPost("/auth/discord/validate", async (TokenValidationRequest request, DiscordOAuthService oauth) =>
        {
            var result = await oauth.ValidateAppTokenAsync(request.Token, forceRoleCheck: true);
            return Results.Ok(new TokenValidationResponse(result.Valid, result.Reauthenticate, result.Error, result.Valid ? result.User : null));
        });

        app.MapGet("/auth/discord/callback", async (HttpContext context, DiscordOAuthService oauth, AuthSessionStore sessions) =>
        {
            context.Response.Headers["Cache-Control"] = "no-store, max-age=0";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";

            var code = context.Request.Query["code"].ToString();
            var state = context.Request.Query["state"].ToString();
            var error = context.Request.Query["error"].ToString();

            if (!string.IsNullOrWhiteSpace(error))
            {
                if (!string.IsNullOrWhiteSpace(state)) sessions.Deny(state, "Connexion Discord annulée.");
                return Html(OAuthPage.Build("Connexion annulée", "La connexion Discord a été annulée. Tu peux revenir dans ARGOS.", false, null));
            }

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
                return Html(OAuthPage.Build("Connexion impossible", "La demande est invalide ou expirée. Relance la connexion depuis ARGOS.", false, null), 400);

            var result = await oauth.CompleteAsync(code, state);
            return Html(OAuthPage.Build(result.Success ? "Accès autorisé" : "Accès refusé", result.Message, result.Success, result.DisplayName));
        });

        // ===== Temps réel =====

        app.Map("/ws", branch => branch.Run(context => context.RequestServices.GetRequiredService<RealtimeHub>().HandleAsync(context)));

        // ===== Données (jeton d'application requis) =====

        app.MapGet("/api/engins", async (HttpContext context, DiscordOAuthService oauth) =>
        {
            var user = await AuthenticateAsync(context, oauth);
            return user is null ? Results.Unauthorized() : Results.Ok(EnginsCatalog.All);
        });

        // ===== ADAGIO ODE =====

        var ode = app.MapGroup("/api/ode");

        ode.MapGet("/state", async (HttpContext context, DiscordOAuthService oauth, OdeService service) =>
            await AuthenticateAsync(context, oauth) is null ? Results.Unauthorized() : Results.Ok(service.Snapshot()));

        ode.MapGet("/journal", async (HttpContext context, DiscordOAuthService oauth, OdeService service, int? limit) =>
            await AuthenticateAsync(context, oauth) is null ? Results.Unauthorized() : Results.Ok(service.Journal(limit ?? 500)));

        ode.MapPost("/calls", async (HttpContext context, DiscordOAuthService oauth, OdeService service) =>
        {
            var user = await AuthenticateAsync(context, oauth);
            if (user is null) return Results.Unauthorized();
            return Results.Ok(await service.CreateCallAsync(user));
        });

        ode.MapGet("/calls/{id}", async (string id, HttpContext context, DiscordOAuthService oauth, OdeService service) =>
        {
            if (await AuthenticateAsync(context, oauth) is null) return Results.Unauthorized();
            var call = service.GetCall(id);
            return call is null ? Results.NotFound() : Results.Ok(call);
        });

        // ?status=Enregistré | Transmis : enregistrer la fiche et changer son statut.
        ode.MapPut("/calls/{id}", async (string id, CallRecord call, string? status, HttpContext context, DiscordOAuthService oauth, OdeService service) =>
        {
            var user = await AuthenticateAsync(context, oauth);
            if (user is null) return Results.Unauthorized();
            call.Id = id;
            return ToResult(await service.UpdateCallAsync(user, call, status));
        });

        ode.MapPost("/calls/{id}/events", async (string id, TextRequest request, HttpContext context, DiscordOAuthService oauth, OdeService service) =>
        {
            var user = await AuthenticateAsync(context, oauth);
            return user is null ? Results.Unauthorized() : ToResult(await service.AddCallEventAsync(user, id, request.Text));
        });

        ode.MapPost("/calls/{id}/sans-suite", async (string id, SansSuiteRequest request, HttpContext context, DiscordOAuthService oauth, OdeService service) =>
        {
            var user = await AuthenticateAsync(context, oauth);
            return user is null ? Results.Unauthorized() : ToResult(await service.CloseWithoutDepartureAsync(user, id, request.Procedure, request.Reason));
        });

        ode.MapPost("/calls/{id}/link", async (string id, LinkCallRequest request, HttpContext context, DiscordOAuthService oauth, OdeService service) =>
        {
            var user = await AuthenticateAsync(context, oauth);
            return user is null ? Results.Unauthorized() : ToResult(await service.LinkCallAsync(user, id, request.InterventionId));
        });

        ode.MapPost("/interventions", async (CreateOdeRequest request, HttpContext context, DiscordOAuthService oauth, OdeService service) =>
        {
            var user = await AuthenticateAsync(context, oauth);
            if (user is null) return Results.Unauthorized();
            var (result, _) = await service.CreateOdeAsync(user, request);
            return ToResult(result);
        });

        ode.MapPost("/interventions/{id}/status", async (string id, EnginStatusRequest request, HttpContext context, DiscordOAuthService oauth, OdeService service) =>
        {
            var user = await AuthenticateAsync(context, oauth);
            return user is null ? Results.Unauthorized() : ToResult(await service.SetEnginStatusAsync(user, id, request.Nom, request.Status));
        });

        ode.MapPost("/interventions/{id}/messages", async (string id, TextRequest request, string? kind, HttpContext context, DiscordOAuthService oauth, OdeService service) =>
        {
            var user = await AuthenticateAsync(context, oauth);
            return user is null ? Results.Unauthorized() : ToResult(await service.AddInterventionMessageAsync(user, id, request.Text, kind == "renfort" ? "renfort" : "message"));
        });

        ode.MapPost("/interventions/{id}/printed/{order:int}", async (string id, int order, PrintRequest? request, HttpContext context,
            DiscordOAuthService oauth, OdeService service, DiscordWebhookService webhook) =>
        {
            var user = await AuthenticateAsync(context, oauth);
            if (user is null) return Results.Unauthorized();
            var result = await service.MarkPrintedAsync(user, id, order);
            if (!result.Ok) return ToResult(result);

            // Export de l'ODE dans le salon Discord (image de la feuille envoyée par le poste).
            if (!webhook.OdeEnabled)
                return Results.Ok(result with { Id = PrintDiscord.Disabled });
            byte[]? png = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(request?.ImagePng) && request.ImagePng.Length < 12_000_000)
                    png = Convert.FromBase64String(request.ImagePng);
            }
            catch (FormatException) { }
            var intervention = service.GetIntervention(id);
            var departure = intervention?.Orders.FirstOrDefault(o => o.Number == order);
            var sent = intervention is not null && departure is not null
                       && await webhook.SendOdeAsync(intervention, departure, user.DisplayName, png);
            return Results.Ok(result with { Id = sent ? PrintDiscord.Sent : PrintDiscord.Failed });
        });

        ode.MapPost("/interventions/{id}/close", async (string id, HttpContext context, DiscordOAuthService oauth, OdeService service) =>
        {
            var user = await AuthenticateAsync(context, oauth);
            return user is null ? Results.Unauthorized() : ToResult(await service.CloseInterventionAsync(user, id));
        });

        // ===== ADAGIO REMISE =====

        var remise = app.MapGroup("/api/remise");

        remise.MapGet("/state", async (HttpContext context, DiscordOAuthService oauth, RemiseService service) =>
        {
            var user = await AuthenticateAsync(context, oauth);
            return user is null ? Results.Unauthorized() : Results.Ok(service.Snapshot(user));
        });

        remise.MapGet("/journal", async (HttpContext context, DiscordOAuthService oauth, OdeService service, int? limit) =>
        {
            if (await AuthenticateAsync(context, oauth) is null) return Results.Unauthorized();
            return Results.Ok(service.Journal(limit ?? 2000).Where(j => RemiseJournal.IsRemise(j.Action)).ToList());
        });

        remise.MapPost("/engins", async (SaveEnginRequest request, HttpContext context, DiscordOAuthService oauth, RemiseService service) =>
        {
            var user = await AuthenticateAsync(context, oauth);
            return user is null ? Results.Unauthorized() : ToResult(await service.SaveEnginAsync(user, request));
        });

        remise.MapPost("/items", async (MaterielItem item, HttpContext context, DiscordOAuthService oauth, RemiseService service) =>
        {
            var user = await AuthenticateAsync(context, oauth);
            return user is null ? Results.Unauthorized() : ToResult(await service.SaveItemAsync(user, item));
        });

        remise.MapPost("/verifications", async (VerificationRequest request, HttpContext context, DiscordOAuthService oauth, RemiseService service) =>
        {
            var user = await AuthenticateAsync(context, oauth);
            return user is null ? Results.Unauthorized() : ToResult(await service.VerifyAsync(user, request));
        });

        remise.MapPost("/maintenances", async (AnomalyRequest request, HttpContext context, DiscordOAuthService oauth, RemiseService service) =>
        {
            var user = await AuthenticateAsync(context, oauth);
            return user is null ? Results.Unauthorized() : ToResult(await service.ReportAnomalyAsync(user, request));
        });

        remise.MapPost("/maintenances/{id}/repairs", async (string id, RepairRequest request, HttpContext context, DiscordOAuthService oauth, RemiseService service) =>
        {
            var user = await AuthenticateAsync(context, oauth);
            return user is null ? Results.Unauthorized() : ToResult(await service.AddRepairAsync(user, id, request));
        });

        remise.MapPost("/maintenances/{id}/close", async (string id, CloseMaintenanceRequest request, HttpContext context, DiscordOAuthService oauth, RemiseService service) =>
        {
            var user = await AuthenticateAsync(context, oauth);
            return user is null ? Results.Unauthorized() : ToResult(await service.CloseMaintenanceAsync(user, id, request));
        });

        remise.MapPost("/stocks", async (StockRequest request, HttpContext context, DiscordOAuthService oauth, RemiseService service) =>
        {
            var user = await AuthenticateAsync(context, oauth);
            return user is null ? Results.Unauthorized() : ToResult(await service.MoveStockAsync(user, request));
        });

        // ===== MCO (main courante opérationnelle) =====

        var mco = app.MapGroup("/api/mco");

        mco.MapGet("/state", async (HttpContext context, DiscordOAuthService oauth, McoService service) =>
            await AuthenticateAsync(context, oauth) is null ? Results.Unauthorized() : Results.Ok(service.Snapshot()));

        mco.MapPost("/reports", async (McoReport report, HttpContext context, DiscordOAuthService oauth, McoService service) =>
        {
            var user = await AuthenticateAsync(context, oauth);
            return user is null ? Results.Unauthorized() : ToResult(await service.SaveAsync(user, report));
        });

        mco.MapPost("/reports/{id}/validate", async (string id, HttpContext context, DiscordOAuthService oauth, McoService service) =>
        {
            var user = await AuthenticateAsync(context, oauth);
            return user is null ? Results.Unauthorized() : ToResult(await service.ValidateAsync(user, id));
        });

        mco.MapPost("/reports/{id}/archive", async (string id, HttpContext context, DiscordOAuthService oauth, McoService service) =>
        {
            var user = await AuthenticateAsync(context, oauth);
            return user is null ? Results.Unauthorized() : ToResult(await service.ArchiveAsync(user, id));
        });

        // ===== Intégrations serveur à serveur (clé ARGOS:AccessKey dans l'en-tête X-Argos-Key) =====

        // Notification de test, et base des futurs liens SIMCALL / eFIBI.
        // Corps : { "type": "notify", "title": "…", "text": "…", "appId": "…" } ; ?userId=… pour un seul joueur.
        app.MapPost("/api/integrations/notify", async (HttpContext context, RealtimeMessage message, RealtimeHub hub) =>
        {
            if (!HasIntegrationKey(context, accessKey))
                return Results.Unauthorized();

            var notification = message with { Type = RealtimeTypes.Notify, Token = null };
            var userId = context.Request.Query["userId"].ToString();
            var sent = string.IsNullOrWhiteSpace(userId)
                ? await hub.BroadcastAsync(notification, context.RequestAborted)
                : await hub.SendToUserAsync(userId, notification, context.RequestAborted);
            return Results.Ok(new { sent });
        });

        // SIMCALL : un opérateur décroche un appel au 18 → fiche d'appel créée et prise d'appel ouverte sur son poste.
        app.MapPost("/api/integrations/simcall/answered", async (HttpContext context, SimcallCallAnswered message, OdeService service, DiscordOAuthService oauth) =>
        {
            if (!HasIntegrationKey(context, accessKey))
                return Results.Unauthorized();
            var user = oauth.FindKnownUser(message.DiscordUserId)
                       ?? new ArgosUserDto(message.DiscordUserId, message.OperatorName, null, [AccessLevels.Sapeur]);
            var call = await service.OnSimcallAnsweredAsync(message, user);
            return Results.Ok(new { callId = call.Id });
        });

        // SIMCALL : fin de l'appel → la durée de l'appel est figée sur la fiche.
        app.MapPost("/api/integrations/simcall/ended", async (HttpContext context, SimcallCallEnded message, OdeService service) =>
        {
            if (!HasIntegrationKey(context, accessKey))
                return Results.Unauthorized();
            await service.OnSimcallEndedAsync(message);
            return Results.Ok();
        });

        await app.RunAsync();
    }

    private static IResult ToResult(OdeResult result)
        => result.Ok ? Results.Ok(result) : Results.BadRequest(result);

    private static IResult Html(string html, int statusCode = 200)
        => Results.Content(html, "text/html; charset=utf-8", Encoding.UTF8, statusCode);

    private static async Task<ArgosUserDto?> AuthenticateAsync(HttpContext context, DiscordOAuthService oauth)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;
        var result = await oauth.ValidateAppTokenAsync(header["Bearer ".Length..].Trim(), forceRoleCheck: false);
        return result.Valid ? result.User : null;
    }

    private static bool HasIntegrationKey(HttpContext context, string expected)
    {
        var provided = context.Request.Headers["X-Argos-Key"].ToString();
        if (string.IsNullOrEmpty(provided))
            return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
    }
}
