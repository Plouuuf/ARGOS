using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Argos.Shared.Contracts;

namespace Argos.Services;

/// <summary>
/// Connexion Discord côté poste, sur le modèle de SIMCALL : le serveur ouvre une session OAuth,
/// le navigateur s'occupe de Discord, et le poste interroge le serveur jusqu'à la réponse.
/// Le jeton obtenu est gardé sur l'appareil pour les connexions suivantes.
/// </summary>
public sealed class DiscordAuthService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public string? SessionToken { get; private set; }
    public ArgosUserDto? User { get; private set; }

    private static Uri BaseUri => ClientSettings.Current.HttpBaseUri;

    /// <summary>
    /// Reprend la session enregistrée sur l'appareil. Retourne false s'il faut se reconnecter ;
    /// lève une exception si le serveur est injoignable.
    /// </summary>
    public async Task<bool> TryResumeAsync(CancellationToken ct = default)
    {
        var saved = LoadSavedSession();
        if (saved is null || string.IsNullOrWhiteSpace(saved.Token))
            return false;

        // Affiche tout de suite le dernier compte connu, avant la réponse du serveur.
        User ??= saved.User;

        var result = await ValidateAsync(saved.Token, ct);
        if (result.Valid && result.User is not null)
        {
            SessionToken = saved.Token;
            User = result.User;
            SaveSession(new SavedSession(saved.Token, result.User));
            return true;
        }

        if (result.Reauthenticate)
        {
            await ForgetAsync();
            return false;
        }

        throw new InvalidOperationException(result.Error ?? "Impossible de vérifier la session Discord.");
    }

    /// <summary>Revalide la session en cours (rôles Discord relus par le serveur).</summary>
    public async Task<TokenValidationResponse> RevalidateAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(SessionToken))
            return new TokenValidationResponse(false, true, "Aucune session.", null);

        var result = await ValidateAsync(SessionToken, ct);
        if (result.Valid && result.User is not null)
        {
            User = result.User;
            SaveSession(new SavedSession(SessionToken, result.User));
        }
        return result;
    }

    public async Task<AuthSessionStartResponse> StartAsync(CancellationToken ct)
    {
        using var response = await _http.PostAsync(new Uri(BaseUri, "auth/discord/session"), null, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(response.StatusCode == HttpStatusCode.ServiceUnavailable
                ? "La connexion Discord n'est pas encore configurée sur le serveur ARGOS."
                : $"Le serveur ARGOS ne répond pas correctement ({(int)response.StatusCode}).");
        return JsonSerializer.Deserialize<AuthSessionStartResponse>(body, JsonOptions)
               ?? throw new InvalidDataException("Réponse du serveur ARGOS invalide.");
    }

    /// <summary>Attend la validation Discord dans le navigateur (5 minutes au plus).</summary>
    public async Task<AuthStatusResponse> WaitForApprovalAsync(string sessionId, CancellationToken ct)
    {
        var endpoint = new Uri(BaseUri, "auth/discord/status/" + Uri.EscapeDataString(sessionId));
        for (var i = 0; i < 150; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);

            AuthStatusResponse? result;
            try
            {
                using var response = await _http.GetAsync(endpoint, ct);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return new AuthStatusResponse("expired", "La demande de connexion a expiré.", null, null);
                result = await response.Content.ReadFromJsonAsync<AuthStatusResponse>(JsonOptions, ct);
            }
            catch (HttpRequestException)
            {
                continue; // Coupure réseau passagère : on réessaie.
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                continue; // Délai dépassé sur une requête : on réessaie.
            }

            if (result is null || result.Status == "pending")
                continue;

            if (result.Status == "approved" && !string.IsNullOrWhiteSpace(result.Token) && result.User is not null)
            {
                SessionToken = result.Token;
                User = result.User;
                SaveSession(new SavedSession(result.Token, result.User));
            }
            return result;
        }

        return new AuthStatusResponse("expired", "La demande de connexion a expiré.", null, null);
    }

    /// <summary>Oublie le compte enregistré sur l'appareil (« Utiliser un autre compte »).</summary>
    public Task ForgetAsync()
    {
        SessionToken = null;
        User = null;
        try
        {
            if (File.Exists(AppPaths.DiscordSessionPath))
                File.Delete(AppPaths.DiscordSessionPath);
        }
        catch
        {
            // Fichier verrouillé : il sera remplacé à la prochaine connexion.
        }
        return Task.CompletedTask;
    }

    /// <summary>Dernier compte enregistré sur l'appareil, sans appel au serveur.</summary>
    public ArgosUserDto? PeekSavedUser() => LoadSavedSession()?.User;

    private async Task<TokenValidationResponse> ValidateAsync(string token, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(new Uri(BaseUri, "auth/discord/validate"), new TokenValidationRequest(token), JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Le serveur ARGOS ne répond pas correctement ({(int)response.StatusCode}).");
        return await response.Content.ReadFromJsonAsync<TokenValidationResponse>(JsonOptions, ct)
               ?? throw new InvalidDataException("Réponse du serveur ARGOS invalide.");
    }

    private static SavedSession? LoadSavedSession()
    {
        try
        {
            var path = AppPaths.DiscordSessionPath;
            return File.Exists(path)
                ? JsonSerializer.Deserialize<SavedSession>(File.ReadAllText(path), JsonOptions)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveSession(SavedSession session)
    {
        try
        {
            var path = AppPaths.DiscordSessionPath;
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(session, JsonOptions));
            File.Move(temp, path, true);
        }
        catch
        {
            // Sans écriture possible, il faudra simplement se reconnecter au prochain lancement.
        }
    }

    private sealed record SavedSession(string Token, ArgosUserDto? User);
}
