using System.Collections.ObjectModel;
using System.Text.Json;
using Argos.Services;
using Argos.Shared.Contracts;
using Argos.ViewModels.Ode;
using Avalonia.Media.Imaging;

namespace Argos.ViewModels.Sigtao;

public sealed record EntryRow(string Kind, string Date, string Title, string Details, string By);

/// <summary>Dossier militaire : identité, affectation, photo, carrière (formations, promotions, entretiens…).</summary>
public sealed class DossierViewModel : ViewModelBase, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly DesktopViewModel _desktop;
    private readonly string _key;
    private string? _id;
    private MilitaryRecord _record = new();
    private bool _loaded;
    private string? _message;
    private bool _messageIsError;
    private Bitmap? _photo;
    private string _entryKind = DossierKinds.Formation;
    private string _entryTitle = string.Empty;
    private string _entryDetails = string.Empty;
    private DateTime? _entryDate = DateTime.Today;

    public DossierViewModel(DesktopViewModel desktop, string? id, string key)
    {
        _desktop = desktop;
        _id = id;
        _key = key;
        SaveCommand = new RelayCommand(() => _ = SaveAsync(), () => CanEdit);
        AddEntryCommand = new RelayCommand(() => _ = AddEntryAsync(), () => CanEdit && _id is not null);
        CloseCommand = new RelayCommand(() => _desktop.CloseWindow(_key));
        RemovePhotoCommand = new RelayCommand(() => SetPhoto(string.Empty), () => CanEdit);
        SigtaoImages.AvatarLoaded += OnAvatar;
        if (ArgosSession.Current is { } session)
        {
            session.Sigtao.Changed += Refresh;
            if (!session.Sigtao.IsLoaded) _ = session.Sigtao.RefreshAsync();
        }
        Refresh();
    }

    public bool CanEdit => ArgosSession.Current?.Sigtao.Snapshot.Rights.ManageCompany == true;
    public bool IsReadOnly => !CanEdit;
    public bool IsNew => _id is null;
    public string Title => IsNew ? "Nouveau dossier militaire" : _record.FullName;
    public MilitaryRecord Record { get => _record; private set => SetProperty(ref _record, value); }
    public Avalonia.Media.Imaging.Bitmap? GradeIcon => ImageLoader.Grade(_record.Grade);

    public IReadOnlyList<string> Grades => BsppGrades.All;
    public IReadOnlyList<string> Statuses => MilitaryStatuses.All;
    public IReadOnlyList<string> Kinds => DossierKinds.All;
    public IReadOnlyList<string> Centres { get; } = [EnginsCatalog.Creteil, EnginsCatalog.MaisonsAlfort, EnginsCatalog.Mass];

    public DateTime? GradeDate { get => Day(_record.GradeDate); set { _record.GradeDate = Offset(value); OnPropertyChanged(); } }
    public DateTime? ArrivalDate
    {
        get => Day(_record.ArrivalDate);
        set { _record.ArrivalDate = Offset(value); OnPropertyChanged(); OnPropertyChanged(nameof(ServiceTime)); }
    }
    public string ServiceTime => CrText.ServiceTime(_record.ArrivalDate, DateTimeOffset.Now) is { Length: > 0 } t ? "Temps de service : " + t : string.Empty;

    public Bitmap? Photo { get => _photo; private set { SetProperty(ref _photo, value); OnPropertyChanged(nameof(HasPhoto)); } }
    public bool HasPhoto => Photo is not null;
    public void SetPhoto(string base64)
    {
        _record.PhotoData = base64;
        Photo = SigtaoImages.Photo(base64, _record.AvatarUrl);
    }

    public ObservableCollection<EntryRow> Entries { get; } = [];
    public bool HasEntries => Entries.Count > 0;
    public string EntryKind { get => _entryKind; set => SetProperty(ref _entryKind, value ?? DossierKinds.Formation); }
    public string EntryTitle { get => _entryTitle; set => SetProperty(ref _entryTitle, value); }
    public string EntryDetails { get => _entryDetails; set => SetProperty(ref _entryDetails, value); }
    public DateTime? EntryDate { get => _entryDate; set => SetProperty(ref _entryDate, value); }
    public string EntryHint => "Promotion : choisir le nouveau grade comme intitulé (« Sergent-chef »). Changement d'affectation : le nouveau CS (« MALF »). Mutation : « Mutation » ou « Départ ».";

    public string? Message
    {
        get => _message;
        private set { if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage)); }
    }
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public bool MessageIsError { get => _messageIsError; private set => SetProperty(ref _messageIsError, value); }

    public RelayCommand SaveCommand { get; }
    public RelayCommand AddEntryCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand RemovePhotoCommand { get; }

    private void Refresh()
    {
        var store = ArgosSession.Current?.Sigtao;
        var record = store?.FindMilitary(_id);
        if (!_loaded && store is { IsLoaded: true } && (record is not null || _id is null))
        {
            _loaded = true;
            Load(record is null ? new MilitaryRecord { ArrivalDate = DateTimeOffset.Now } : Clone(record));
        }
        else if (record is not null)
        {
            _record.Entries = record.Entries;
            BuildEntries();
        }
        foreach (var n in new[] { nameof(CanEdit), nameof(IsReadOnly) }) OnPropertyChanged(n);
        foreach (var c in new[] { SaveCommand, AddEntryCommand, RemovePhotoCommand }) c.RaiseCanExecuteChanged();
    }

    private void Load(MilitaryRecord record)
    {
        Record = record;
        Photo = SigtaoImages.Photo(record.PhotoData, record.AvatarUrl);
        foreach (var n in new[] { nameof(GradeDate), nameof(ArrivalDate), nameof(ServiceTime), nameof(Title), nameof(IsNew), nameof(GradeIcon) })
            OnPropertyChanged(n);
        BuildEntries();
    }

    private void BuildEntries()
    {
        Entries.Clear();
        foreach (var e in _record.Entries.OrderByDescending(e => e.Date))
            Entries.Add(new EntryRow(e.Kind, e.Date.ToLocalTime().ToString("dd/MM/yyyy"), e.Title, e.Details, e.By));
        OnPropertyChanged(nameof(HasEntries));
    }

    /// <param name="draft">Vrai : brouillon (sans numéro, modifiable plus tard) ; faux : validation et transmission.</param>
    private async Task SaveAsync(bool draft)
    {
        var session = ArgosSession.Current;
        if (session is null || _busy) return;
        _busy = true;
        try
        {
            if (!draft && string.IsNullOrWhiteSpace(_cr.Synthese)) _cr.Synthese = CrText.Synthese(_cr);
            _cr.IsDraft = draft;
            var result = await session.Api.SendAsync(HttpMethod.Post, "api/sigtao/reports", _cr);
            if (!result.Ok)
            {
                if (!draft && !string.IsNullOrEmpty(_cr.Id)) _cr.IsDraft = true;
                ShowMessage(result.Error ?? "Enregistrement refusé.", true);
                return;
            }
            await session.Sigtao.RefreshAsync();
            if (result.Id is { } id && session.Sigtao.FindReport(id) is { } saved)
            {
                _cr = saved;
                IsReadOnly = !saved.IsDraft;
                foreach (var n in new[] { nameof(Report), nameof(IsReadOnly), nameof(IsEditable), nameof(IsDraft), nameof(Title), nameof(NumLine),
                                          nameof(Body), nameof(Synthese) })
                    OnPropertyChanged(n);
                SaveCommand.RaiseCanExecuteChanged();
                SaveDraftCommand.RaiseCanExecuteChanged();
                DeleteDraftCommand.RaiseCanExecuteChanged();
                GenerateCommand.RaiseCanExecuteChanged();
            }
            ShowMessage(draft
                ? $"Brouillon enregistré à {DateTime.Now:HH:mm} : il reste modifiable depuis la liste des comptes-rendus."
                : $"Compte-rendu N° {_cr.NumDoc} enregistré.", false);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task DeleteDraftAsync()
    {
        var session = ArgosSession.Current;
        if (session is null || _busy || string.IsNullOrEmpty(_cr.Id)) return;
        _busy = true;
        try
        {
            var result = await session.Api.SendAsync(HttpMethod.Post, $"api/sigtao/reports/{_cr.Id}/delete");
            if (!result.Ok)
            {
                ShowMessage(result.Error ?? "Suppression refusée.", true);
                return;
            }
            await session.Sigtao.RefreshAsync();
            _desktop.CloseWindow(_key);
        }
        finally
        {
            _busy = false;
        }
    }

    private void ShowMessage(string text, bool error)
    {
        MessageIsError = error;
        Message = text;
    }

    private static DateTime? Day(DateTimeOffset? d) => d?.ToLocalTime().Date;
    private static DateTimeOffset? Offset(DateTime? d) => d is { } x ? new DateTimeOffset(x.Date, TimeZoneInfo.Local.GetUtcOffset(x.Date)) : null;
    private static MilitaryRecord Clone(MilitaryRecord r)
        => JsonSerializer.Deserialize<MilitaryRecord>(JsonSerializer.Serialize(r, Json), Json) ?? new MilitaryRecord();

    private void OnAvatar() => Photo = SigtaoImages.Photo(_record.PhotoData, _record.AvatarUrl);

    public void Dispose()
    {
        SigtaoImages.AvatarLoaded -= OnAvatar;
        if (ArgosSession.Current is { } session) session.Sigtao.Changed -= Refresh;
    }
}

/// <summary>
/// E-COMPTE-RENDU : rédaction (rédacteur, destinataire, objet, référence, texte mis en forme, pièces jointes,
/// synthèse) puis consultation en lecture seule une fois enregistré.
/// </summary>
public sealed class CompteRenduViewModel : ViewModelBase
{
    private readonly DesktopViewModel _desktop;
    private readonly string _key;
    private CompteRendu _cr;
    private string? _message;
    private bool _messageIsError;
    private bool _busy;

    public CompteRenduViewModel(DesktopViewModel desktop, string? id, string key)
    {
        _desktop = desktop;
        _key = key;
        var store = ArgosSession.Current?.Sigtao;
        var existing = id is null ? null : store?.FindReport(id);
        if (existing is not null)
        {
            _cr = existing;
            // Un brouillon (visible de son seul rédacteur) reste modifiable jusqu'à sa transmission.
            IsReadOnly = !existing.IsDraft;
        }
        else
        {
            // Rédacteur pré-rempli depuis le dossier militaire du connecté.
            var me = store?.Me;
            _cr = new CompteRendu
            {
                Date = DateTimeOffset.Now,
                AuthorGrade = me?.Grade ?? string.Empty,
                AuthorNom = me?.Nom ?? string.Empty,
                AuthorPrenom = me?.Prenom ?? string.Empty,
                AuthorNid = me?.Nid ?? string.Empty,
                AuthorTempsService = me is null ? string.Empty : CrText.ServiceTime(me.ArrivalDate, DateTimeOffset.Now),
                Unite = me?.Unite ?? "17e Cie"
            };
        }
        SaveCommand = new RelayCommand(() => _ = SaveAsync(draft: false), () => !IsReadOnly);
        SaveDraftCommand = new RelayCommand(() => _ = SaveAsync(draft: true), () => !IsReadOnly);
        DeleteDraftCommand = new RelayCommand(() => _ = DeleteDraftAsync(), () => !IsReadOnly && IsDraft);
        GenerateCommand = new RelayCommand(() => { _cr.Synthese = CrText.Synthese(_cr); OnPropertyChanged(nameof(Synthese)); }, () => !IsReadOnly);
        CloseCommand = new RelayCommand(() => _desktop.CloseWindow(_key));
        RemoveAttachmentCommand = new RelayCommand(p =>
        {
            if (p is CrAttachment a && !IsReadOnly) { _cr.Attachments.Remove(a); RefreshAttachments(); }
        });
        RefreshAttachments();
    }

    public CompteRendu Report => _cr;
    public bool IsReadOnly { get; private set; }
    public bool IsEditable => !IsReadOnly;
    public bool IsDraft => _cr.IsDraft && !string.IsNullOrEmpty(_cr.Id);
    public string Title => IsReadOnly ? $"E-COMPTE-RENDU N° {_cr.NumDoc}" : IsDraft ? "Brouillon de compte-rendu" : "Nouveau compte-rendu";
    public IReadOnlyList<string> Grades => BsppGrades.All;
    public IReadOnlyList<string> Mentions => CrMentions.All;
    public string DateLine => $"A {_cr.Lieu}, le {_cr.Date.ToLocalTime().ToString("d MMMM yyyy", OdeFormat.French)}";
    public string NumLine => IsReadOnly ? $"L Nacre  N°{_cr.NumDoc}" : "L Nacre  N° (attribué à l'enregistrement)";

    public string Body
    {
        get => _cr.Body;
        set { _cr.Body = value ?? string.Empty; OnPropertyChanged(); }
    }

    public string Synthese { get => _cr.Synthese; set { _cr.Synthese = value ?? string.Empty; OnPropertyChanged(); } }

    public ObservableCollection<CrAttachment> Attachments { get; } = [];
    public bool HasAttachments => Attachments.Count > 0;

    public void AddAttachment(string name, string contentType, byte[] data)
    {
        if (IsReadOnly) return;
        if (data.Length > 3_000_000)
        {
            ShowMessage($"{name} : fichier trop lourd (3 Mo maximum).", true);
            return;
        }
        _cr.Attachments.Add(new CrAttachment { Name = name, ContentType = contentType, Data = Convert.ToBase64String(data), Size = data.Length });
        RefreshAttachments();
    }

    private void RefreshAttachments()
    {
        Attachments.Clear();
        foreach (var a in _cr.Attachments) Attachments.Add(a);
        OnPropertyChanged(nameof(HasAttachments));
    }

    public string? Message
    {
        get => _message;
        private set { if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage)); }
    }
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public bool MessageIsError { get => _messageIsError; private set => SetProperty(ref _messageIsError, value); }

    public RelayCommand SaveCommand { get; }
    public RelayCommand SaveDraftCommand { get; }
    public RelayCommand DeleteDraftCommand { get; }
    public RelayCommand GenerateCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand RemoveAttachmentCommand { get; }

    private async Task SaveAsync()
    {
        var session = ArgosSession.Current;
        if (session is null || _busy) return;
        _busy = true;
        try
        {
            if (string.IsNullOrWhiteSpace(_cr.Synthese)) _cr.Synthese = CrText.Synthese(_cr);
            var result = await session.Api.SendAsync(HttpMethod.Post, "api/sigtao/reports", _cr);
            if (!result.Ok)
            {
                ShowMessage(result.Error ?? "Enregistrement refusé.", true);
                return;
            }
            await session.Sigtao.RefreshAsync();
            if (result.Id is { } id && session.Sigtao.FindReport(id) is { } saved)
            {
                _cr = saved;
                IsReadOnly = true;
                foreach (var n in new[] { nameof(Report), nameof(IsReadOnly), nameof(IsEditable), nameof(Title), nameof(NumLine),
                                          nameof(Body), nameof(Synthese) })
                    OnPropertyChanged(n);
                SaveCommand.RaiseCanExecuteChanged();
                GenerateCommand.RaiseCanExecuteChanged();
            }
            ShowMessage($"Compte-rendu N° {_cr.NumDoc} enregistré.", false);
        }
        finally
        {
            _busy = false;
        }
    }

    private void ShowMessage(string text, bool error)
    {
        MessageIsError = error;
        Message = text;
    }
}
