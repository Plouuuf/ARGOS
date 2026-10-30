using System.Collections.ObjectModel;
using System.Globalization;
using Argos.Services;
using Argos.Shared.Contracts;

namespace Argos.ViewModels.Accessoires;

public sealed record TrashItem(string Id, string Name, string Origin, string DeletedAt, string Size, string Type, bool IsFolder,
    bool Selected, RelayCommand SelectCommand);

/// <summary>Corbeille : éléments supprimés de « Mes documents » (restaurer, supprimer définitivement, vider).</summary>
public sealed class CorbeilleViewModel : ViewModelBase, IDisposable
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private string? _selectedId;
    private string? _message;
    private bool _confirmEmpty;

    public CorbeilleViewModel()
    {
        RestoreCommand = new RelayCommand(() => _ = RunAsync($"api/files/{_selectedId}/restore", "Élément restauré."), () => _selectedId is not null);
        PurgeCommand = new RelayCommand(() => _ = RunAsync($"api/files/{_selectedId}/purge", "Élément supprimé définitivement."), () => _selectedId is not null);
        RestoreAllCommand = new RelayCommand(() => _ = RestoreAllAsync(), () => Items.Count > 0);
        EmptyCommand = new RelayCommand(() => ConfirmEmpty = true, () => Items.Count > 0);
        ConfirmEmptyCommand = new RelayCommand(() => { ConfirmEmpty = false; _ = RunAsync("api/files/trash/empty", "Corbeille vidée."); });
        CancelEmptyCommand = new RelayCommand(() => ConfirmEmpty = false);
        if (ArgosSession.Current?.Files is { } store)
        {
            store.Changed += Build;
            if (!store.IsLoaded) _ = store.RefreshAsync();
        }
        Build();
    }

    public ObservableCollection<TrashItem> Items { get; } = [];
    public bool IsEmpty => Items.Count == 0;
    public string StatusText => $"{Items.Count} élément(s)";
    public string? Message { get => _message; private set { if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage)); } }
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public bool ConfirmEmpty { get => _confirmEmpty; set => SetProperty(ref _confirmEmpty, value); }

    public RelayCommand RestoreCommand { get; }
    public RelayCommand PurgeCommand { get; }
    public RelayCommand RestoreAllCommand { get; }
    public RelayCommand EmptyCommand { get; }
    public RelayCommand ConfirmEmptyCommand { get; }
    public RelayCommand CancelEmptyCommand { get; }

    private void Build()
    {
        var store = ArgosSession.Current?.Files;
        var entries = store?.Snapshot.Entries ?? [];
        var trash = entries.Where(e => e.Deleted && !e.DeletedWithParent).OrderByDescending(e => e.DeletedAt).ToList();
        if (_selectedId is not null && trash.All(e => e.Id != _selectedId)) _selectedId = null;
        Items.Clear();
        foreach (var e in trash)
        {
            var id = e.Id;
            var size = e.IsFolder ? Inside(entries, e.Id) : e.Size;
            Items.Add(new TrashItem(e.Id, e.Name, store!.PathOf(e.ParentId), e.DeletedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Fr) ?? string.Empty,
                FilesApi.Size(size), FileKinds.Label(e), e.IsFolder, id == _selectedId, new RelayCommand(() => { _selectedId = id; Build(); })));
        }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(StatusText));
        foreach (var c in new[] { RestoreCommand, PurgeCommand, RestoreAllCommand, EmptyCommand }) c.RaiseCanExecuteChanged();
    }

    private static long Inside(List<FileEntry> entries, string folderId)
    {
        long total = 0;
        foreach (var child in entries.Where(e => e.ParentId == folderId))
            total += child.IsFolder ? Inside(entries, child.Id) : child.Size;
        return total;
    }

    private async Task RunAsync(string path, string success)
    {
        var result = await FilesApi.PostAsync(path);
        Message = result.Ok ? success : result.Error ?? "Action refusée.";
    }

    private async Task RestoreAllAsync()
    {
        foreach (var id in Items.Select(i => i.Id).ToList())
            await ArgosSession.Current!.Api.SendAsync(HttpMethod.Post, $"api/files/{id}/restore");
        await (ArgosSession.Current?.Files.RefreshAsync() ?? Task.CompletedTask);
        Message = "Tous les éléments ont été restaurés.";
    }

    public void Dispose()
    {
        if (ArgosSession.Current?.Files is { } store) store.Changed -= Build;
    }
}
