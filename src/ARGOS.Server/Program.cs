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

        var app = builder.Build();

        var accessKey = app.Configuration["ARGOS:AccessKey"];
        if (string.IsNullOrWhiteSpace(accessKey) || accessKey.StartsWith("CHANGE_ME", StringComparison.Ordinal))
            throw new InvalidOperationException("La clé du serveur (ARGOS:AccessKey) doit être configurée avant le démarrage.");

        if (!app.Services.GetRequiredService<DiscordOAuthService>().IsConfigured)
            app.Logger.LogWarning("Connexion Discord non configurée : renseigner Discord:ClientId, ClientSecret, PublicBaseUrl, GuildId et RequiredRoleId.");

        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });

        app.MapGet("/", () => Results.Ok(new { service = "ARGOS Server", status = "online" }));
        app.MapGet("/health", (RealtimeHub hub) => Results.Ok(new { status = "ok", utc = DateTimeOffset.UtcNow, connections = hub.ConnectionCount }));

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

        await app.RunAsync();
    }

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
