using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Argos.Shared.Contracts;

namespace Argos.Server.Services;

/// <summary>
/// Connexions temps réel des postes ARGOS (/ws). Chaque poste s'authentifie avec son jeton d'application,
/// puis reçoit les messages poussés par le serveur (notifications, et plus tard : prise d'appel, ordres de départ…).
/// </summary>
public sealed class RealtimeHub(DiscordOAuthService oauth, ILogger<RealtimeHub> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxMessageBytes = 64 * 1024;

    private readonly ConcurrentDictionary<Guid, Connection> _connections = new();

    public int ConnectionCount => _connections.Count;

    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var aborted = context.RequestAborted;

        // 1. Authentification : le premier message doit arriver dans les 15 secondes.
        using var authTimeout = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        authTimeout.CancelAfter(TimeSpan.FromSeconds(15));
        RealtimeMessage? first;
        try
        {
            first = await ReceiveAsync(socket, authTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            await CloseQuietlyAsync(socket, WebSocketCloseStatus.PolicyViolation, "Authentification attendue.");
            return;
        }

        if (first is null || first.Type != RealtimeTypes.Auth)
        {
            await CloseQuietlyAsync(socket, WebSocketCloseStatus.PolicyViolation, "Authentification attendue.");
            return;
        }

        var validation = await oauth.ValidateAppTokenAsync(first.Token, forceRoleCheck: false);
        if (!validation.Valid || validation.User is null)
        {
            await SendAsync(socket, new RealtimeMessage(RealtimeTypes.Denied, Text: validation.Error ?? "Session refusée."), aborted);
            await CloseQuietlyAsync(socket, WebSocketCloseStatus.PolicyViolation, "Session refusée.");
            return;
        }

        var connection = new Connection(Guid.NewGuid(), validation.User.UserId, socket);
        _connections[connection.Id] = connection;
        logger.LogInformation("Poste connecté : {Display} ({UserId}), {Count} en ligne",
            validation.User.DisplayName, validation.User.UserId, _connections.Count);

        try
        {
            await connection.SendAsync(new RealtimeMessage(RealtimeTypes.Welcome, Text: validation.User.DisplayName), aborted);

            // 2. Boucle de réception.
            while (socket.State == WebSocketState.Open && !aborted.IsCancellationRequested)
            {
                var message = await ReceiveAsync(socket, aborted);
                if (message is null)
                    break;

                if (message.Type == RealtimeTypes.Ping)
                    await connection.SendAsync(new RealtimeMessage(RealtimeTypes.Pong), aborted);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or JsonException)
        {
            // Déconnexion du poste : rien à signaler.
        }
        finally
        {
            _connections.TryRemove(connection.Id, out _);
            await CloseQuietlyAsync(socket, WebSocketCloseStatus.NormalClosure, "Fin de session.");
            logger.LogInformation("Poste déconnecté : {UserId}, {Count} en ligne", connection.UserId, _connections.Count);
        }
    }

    /// <summary>Envoie un message à tous les postes ouverts par ce compte Discord. Retourne le nombre de postes atteints.</summary>
    public async Task<int> SendToUserAsync(string userId, RealtimeMessage message, CancellationToken ct = default)
    {
        var sent = 0;
        foreach (var connection in _connections.Values.Where(c => c.UserId == userId))
        {
            if (await connection.TrySendAsync(message, ct))
                sent++;
        }
        return sent;
    }

    /// <summary>Envoie un message à tous les postes connectés.</summary>
    public async Task<int> BroadcastAsync(RealtimeMessage message, CancellationToken ct = default)
    {
        var sent = 0;
        foreach (var connection in _connections.Values)
        {
            if (await connection.TrySendAsync(message, ct))
                sent++;
        }
        return sent;
    }

    private static async Task<RealtimeMessage?> ReceiveAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8 * 1024];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            stream.Write(buffer, 0, result.Count);
            if (stream.Length > MaxMessageBytes)
                throw new WebSocketException("Message trop long.");
            if (result.EndOfMessage)
                break;
        }

        if (stream.Length == 0)
            return new RealtimeMessage(string.Empty);
        return JsonSerializer.Deserialize<RealtimeMessage>(stream.ToArray(), JsonOptions);
    }

    private static Task SendAsync(WebSocket socket, RealtimeMessage message, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private static async Task CloseQuietlyAsync(WebSocket socket, WebSocketCloseStatus status, string reason)
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await socket.CloseAsync(status, reason, CancellationToken.None);
        }
        catch
        {
            // Le poste est déjà parti.
        }
    }

    private sealed class Connection(Guid id, string userId, WebSocket socket)
    {
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public Guid Id { get; } = id;
        public string UserId { get; } = userId;

        public async Task SendAsync(RealtimeMessage message, CancellationToken ct)
        {
            await _sendLock.WaitAsync(ct);
            try
            {
                await RealtimeHub.SendAsync(socket, message, ct);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public async Task<bool> TrySendAsync(RealtimeMessage message, CancellationToken ct)
        {
            if (socket.State != WebSocketState.Open)
                return false;
            try
            {
                await SendAsync(message, ct);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
