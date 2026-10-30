using System.Collections.ObjectModel;
using System.Globalization;
using Argos.Services;
using Argos.Shared.Contracts;
using Argos.ViewModels.Mail;
using Argos.Views.Apps.Accessoires;
using Avalonia.Platform.Storage;

namespace Argos.ViewModels.Accessoires;

public sealed record FolderNode(string Id, string Name, int Depth, bool Selected, RelayCommand SelectCommand)
{
    public Avalonia.Thickness Indent => new(10 + Depth * 16, 0, 0, 0);
}

public sealed record ExplorerItem(string Id, string Name, string Type, string Modified, string Size, bool IsFolder, bool IsText,
    bool Selected, RelayCommand SelectCommand, RelayCommand OpenCommand)
{
}

/// <summary>Opérations communes sur « Mes documents » (Explorateur, Bloc-notes, Corbeille).</summary>
public static class FilesApi
{
    public static async Task<OdeResult> PostAsync(string path, object? body = null)
    {
        var session = ArgosSession.Current;
        if (session is null) return new OdeResult(false, "Session fermée.");
        var result = await session.Api.SendAsync(HttpMethod.Post, path, body ?? new { });
        await session.Files.RefreshAsync();
        return result;
    }

    public static Task<OdeResult> UploadAsync(string parentId, string name, byte[] data, string? replaceId = null)
        => PostAsync("api/files/upload", new FileUploadRequest
        {
            ParentId = parentId, Name = name, ContentType = FileKinds.ContentType(name), Data = Convert.ToBase64String(data), ReplaceId = replaceId
        });

    public static async Task<byte[]> DownloadAsync(string id)
        => await (ArgosSession.Current?.Api.GetBytesAsync($"api/files/{id}/content") ?? Task.FromResult(Array.Empty<byte>()));

    public static string Size(long bytes) => MailAppViewModel.Size(bytes);
}

