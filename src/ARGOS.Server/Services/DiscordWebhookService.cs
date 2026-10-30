using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Argos.Shared.Contracts;
using Microsoft.Extensions.Options;

namespace Argos.Server.Services;

/// <summary>Salons Discord alimentés par ARGOS (adresses de webhook, dans appsettings.Local.json).</summary>
public sealed class WebhookOptions
{
    /// <summary>Webhook du salon qui reçoit les ODE imprimés (Discord › salon › Intégrations › Webhooks).</summary>
    public string Ode { get; set; } = string.Empty;

    /// <summary>Webhook du salon qui reçoit les feuilles de garde (SIGTAO).</summary>
    public string Garde { get; set; } = string.Empty;
}

/// <summary>Publication des ODE imprimés dans un salon Discord : image de la feuille + résumé.</summary>
public sealed class DiscordWebhookService(IOptionsMonitor<WebhookOptions> options, ILogger<DiscordWebhookService> logger)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public bool OdeEnabled => IsWebhook(options.CurrentValue.Ode);

    private static bool IsWebhook(string? url)
        => !string.IsNullOrWhiteSpace(url)
           && Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && uri.Scheme == Uri.UriSchemeHttps
           && (uri.Host.EndsWith("discord.com", StringComparison.OrdinalIgnoreCase)
               || uri.Host.EndsWith("discordapp.com", StringComparison.OrdinalIgnoreCase))
           && uri.AbsolutePath.Contains("/api/webhooks/", StringComparison.Ordinal);

    /// <summary>Envoie l'ODE ; renvoie false (sans lever d'erreur) si l'envoi échoue.</summary>
    public async Task<bool> SendOdeAsync(Intervention intervention, DepartureOrder order, string printedBy, byte[]? png)
    {
        if (!OdeEnabled)
            return false;
        try
        {
            var procedure = Procedures.Find(order.Procedure);
            var nature = Natures.Find(intervention.NatureCode)?.Display ?? intervention.NatureCode;
            var address = string.Join(" - ", new[] { intervention.Address, intervention.Locality, intervention.Building }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            var fileName = $"ODE-{intervention.Number}-{order.Number}.png";
            var color = int.Parse((procedure?.Color ?? "#FFFFFF").TrimStart('#'), NumberStyles.HexNumber);

            var fields = new List<object>
            {
                new { name = "Nature", value = Limit(nature, 1024), inline = false },
                new { name = "Adresse", value = Limit(Or(address), 1024), inline = false },
                new { name = order.Number == 1 ? "Premier départ" : $"Départ complémentaire n°{order.Number}",
                      value = Limit(Or(string.Join("\n", order.Engins)), 1024), inline = true },
                new { name = "Procédure", value = procedure?.Label ?? order.Procedure, inline = true },
                new { name = "CSTC", value = Or(intervention.Cstc), inline = true }
            };
            if (!string.IsNullOrWhiteSpace(intervention.CallerInfo))
                fields.Add(new { name = "Renseignements", value = Limit(intervention.CallerInfo, 1024), inline = false });

            var embed = new Dictionary<string, object>
            {
                ["title"] = $"ODE {intervention.Number} - {Procedures.MissionTitle(order.Procedure)}",
                ["color"] = color,
                ["fields"] = fields,
                ["footer"] = new { text = $"Opérateur {order.OperatorName} · imprimé par {printedBy}" +
                                          (order.PrintCount > 1 ? $" · réimpression n°{order.PrintCount - 1}" : string.Empty) },
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
            };
            if (png is { Length: > 0 })
                embed["image"] = new { url = "attachment://" + fileName };

            var payload = new Dictionary<string, object>
            {
                ["username"] = "ADAGIO ODE",
                ["embeds"] = new[] { embed },
                ["allowed_mentions"] = new { parse = Array.Empty<string>() }
            };
            if (png is { Length: > 0 })
                payload["attachments"] = new[] { new { id = 0, filename = fileName } };

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json"), "payload_json");
            if (png is { Length: > 0 })
            {
                var file = new ByteArrayContent(png);
                file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                form.Add(file, "files[0]", fileName);
            }

            using var response = await Http.PostAsync(options.CurrentValue.Ode, form);
            if (response.IsSuccessStatusCode)
                return true;
            logger.LogWarning("Webhook Discord ODE refusé ({Status}) : {Body}", (int)response.StatusCode,
                Limit(await response.Content.ReadAsStringAsync(), 300));
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Webhook Discord ODE injoignable : {Message}", ex.Message);
            return false;
        }
    }

    public bool GardeEnabled => IsWebhook(options.CurrentValue.Garde);

    /// <summary>Publie la feuille de garde (image + résumé) ; false (sans erreur levée) si l'envoi échoue.</summary>
    public async Task<bool> SendGardeAsync(FeuilleGarde feuille, string publishedBy, byte[]? png)
    {
        if (!GardeEnabled)
            return false;
        try
        {
            var day = DateTime.TryParseExact(feuille.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d.ToString("dddd d MMMM yyyy", CultureInfo.GetCultureInfo("fr-FR")) : feuille.Date;
            var fileName = $"Garde-{feuille.Date}.png";
            var chef = feuille.Service.FirstOrDefault(a => a.Poste == GardePostes.ChefDeGarde && a.Pourvu)?.Nom;
            var armes = feuille.Engins.Where(e => e.Arme).Select(e => $"{e.Engin} ({e.Postes.Count(p => p.Pourvu)}/{e.Postes.Count})").ToList();
            var nonArmes = feuille.Engins.Where(e => !e.Arme).Select(e => e.Engin).ToList();
            var fields = new List<object>
            {
                new { name = "Chef de garde", value = Or(chef), inline = true },
                new { name = "Effectif", value = feuille.Effectif.ToString(), inline = true },
                new { name = "Horaires", value = Or(feuille.Horaires), inline = true },
                new { name = "Engins armés", value = Limit(Or(string.Join("\n", armes)), 1024), inline = false }
            };
            if (nonArmes.Count > 0)
                fields.Add(new { name = "Non armés (indisponibles)", value = Limit(string.Join("\n", nonArmes), 1024), inline = false });
            if (!string.IsNullOrWhiteSpace(feuille.Observations))
                fields.Add(new { name = "Observations", value = Limit(feuille.Observations, 1024), inline = false });

            var embed = new Dictionary<string, object>
            {
                ["title"] = feuille.Centre == FeuilleGarde.TousCentres ? $"Feuille de garde - {day}" : $"Feuille de garde {feuille.Centre} - {day}",
                ["color"] = 0x13294B,
                ["fields"] = fields,
                ["footer"] = new { text = (feuille.IsValidated ? $"Validée par {feuille.ValidatedBy}" : "Non validée") + $" · publiée par {publishedBy}" },
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
            };
            if (png is { Length: > 0 })
                embed["image"] = new { url = "attachment://" + fileName };
            var payload = new Dictionary<string, object>
            {
                ["username"] = "SIGTAO",
                ["embeds"] = new[] { embed },
                ["allowed_mentions"] = new { parse = Array.Empty<string>() }
            };
            if (png is { Length: > 0 })
                payload["attachments"] = new[] { new { id = 0, filename = fileName } };

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json"), "payload_json");
            if (png is { Length: > 0 })
            {
                var file = new ByteArrayContent(png);
                file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                form.Add(file, "files[0]", fileName);
            }
            using var response = await Http.PostAsync(options.CurrentValue.Garde, form);
            if (response.IsSuccessStatusCode)
                return true;
            logger.LogWarning("Webhook Discord garde refusé ({Status}) : {Body}", (int)response.StatusCode,
                Limit(await response.Content.ReadAsStringAsync(), 300));
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Webhook Discord garde injoignable : {Message}", ex.Message);
            return false;
        }
    }

    private static string Or(string? v) => string.IsNullOrWhiteSpace(v) ? "—" : v;
    private static string Limit(string v, int max) => v.Length <= max ? v : v[..(max - 1)] + "…";
}
