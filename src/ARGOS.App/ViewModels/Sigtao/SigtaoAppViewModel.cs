using System.Collections.ObjectModel;
using Argos.Services;
using Argos.Shared.Contracts;
using Argos.ViewModels.Ode;
using Argos.Views.Apps.Sigtao;
using Avalonia.Media.Imaging;

namespace Argos.ViewModels.Sigtao;

public sealed record MilitaryRow(string Id, string Grade, string Nom, string Prenom, string Nid, string Unite, string Affectation,
    string Fonction, string Status, string Email, string Telephone, Bitmap? Photo, RelayCommand OpenCommand)
{
    public Bitmap? GradeIcon => ImageLoader.Grade(Grade);
    public bool HasPhoto => Photo is not null;
    public string Initials => $"{(Nom.Length > 0 ? Nom[0] : ' ')}{(Prenom.Length > 0 ? Prenom[0] : ' ')}".Trim().ToUpperInvariant();
}

public sealed record ReportRow(string NumDoc, string Date, string Author, string Destinataire, string Objet, string Mention,
    int Attachments, RelayCommand OpenCommand)
{
    public bool HasAttachments => Attachments > 0;
}

public sealed record AccountRow(string Id, string Name, string Nid, string Discord, string Email, string Habilitations,
    bool Active, string Password, RelayCommand EditCommand);

public sealed record NoticeRow(string Title, string Text, bool Important);

/// <summary>Case d'habilitation dans l'édition d'un compte.</summary>
public sealed class HabilitationChoice(string code, string label, bool selected) : ViewModelBase
{
    private bool _selected = selected;
    public string Code { get; } = code;
    public string Label { get; } = label;
    public bool Selected { get => _selected; set => SetProperty(ref _selected, value); }
}

/// <summary>
/// SIGTAO : tableau de bord, comptes-rendus, gestion de la compagnie, comptes et habilitations,
/// annuaire, historique administratif.
/// </summary>
public sealed class SigtaoAppViewModel : ViewModelBase, IDisposable
{
    public enum Page { Accueil, ComptesRendus, Compagnie, Comptes, Annuaire, Historique }

    private readonly DesktopViewModel _desktop;
    private Page _page = Page.Accueil;
    private string? _message;
    private bool _messageIsError;

    private string _reportSearch = string.Empty;
    private string _militarySearch = string.Empty;
    private string _statusFilter = "Tous";
    private string _directorySearch = string.Empty;
    private string _historySearch = string.Empty;
    private string _historyFilter = "Toutes";
    private List<JournalEntry> _history = [];

    // Édition d'un compte
    private bool _accountVisible;
    private string? _accountId;
    private string _accountDiscord = string.Empty;
    private string _accountEmail = string.Empty;
    private bool _accountActive = true;
    private MilitaryChoice? _accountDossier;
    private string? _temporaryPassword;
    private bool _importVisible;
    private string _importText = string.Empty;

    public SigtaoAppViewModel(DesktopViewModel desktop)
    {
        _desktop = desktop;
        NavigateCommand = new RelayCommand(p =>
        {
            if (p is string s && Enum.TryParse<Page>(s, out var page)) CurrentPage = page;
        });
        NewReportCommand = new RelayCommand(() => _desktop.OpenWindow(SigtaoWindows.Report(_desktop, null)));
        NewMilitaryCommand = new RelayCommand(() => _desktop.OpenWindow(SigtaoWindows.Dossier(_desktop, null)), () => Rights.ManageCompany);
        MyDossierCommand = new RelayCommand(() =>
        {
            if (Store?.Me is { } me) _desktop.OpenWindow(SigtaoWindows.Dossier(_desktop, me.Id));
        });
        NewAccountCommand = new RelayCommand(() => EditAccount(null), () => Rights.ManageAccounts);
        SaveAccountCommand = new RelayCommand(() => _ = SaveAccountAsync(), () => Rights.ManageAccounts);
        CancelAccountCommand = new RelayCommand(() => AccountVisible = false);
        ResetAccessCommand = new RelayCommand(() => _ = ResetAccessAsync(), () => Rights.ManageAccounts && _accountId is not null);
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync());
        ToggleImportCommand = new RelayCommand(() => ImportVisible = !ImportVisible, () => Rights.ManageCompany);
        ImportCommand = new RelayCommand(() => _ = ImportAsync(), () => Rights.ManageCompany);

