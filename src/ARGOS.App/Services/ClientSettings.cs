using System.Text.Json;

namespace Argos.Services;

/// <summary>
/// Réglages du poste. Valeurs intégrées (client.defaults.json), puis, sur ordinateur,
/// un éventuel argos.local.json à côté de l'exécutable pour pointer vers un autre serveur (tests).
/// </summary>
public sealed class ClientSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private static ClientSettings? _current;

    public string ServerUrl { get; set; } = "http://127.0.0.1:8092";

    public static ClientSettings Current => _current ??= Load();

    /// <summary>Adresse HTTP de base du serveur (toujours terminée par « / »).</summary>
    public Uri HttpBaseUri
    {
        get
        {
            if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri))
                throw new InvalidOperationException("Adresse du serveur ARGOS invalide.");
            var builder = new UriBuilder(uri)
            {
                Scheme = uri.Scheme switch { "wss" => "https", "ws" => "http", _ => uri.Scheme },
                Path = "/",
                Query = string.Empty,
                Fragment = string.Empty
            };
            return builder.Uri;
        }
    }

    /// <summary>Adresse de la connexion temps réel.</summary>
    public Uri RealtimeUri
    {
        get
        {
            var http = HttpBaseUri;
            var builder = new UriBuilder(http)
            {
                Scheme = http.Scheme == "https" ? "wss" : "ws",
                Path = "/ws"
            };
            return builder.Uri;
        }
    }

    private static ClientSettings Load()
    {
        var settings = new ClientSettings();
        try
        {
            using var stream = typeof(ClientSettings).Assembly.GetManifestResourceStream("ARGOS.client.defaults.json");
            if (stream is not null)
                Apply(settings, JsonSerializer.Deserialize<ClientSettings>(stream, JsonOptions));
        }
        catch
        {
            // Valeurs par défaut.
        }

        if (!AppPaths.IsMobile)
        {
            try
            {
                var local = Path.Combine(AppContext.BaseDirectory, "argos.local.json");
                if (File.Exists(local))
                    Apply(settings, JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(local), JsonOptions));
            }
            catch
            {
                // Fichier local illisible : on garde les valeurs intégrées.
            }
        }

        return settings;
    }

    private static void Apply(ClientSettings target, ClientSettings? source)
    {
        if (source is not null && !string.IsNullOrWhiteSpace(source.ServerUrl))
            target.ServerUrl = source.ServerUrl.Trim();
    }
}
