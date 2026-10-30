using Argos.Shared.Contracts;
using Avalonia.Threading;

namespace Argos.Services;

/// <summary>
/// File de l'imprimante virtuelle par les fenêtres du poste.
/// Rechargé à l'ouverture puis à chaque signal « printer-changed » du serveur.
/// <see cref="Changed"/> est toujours levé sur le fil de l'interface.
/// </summary>
public sealed class PrinterStore(ApiClient api)
{
    private readonly object _sync = new();
    private Task? _running;
    private bool _pending;

    public PrinterSnapshot Snapshot { get; private set; } = new();
    public bool IsLoaded { get; private set; }
    public string? LastError { get; private set; }

    public event Action? Changed;

    public Task RefreshAsync()
    {
        lock (_sync)
        {
            if (_running is { IsCompleted: false })
            {
                _pending = true;
                return _running;
            }
            _pending = false;
            _running = RunAsync();
            return _running;
        }
    }

    private async Task RunAsync()
    {
        while (true)
        {
            try
            {
                Snapshot = await api.GetAsync<PrinterSnapshot>("api/printer/state");
                IsLoaded = true;
                LastError = null;
            }
            catch (Exception ex)
            {
                LastError = ex switch
                {
                    UnauthorizedAccessException => ex.Message,
                    InvalidOperationException when ex.Message.Contains("(404)") =>
                        "Le serveur ARGOS n'est pas à jour : l'imprimante virtuelle n'y est pas installée (mettre à jour le serveur du VPS).",
                    InvalidOperationException => "Erreur du serveur ARGOS : " + ex.Message,
                    HttpRequestException or TaskCanceledException => "Serveur ARGOS injoignable.",
                    _ => "Réponse illisible du serveur ARGOS : " + ex.Message
                };
            }
            Dispatcher.UIThread.Post(() => Changed?.Invoke());

            lock (_sync)
            {
                if (!_pending)
                    return;
                _pending = false;
            }
        }
    }

    /// <summary>Remplace l'état sans passer par le serveur (captures d'écran de test).</summary>
    public void Apply(PrinterSnapshot snapshot)
    {
        Snapshot = snapshot;
        IsLoaded = true;
        LastError = null;
        Changed?.Invoke();
    }

    public PrintJob? Find(string? id) => Snapshot.Jobs.FirstOrDefault(j => j.Id == id);
    public int Pending => Snapshot.Jobs.Count(j => j.Status != PrintStatuses.Imprime);
}