        SigtaoImages.AvatarLoaded += Build;
        if (ArgosSession.Current is { } session)
        {
            session.Sigtao.Changed += Build;
            if (!session.Sigtao.IsLoaded) _ = session.Sigtao.RefreshAsync();
        }
        Build();
    }

    private SigtaoStore? Store => ArgosSession.Current?.Sigtao;
    private SigtaoSnapshot Snapshot => Store?.Snapshot ?? new SigtaoSnapshot();
    public SigtaoRights Rights => Snapshot.Rights;

    // ===== Navigation =====

    public Page CurrentPage
    {
        get => _page;
        set
        {
            if (!SetProperty(ref _page, value)) return;
            foreach (var p in Enum.GetNames<Page>()) OnPropertyChanged("Is" + p);
            Message = null;
            if (value == Page.Historique) _ = LoadHistoryAsync();
        }
    }

    public bool IsAccueil => CurrentPage == Page.Accueil;
    public bool IsComptesRendus => CurrentPage == Page.ComptesRendus;
    public bool IsCompagnie => CurrentPage == Page.Compagnie;
    public bool IsComptes => CurrentPage == Page.Comptes;
    public bool IsAnnuaire => CurrentPage == Page.Annuaire;
    public bool IsHistorique => CurrentPage == Page.Historique;

    public RelayCommand NavigateCommand { get; }
    public RelayCommand NewReportCommand { get; }
    public RelayCommand NewMilitaryCommand { get; }
    public RelayCommand MyDossierCommand { get; }
    public RelayCommand NewAccountCommand { get; }
    public RelayCommand SaveAccountCommand { get; }
    public RelayCommand CancelAccountCommand { get; }
    public RelayCommand ResetAccessCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ToggleImportCommand { get; }
    public RelayCommand ImportCommand { get; }
    public bool ImportVisible { get => _importVisible; set => SetProperty(ref _importVisible, value); }
    public string ImportText { get => _importText; set => SetProperty(ref _importText, value); }

    public string? Message
    {
        get => _message;
        private set { if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage)); }
    }
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public bool MessageIsError { get => _messageIsError; private set => SetProperty(ref _messageIsError, value); }
    public string Today => DateTime.Now.ToString("dddd d MMMM yyyy", OdeFormat.French);

    // ===== Accueil =====

    public bool HasMe => Store?.Me is not null;
    public string MeName => Store?.Me is { } m ? $"{m.Nom.ToUpperInvariant()} {m.Prenom}" : ArgosSession.Current?.User.DisplayName ?? string.Empty;
    public string MeGrade => Store?.Me?.Grade ?? "Dossier militaire non renseigné";
    public string MeNid => Store?.Me is { Nid.Length: > 0 } m ? "NID " + m.Nid : "NID —";
    public string MeUnit => Store?.Me is { } m ? $"{m.Unite} · CS {m.Affectation}{(m.Section.Length > 0 ? " · " + m.Section : string.Empty)}" : "—";
    public string MeFonction => Store?.Me is { Fonction.Length: > 0 } m ? m.Fonction : "—";
    public string MeService => Store?.Me is { } m ? CrText.ServiceTime(m.ArrivalDate, DateTimeOffset.Now) : string.Empty;
    public Bitmap? MePhoto => Store?.Me is { } m ? SigtaoImages.Photo(m.PhotoData, m.AvatarUrl) : null;
    public bool HasMePhoto => MePhoto is not null;
    public Bitmap? MeGradeIcon => ImageLoader.Grade(Store?.Me?.Grade);
    public string MeHabilitations => Rights.Habilitations.Count == 0 ? "—" : string.Join("  ·  ", Rights.Habilitations);
    public ObservableCollection<ReportRow> LatestReports { get; } = [];
    public ObservableCollection<NoticeRow> Notices { get; } = [];

    // ===== Comptes-rendus =====

    public string ReportSearch { get => _reportSearch; set { if (SetProperty(ref _reportSearch, value)) BuildReports(); } }
    public ObservableCollection<ReportRow> Reports { get; } = [];
    public string ReportsScope => Rights.ReadAllReports ? "Tous les comptes-rendus (habilitation administrateur)" : "Mes comptes-rendus";

    // ===== Compagnie =====

    public string MilitarySearch { get => _militarySearch; set { if (SetProperty(ref _militarySearch, value)) BuildMilitary(); } }
    public string StatusFilter { get => _statusFilter; set { if (SetProperty(ref _statusFilter, value ?? "Tous")) BuildMilitary(); } }
    public IReadOnlyList<string> StatusFilters { get; } = ["Tous", .. MilitaryStatuses.All];
    public ObservableCollection<MilitaryRow> Military { get; } = [];
    public string MilitaryCount => $"{Military.Count} militaire(s)";

    // ===== Comptes =====

    public ObservableCollection<AccountRow> Accounts { get; } = [];
    public bool AccountVisible { get => _accountVisible; set => SetProperty(ref _accountVisible, value); }
    public string AccountTitle => _accountId is null ? "Nouveau compte utilisateur" : "Modifier le compte";
    /// <summary>Identifiant Discord : s'il correspond à un dossier, ce dossier est sélectionné.</summary>
    public string AccountDiscord
    {
        get => _accountDiscord;
        set
        {
            if (!SetProperty(ref _accountDiscord, value)) return;
            var id = (value ?? string.Empty).Trim();
            if (id.Length > 0 && AccountDossier is null
                && Snapshot.Military.FirstOrDefault(m => m.DiscordUserId == id) is { } dossier)
                AccountDossier = DossierChoices.FirstOrDefault(c => c.Id == dossier.Id);
        }
    }
    public string AccountEmail { get => _accountEmail; set => SetProperty(ref _accountEmail, value); }
    public bool AccountActive { get => _accountActive; set => SetProperty(ref _accountActive, value); }
    public ObservableCollection<MilitaryChoice> DossierChoices { get; } = [];
    /// <summary>Dossier choisi : son identifiant Discord et son adresse électronique remplissent le compte.</summary>
    public MilitaryChoice? AccountDossier
    {
        get => _accountDossier;
        set
        {
            if (!SetProperty(ref _accountDossier, value) || value is null) return;
            if (Snapshot.Military.FirstOrDefault(m => m.Id == value.Id) is not { } dossier) return;
            if (dossier.DiscordUserId.Length > 0) AccountDiscord = dossier.DiscordUserId;
            if (dossier.Email.Length > 0 && string.IsNullOrWhiteSpace(AccountEmail)) AccountEmail = dossier.Email;
        }
    }
    public ObservableCollection<HabilitationChoice> AccountHabilitations { get; } = [];
    public string? TemporaryPassword
    {
        get => _temporaryPassword;
        private set { if (SetProperty(ref _temporaryPassword, value)) OnPropertyChanged(nameof(HasTemporaryPassword)); }
    }
    public bool HasTemporaryPassword => !string.IsNullOrEmpty(TemporaryPassword);

    // ===== Annuaire =====

    public string DirectorySearch { get => _directorySearch; set { if (SetProperty(ref _directorySearch, value)) BuildDirectory(); } }
    public ObservableCollection<MilitaryRow> Directory { get; } = [];

    // ===== Historique =====

    public string HistorySearch { get => _historySearch; set { if (SetProperty(ref _historySearch, value)) BuildHistory(); } }
    public string HistoryFilter { get => _historyFilter; set { if (SetProperty(ref _historyFilter, value ?? "Toutes")) BuildHistory(); } }
    public IReadOnlyList<string> HistoryFilters { get; } = ["Toutes", .. SigtaoJournal.Actions];
    public ObservableCollection<JournalEntry> History { get; } = [];
    public bool CanSeeHistory => Rights.ManageCompany || Rights.ManageAccounts;

    // ===== Construction =====

    private async Task RefreshAsync()
    {
        if (Store is { } store) await store.RefreshAsync();
        if (CurrentPage == Page.Historique) await LoadHistoryAsync();
    }

    private void Build()
    {
        if (Store?.LastError is { } error)
        {
            MessageIsError = true;
            Message = error;
        }
        var s = Snapshot;
        foreach (var n in new[] { nameof(Rights), nameof(HasMe), nameof(MeName), nameof(MeGrade), nameof(MeNid), nameof(MeUnit),
                                  nameof(MeFonction), nameof(MeService), nameof(MePhoto), nameof(HasMePhoto), nameof(MeGradeIcon), nameof(MeHabilitations),
                                  nameof(ReportsScope), nameof(CanSeeHistory) })
            OnPropertyChanged(n);
        foreach (var c in new[] { NewMilitaryCommand, NewAccountCommand, SaveAccountCommand, ResetAccessCommand, ToggleImportCommand, ImportCommand })
            c.RaiseCanExecuteChanged();

        Fill(LatestReports, s.Reports.OrderByDescending(r => r.CreatedAt).Take(6).Select(RowOf));

        var notices = new List<NoticeRow>();
        var me = Store?.Me;
        if (me is null)
            notices.Add(new NoticeRow("Dossier militaire", "Aucun dossier n'est associé à ton compte Discord : demande sa création à la RH de la compagnie.", true));
        else
        {
            var lastSemestriel = me.Entries.Where(e => e.Kind == DossierKinds.EntretienSemestriel).Select(e => e.Date).DefaultIfEmpty().Max();
            if (lastSemestriel == default || lastSemestriel < DateTimeOffset.Now.AddMonths(-6))
                notices.Add(new NoticeRow("Entretien semestriel", "Aucun entretien semestriel depuis plus de 6 mois.", true));
            foreach (var e in me.Entries.OrderByDescending(e => e.RecordedAt).Take(3))
                notices.Add(new NoticeRow(e.Kind, $"{e.Title} - {e.Date.ToLocalTime():dd/MM/yyyy}", false));
        }
        if (Rights.ManageCompany)
        {
            var arrivals = s.Military.Count(m => m.ArrivalDate is { } d && d > DateTimeOffset.Now.AddDays(-30) && m.Status == MilitaryStatuses.Actif);
            if (arrivals > 0) notices.Add(new NoticeRow("Compagnie", $"{arrivals} arrivée(s) ces 30 derniers jours : entretiens d'intégration à prévoir.", false));
        }
        Fill(Notices, notices);

        var choices = s.Military.Select(m => new MilitaryChoice(m.Id, m.FullName)).ToList();
        if (!choices.SequenceEqual(DossierChoices))
        {
            var keep = AccountDossier?.Id;
            Fill(DossierChoices, choices);
            AccountDossier = DossierChoices.FirstOrDefault(c => c.Id == keep);
        }

        BuildReports();
        BuildMilitary();
        BuildAccounts();
        BuildDirectory();
    }

    private ReportRow RowOf(CompteRendu r)
    {
        var id = r.Id;
        return new ReportRow(r.NumDoc, r.Date.ToLocalTime().ToString("dd/MM/yyyy"),
            $"{BsppGrades.Short(r.AuthorGrade)} {r.AuthorNom.ToUpperInvariant()} {r.AuthorPrenom}".Trim(),
            $"{BsppGrades.Short(r.DestGrade)} {r.DestNom.ToUpperInvariant()}".Trim(), r.Objet, r.Mention, r.Attachments.Count,
            new RelayCommand(() => _desktop.OpenWindow(SigtaoWindows.Report(_desktop, id))));
    }

    private void BuildReports()
    {
        var q = VdmsMap.Normalize(ReportSearch);
        Fill(Reports, Snapshot.Reports
            .Where(r => q.Length == 0 || VdmsMap.Normalize(string.Join(" ", r.NumDoc, r.AuthorNom, r.AuthorPrenom, r.Objet, r.Reference,
                r.DestNom, r.Date.ToLocalTime().ToString("dd/MM/yyyy"))).Contains(q))
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.CreatedAt)
            .Select(RowOf));
    }

    private MilitaryRow MilitaryRowOf(MilitaryRecord m)
    {
        var id = m.Id;
        return new MilitaryRow(m.Id, m.Grade, m.Nom.Length == 0 ? "(nom à la 1re connexion)" : m.Nom.ToUpperInvariant(), m.Prenom, OdeFormat.OrDash(m.Nid), m.Unite, m.Affectation,
            OdeFormat.OrDash(m.Fonction), m.Status, OdeFormat.OrDash(m.Email), OdeFormat.OrDash(m.Telephone),
            SigtaoImages.Photo(m.PhotoData, m.AvatarUrl),
            new RelayCommand(() => _desktop.OpenWindow(SigtaoWindows.Dossier(_desktop, id))));
    }

    private void BuildMilitary()
    {
        var q = VdmsMap.Normalize(MilitarySearch);
        Fill(Military, Snapshot.Military
            .Where(m => StatusFilter == "Tous" || m.Status == StatusFilter)
            .Where(m => q.Length == 0 || VdmsMap.Normalize(string.Join(" ", m.Nom, m.Prenom, m.Grade, BsppGrades.Short(m.Grade), m.Nid,
                m.Unite, m.Affectation, m.Section, m.Fonction)).Contains(q))
            .Select(MilitaryRowOf));
        OnPropertyChanged(nameof(MilitaryCount));
    }

    private void BuildDirectory()
    {
        var q = VdmsMap.Normalize(DirectorySearch);
        Fill(Directory, Snapshot.Military
            .Where(m => m.Status == MilitaryStatuses.Actif)
            .Where(m => q.Length == 0 || VdmsMap.Normalize(string.Join(" ", m.Nom, m.Prenom, m.Grade, BsppGrades.Short(m.Grade), m.Nid)).Contains(q))
            .Select(MilitaryRowOf));
    }

    private void BuildAccounts()
    {
        Fill(Accounts, Snapshot.Accounts.OrderBy(a => a.Nom).Select(a =>
        {
            var id = a.Id;
            return new AccountRow(a.Id, $"{BsppGrades.Short(a.Grade)} {a.Nom.ToUpperInvariant()} {a.Prenom}".Trim(), OdeFormat.OrDash(a.Nid),
                a.DiscordUserId, OdeFormat.OrDash(a.Email), a.Habilitations.Count == 0 ? "—" : string.Join(", ", a.Habilitations),
                a.Active, a.HasPassword ? "Défini" : "À initialiser", new RelayCommand(() => EditAccount(id)));
        }));
    }

    // ===== Comptes =====

    private void EditAccount(string? id)
    {
        var account = id is null ? null : Snapshot.Accounts.FirstOrDefault(a => a.Id == id);
        _accountId = account?.Id;
        AccountDiscord = account?.DiscordUserId ?? string.Empty;
        AccountEmail = account?.Email ?? string.Empty;
        AccountActive = account?.Active ?? true;
        AccountDossier = DossierChoices.FirstOrDefault(c => c.Id == account?.DossierId);
        AccountHabilitations.Clear();
        foreach (var (code, label) in Habilitations.All)
            AccountHabilitations.Add(new HabilitationChoice(code, label, account?.Habilitations.Contains(code) == true));
        TemporaryPassword = null;
        OnPropertyChanged(nameof(AccountTitle));
        ResetAccessCommand.RaiseCanExecuteChanged();
        AccountVisible = true;
    }

    private async Task SaveAccountAsync()
    {
        var session = ArgosSession.Current;
        if (session is null) return;
        var result = await session.Api.SendAsync(HttpMethod.Post, "api/sigtao/accounts", new UserAccount
        {
            Id = _accountId ?? string.Empty,
            DiscordUserId = AccountDiscord,
            DossierId = AccountDossier?.Id ?? string.Empty,
            Email = AccountEmail,
            Active = AccountActive,
            Habilitations = AccountHabilitations.Where(h => h.Selected).Select(h => h.Code).ToList()
        });
        ShowMessage(result.Ok ? "Compte enregistré." : result.Error ?? "Enregistrement refusé.", !result.Ok);
        if (result.Ok)
        {
            _accountId = result.Id;
            OnPropertyChanged(nameof(AccountTitle));
            ResetAccessCommand.RaiseCanExecuteChanged();
        }
        await session.Sigtao.RefreshAsync();
    }

    private async Task ResetAccessAsync()
    {
        var session = ArgosSession.Current;
        if (session is null || _accountId is null) return;
        try
        {
            var result = await session.Api.PostAsync<AccountPasswordResult>($"api/sigtao/accounts/{_accountId}/reset");
            TemporaryPassword = result.TemporaryPassword;
            ShowMessage("Accès réinitialisés : transmets le mot de passe provisoire au militaire (il ne sera plus affiché).", false);
        }
        catch (Exception ex)
        {
            ShowMessage("Réinitialisation refusée : " + ex.Message, true);
        }
        await session.Sigtao.RefreshAsync();
    }

    // ===== Import de l'effectif Discord =====

    private async Task ImportAsync()
    {
        var session = ArgosSession.Current;
        if (session is null) return;
        var result = await session.Api.SendAsync(HttpMethod.Post, "api/sigtao/military/import", new RosterImportRequest { Text = ImportText });
        ShowMessage(result.Ok ? "Effectif importé : " + result.Error : result.Error ?? "Import refusé.", !result.Ok);
        if (result.Ok)
        {
            ImportText = string.Empty;
            ImportVisible = false;
        }
        await session.Sigtao.RefreshAsync();
    }

    // ===== Historique =====

    private async Task LoadHistoryAsync()
    {
        var session = ArgosSession.Current;
        if (session is null) return;
        try
        {
            _history = await session.Api.GetAsync<List<JournalEntry>>("api/sigtao/history");
            BuildHistory();
        }
        catch (Exception ex)
        {
            ShowMessage("Historique indisponible : " + ex.Message, true);
        }
    }

    private void BuildHistory()
    {
        var q = VdmsMap.Normalize(HistorySearch);
        Fill(History, _history
            .Where(j => HistoryFilter == "Toutes" || j.Action == HistoryFilter)
            .Where(j => q.Length == 0 || VdmsMap.Normalize($"{j.UserName} {j.Target} {j.Details}").Contains(q)));
    }

    private void ShowMessage(string text, bool error)
    {
        MessageIsError = error;
        Message = text;
    }

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }

    public void Dispose()
    {
        SigtaoImages.AvatarLoaded -= Build;
        if (ArgosSession.Current is { } session)
            session.Sigtao.Changed -= Build;
    }
}

