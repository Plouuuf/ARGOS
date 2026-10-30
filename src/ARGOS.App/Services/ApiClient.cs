using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Argos.Shared.Contracts;

namespace Argos.Services;

/// <summary>Appels au serveur ARGOS avec le jeton de la session (en-tête Authorization: Bearer).</summary>
public sealed class ApiClient(string token)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };

    private HttpRequestMessage Request(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, new Uri(ClientSettings.Current.HttpBaseUri, path.TrimStart('/')));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        return request;
    }

    public async Task<T> GetAsync<T>(string path, CancellationToken ct = default)
    {
        using var response = await _http.SendAsync(Request(HttpMethod.Get, path), ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
               ?? throw new InvalidDataException("Réponse vide du serveur ARGOS.");
    }

    /// <summary>Contenu binaire (document ILIAS, pièce jointe).</summary>
    public async Task<byte[]> GetBytesAsync(string path, CancellationToken ct = default)
    {
        using var response = await _http.SendAsync(Request(HttpMethod.Get, path), ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task<T> PostAsync<T>(string path, object? body = null, CancellationToken ct = default)
    {
        using var response = await _http.SendAsync(Request(HttpMethod.Post, path, body ?? new { }), ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
               ?? throw new InvalidDataException("Réponse vide du serveur ARGOS.");
    }

    /// <summary>Action ODE : renvoie toujours un <see cref="OdeResult"/>, avec le message d'erreur du serveur si refusée.</summary>
    public async Task<OdeResult> SendAsync(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.SendAsync(Request(method, path, body ?? new { }), ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new OdeResult(false, "Session expirée : reconnecte-toi.");

            var text = await response.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    var result = JsonSerializer.Deserialize<OdeResult>(text, JsonOptions);
                    if (result is not null)
                        return result;
                }
                catch (JsonException)
                {
                    // Réponse qui n'est pas un OdeResult : on retombe sur le code HTTP.
                }
            }
            return response.IsSuccessStatusCode
                ? new OdeResult(true)
                : new OdeResult(false, $"Le serveur a refusé l'action ({(int)response.StatusCode}).");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new OdeResult(false, "Serveur ARGOS injoignable. Réessaie dans un instant.");
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("Session expirée : reconnecte-toi.");
        var text = await response.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException($"Erreur du serveur ARGOS ({(int)response.StatusCode}). {text}");
    }
}
