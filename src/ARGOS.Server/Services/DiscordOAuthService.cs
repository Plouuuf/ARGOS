using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Argos.Shared.Contracts;
using Microsoft.Extensions.Options;

namespace Argos.Server.Services;

/// <summary>
/// Connexion Discord (OAuth2, scopes identify + guilds.members.read), sur le modèle de SIMCALL.
/// Le joueur doit être membre du Discord VDMS avec le rôle <see cref="DiscordOAuthOptions.RequiredRoleId"/> ;
/// ses autres rôles donnent les niveaux d'accès ARGOS (<see cref="AccessLevels"/>).
/// </summary>
public sealed class DiscordOAuthService(
    IOptionsMonitor<DiscordOAuthOptions> options,
    AuthSessionStore sessions,
    PersistentDiscordSessionStore persistentSessions,
    ILogger<DiscordOAuthService> logger)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
    private static readonly TimeSpan RoleCheckInterval = TimeSpan.FromMinutes(5);

    // Dernier profil connu par compte Discord (pseudo, avatar, niveaux), pour éviter un appel Discord à chaque reconnexion.
    private readonly ConcurrentDictionary<string, ArgosUserDto> _users = new(StringComparer.Ordinal);

    public DiscordOAuthOptions Options => options.CurrentValue;

    /// <summary>Dernier profil connu d'un compte Discord (s'il s'est déjà connecté à ARGOS depuis le démarrage du serveur).</summary>
    public ArgosUserDto? FindKnownUser(string userId)
        => _users.TryGetValue(userId, out var user) ? user : null;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Options.ClientId) &&
        !string.IsNullOrWhiteSpace(Options.ClientSecret) &&
        TryBuildCallbackUri(out _) &&
        !string.IsNullOrWhiteSpace(Options.GuildId) &&
        !string.IsNullOrWhiteSpace(Options.RequiredRoleId);

    public string BuildAuthorizeUrl(string sessionId)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Discord OAuth n'est pas configuré sur le serveur ARGOS.");

        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = Options.ClientId,
            ["scope"] = "identify guilds.members.read",
            ["state"] = sessionId,
            ["redirect_uri"] = BuildCallbackUri(),
            ["prompt"] = "consent"
        };
        return "https://discord.com/oauth2/authorize?" +
               string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
    }

    public async Task<OAuthCompletionResult> CompleteAsync(string code, string state)
    {
        if (!sessions.TryGetSession(state, out var pending) || pending.ExpiresAt <= DateTimeOffset.UtcNow)
            return new(false, "Session de connexion expirée ou invalide.", null);

        try
        {
            var token = await ExchangeAuthorizationCodeAsync(code);
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken) || string.IsNullOrWhiteSpace(token.RefreshToken))
            {
                sessions.Deny(state, "Jeton Discord invalide.");
                return new(false, "Jeton Discord invalide.", null);
            }

            var identity = await ReadIdentityAndMembershipAsync(token.AccessToken);
            if (!identity.Success || identity.User is null)
            {
                sessions.Deny(state, identity.Message);
                return new(false, identity.Message, identity.User?.DisplayName);
            }

            var user = identity.User;
            var appToken = sessions.Approve(state, user, SessionLifetime);
            persistentSessions.Save(appToken, user.UserId, user.DisplayName, token.RefreshToken);
            logger.LogInformation("Connexion ARGOS autorisée pour {UserId} ({Display}), niveaux : {Levels}",
                user.UserId, user.DisplayName, string.Join(", ", user.Levels));
            return new(true, $"Accès à ARGOS autorisé pour {user.DisplayName}.", user.DisplayName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Erreur OAuth Discord");
            sessions.Deny(state, "Erreur pendant la vérification Discord.");
            return new(false, "Erreur pendant la vérification Discord.", null);
        }
    }

    /// <summary>
    /// Vérifie un jeton d'application. Avec <paramref name="forceRoleCheck"/>, les rôles sont relus sur Discord
    /// (sauf vérification de moins de 5 minutes) : un rôle retiré ferme la session.
    /// </summary>
    public async Task<AppTokenValidationResult> ValidateAppTokenAsync(string? appToken, bool forceRoleCheck = true)
    {
        if (string.IsNullOrWhiteSpace(appToken))
            return new(false, true, "Connexion Discord requise.", null);

        // Jeton connu en mémoire : réponse immédiate, sauf si les rôles n'ont pas été relus depuis 5 minutes
        // (un rôle ajouté ou retiré sur Discord s'applique alors sans redémarrer ARGOS).
        if (!forceRoleCheck && sessions.ValidateToken(appToken, out var memory)
            && (!persistentSessions.TryGet(appToken, out var known) || DateTimeOffset.UtcNow - known.LastValidatedAt < RoleCheckInterval))
            return new(true, false, null, memory.User);

        if (!persistentSessions.TryGet(appToken, out var stored))
        {
            sessions.Revoke(appToken);
            return new(false, true, "Session Discord expirée. Reconnecte-toi.", null);
        }

        try
        {
            if (DateTimeOffset.UtcNow - stored.LastValidatedAt < RoleCheckInterval &&
                _users.TryGetValue(stored.UserId, out var recent))
            {
                sessions.Restore(appToken, recent, SessionLifetime);
                return new(true, false, null, recent);
            }

            var refreshed = await RefreshTokenAsync(persistentSessions.GetRefreshToken(stored));
            if (refreshed is null || string.IsNullOrWhiteSpace(refreshed.AccessToken) || string.IsNullOrWhiteSpace(refreshed.RefreshToken))
            {
                RevokeEverywhere(appToken);
                return new(false, true, "La session Discord a expiré. Une nouvelle connexion est nécessaire.", null);
            }

            var identity = await ReadIdentityAndMembershipAsync(refreshed.AccessToken);
            if (!identity.Success || identity.User is null)
            {
                if (identity.RequiresLogin)
                    RevokeEverywhere(appToken);
                return new(false, identity.RequiresLogin, identity.Message, identity.User);
            }

            persistentSessions.MarkValidated(appToken, identity.User.DisplayName, refreshed.RefreshToken);
            sessions.Restore(appToken, identity.User, SessionLifetime);
            return new(true, false, null, identity.User);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Impossible de revalider une session Discord");
            return new(false, false, "Impossible de revalider la session Discord pour le moment.", null);
        }
    }

    private bool TryBuildCallbackUri(out string callbackUri)
    {
        callbackUri = string.Empty;
        var publicBase = (Options.PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(publicBase) ||
            !Uri.TryCreate(publicBase, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme is not ("http" or "https"))
            return false;

        callbackUri = publicBase + "/auth/discord/callback";
        return true;
    }

    private string BuildCallbackUri()
        => TryBuildCallbackUri(out var callbackUri)
            ? callbackUri
            : throw new InvalidOperationException("Discord:PublicBaseUrl est invalide.");

    private void RevokeEverywhere(string appToken)
    {
        sessions.Revoke(appToken);
        persistentSessions.Revoke(appToken);
    }

    private async Task<TokenResponse?> ExchangeAuthorizationCodeAsync(string code)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://discord.com/api/v10/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = Options.ClientId,
                ["client_secret"] = Options.ClientSecret,
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = BuildCallbackUri()
            })
        };
        return await SendTokenRequestAsync(request);
    }

    private async Task<TokenResponse?> RefreshTokenAsync(string refreshToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://discord.com/api/v10/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = Options.ClientId,
                ["client_secret"] = Options.ClientSecret,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            })
        };
        return await SendTokenRequestAsync(request);
    }

    private static async Task<TokenResponse?> SendTokenRequestAsync(HttpRequestMessage request)
    {
        using var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;
        return JsonSerializer.Deserialize<TokenResponse>(await response.Content.ReadAsStringAsync(), JsonOptions);
    }

    private async Task<IdentityResult> ReadIdentityAndMembershipAsync(string accessToken)
    {
        using var userRequest = AuthorizedGet("https://discord.com/api/v10/users/@me", accessToken);
        using var memberRequest = AuthorizedGet($"https://discord.com/api/v10/users/@me/guilds/{Options.GuildId}/member", accessToken);
        var userTask = Http.SendAsync(userRequest);
        var memberTask = Http.SendAsync(memberRequest);
        await Task.WhenAll(userTask, memberTask);
        using var userResponse = await userTask;
        using var memberResponse = await memberTask;

        if (!userResponse.IsSuccessStatusCode)
            return new(false, userResponse.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden,
                "Impossible de lire le compte Discord.", null);
        if (!memberResponse.IsSuccessStatusCode)
            return new(false, true, "Accès refusé : tu n'es pas membre du Discord Val De Marne Simulation.", null);

        var discordUser = JsonSerializer.Deserialize<DiscordUser>(await userResponse.Content.ReadAsStringAsync(), JsonOptions);
        var member = JsonSerializer.Deserialize<DiscordMember>(await memberResponse.Content.ReadAsStringAsync(), JsonOptions);
        if (discordUser is null || member?.Roles is null)
            return new(false, false, "Réponse Discord invalide.", null);

        var display = FirstNonEmpty(member.Nick, discordUser.GlobalName, discordUser.Username) ?? "Utilisateur Discord";
        var levels = ComputeLevels(member.Roles);
        var user = new ArgosUserDto(discordUser.Id, display, BuildAvatarUrl(discordUser), levels);

        if (!levels.Contains(AccessLevels.Sapeur))
        {
            _users.TryRemove(discordUser.Id, out _);
            return new(false, true, "Accès refusé : aucun de tes rôles Discord ne donne accès à ARGOS.", user);
        }

        _users[discordUser.Id] = user;
        return new(true, false, "OK", user);
    }

    private List<string> ComputeLevels(string[] roles)
    {
        static string[] Ids(string? value) => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool HasAny(string? value) => Ids(value).Any(id => roles.Contains(id, StringComparer.Ordinal));

        // Accès par application : un rôle Discord par logiciel.
        var apps = Options.AppRoles.Where(a => HasAny(a.Value)).Select(a => AccessLevels.App(a.Key.ToLowerInvariant())).ToList();
        var required = !string.IsNullOrWhiteSpace(Options.RequiredRoleId) && HasAny(Options.RequiredRoleId);
        var levels = new List<string>();
        if (apps.Count == 0 && !required)
            return levels;                      // aucun rôle ARGOS : accès refusé

        levels.Add(AccessLevels.Sapeur);
        levels.AddRange(apps);
        if (HasAny(Options.RhAdminRoleIds))
            levels.Add(AccessLevels.RhAdmin);
        foreach (var (level, roleIds) in Options.LevelRoles)
            if (HasAny(roleIds))
                levels.Add(level.ToLowerInvariant());
        return levels.Distinct().ToList();
    }

    private static string BuildAvatarUrl(DiscordUser user)
    {
        if (!string.IsNullOrWhiteSpace(user.Avatar))
        {
            // Toujours en PNG : Avalonia n'anime pas les GIF, Discord fournit l'image fixe d'un avatar animé.
            return $"https://cdn.discordapp.com/avatars/{user.Id}/{user.Avatar}.png?size=256";
        }

        // Avatar par défaut de Discord (nouveau système de pseudos).
        var index = ulong.TryParse(user.Id, out var id) ? (int)((id >> 22) % 6) : 0;
        return $"https://cdn.discordapp.com/embed/avatars/{index}.png";
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static HttpRequestMessage AuthorizedGet(string url, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.UserAgent.ParseAdd("ARGOS/0.1");
        return request;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class DiscordUser
    {
        public string Id { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        [JsonPropertyName("global_name")]
        public string? GlobalName { get; set; }
        public string? Avatar { get; set; }
    }

    private sealed class DiscordMember
    {
        public string? Nick { get; set; }
        public string[] Roles { get; set; } = [];
    }

    private sealed record IdentityResult(bool Success, bool RequiresLogin, string Message, ArgosUserDto? User);

    public sealed record OAuthCompletionResult(bool Success, string Message, string? DisplayName);
    public sealed record AppTokenValidationResult(bool Valid, bool Reauthenticate, string? Error, ArgosUserDto? User);
}