public sealed record MilitaryChoice(string Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Photos des dossiers : la photo choisie dans SIGTAO, sinon l'avatar Discord du joueur
/// (téléchargé en arrière-plan ; <see cref="AvatarLoaded"/> prévient quand il est prêt).
/// </summary>
public static class SigtaoImages
{
    private static readonly Dictionary<int, Bitmap> Cache = [];
    private static readonly Dictionary<string, Bitmap?> Avatars = [];

    public static event Action? AvatarLoaded;

    public static Bitmap? Photo(string? data, string? avatarUrl = null)
    {
        if (!string.IsNullOrWhiteSpace(data))
        {
            var key = data.GetHashCode(StringComparison.Ordinal);
            if (Cache.TryGetValue(key, out var cached)) return cached;
            try
            {
                using var stream = new MemoryStream(Convert.FromBase64String(data));
                return Cache[key] = new Bitmap(stream);
            }
            catch
            {
                // Photo illisible : on tente l'avatar.
            }
        }
        if (string.IsNullOrWhiteSpace(avatarUrl)) return null;
        if (Avatars.TryGetValue(avatarUrl, out var avatar)) return avatar;
        Avatars[avatarUrl] = null;
        _ = LoadAsync(avatarUrl);
        return null;
    }

    private static async Task LoadAsync(string url)
    {
        var bitmap = await Argos.Services.ImageLoader.AvatarAsync(url);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Avatars[url] = bitmap;
            if (bitmap is not null) AvatarLoaded?.Invoke();
        });
    }
}
