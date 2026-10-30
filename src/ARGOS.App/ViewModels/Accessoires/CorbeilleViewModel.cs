using System.Collections.ObjectModel;
using System.Globalization;
using Argos.Services;
using Argos.Shared.Contracts;

namespace Argos.ViewModels.Accessoires;

public sealed class TrashItem(string id, string name, string origin, string deletedAt, string size, string type, bool isFolder,
    bool selected, RelayCommand selectCommand, Action changed) : ViewModelBase
{
    private bool _checked;
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Origin { get; } = origin;
    public string DeletedAt { get; } = deletedAt;
    public string Size { get; } = size;
    public string Type { get; } = type;
    public bool IsFolder { get; } = isFolder;
    public bool Selected { get; } = selected;
    public RelayCommand SelectCommand { get; } = selectCommand;
    /// <summary>Case cochée : sélection multiple (restaurer / supprimer plusieurs éléments).</summary>
    public bool Checked { get => _checked; set { if (SetProperty(ref _checked, value)) changed(); } }
}

/// <summary>Corbeille : éléments supprimés de « Mes documents » (restaurer, supprimer définitivement, vider).</summary>
public sealed class CorbeilleViewModel : ViewModelBase, IDisposable
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private string? _selectedId;
    private string? _message;
    private bool _confirmEmpty;
    private string _search = string.Empty;
    private readonly HashSet<string> _checked = [];

    public CorbeilleViewModel()
    {
        RestoreCommand = new RelayCommand(() => _ = RunManyAsync("restore", "restauré(s)"), () => Targets.Count > 0);
        PurgeCommand = new RelayCommand(() => _ = RunManyAsync("purge", "supprimé(s) définitivement"), () => Targets.Count > 0);
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
    public string Search { get => _search; set { if (SetProperty(ref _search, value ?? string.Empty)) Build(); } }

    /// <summary>Éléments visés : les cases cochées, sinon l'élément sélectionné.</summary>
    private List<string> Targets
    {
        get
        {
            if (_checked.Count > 0) return _checked.ToList();
            var list = new List<string>();
            if (_selectedId is not null) list.Add(_selectedId);
            return list;
        }
    }
    public string SelectionText => _checked.Count > 0 ? $"{_checked.Count} élément(s) coché(s)" : string.Empty;
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
        var q = Search.Trim();
        var trash = entries.Where(e => e.Deleted && !e.DeletedWithParent)
            .Where(e => q.Length == 0 || e.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(e => e.DeletedAt).ToList();
        _checked.IntersectWith(trash.Select(e => e.Id));
        if (_selectedId is not null && trash.All(e => e.Id != _selectedId)) _selectedId = null;
        Items.Clear();
        foreach (var e in trash)
        {
            var id = e.Id;
            var size = e.IsFolder ? Inside(entries, e.Id) : e.Size;
            var item = new TrashItem(e.Id, e.Name, store!.PathOf(e.ParentId), e.DeletedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Fr) ?? string.Empty,
                FilesApi.Size(size), FileKinds.Label(e), e.IsFolder, id == _selectedId, new RelayCommand(() => { _selectedId = id; Build(); }), () => { })
            { Checked = _checked.Contains(id) };
            item.PropertyChanged += (_, a) =>
            {
                if (a.PropertyName != nameof(TrashItem.Checked)) return;
                if (item.Checked) _checked.Add(id); else _checked.Remove(id);
                OnPropertyChanged(nameof(SelectionText));
                RestoreCommand.RaiseCanExecuteChanged();
                PurgeCommand.RaiseCanExecuteChanged();
            };
            Items.Add(item);
        }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(SelectionText));
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

    private async Task RunManyAsync(string action, string label)
    {
        var ids = Targets;
        var errors = 0;
        foreach (var id in ids)
            if (!(await ArgosSession.Current!.Api.SendAsync(HttpMethod.Post, $"api/files/{id}/{action}")).Ok) errors++;
        _checked.Clear();
        await (ArgosSession.Current?.Files.RefreshAsync() ?? Task.CompletedTask);
        Message = errors == 0 ? $"{ids.Count} élément(s) {label}." : $"{ids.Count - errors} élément(s) {label}, {errors} en échec.";
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