/// <summary>Explorateur de fichiers : arborescence, liste détaillée, création, import, renommage, déplacement, corbeille.</summary>
public sealed class ExplorateurViewModel : ViewModelBase, IDisposable
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private readonly DesktopViewModel _desktop;
    private readonly Stack<string> _back = new();
    private string _folderId = string.Empty;
    private string? _selectedId;
    private string _search = string.Empty;
    private string? _message;
    private bool _messageIsError;
    private bool _isRenaming;
    private string _renameText = string.Empty;
    private FolderNode? _moveTarget;

    public ExplorateurViewModel(DesktopViewModel desktop)
    {
        _desktop = desktop;
        BackCommand = new RelayCommand(GoBack, () => _back.Count > 0);
        UpCommand = new RelayCommand(() => Navigate(Store?.Find(_folderId)?.ParentId ?? string.Empty), () => _folderId.Length > 0);
        NewFolderCommand = new RelayCommand(() => _ = NewFolderAsync());
        NewTextCommand = new RelayCommand(() => _ = NewTextAsync());
        OpenCommand = new RelayCommand(() => { if (_selectedId is { } id) Open(id); }, () => _selectedId is not null);
        RenameCommand = new RelayCommand(StartRename, () => _selectedId is not null);
        ConfirmRenameCommand = new RelayCommand(() => _ = ConfirmRenameAsync());
        CancelRenameCommand = new RelayCommand(() => IsRenaming = false);
        DeleteCommand = new RelayCommand(() => _ = DeleteAsync(), () => _selectedId is not null);
        MoveCommand = new RelayCommand(() => _ = MoveAsync(), () => _selectedId is not null && MoveTarget is not null);
        RefreshCommand = new RelayCommand(() => _ = Store?.RefreshAsync());
        RootCommand = new RelayCommand(() => Navigate(string.Empty));

        if (Store is { } store)
        {
            store.Changed += Build;
            if (!store.IsLoaded) _ = store.RefreshAsync();
        }
        Build();
    }

    private static FilesStore? Store => ArgosSession.Current?.Files;
    private FilesSnapshot S => Store?.Snapshot ?? new FilesSnapshot();

    public IStorageProvider? Storage { get; set; }
    public ILauncher? Launcher { get; set; }

    public ObservableCollection<FolderNode> Folders { get; } = [];
    public ObservableCollection<FolderNode> MoveTargets { get; } = [];
    public ObservableCollection<ExplorerItem> Items { get; } = [];

    public string FolderId => _folderId;
    public string PathText => Store?.PathOf(_folderId) ?? "Mes documents";
    public bool IsRoot => _folderId.Length == 0;
    public bool HasItems => Items.Count > 0;
    public string StatusText => $"{Items.Count} élément(s)" + (Selected is { } s ? $"   1 élément sélectionné{(s.IsFolder ? "" : "  " + FilesApi.Size(s.Size))}" : string.Empty);
    public string QuotaText => $"{FilesApi.Size(S.Used)} utilisés sur {FilesApi.Size(S.Quota == 0 ? FilesQuota : S.Quota)}";
    public double QuotaRatio => S.Quota == 0 ? 0 : (double)S.Used / S.Quota;
    private const long FilesQuota = 200L * 1024 * 1024;

    public FileEntry? Selected => Store?.Find(_selectedId);

    public string Search
    {
        get => _search;
        set { if (SetProperty(ref _search, value)) Build(); }
    }

    public string? Message { get => _message; private set { if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage)); } }
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public bool MessageIsError { get => _messageIsError; private set => SetProperty(ref _messageIsError, value); }

    public bool IsRenaming { get => _isRenaming; set => SetProperty(ref _isRenaming, value); }
    public string RenameText { get => _renameText; set => SetProperty(ref _renameText, value ?? string.Empty); }
    public FolderNode? MoveTarget { get => _moveTarget; set { SetProperty(ref _moveTarget, value); MoveCommand.RaiseCanExecuteChanged(); } }

    public RelayCommand BackCommand { get; }
    public RelayCommand UpCommand { get; }
    public RelayCommand RootCommand { get; }
    public RelayCommand NewFolderCommand { get; }
    public RelayCommand NewTextCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand RenameCommand { get; }
    public RelayCommand ConfirmRenameCommand { get; }
    public RelayCommand CancelRenameCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand MoveCommand { get; }
    public RelayCommand RefreshCommand { get; }

    // ===== Construction =====

    private void Build()
    {
        var entries = S.Entries.Where(e => !e.Deleted).ToList();
        if (_folderId.Length > 0 && entries.All(e => e.Id != _folderId)) _folderId = string.Empty;
        if (_selectedId is not null && entries.All(e => e.Id != _selectedId)) _selectedId = null;
        if (Store?.LastError is { } error) Show(error, true);

        var tree = new List<FolderNode> { new(string.Empty, "Mes documents", 0, _folderId.Length == 0, new RelayCommand(() => Navigate(string.Empty))) };
        void Walk(string parent, int depth)
        {
            foreach (var f in entries.Where(e => e.IsFolder && e.ParentId == parent).OrderBy(e => e.Name, StringComparer.Create(Fr, true)))
            {
                var id = f.Id;
                tree.Add(new FolderNode(f.Id, f.Name, depth, f.Id == _folderId, new RelayCommand(() => Navigate(id))));
                Walk(f.Id, depth + 1);
            }
        }
        Walk(string.Empty, 1);
        Replace(Folders, tree);
        var moveId = MoveTarget?.Id;
        Replace(MoveTargets, tree.Select(t => t with { Selected = false }));
        _moveTarget = MoveTargets.FirstOrDefault(t => t.Id == moveId);

        IEnumerable<FileEntry> visible;
        var query = Search.Trim();
        if (query.Length > 0)
        {
            var scope = Descendants(entries, _folderId);
            visible = scope.Where(e => e.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }
        else
        {
            visible = entries.Where(e => e.ParentId == _folderId);
        }
        Replace(Items, visible.OrderByDescending(e => e.IsFolder).ThenBy(e => e.Name, StringComparer.Create(Fr, true)).Select(ItemOf));

        foreach (var n in new[] { nameof(FolderId), nameof(PathText), nameof(IsRoot), nameof(HasItems), nameof(StatusText), nameof(QuotaText), nameof(QuotaRatio), nameof(Selected), nameof(MoveTarget) })
            OnPropertyChanged(n);
        foreach (var c in new[] { BackCommand, UpCommand, OpenCommand, RenameCommand, DeleteCommand, MoveCommand })
            c.RaiseCanExecuteChanged();
    }

    private static List<FileEntry> Descendants(List<FileEntry> entries, string folderId)
    {
        var result = new List<FileEntry>();
        var queue = new Queue<string>([folderId]);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            foreach (var child in entries.Where(e => e.ParentId == id))
            {
                result.Add(child);
                if (child.IsFolder) queue.Enqueue(child.Id);
            }
        }
        return result;
    }

    private ExplorerItem ItemOf(FileEntry e)
    {
        var id = e.Id;
        return new ExplorerItem(e.Id, e.Name, FileKinds.Label(e), e.UpdatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Fr),
            e.IsFolder ? string.Empty : FilesApi.Size(e.Size), e.IsFolder, !e.IsFolder && FileKinds.IsText(e.Name), id == _selectedId,
            new RelayCommand(() => Select(id)), new RelayCommand(() => Open(id)));
    }

    // ===== Navigation =====

    public void Navigate(string folderId)
    {
        if (folderId == _folderId) return;
        _back.Push(_folderId);
        _folderId = folderId;
        _selectedId = null;
        IsRenaming = false;
        Search = string.Empty;
        Build();
    }

    private void GoBack()
    {
        if (_back.Count == 0) return;
        _folderId = _back.Pop();
        _selectedId = null;
        Build();
    }

    public void Select(string id)
    {
        if (_selectedId == id) return;
        _selectedId = id;
        IsRenaming = false;
        Build();
    }

    public void Open(string id)
    {
        var entry = Store?.Find(id);
        if (entry is null) return;
        if (entry.IsFolder) { Navigate(id); return; }
        if (FileKinds.IsText(entry.Name)) { _desktop.OpenWindow(AccessoryWindows.Notepad(_desktop, id)); return; }
        _ = LaunchAsync(entry);
    }

    private async Task LaunchAsync(FileEntry entry)
    {
        try
        {
            var bytes = await FilesApi.DownloadAsync(entry.Id);
            var dir = Path.Combine(Path.GetTempPath(), "argos-documents", entry.Id);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, entry.Name);
            await File.WriteAllBytesAsync(path, bytes);
            if (Launcher is null || !await Launcher.LaunchFileInfoAsync(new FileInfo(path)))
                Show("Aucune application de ce poste ne sait ouvrir ce fichier : utilise « Télécharger ».", true);
        }
        catch (Exception ex)
        {
            Show("Ouverture impossible : " + ex.Message, true);
        }
    }

    // ===== Actions =====

    private async Task NewFolderAsync()
    {
        var result = await FilesApi.PostAsync("api/files/folders", new FileFolderRequest { ParentId = _folderId, Name = "Nouveau dossier" });
        if (!Report(result, null) || result.Id is null) return;
        Select(result.Id);
        StartRename();
    }

    private async Task NewTextAsync()
    {
        var result = await FilesApi.UploadAsync(_folderId, "Nouveau document texte.txt", []);
        if (!Report(result, null) || result.Id is null) return;
        Select(result.Id);
        StartRename();
    }

    /// <summary>Import depuis le poste (sélecteur de fichiers de la vue).</summary>
    public async Task ImportAsync(string name, byte[] data)
        => Report(await FilesApi.UploadAsync(_folderId, name, data), $"« {name} » importé.");

    /// <summary>Contenu du fichier sélectionné (téléchargement sur le poste).</summary>
    public async Task<(string Name, byte[] Data)?> ExportAsync()
    {
        if (Selected is not { IsFolder: false } entry) return null;
        try
        {
            return (entry.Name, await FilesApi.DownloadAsync(entry.Id));
        }
        catch (Exception ex)
        {
            Show("Téléchargement impossible : " + ex.Message, true);
            return null;
        }
    }

    private void StartRename()
    {
        if (Selected is not { } entry) return;
        RenameText = entry.Name;
        IsRenaming = true;
    }

    private async Task ConfirmRenameAsync()
    {
        if (_selectedId is not { } id) return;
        IsRenaming = false;
        if (Selected?.Name == RenameText) return;
        Report(await FilesApi.PostAsync($"api/files/{id}/rename", new FileRenameRequest { Name = RenameText }), null);
    }

    private async Task DeleteAsync()
    {
        if (Selected is not { } entry) return;
        Report(await FilesApi.PostAsync($"api/files/{entry.Id}/delete"), $"« {entry.Name} » déplacé dans la corbeille.");
    }

    private async Task MoveAsync()
    {
        if (Selected is not { } entry || MoveTarget is not { } target) return;
        Report(await FilesApi.PostAsync($"api/files/{entry.Id}/move", new FileMoveRequest { ParentId = target.Id }),
            $"« {entry.Name} » déplacé vers {target.Name}.");
    }

    private bool Report(OdeResult result, string? success)
    {
        if (!result.Ok) Show(result.Error ?? "Action refusée.", true);
        else if (success is not null) Show(success, false);
        else Message = null;
        return result.Ok;
    }

    public void Show(string text, bool error)
    {
        MessageIsError = error;
        Message = text;
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }

    public void Dispose()
    {
        if (Store is { } store) store.Changed -= Build;
    }
}
