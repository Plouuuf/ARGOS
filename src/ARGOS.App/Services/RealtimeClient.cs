using System.Net.WebSockets;
using System.Text.Json;
using Argos.Shared.Contracts;

namespace Argos.Services;

/// <summary>
/// Connexion temps réel au serveur ARGOS, reconnectée automatiquement.
/// Les événements sont levés hors du fil de l'interface : l'abonné repasse sur Dispatcher.UIThread.
/// </summary>
public sealed class RealtimeClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public event Action<bool>? ConnectionChanged;
    public event Action<RealtimeMessage>? MessageReceived;
    public event Action<string>? SessionDenied;

    public bool IsConnected { get; private set; }

    public void Start(string token)
    {
        if (_loop is not null)
            return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(token, _cts.Token));
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        var loop = _loop;
        _cts = null;
        _loop = null;
        if (cts is null)
            return;

        cts.Cancel();
        try
        {
            if (loop is not null)
                await loop;
        }
        catch
        {
            // Arrêt demandé.
        }
        cts.Dispose();
        SetConnected(false);
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task RunAsync(string token, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                await socket.ConnectAsync(ClientSettings.Current.RealtimeUri, ct);
                await SendAsync(socket, new RealtimeMessage(RealtimeTypes.Auth, Token: token), ct);

                while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var message = await ReceiveAsync(socket, ct);
                    if (message is null)
                        break;

                    switch (message.Type)
                    {
                        case RealtimeTypes.Welcome:
                            SetConnected(true);
                            delay = TimeSpan.FromSeconds(2);
                            break;
                        case RealtimeTypes.Denied:
                            SessionDenied?.Invoke(message.Text ?? "Session refusée par le serveur.");
                            return;
                        case RealtimeTypes.Pong:
                            break;
                        default:
                            MessageReceived?.Invoke(message);
                            break;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // Serveur injoignable ou connexion coupée : nouvelle tentative plus bas.
            }

            SetConnected(false);
            try
            {
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
        }
    }

    private void SetConnected(bool connected)
    {
        if (IsConnected == connected)
            return;
        IsConnected = connected;
        ConnectionChanged?.Invoke(connected);
    }

    private static Task SendAsync(ClientWebSocket socket, RealtimeMessage message, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private static async Task<RealtimeMessage?> ReceiveAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8 * 1024];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                break;
        }
        return stream.Length == 0
            ? new RealtimeMessage(string.Empty)
            : JsonSerializer.Deserialize<RealtimeMessage>(stream.ToArray(), JsonOptions);
    }
}
