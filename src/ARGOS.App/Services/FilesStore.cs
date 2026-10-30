using Argos.Shared.Contracts;
using Avalonia.Threading;

namespace Argos.Services;

/// <summary>
/// « Mes documents » par les fenêtres du poste.
/// Rechargé à l'ouverture puis à chaque signal « files-changed » du serveur.
/// <see cref="Changed"/> est toujours levé sur le fil de l'interface.
/// </summary>
public sealed class FilesStore(ApiClient api)
{
    private readonly object _sync = new();
    private Task? _running;
    private bool _pending;

    public FilesSnapshot Snapshot { get; private set; } = new();
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
                Snapshot = await api.GetAsync<FilesSnapshot>("api/files/state");
                IsLoaded = true;
                LastError = null;
            }
            catch (Exception ex)
            {
                LastError = ex switch
                {
                    UnauthorizedAccessException => ex.Message,
                    InvalidOperationException when ex.Message.Contains("(404)") =>
                        "Le serveur ARGOS n'est pas à jour : « Mes documents » n'y est pas installé (mettre à jour le serveur du VPS).",
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
    public void Apply(FilesSnapshot snapshot)
    {
        Snapshot = snapshot;
        IsLoaded = true;
        LastError = null;
        Changed?.Invoke();
    }

    public FileEntry? Find(string? id) => Snapshot.Entries.FirstOrDefault(e => e.Id == id);

    /// <summary>Chemin affiché : « Ce PC › Documents › Gardes ».</summary>
    public string PathOf(string? folderId)
    {
        var parts = new List<string>();
        for (var e = Find(folderId); e is not null && parts.Count < 50; e = Find(e.ParentId))
            parts.Insert(0, e.Name);
        parts.Insert(0, "Ce PC");
        return string.Join(" › ", parts);
    }
}
