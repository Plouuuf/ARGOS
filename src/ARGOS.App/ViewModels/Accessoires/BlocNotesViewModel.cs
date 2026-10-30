using System.Collections.ObjectModel;
using System.Text;
using Argos.Services;
using Argos.Shared.Contracts;

namespace Argos.ViewModels.Accessoires;

public sealed record FileChoice(string Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Bloc-notes : fichiers texte de « Mes documents ». Nouveau, Ouvrir, Enregistrer, Enregistrer sous,
/// retour automatique à la ligne, zoom, heure/date (F5), position du curseur.
/// </summary>
public sealed class BlocNotesViewModel : ViewModelBase, ICloseGuard
{
    private readonly DesktopViewModel _desktop;
    private readonly string _key;
    private string? _fileId;
    private string _fileName = "Sans titre";
    private string _text = string.Empty;
    private bool _dirty;
    private bool _wordWrap = true;
    private double _fontSize = 14;
    private int _caret;
    private string? _status;
    private bool _statusIsError;
    private bool _showSaveAs, _showOpen, _confirmDiscard;
    private string _saveAsName = "Sans titre.txt";
    private FolderNode? _saveAsFolder;
    private FileChoice? _openChoice;
    private Action? _afterDiscard;

    public BlocNotesViewModel(DesktopViewModel desktop, string key, string? fileId)
    {
        _desktop = desktop;
        _key = key;
        NewCommand = new RelayCommand(() => Guard(Reset));
        OpenCommand = new RelayCommand(() => Guard(ShowOpenPanel));
        SaveCommand = new RelayCommand(() => _ = SaveAsync());
        SaveAsCommand = new RelayCommand(ShowSaveAsPanel);
        ConfirmSaveAsCommand = new RelayCommand(() => _ = SaveAsAsync());
        ConfirmOpenCommand = new RelayCommand(() => { if (OpenChoice is { } c) { ShowOpen = false; _ = LoadAsync(c.Id); } });
        CancelPanelCommand = new RelayCommand(() => { ShowOpen = false; ShowSaveAs = false; ConfirmDiscard = false; });
        DiscardCommand = new RelayCommand(() => { ConfirmDiscard = false; _dirty = false; _afterDiscard?.Invoke(); });
        SaveThenCommand = new RelayCommand(() => _ = SaveThenAsync());
        ZoomInCommand = new RelayCommand(() => FontSize = Math.Min(48, FontSize + 2));
        ZoomOutCommand = new RelayCommand(() => FontSize = Math.Max(8, FontSize - 2));
        ZoomResetCommand = new RelayCommand(() => FontSize = 14);
        CloseCommand = new RelayCommand(() => Guard(() => _desktop.CloseWindow(_key)));
        if (fileId is not null) _ = LoadAsync(fileId);
    }

    public string Text
    {
        get => _text;
        set
        {
            if (!SetProperty(ref _text, value ?? string.Empty)) return;
            if (!_dirty) { _dirty = true; OnPropertyChanged(nameof(Title)); }
            OnPropertyChanged(nameof(Position));
            OnPropertyChanged(nameof(Count));
        }
    }

    public string Title => (_dirty ? "*" : string.Empty) + _fileName + " - Bloc-notes";
    public string? FileId => _fileId;
    public bool IsDirty => _dirty;
    public bool WordWrap { get => _wordWrap; set { if (SetProperty(ref _wordWrap, value)) OnPropertyChanged(nameof(Wrapping)); } }
    public Avalonia.Media.TextWrapping Wrapping => WordWrap ? Avalonia.Media.TextWrapping.Wrap : Avalonia.Media.TextWrapping.NoWrap;
    public double FontSize { get => _fontSize; set { if (SetProperty(ref _fontSize, value)) OnPropertyChanged(nameof(Zoom)); } }
    public string Zoom => $"{Math.Round(FontSize / 14 * 100)} %";

    /// <summary>Position du curseur (liée au TextBox).</summary>
    public int Caret { get => _caret; set { if (SetProperty(ref _caret, value)) OnPropertyChanged(nameof(Position)); } }

    public string Position
    {
        get
        {
            var upTo = _text[..Math.Clamp(_caret, 0, _text.Length)];
            var line = upTo.Count(c => c == '\n') + 1;
            var col = upTo.Length - (upTo.LastIndexOf('\n') + 1) + 1;
            return $"Ln {line}, Col {col}";
        }
    }

    public string Count => $"{_text.Length} caractère(s)";

    public string? Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool StatusIsError { get => _statusIsError; private set => SetProperty(ref _statusIsError, value); }

    // Panneaux « Ouvrir », « Enregistrer sous », « Enregistrer les modifications ? »
    public bool ShowOpen { get => _showOpen; set => SetProperty(ref _showOpen, value); }
    public bool ShowSaveAs { get => _showSaveAs; set => SetProperty(ref _showSaveAs, value); }
    public bool ConfirmDiscard { get => _confirmDiscard; set => SetProperty(ref _confirmDiscard, value); }
    public string SaveAsName { get => _saveAsName; set => SetProperty(ref _saveAsName, value ?? string.Empty); }
    public FolderNode? SaveAsFolder { get => _saveAsFolder; set => SetProperty(ref _saveAsFolder, value); }
    public FileChoice? OpenChoice { get => _openChoice; set => SetProperty(ref _openChoice, value); }
    public ObservableCollection<FolderNode> Folders { get; } = [];
    public ObservableCollection<FileChoice> TextFiles { get; } = [];

    public RelayCommand NewCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand SaveAsCommand { get; }
    public RelayCommand ConfirmSaveAsCommand { get; }
    public RelayCommand ConfirmOpenCommand { get; }
    public RelayCommand CancelPanelCommand { get; }
    public RelayCommand DiscardCommand { get; }
    public RelayCommand SaveThenCommand { get; }
    public RelayCommand ZoomInCommand { get; }
    public RelayCommand ZoomOutCommand { get; }
    public RelayCommand ZoomResetCommand { get; }
    public RelayCommand CloseCommand { get; }

    /// <summary>Heure/Date (F5) : insère « HH:mm dd/MM/yyyy » au curseur.</summary>
    public void InsertDateTime()
    {
        var stamp = DateTime.Now.ToString("HH:mm dd/MM/yyyy");
        var at = Math.Clamp(_caret, 0, _text.Length);
        Text = _text[..at] + stamp + _text[at..];
        Caret = at + stamp.Length;
    }

    // ===== Fichiers =====

    private async Task LoadAsync(string id)
    {
        var entry = ArgosSession.Current?.Files.Find(id);
        try
        {
            var bytes = await FilesApi.DownloadAsync(id);
            _text = Decode(bytes);
            _fileId = id;
            _fileName = entry?.Name ?? "Document";
            _dirty = false;
            Caret = 0;
            Status = null;
            foreach (var n in new[] { nameof(Text), nameof(Title), nameof(Count), nameof(Position), nameof(FileId) }) OnPropertyChanged(n);
        }
        catch (Exception ex)
        {
            SetStatus("Ouverture impossible : " + ex.Message, true);
        }
    }

    /// <summary>UTF-8 (avec ou sans BOM), sinon Windows-1252 / Latin-1 pour les anciens fichiers.</summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private async Task<bool> SaveAsync()
    {
        if (_fileId is null)
        {
            ShowSaveAsPanel();
            return false;
        }
        var result = await FilesApi.UploadAsync(string.Empty, _fileName, Encoding.UTF8.GetBytes(_text), _fileId);
        if (!result.Ok)
        {
            SetStatus(result.Error ?? "Enregistrement refusé.", true);
            return false;
        }
        _dirty = false;
        OnPropertyChanged(nameof(Title));
        SetStatus($"Enregistré à {DateTime.Now:HH:mm}", false);
        return true;
    }

    private async Task SaveAsAsync()
    {
        var name = FileKinds.CleanName(SaveAsName);
        if (name is null)
        {
            SetStatus("Nom de fichier invalide.", true);
            return;
        }
        if (Path.GetExtension(name).Length == 0) name += ".txt";
        var result = await FilesApi.UploadAsync(SaveAsFolder?.Id ?? string.Empty, name, Encoding.UTF8.GetBytes(_text));
        if (!result.Ok || result.Id is null)
        {
            SetStatus(result.Error ?? "Enregistrement refusé.", true);
            return;
        }
        ShowSaveAs = false;
        _fileId = result.Id;
        _fileName = ArgosSession.Current?.Files.Find(result.Id)?.Name ?? name;
        _dirty = false;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(FileId));
        SetStatus($"Enregistré dans {ArgosSession.Current?.Files.PathOf(SaveAsFolder?.Id) ?? "Ce PC"}", false);
        _afterSave?.Invoke();
        _afterSave = null;
    }

    private Action? _afterSave;

    private async Task SaveThenAsync()
    {
        ConfirmDiscard = false;
        var next = _afterDiscard;
        if (_fileId is null)
        {
            _afterSave = next;
            ShowSaveAsPanel();
            return;
        }
        if (await SaveAsync()) next?.Invoke();
    }

    private void ShowSaveAsPanel()
    {
        var store = ArgosSession.Current?.Files;
        Folders.Clear();
        Folders.Add(new FolderNode(string.Empty, "Ce PC", 0, false, new RelayCommand(() => { })));
        foreach (var f in (store?.Snapshot.Entries ?? []).Where(e => e.IsFolder && !e.Deleted).OrderBy(e => store!.PathOf(e.Id)))
            Folders.Add(new FolderNode(f.Id, store!.PathOf(f.Id).Replace("Ce PC › ", ""), 1, false, new RelayCommand(() => { })));
        var current = _fileId is null ? null : store?.Find(_fileId)?.ParentId;
        SaveAsFolder = Folders.FirstOrDefault(f => f.Id == (current ?? string.Empty) && current is not null)
                       ?? Folders.FirstOrDefault(f => f.Name == "Documents") ?? Folders[0];
        SaveAsName = _fileId is null ? "Sans titre.txt" : _fileName;
        ShowOpen = false;
        ShowSaveAs = true;
    }

    private void ShowOpenPanel()
    {
        var store = ArgosSession.Current?.Files;
        TextFiles.Clear();
        foreach (var f in (store?.Snapshot.Entries ?? []).Where(e => !e.IsFolder && !e.Deleted && FileKinds.IsText(e.Name))
                     .OrderByDescending(e => e.UpdatedAt))
            TextFiles.Add(new FileChoice(f.Id, store!.PathOf(f.ParentId).Replace("Ce PC", "").TrimStart(' ', '›') is { Length: > 0 } dir ? $"{f.Name}  ({dir})" : f.Name));
        OpenChoice = TextFiles.FirstOrDefault();
        ShowSaveAs = false;
        ShowOpen = true;
        if (TextFiles.Count == 0) SetStatus("Aucun fichier texte dans Mes documents.", false);
    }

    private void Reset()
    {
        _fileId = null;
        _fileName = "Sans titre";
        _text = string.Empty;
        _dirty = false;
        Caret = 0;
        Status = null;
        foreach (var n in new[] { nameof(Text), nameof(Title), nameof(Count), nameof(Position), nameof(FileId) }) OnPropertyChanged(n);
    }

    /// <summary>Croix de la fenêtre : même question que « Fichier › Quitter ».</summary>
    public bool TryClose()
    {
        if (!_dirty || (_fileId is null && _text.Length == 0)) return true;
        Guard(() => _desktop.CloseWindow(_key));
        return false;
    }

    /// <summary>Si le texte est modifié : « Voulez-vous enregistrer les modifications ? » avant de continuer.</summary>
    private void Guard(Action next)
    {
        if (!_dirty || (_fileId is null && _text.Length == 0))
        {
            next();
            return;
        }
        _afterDiscard = next;
        ShowOpen = false;
        ShowSaveAs = false;
        ConfirmDiscard = true;
    }

    private void SetStatus(string text, bool error)
    {
        StatusIsError = error;
        Status = text;
    }
}
