using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Argos.Shared.Contracts;
using Microsoft.Extensions.Options;

namespace Argos.Server.Services;

/// <summary>Message privé à envoyer : un embed Discord (titre, texte court, couleur).</summary>
public sealed record DiscordDirectMessage(string Title, string Description, int Color);

/// <summary>Résultat d'un envoi de message privé ; <see cref="Error"/> est lisible par le joueur.</summary>
public sealed record DirectMessageResult(bool Ok, string? Error = null);

/// <summary>Envoi d'un message privé Discord (remplacé par un faux dans les tests).</summary>
public interface IDiscordDirectMessenger
{
    /// <summary>Faux : pas de jeton de bot, aucun message ne part.</summary>
    bool IsConfigured { get; }

    /// <summary>Envoie le message ; ne lève jamais d'exception.</summary>
    Task<DirectMessageResult> SendAsync(string discordId, DiscordDirectMessage message, CancellationToken ct = default);
}

/// <summary>
/// Bot Discord ARGOS (API REST v10) : ouverture du canal privé (mis en cache par joueur) puis envoi de l'embed.
/// Respecte les limites de débit (429 → attente de retry_after) ; un joueur qui refuse les messages privés
/// (erreur 50007) est simplement ignoré.
/// </summary>
public sealed class DiscordBotMessenger(IOptionsMonitor<DiscordOAuthOptions> options, ILogger<DiscordBotMessenger> logger)
    : IDiscordDirectMessenger
{
    private const string Api = "https://discord.com/api/v10";
    private const int CannotSendToUser = 50007;
    private const int UnknownChannel = 10003;
    private const int MaxAttempts = 3;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly string UserAgent =
        $"DiscordBot (ARGOS, {typeof(DiscordBotMessenger).Assembly.GetName().Version?.ToString(3) ?? "1.0"})";

    private readonly ConcurrentDictionary<string, string> _channels = new(StringComparer.Ordinal);

    private string Token => (options.CurrentValue.BotToken ?? string.Empty).Trim();

    public bool IsConfigured => Token.Length > 0;

    public async Task<DirectMessageResult> SendAsync(string discordId, DiscordDirectMessage message, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return new(false, "Le bot Discord ARGOS n'est pas configuré sur le serveur.");
        if (string.IsNullOrWhiteSpace(discordId) || !discordId.All(char.IsAsciiDigit))
            return new(false, "Identifiant Discord invalide.");
        try
        {
            for (var round = 0; round < 2; round++)
            {
                var channel = await ChannelOfAsync(discordId, ct);
                if (channel.Error is not null)
                    return new(false, channel.Error);

                var embed = new Dictionary<string, object>
                {
                    ["title"] = Limit(message.Title, 256),
                    ["description"] = Limit(message.Description, 300),
                    ["color"] = message.Color,
                    ["footer"] = new { text = "ARGOS · VDMS" },
                    ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
                };
                var payload = new Dictionary<string, object>
                {
                    ["embeds"] = new[] { embed },
                    ["allowed_mentions"] = new { parse = Array.Empty<string>() }
                };
                var (status, body) = await PostAsync($"{Api}/channels/{channel.Id}/messages", payload, ct);
                if (IsSuccess(status))
                    return new(true);

                var code = ErrorCode(body);
                if (code == UnknownChannel || status == HttpStatusCode.NotFound)
                {
                    // Canal privé disparu : on le rouvre une fois.
                    _channels.TryRemove(discordId, out _);
                    continue;
                }
                return Failure(discordId, status, code, body);
            }
            return new(false, "Canal privé Discord introuvable.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new(false, "Envoi annulé.");
        }
        catch (Exception ex)
        {
            logger.LogWarning("Bot Discord injoignable : {Message}", ex.Message);
            return new(false, "Discord est injoignable pour le moment.");
        }
    }

    private async Task<(string? Id, string? Error)> ChannelOfAsync(string discordId, CancellationToken ct)
    {
        if (_channels.TryGetValue(discordId, out var cached))
            return (cached, null);
        var (status, body) = await PostAsync($"{Api}/users/@me/channels", new { recipient_id = discordId }, ct);
        if (!IsSuccess(status))
            return (null, Failure(discordId, status, ErrorCode(body), body).Error);
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } value)
            {
                _channels[discordId] = value;
                return (value, null);
            }
        }
        catch (JsonException)
        {
            // Réponse inattendue : traitée ci-dessous.
        }
        logger.LogWarning("Bot Discord : réponse inattendue à l'ouverture du canal privé de {DiscordId}.", discordId);
        return (null, "Réponse inattendue de Discord.");
    }

    /// <summary>POST JSON avec le jeton du bot ; attend et réessaie sur 429 (limite de débit).</summary>
    private async Task<(HttpStatusCode Status, string Body)> PostAsync(string url, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Authorization", "Bot " + Token);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            using var response = await Http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= MaxAttempts)
                return (response.StatusCode, body);

            var wait = RetryAfter(body) ?? response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
            if (wait > TimeSpan.FromSeconds(30)) wait = TimeSpan.FromSeconds(30);
            logger.LogDebug("Bot Discord : limite de débit, nouvel essai dans {Delay} s.", wait.TotalSeconds);
            await Task.Delay(wait, ct);
        }
    }

    private DirectMessageResult Failure(string discordId, HttpStatusCode status, int? code, string body)
    {
        if (code == CannotSendToUser)
        {
            logger.LogDebug("Bot Discord : {DiscordId} n'accepte pas les messages privés (50007).", discordId);
            return new(false, "Discord refuse le message : autorise les messages privés des membres du serveur VDMS "
                              + "(paramètres de confidentialité du serveur) et vérifie que le bot ARGOS est bien sur ce serveur.");
        }
        if (status == HttpStatusCode.Unauthorized)
        {
            logger.LogWarning("Bot Discord : jeton refusé (401). Vérifier Discord:BotToken dans appsettings.Local.json.");
            return new(false, "Le jeton du bot Discord ARGOS est refusé par Discord.");
        }
        if (status == HttpStatusCode.TooManyRequests)
        {
            logger.LogWarning("Bot Discord : limite de débit toujours atteinte pour {DiscordId}, message abandonné.", discordId);
            return new(false, "Discord limite les envois pour le moment : réessaie dans quelques minutes.");
        }
        logger.LogWarning("Bot Discord : envoi refusé ({Status}, code {Code}) : {Body}", (int)status, code, Limit(body, 300));
        return new(false, $"Discord a refusé l'envoi (erreur {(int)status}).");
    }

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    private static int? ErrorCode(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("code", out var code) && code.TryGetInt32(out var value)
                ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TimeSpan? RetryAfter(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("retry_after", out var value) && value.TryGetDouble(out var seconds)
                ? TimeSpan.FromSeconds(Math.Max(0.1, seconds)) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string Limit(string? value, int max)
    {
        var v = value ?? string.Empty;
        return v.Length <= max ? v : v[..(max - 1)] + "…";
    }
}

/// <summary>Une notification en attente d'envoi.</summary>
public sealed record PendingNotification(string ArgosUserId, NotificationKind Kind, string Title, string Text);

/// <summary>
/// Notifications par message privé Discord quand le joueur n'a pas ARGOS ouvert.
/// Les services appellent <see cref="Notify"/> (file d'attente, ne ralentit pas la requête) ;
/// la tâche de fond décide (préférences, poste ouvert) puis envoie.
/// </summary>
public sealed class DiscordNotifier : BackgroundService
{
    private readonly NotificationPreferencesService _preferences;
    private readonly IDiscordDirectMessenger _messenger;
    private readonly Func<string, bool> _isDiscordUserConnected;
    private readonly ILogger<DiscordNotifier> _logger;
    private readonly Channel<PendingNotification> _queue =
        Channel.CreateBounded<PendingNotification>(new BoundedChannelOptions(2000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

    /// <param name="isDiscordUserConnected">Vrai si ce compte Discord a un poste ARGOS ouvert (<see cref="RealtimeHub.IsDiscordUserConnected"/>).</param>
    public DiscordNotifier(NotificationPreferencesService preferences, IDiscordDirectMessenger messenger,
        Func<string, bool> isDiscordUserConnected, ILogger<DiscordNotifier> logger)
    {
        _preferences = preferences;
        _messenger = messenger;
        _isDiscordUserConnected = isDiscordUserConnected;
        _logger = logger;
    }

    /// <summary>Faux : pas de bot configuré, toutes les notifications sont ignorées.</summary>
    public bool IsEnabled => _messenger.IsConfigured;

    /// <summary>Met une notification en file d'attente (aucune attente, aucune exception). Sans bot : rien.</summary>
    public void Notify(string argosUserId, NotificationKind kind, string title, string text)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(argosUserId))
            return;
        _queue.Writer.TryWrite(new PendingNotification(argosUserId, kind, title, text));
    }

    /// <summary>
    /// Décide et envoie une notification : vrai si un message privé est parti.
    /// Rien si le bot n'est pas configuré, si le joueur a désactivé ce type de notification ou s'il a ARGOS ouvert.
    /// </summary>
    public async Task<bool> NotifyAsync(string argosUserId, NotificationKind kind, string title, string text, CancellationToken ct = default)
    {
        try
        {
            if (!IsEnabled || string.IsNullOrWhiteSpace(argosUserId))
                return false;
            var discordId = ArgosUserDto.DiscordOf(argosUserId.Trim());
            if (discordId.Length == 0)
                return false;
            if (!_preferences.Get(discordId).Allows(kind))
                return false;
            if (_isDiscordUserConnected(discordId))
                return false;
            var result = await _messenger.SendAsync(discordId, new DiscordDirectMessage(title, text, ColorOf(kind)), ct);
            return result.Ok;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Notification Discord non envoyée : {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>Message de test (Paramètres) : envoyé tout de suite, même si ARGOS est ouvert ou les notifications coupées.</summary>
    public async Task<OdeResult> SendTestAsync(ArgosUserDto user, CancellationToken ct = default)
    {
        if (!_messenger.IsConfigured)
            return new(false, "Le bot Discord ARGOS n'est pas encore configuré sur le serveur : contacte un administrateur.");
        var result = await _messenger.SendAsync(user.DiscordId, new DiscordDirectMessage(
            "🔔 Notification de test",
            $"Bonjour {user.DisplayName}, les notifications ARGOS arrivent bien dans tes messages privés Discord.",
            ColorOf(NotificationKind.Test)), ct);
        return result.Ok ? new(true) : new(false, result.Error ?? "L'envoi du message privé a échoué.");
    }

    /// <summary>Traite les notifications déjà en file (tests : la tâche de fond n'y tourne pas). Renvoie le nombre de messages envoyés.</summary>
    public async Task<int> FlushAsync(CancellationToken ct = default)
    {
        var sent = 0;
        while (_queue.Reader.TryRead(out var pending))
            if (await NotifyAsync(pending.ArgosUserId, pending.Kind, pending.Title, pending.Text, ct))
                sent++;
        return sent;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IsEnabled)
            _logger.LogInformation("Notifications Discord désactivées : renseigner Discord:BotToken dans appsettings.Local.json.");
        try
        {
            await foreach (var pending in _queue.Reader.ReadAllAsync(stoppingToken))
                await NotifyAsync(pending.ArgosUserId, pending.Kind, pending.Title, pending.Text, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Arrêt du serveur.
        }
    }

    public static int ColorOf(NotificationKind kind) => kind switch
    {
        NotificationKind.Mail => 0x1D4ED8,
        NotificationKind.Garde => 0xC8102E,
        NotificationKind.CompteRendu => 0x13294B,
        _ => 0x2F855A
    };

    /// <summary>« jeudi 26 novembre 2026 » d'après « 2026-11-26 ».</summary>
    public static string FormatDay(string date)
        => DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dddd d MMMM yyyy", CultureInfo.GetCultureInfo("fr-FR")) : date;
}
