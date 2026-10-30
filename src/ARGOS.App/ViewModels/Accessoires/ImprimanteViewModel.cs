using System.Collections.ObjectModel;
using System.Globalization;
using Argos.Services;
using Argos.Shared.Contracts;
using Argos.Views.Apps.Ode;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace Argos.ViewModels.Accessoires;

public sealed record PrintRow(string Id, string Title, string Owner, string Status, string Created, string Printed, string Size,
    bool Pending, bool Selected, RelayCommand SelectCommand);

/// <summary>
/// Imprimante du centre : documents en attente et imprimés (file partagée), aperçu du document,
/// réimpression, suppression (auteur ou encadrement), enregistrement sur le poste.
/// </summary>
public sealed class ImprimanteViewModel : ViewModelBase, IDisposable
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private readonly DesktopViewModel _desktop;
    private string? _selectedId;
    private string _filter = "Tous";
    private string _search = string.Empty;
    private string? _message;
    private Bitmap? _preview;
    private bool _previewLoading;
    private readonly Dictionary<string, Bitmap> _images = [];

    public ImprimanteViewModel(DesktopViewModel desktop)
    {
        _desktop = desktop;
        ReprintCommand = new RelayCommand(() => _ = RunAsync($"api/printer/jobs/{_selectedId}/reprint", "Document renvoyé à l'imprimante."), () => Selected is not null);
        DeleteCommand = new RelayCommand(() => _ = RunAsync($"api/printer/jobs/{_selectedId}/delete", "Document supprimé de l'imprimante."), () => CanDelete);
        OpenInterventionCommand = new RelayCommand(() =>
        {
            if (Selected?.InterventionId is { Length: > 0 } id) _desktop.OpenWindow(OdeWindows.Intervention(_desktop, id));
        }, () => Selected?.InterventionId is { Length: > 0 });
        RefreshCommand = new RelayCommand(() => _ = Store?.RefreshAsync());
        if (Store is { } store)
        {
            store.Changed += Build;
            if (!store.IsLoaded) _ = store.RefreshAsync();
        }
        Build();
    }

    private static PrinterStore? Store => ArgosSession.Current?.Printer;

    public IStorageProvider? Storage { get; set; }

    public ObservableCollection<PrintRow> Jobs { get; } = [];
    public IReadOnlyList<string> Filters { get; } = ["Tous", "En attente", "Imprimés", "Mes documents"];
    public string Filter { get => _filter; set { if (SetProperty(ref _filter, value ?? "Tous")) Build(); } }
    public string Search { get => _search; set { if (SetProperty(ref _search, value ?? string.Empty)) Build(); } }

    public PrintJob? Selected => Store?.Find(_selectedId);
    public bool HasSelection => Selected is not null;
    public bool CanDelete => Selected is { } j && (j.OwnerId == ArgosSession.Current?.User.UserId || Store?.Snapshot.CanManageAll == true);
    public string SelectedTitle => Selected?.Title ?? string.Empty;
    public string SelectedInfo => Selected is { } j
        ? $"{j.Status} · envoyé par {j.OwnerName} le {j.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", Fr)}"
          + (j.PrintedAt is { } p ? $" · imprimé à {p.ToLocalTime():HH:mm:ss}" : string.Empty) + (j.Reprint ? " · réimpression" : string.Empty)
        : string.Empty;
    public Bitmap? Preview { get => _preview; private set => SetProperty(ref _preview, value); }
    public bool HasPreview => Preview is not null;
    public bool PreviewLoading { get => _previewLoading; private set => SetProperty(ref _previewLoading, value); }
    public bool NoImage => HasSelection && !HasPreview && !PreviewLoading;

    public string StatusText => Store is { } s ? $"{s.Snapshot.Jobs.Count} document(s) · {s.Pending} en attente" : string.Empty;
    public string? Message { get => _message; private set { if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage)); } }
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    public RelayCommand ReprintCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand OpenInterventionCommand { get; }
    public RelayCommand RefreshCommand { get; }

    private void Build()
    {
        var me = ArgosSession.Current?.User.UserId;
        var jobs = Store?.Snapshot.Jobs ?? [];
        if (Store?.LastError is { } error) Message = error;
        if (_selectedId is not null && jobs.All(j => j.Id != _selectedId)) Select(null);
        var q = Search.Trim();
        var visible = jobs.Where(j => Filter switch
            {
                "En attente" => j.Status != PrintStatuses.Imprime,
                "Imprimés" => j.Status == PrintStatuses.Imprime,
                "Mes documents" => j.OwnerId == me,
                _ => true
            })
            .Where(j => q.Length == 0 || j.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                        || j.OwnerName.Contains(q, StringComparison.CurrentCultureIgnoreCase));
        Jobs.Clear();
        foreach (var j in visible)
        {
            var id = j.Id;
            Jobs.Add(new PrintRow(j.Id, j.Title, j.OwnerName, j.Status, j.CreatedAt.ToLocalTime().ToString("dd/MM HH:mm:ss", Fr),
                j.PrintedAt?.ToLocalTime().ToString("HH:mm:ss", Fr) ?? "—", j.HasImage ? FilesApi.Size(j.Size) : "—",
                j.Status != PrintStatuses.Imprime, id == _selectedId, new RelayCommand(() => Select(id))));
        }
        foreach (var n in new[] { nameof(Selected), nameof(HasSelection), nameof(CanDelete), nameof(SelectedTitle), nameof(SelectedInfo),
                                  nameof(StatusText), nameof(NoImage) })
            OnPropertyChanged(n);
        foreach (var c in new[] { ReprintCommand, DeleteCommand, OpenInterventionCommand }) c.RaiseCanExecuteChanged();
    }

    public void Select(string? id)
    {
        if (_selectedId == id) return;
        _selectedId = id;
        Preview = null;
        OnPropertyChanged(nameof(HasPreview));
        if (id is not null) _ = LoadPreviewAsync(id);
        Build();
    }

    private async Task LoadPreviewAsync(string id)
    {
        if (_images.TryGetValue(id, out var cached)) { ShowPreview(id, cached); return; }
        if (Store?.Find(id) is not { HasImage: true } || ArgosSession.Current is not { } session) return;
        PreviewLoading = true;
        OnPropertyChanged(nameof(NoImage));
        try
        {
            var bytes = await session.Api.GetBytesAsync($"api/printer/jobs/{id}/image");
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            _images[id] = bitmap;
            ShowPreview(id, bitmap);
        }
        catch (Exception ex)
        {
            Message = "Aperçu indisponible : " + ex.Message;
        }
        finally
        {
            PreviewLoading = false;
            OnPropertyChanged(nameof(NoImage));
        }
    }

    private void ShowPreview(string id, Bitmap bitmap)
    {
        if (_selectedId != id) return;
        Preview = bitmap;
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(NoImage));
    }

    /// <summary>Enregistre le document sélectionné sur le poste (PNG).</summary>
    public async Task SaveAsync()
    {
        if (Selected is not { HasImage: true } job || Storage is null || ArgosSession.Current is not { } session) return;
        var file = await Storage.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Enregistrer le document", SuggestedFileName = job.Title + ".png" });
        if (file is null) return;
        var bytes = await session.Api.GetBytesAsync($"api/printer/jobs/{job.Id}/image");
        await using var target = await file.OpenWriteAsync();
        await target.WriteAsync(bytes);
        Message = $"« {job.Title} » enregistré.";
    }

    private async Task RunAsync(string path, string success)
    {
        var result = await FilesApi.PostAsync(path);
        await (Store?.RefreshAsync() ?? Task.CompletedTask);
        Message = result.Ok ? success : result.Error ?? "Action refusée.";
    }

    public void Dispose()
    {
        if (Store is { } store) store.Changed -= Build;
        foreach (var b in _images.Values) b.Dispose();
    }
}
