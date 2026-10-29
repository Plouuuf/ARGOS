using Argos.Shared.Contracts;
using Avalonia.Threading;

namespace Argos.Services;

/// <summary>
/// État ADAGIO REMISE partagé par les fenêtres du poste (parc, matériel, maintenance, stocks).
/// Rechargé à l'ouverture puis à chaque signal « remise-changed » du serveur.
/// <see cref="Changed"/> est toujours levé sur le fil de l'interface.
/// </summary>
public sealed class RemiseStore(ApiClient api)
{
    private readonly object _sync = new();
    private Task? _running;
    private bool _pending;

    public RemiseSnapshot Snapshot { get; private set; } = new();
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
                Snapshot = await api.GetAsync<RemiseSnapshot>("api/remise/state");
                IsLoaded = true;
                LastError = null;
            }
            catch (Exception ex)
            {
                LastError = ex is UnauthorizedAccessException ? ex.Message : "Serveur ARGOS injoignable.";
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
    public void Apply(RemiseSnapshot snapshot)
    {
        Snapshot = snapshot;
        IsLoaded = true;
        LastError = null;
        Changed?.Invoke();
    }

    public FleetEngin? FindEngin(string nom)
        => Snapshot.Engins.FirstOrDefault(e => string.Equals(e.Nom, nom, StringComparison.OrdinalIgnoreCase));

    public MaterielItem? FindItem(string id) => Snapshot.Items.FirstOrDefault(i => i.Id == id);
}
