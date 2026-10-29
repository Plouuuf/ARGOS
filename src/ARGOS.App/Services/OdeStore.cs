using Argos.Shared.Contracts;
using Avalonia.Threading;

namespace Argos.Services;

/// <summary>
/// État ADAGIO ODE partagé par toutes les fenêtres du poste (appels, interventions, engins).
/// Rechargé à l'ouverture puis à chaque signal « ode-changed » du serveur.
/// L'événement <see cref="Changed"/> est toujours levé sur le fil de l'interface.
/// </summary>
public sealed class OdeStore(ApiClient api)
{
    private readonly object _sync = new();
    private Task? _running;
    private bool _pending;

    public OdeSnapshot Snapshot { get; private set; } = new();
    public bool IsLoaded { get; private set; }
    public string? LastError { get; private set; }

    public event Action? Changed;

    public ApiClient Api => api;

    /// <summary>
    /// Recharge l'état. Plusieurs signaux rapprochés ne lancent qu'une requête à la fois,
    /// suivie d'une dernière pour être à jour ; l'appelant attend toujours une lecture récente.
    /// </summary>
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
                Snapshot = await api.GetAsync<OdeSnapshot>("api/ode/state");
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

    public CallRecord? FindCall(string id) => Snapshot.Calls.FirstOrDefault(c => c.Id == id);

    public Intervention? FindIntervention(string id) => Snapshot.Interventions.FirstOrDefault(i => i.Id == id);

    public Intervention? FindInterventionByNumber(string number)
        => Snapshot.Interventions.FirstOrDefault(i => i.Number == number);

    public EnginState? FindEngin(string nom)
        => Snapshot.Engins.FirstOrDefault(e => string.Equals(e.Nom, nom, StringComparison.OrdinalIgnoreCase));

    /// <summary>Fiche d'appel à jour, directement depuis le serveur (appel absent de l'état ou trop récent).</summary>
    public async Task<CallRecord?> LoadCallAsync(string id)
    {
        try
        {
            return await api.GetAsync<CallRecord>("api/ode/calls/" + Uri.EscapeDataString(id));
        }
        catch
        {
            return FindCall(id);
        }
    }
}
