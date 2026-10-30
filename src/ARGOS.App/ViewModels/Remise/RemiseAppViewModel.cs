using System.Collections.ObjectModel;
using Argos.Services;
using Argos.Shared.Contracts;
using Argos.ViewModels.Ode;
using Argos.Views.Apps.Remise;

namespace Argos.ViewModels.Remise;

/// <summary>
/// ADAGIO REMISE : tableau de bord, parc des engins (feuille GDM), inventaire, vérifications,
/// maintenance, stocks, historique. Le statut opérationnel des engins vient d'ADAGIO ODE.
/// </summary>
public sealed class RemiseAppViewModel : ViewModelBase, IDisposable
{
    public enum Page { Dashboard, Parc, Inventaire, Verification, Maintenance, Stocks, Historique }

    public const string Tous = "Tous";
    public const string Engage = "Engagé";

    private readonly DesktopViewModel _desktop;
    private Page _page = Page.Dashboard;
    private string? _message;
    private bool _messageIsError;
    private bool _busy;

    // Parc
    private string _parcSearch = string.Empty;
    private string _centreFilter = Tous;
    private string _statusFilter = Tous;

    // Inventaire
    private string _itemSearch = string.Empty;
    private string _categoryFilter = "Toutes";
    private bool _missingOnly;
    private string? _editItemId;
    private string _editName = string.Empty;
    private string _editCategory = string.Empty;
    private string _editUnit = "unité";
    private string _editStock = "0";
    private string _editThreshold = "0";
    private bool _editActive = true;
    private bool _editVisible;

    // Vérification
    private string? _verifEngin;
    private string _verifObservations = string.Empty;

    // Maintenance
    private bool _openOnly = true;
    private string? _anomalyEngin;
    private string _anomalyDescription = string.Empty;
    private string _anomalyLevel = Immobilisations.Aucune;
    private MaintenanceRecord? _selectedMaintenance;
    private string _repairText = string.Empty;
    private DateTime? _repairNextRevision;
    private bool _returnToService = true;

    // Stocks
    private bool _lowOnly;
    private ItemChoice? _stockItem;
    private string _stockKind = StockKinds.Entree;
    private string _stockQuantity = "1";
    private string? _stockEngin;
    private string _stockComment = string.Empty;

    // Historique
    private List<JournalEntry> _journal = [];
    private string _historySearch = string.Empty;
    private string _historyPeriod = "7 derniers jours";
    private string _historyCategory = "Toutes";

    public RemiseAppViewModel(DesktopViewModel desktop)
    {
        _desktop = desktop;
        NavigateCommand = new RelayCommand(p =>
        {
            if (p is string s && Enum.TryParse<Page>(s, out var page))
                CurrentPage = page;
        });
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync());
        AddEnginCommand = new RelayCommand(() => _desktop.OpenWindow(RemiseWindows.Fiche(_desktop, null)), () => CanManage);
        CentreCommand = new RelayCommand(p => CentreFilter = p as string ?? Tous);

        NewItemCommand = new RelayCommand(() => EditItem(null), () => CanManage);
        SaveItemCommand = new RelayCommand(() => _ = SaveItemAsync(), () => CanManage);
        CancelItemCommand = new RelayCommand(() => EditVisible = false);

        ValidateVerificationCommand = new RelayCommand(() => _ = ValidateVerificationAsync());
        ReportAnomalyCommand = new RelayCommand(() => _ = ReportAnomalyAsync());
        AddRepairCommand = new RelayCommand(() => _ = AddRepairAsync(), () => SelectedMaintenance?.IsOpen == true);
        CloseMaintenanceCommand = new RelayCommand(() => _ = CloseMaintenanceAsync(), () => SelectedMaintenance?.IsOpen == true && CanManage);
        MoveStockCommand = new RelayCommand(() => _ = MoveStockAsync(), () => CanManage);

        if (ArgosSession.Current is { } session)
        {
            session.Remise.Changed += Rebuild;
            session.Ode.Changed += Rebuild;
            if (!session.Remise.IsLoaded)
                _ = session.Remise.RefreshAsync();
            if (!session.Ode.IsLoaded)
                _ = session.Ode.RefreshAsync();
        }
        Rebuild();
    }

    // ===== Navigation et état =====

    public Page CurrentPage
    {
        get => _page;
        set
        {
            if (!SetProperty(ref _page, value)) return;
            foreach (var p in Enum.GetNames<Page>())
                OnPropertyChanged("Is" + p);
            Message = null;
            if (value == Page.Historique)
                _ = LoadJournalAsync();
        }
    }

    public bool IsDashboard => CurrentPage == Page.Dashboard;
    public bool IsParc => CurrentPage == Page.Parc;
    public bool IsInventaire => CurrentPage == Page.Inventaire;
    public bool IsVerification => CurrentPage == Page.Verification;
    public bool IsMaintenance => CurrentPage == Page.Maintenance;
    public bool IsStocks => CurrentPage == Page.Stocks;
    public bool IsHistorique => CurrentPage == Page.Historique;

    public RelayCommand NavigateCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand AddEnginCommand { get; }
    public RelayCommand CentreCommand { get; }

    public string OperatorName => ArgosSession.Current?.OperatorName ?? string.Empty;
    public bool CanManage => Remise.CanManage;
    public string RightsLabel => CanManage ? "Gestion du parc autorisée" : "Consultation, vérifications et signalements";

    public string? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
                OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public bool MessageIsError { get => _messageIsError; private set => SetProperty(ref _messageIsError, value); }
    public bool Busy { get => _busy; private set => SetProperty(ref _busy, value); }

    private RemiseSnapshot Remise => ArgosSession.Current?.Remise.Snapshot ?? new RemiseSnapshot();
    private OdeSnapshot Ode => ArgosSession.Current?.Ode.Snapshot ?? new OdeSnapshot();

    // ===== Tableau de bord =====

    public int TotalEngins { get; private set; }
    public int Disponibles { get; private set; }
    public int Engages { get; private set; }
    public int Indisponibles { get; private set; }
    public int EnMaintenance { get; private set; }
    public int Revisions { get; private set; }
    public int Anomalies { get; private set; }
    public int StockAlerts { get; private set; }

    public ObservableCollection<RevisionRow> NextRevisions { get; } = [];
    public ObservableCollection<AnomalyRow> OpenAnomalies { get; } = [];
    public ObservableCollection<StockAlertRow> StockAlertRows { get; } = [];

    // ===== Parc =====

    public string ParcSearch { get => _parcSearch; set { if (SetProperty(ref _parcSearch, value)) BuildParc(); } }
    public string CentreFilter { get => _centreFilter; set { if (SetProperty(ref _centreFilter, value ?? Tous)) BuildParc(); } }
    public string StatusFilter { get => _statusFilter; set { if (SetProperty(ref _statusFilter, value ?? Tous)) BuildParc(); } }
    public ObservableCollection<string> Centres { get; } = [];
    public IReadOnlyList<string> StatusFilters { get; } =
        [Tous, EnginStatuses.Disponible, Engage, EnginStatuses.Indisponible, EnginStatuses.Maintenance];
    public ObservableCollection<EnginStatusRow> EnginRows { get; } = [];
    public string CentreTitle => CentreFilter == Tous ? "TOUS CENTRES" : CentreFilter;
    public string CentreShort => CentreFilter == Tous ? "TOUS" : CentreFilter;
    public string ParcCount => $"{EnginRows.Count} engin(s)";

    // ===== Inventaire =====

    public string ItemSearch { get => _itemSearch; set { if (SetProperty(ref _itemSearch, value)) BuildItems(); } }
    public string CategoryFilter { get => _categoryFilter; set { if (SetProperty(ref _categoryFilter, value ?? "Toutes")) BuildItems(); } }
    public bool MissingOnly { get => _missingOnly; set { if (SetProperty(ref _missingOnly, value)) BuildItems(); } }
    public ObservableCollection<string> Categories { get; } = [];
    public ObservableCollection<ItemRow> Items { get; } = [];
    public string ItemsCount => $"{Items.Count} référence(s)";

    public bool EditVisible { get => _editVisible; set => SetProperty(ref _editVisible, value); }
    public bool EditIsNew => _editItemId is null;
    public string EditTitle => _editItemId is null ? "Nouvelle référence de matériel" : "Modifier la référence";
    public string EditName { get => _editName; set => SetProperty(ref _editName, value); }
    public string EditCategory { get => _editCategory; set => SetProperty(ref _editCategory, value); }
    public string EditUnit { get => _editUnit; set => SetProperty(ref _editUnit, value); }
    public string EditStock { get => _editStock; set => SetProperty(ref _editStock, value); }
    public string EditThreshold { get => _editThreshold; set => SetProperty(ref _editThreshold, value); }
    public bool EditActive { get => _editActive; set => SetProperty(ref _editActive, value); }
    public RelayCommand NewItemCommand { get; }
    public RelayCommand SaveItemCommand { get; }
    public RelayCommand CancelItemCommand { get; }

    // ===== Vérification =====

    public ObservableCollection<string> EnginNames { get; } = [];
    public string? VerifEngin
    {
        get => _verifEngin;
        set
        {
            if (SetProperty(ref _verifEngin, value))
                LoadVerificationLines();
        }
    }
    public ObservableCollection<VerificationLineViewModel> VerifLines { get; } = [];
    public bool HasVerifLines => VerifLines.Count > 0;
    public string VerifObservations { get => _verifObservations; set => SetProperty(ref _verifObservations, value); }
    public string VerifResponsible => $"{OperatorName} - {DateTime.Now:dd/MM/yyyy HH:mm}";
    public RelayCommand ValidateVerificationCommand { get; }
    public ObservableCollection<VerificationRow> Verifications { get; } = [];

    // ===== Maintenance =====

    public bool OpenOnly { get => _openOnly; set { if (SetProperty(ref _openOnly, value)) BuildMaintenances(); } }
    public ObservableCollection<AnomalyRow> Maintenances { get; } = [];
    public IReadOnlyList<string> Levels => Immobilisations.All;
    public string? AnomalyEngin { get => _anomalyEngin; set => SetProperty(ref _anomalyEngin, value); }
    public string AnomalyDescription { get => _anomalyDescription; set => SetProperty(ref _anomalyDescription, value); }
    public string AnomalyLevel { get => _anomalyLevel; set => SetProperty(ref _anomalyLevel, value ?? Immobilisations.Aucune); }
    public RelayCommand ReportAnomalyCommand { get; }

    public MaintenanceRecord? SelectedMaintenance
    {
        get => _selectedMaintenance;
        private set
        {
            SetProperty(ref _selectedMaintenance, value);
            foreach (var name in new[] { nameof(HasSelection), nameof(SelectionTitle), nameof(SelectionInfo), nameof(SelectionDescription),
                                         nameof(SelectionRepairs), nameof(SelectionIsOpen), nameof(SelectionClosed) })
                OnPropertyChanged(name);
            AddRepairCommand.RaiseCanExecuteChanged();
            CloseMaintenanceCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelection => SelectedMaintenance is not null;
    public bool SelectionIsOpen => SelectedMaintenance?.IsOpen == true;
    public string SelectionTitle => SelectedMaintenance is { } m ? $"{m.Number} - {m.Engin}" : string.Empty;
    public string SelectionInfo => SelectedMaintenance is { } m
        ? $"Signalée le {OdeFormat.Short(m.ReportedAt)} par {m.ReportedBy} ({m.Source}) · {m.Immobilisation}"
        : string.Empty;
    public string SelectionDescription => SelectedMaintenance?.Description ?? string.Empty;
    public string SelectionClosed => SelectedMaintenance is { IsOpen: false } m && m.ClosedAt is { } at
        ? $"Clôturée le {OdeFormat.Short(at)} par {m.ClosedBy}."
        : string.Empty;
    public IReadOnlyList<RepairEntry> SelectionRepairs => SelectedMaintenance?.Repairs ?? [];
    public string RepairText { get => _repairText; set => SetProperty(ref _repairText, value); }
    public DateTime? RepairNextRevision { get => _repairNextRevision; set => SetProperty(ref _repairNextRevision, value); }
    public bool ReturnToService { get => _returnToService; set => SetProperty(ref _returnToService, value); }
    public RelayCommand AddRepairCommand { get; }
    public RelayCommand CloseMaintenanceCommand { get; }

    // ===== Stocks =====

    public bool LowOnly { get => _lowOnly; set { if (SetProperty(ref _lowOnly, value)) BuildItems(); } }
    public ObservableCollection<ItemRow> StockRows { get; } = [];
    public ObservableCollection<ItemChoice> ItemChoices { get; } = [];
    public ItemChoice? StockItem { get => _stockItem; set => SetProperty(ref _stockItem, value); }
    public IReadOnlyList<string> StockKindList => StockKinds.All;
    public string StockKind
    {
        get => _stockKind;
        set
        {
            if (SetProperty(ref _stockKind, value ?? StockKinds.Entree))
                OnPropertyChanged(nameof(StockNeedsEngin));
        }
    }
    public bool StockNeedsEngin => StockKinds.NeedsEngin(StockKind);
    public string StockQuantity { get => _stockQuantity; set => SetProperty(ref _stockQuantity, value); }
    public string? StockEngin { get => _stockEngin; set => SetProperty(ref _stockEngin, value); }
    public string StockComment { get => _stockComment; set => SetProperty(ref _stockComment, value); }
    public RelayCommand MoveStockCommand { get; }
    public ObservableCollection<MovementRow> Movements { get; } = [];

    // ===== Historique =====

    public IReadOnlyList<string> Periods { get; } = ["Aujourd'hui", "7 derniers jours", "30 derniers jours", "Tout"];
    public IReadOnlyList<string> HistoryCategories { get; } =
        ["Toutes", "Engins", "Statuts", "Vérifications", "Maintenances", "Stocks"];
    public string HistorySearch { get => _historySearch; set { if (SetProperty(ref _historySearch, value)) BuildJournal(); } }
    public string HistoryPeriod { get => _historyPeriod; set { if (SetProperty(ref _historyPeriod, value ?? "Tout")) BuildJournal(); } }
    public string HistoryCategory { get => _historyCategory; set { if (SetProperty(ref _historyCategory, value ?? "Toutes")) BuildJournal(); } }
    public ObservableCollection<JournalEntry> History { get; } = [];

    // ===== Construction =====

    private async Task RefreshAsync()
    {
        if (ArgosSession.Current is not { } session) return;
        await Task.WhenAll(session.Remise.RefreshAsync(), session.Ode.RefreshAsync());
        if (CurrentPage == Page.Historique)
            await LoadJournalAsync();
    }

    private EnginState? OdeState(string nom)
        => Ode.Engins.FirstOrDefault(e => string.Equals(e.Nom, nom, StringComparison.OrdinalIgnoreCase));

    /// <summary>Statut affiché : celui de l'ODE (alerté, sur les lieux…), sinon la disponibilité REMISE.</summary>
    public static string StatusOf(FleetEngin engin, EnginState? state)
        => state?.Status ?? engin.Availability switch
        {
            FleetAvailability.Indisponible => EnginStatuses.Indisponible,
            FleetAvailability.Maintenance => EnginStatuses.Maintenance,
            _ => EnginStatuses.Disponible
        };

    private void Rebuild()
    {
        var store = ArgosSession.Current?.Remise;
        if (store?.LastError is { } error)
            ShowMessage(error, true);

        var r = Remise;
        var engins = r.Engins.Where(e => e.Active).ToList();
        var statuses = engins.ToDictionary(e => e.Nom, e => StatusOf(e, OdeState(e.Nom)));

        TotalEngins = engins.Count;
        Disponibles = statuses.Values.Count(EnginStatuses.IsAvailable);
        Engages = statuses.Values.Count(EnginStatuses.IsEngaged);
        Indisponibles = statuses.Values.Count(s => s == EnginStatuses.Indisponible);
        EnMaintenance = statuses.Values.Count(s => s == EnginStatuses.Maintenance);
        var soon = DateTimeOffset.Now.AddDays(30);
        Revisions = engins.Count(e => e.NextRevision is { } d && d <= soon);
        Anomalies = r.Maintenances.Count(m => m.IsOpen);
        StockAlerts = r.Items.Count(i => i.Active && i.Stock < i.AlertThreshold);
        foreach (var name in new[] { nameof(TotalEngins), nameof(Disponibles), nameof(Engages), nameof(Indisponibles),
                                     nameof(EnMaintenance), nameof(Revisions), nameof(Anomalies), nameof(StockAlerts),
                                     nameof(CanManage), nameof(RightsLabel) })
            OnPropertyChanged(name);
        foreach (var c in new[] { AddEnginCommand, NewItemCommand, SaveItemCommand, MoveStockCommand, CloseMaintenanceCommand })
            c.RaiseCanExecuteChanged();

        Fill(NextRevisions, engins.Where(e => e.NextRevision is not null).OrderBy(e => e.NextRevision).Take(8).Select(e =>
        {
            var days = (int)Math.Floor((e.NextRevision!.Value.Date - DateTime.Today).TotalDays);
            var nom = e.Nom;
            return new RevisionRow(e.Nom, e.NextRevision.Value.ToString("dd/MM/yyyy"),
                days < 0 ? $"dépassée de {-days} j" : days == 0 ? "aujourd'hui" : $"dans {days} j", days < 0,
                new RelayCommand(() => OpenFiche(nom)));
        }));
        Fill(OpenAnomalies, r.Maintenances.Where(m => m.IsOpen).OrderByDescending(m => m.ReportedAt).Take(10).Select(AnomalyRowOf));
        Fill(StockAlertRows, r.Items.Where(i => i.Active && i.Stock < i.AlertThreshold).OrderBy(i => i.Name)
            .Select(i => new StockAlertRow(i.Name, i.Category, $"{i.Stock} {i.Unit}", i.AlertThreshold.ToString())));

        // Listes de choix
        Sync(Centres, new[] { Tous }.Concat(engins.Select(e => e.Centre).Distinct().OrderBy(RemiseText.CentreRank)));
        Sync(EnginNames, engins.Select(e => e.Nom));
        Sync(Categories, new[] { "Toutes" }.Concat(r.Items.Select(i => i.Category).Distinct().OrderBy(c => c, StringComparer.Create(OdeFormat.French, true))));
        var choices = r.Items.Where(i => i.Active).OrderBy(i => i.Name, StringComparer.Create(OdeFormat.French, true))
            .Select(i => new ItemChoice(i.Id, i.Name, i.Unit)).ToList();
        if (!choices.SequenceEqual(ItemChoices))
        {
            var keep = StockItem?.Id;
            Fill(ItemChoices, choices);
            StockItem = ItemChoices.FirstOrDefault(c => c.Id == keep);
        }

        BuildParc();
        BuildItems();
        BuildVerifications();
        BuildMaintenances();
        BuildMovements();
        if (VerifEngin is not null && VerifLines.Count == 0)
            LoadVerificationLines();
        if (SelectedMaintenance is { } sel)
            SelectedMaintenance = r.Maintenances.FirstOrDefault(m => m.Id == sel.Id);
    }

    private void BuildParc()
    {
        var q = VdmsMap.Normalize(ParcSearch);
        var rows = Remise.Engins.Where(e => e.Active)
            .Where(e => CentreFilter == Tous || e.Centre == CentreFilter)
            .Where(e => q.Length == 0 || VdmsMap.Normalize($"{e.Nom} {e.Type} {e.Libelle} {e.Immatriculation}").Contains(q, StringComparison.Ordinal))
            .Select(RowOf)
            .Where(r => GdmParcViewModel.Matches(r.Status, StatusFilter));
        Fill(EnginRows, rows);
        OnPropertyChanged(nameof(ParcCount));

        // Engins retirés du parc (case « Engin au parc » décochée) : toujours visibles pour pouvoir les remettre.
        Fill(RetiredRows, Remise.Engins.Where(e => !e.Active)
            .Where(e => CentreFilter == Tous || e.Centre == CentreFilter)
            .Select(e =>
            {
                var nom = e.Nom;
                return new RetiredEnginRow(e.Nom, e.Libelle, e.Centre, new RelayCommand(() => OpenFiche(nom)),
                    new RelayCommand(() => _ = RestoreAsync(nom), () => CanManage));
            }));
        OnPropertyChanged(nameof(HasRetired));
    }

    public ObservableCollection<RetiredEnginRow> RetiredRows { get; } = [];
    public bool HasRetired => RetiredRows.Count > 0;

    /// <summary>Remet au parc un engin retiré : il réapparaît dans REMISE, l'ODE et la feuille de garde.</summary>
    private async Task RestoreAsync(string nom)
    {
        var session = ArgosSession.Current;
        if (session?.Remise.FindEngin(nom) is not { } engin) return;
        var copy = System.Text.Json.JsonSerializer.Deserialize<FleetEngin>(System.Text.Json.JsonSerializer.Serialize(engin))!;
        copy.Active = true;
        var result = await session.Api.SendAsync(HttpMethod.Post, "api/remise/engins", new SaveEnginRequest { OriginalNom = nom, Engin = copy });
        if (!result.Ok)
        {
            _desktop.ShowNotification("ADAGIO REMISE", result.Error ?? "Remise au parc refusée.");
            return;
        }
        await session.Remise.RefreshAsync();
        _ = session.Ode.RefreshAsync();
        _desktop.ShowNotification("ADAGIO REMISE", $"{nom} remis au parc.");
    }

    private EnginStatusRow RowOf(FleetEngin e)
    {
        var state = OdeState(e.Nom);
        var status = StatusOf(e, state);
        var nom = e.Nom;
        var overdue = e.NextRevision is { } d && d.Date < DateTime.Today;
        var remise = e.Availability == FleetAvailability.Disponible
            ? FleetAvailability.Disponible
            : e.Availability + (string.IsNullOrWhiteSpace(e.AvailabilityReason) ? string.Empty : " - " + e.AvailabilityReason);
        return new EnginStatusRow(e.Nom, e.Libelle, e.Centre, status, RemiseColors.ForStatus(status),
            state?.InterventionNumber ?? "—", remise,
            e.NextRevision is { } r ? r.ToLocalTime().ToString("dd/MM/yyyy") : "—",
            overdue ? RemiseColors.Alerte : RemiseColors.Normal,
            OdeFormat.Short(state?.UpdatedAt ?? e.AvailabilitySince),
            new RelayCommand(() => OpenFiche(nom)));
    }

    private void BuildItems()
    {
        var r = Remise;
        var q = ItemSearch.Trim();
        var rows = r.Items.Select(i => ItemRowOf(i, r)).ToList();
        Fill(Items, rows
            .Where(i => CategoryFilter == "Toutes" || i.Category == CategoryFilter)
            .Where(i => q.Length == 0 || Has(i.Name, q) || Has(i.Category, q))
            .Where(i => !MissingOnly || i.Missing > 0)
            .OrderBy(i => i.Category).ThenBy(i => i.Name));
        Fill(StockRows, rows.Where(i => i.Active && (!LowOnly || i.LowStock)).OrderByDescending(i => i.LowStock).ThenBy(i => i.Name));
        OnPropertyChanged(nameof(ItemsCount));
    }

    private ItemRow ItemRowOf(MaterielItem i, RemiseSnapshot r)
    {
        var lines = r.Engins.Where(e => e.Active).SelectMany(e => e.Dotation).Where(d => d.ItemId == i.Id).ToList();
        var id = i.Id;
        return new ItemRow(i.Id, i.Name, i.Category, i.Unit, i.Stock, lines.Sum(d => d.Present), lines.Sum(d => d.Required),
            lines.Sum(d => Math.Max(0, d.Required - d.Present)), i.AlertThreshold, i.Active && i.Stock < i.AlertThreshold, i.Active,
            new RelayCommand(() => EditItem(id)));
    }

    private void BuildVerifications()
        => Fill(Verifications, Remise.Verifications.OrderByDescending(v => v.At).Take(100).Select(v => new VerificationRow(
            OdeFormat.Short(v.At), v.Engin, v.By,
            RemiseText.Result(v),
            v.Conforme,
            string.Join(" ; ", v.Lines.Where(l => l.Result != CheckResults.Present || l.Counted < l.Required)
                .Select(l => $"{l.Name} : {l.Result.ToLowerInvariant()} {l.Counted}/{l.Required}")
                .Append(v.Observations).Where(s => !string.IsNullOrWhiteSpace(s))))));

    private void BuildMaintenances()
        => Fill(Maintenances, Remise.Maintenances.Where(m => !OpenOnly || m.IsOpen)
            .OrderByDescending(m => m.IsOpen).ThenByDescending(m => m.ReportedAt).Select(AnomalyRowOf));

    private void BuildMovements()
        => Fill(Movements, Remise.Movements.OrderByDescending(m => m.At).Take(200).Select(m => new MovementRow(
            OdeFormat.Short(m.At), m.By, m.ItemName, m.Kind, m.Quantity.ToString(), OdeFormat.OrDash(m.Engin),
            m.StockAfter.ToString(), m.Comment)));

    private AnomalyRow AnomalyRowOf(MaintenanceRecord m)
    {
        var id = m.Id;
        return new AnomalyRow(m.Number, m.Engin, OdeFormat.Short(m.ReportedAt), m.Immobilisation, m.Description,
            m.Status, m.ReportedBy, m.IsOpen, new RelayCommand(() => SelectMaintenance(id)));
    }

    private void SelectMaintenance(string id)
    {
        SelectedMaintenance = Remise.Maintenances.FirstOrDefault(m => m.Id == id);
        RepairText = string.Empty;
        RepairNextRevision = null;
        ReturnToService = true;
        if (CurrentPage != Page.Maintenance)
            CurrentPage = Page.Maintenance;
    }

    private void OpenFiche(string nom) => _desktop.OpenWindow(RemiseWindows.Fiche(_desktop, nom));

    // ===== Inventaire : édition d'une référence =====

    private void EditItem(string? id)
    {
        var item = id is null ? null : Remise.Items.FirstOrDefault(i => i.Id == id);
        _editItemId = item?.Id;
        EditName = item?.Name ?? string.Empty;
        EditCategory = item?.Category ?? (CategoryFilter == "Toutes" ? string.Empty : CategoryFilter);
        EditUnit = item?.Unit ?? "unité";
        EditStock = (item?.Stock ?? 0).ToString();
        EditThreshold = (item?.AlertThreshold ?? 0).ToString();
        EditActive = item?.Active ?? true;
        OnPropertyChanged(nameof(EditIsNew));
        OnPropertyChanged(nameof(EditTitle));
        EditVisible = true;
    }

    private async Task SaveItemAsync()
    {
        if (!int.TryParse(EditStock, out var stock) || !int.TryParse(EditThreshold, out var threshold))
        {
            ShowMessage("Stock et seuil doivent être des nombres.", true);
            return;
        }
        var result = await SendAsync("api/remise/items", new MaterielItem
        {
            Id = _editItemId ?? string.Empty,
            Name = EditName,
            Category = EditCategory,
            Unit = EditUnit,
            Stock = stock,
            AlertThreshold = threshold,
            Active = EditActive
        }, "Référence enregistrée.");
        if (result)
            EditVisible = false;
    }

    // ===== Vérification =====

    private void LoadVerificationLines()
    {
        VerifLines.Clear();
        var engin = VerifEngin is null ? null : ArgosSession.Current?.Remise.FindEngin(VerifEngin);
        if (engin is not null)
        {
            foreach (var d in engin.Dotation)
            {
                var name = ArgosSession.Current?.Remise.FindItem(d.ItemId)?.Name ?? d.ItemId;
                VerifLines.Add(new VerificationLineViewModel(d.ItemId, name, d.Required));
            }
        }
        OnPropertyChanged(nameof(HasVerifLines));
        OnPropertyChanged(nameof(VerifResponsible));
    }

    private async Task ValidateVerificationAsync()
    {
        if (VerifEngin is null || VerifLines.Count == 0)
        {
            ShowMessage("Choisis l'engin à vérifier.", true);
            return;
        }
        var ok = await SendAsync("api/remise/verifications", new VerificationRequest
        {
            Engin = VerifEngin,
            Lines = VerifLines.Select(l => l.ToLine()).ToList(),
            Observations = VerifObservations
        }, $"Vérification de {VerifEngin} enregistrée.");
        if (ok)
        {
            VerifObservations = string.Empty;
            LoadVerificationLines();
        }
    }

    // ===== Maintenance =====

    private async Task ReportAnomalyAsync()
    {
        if (AnomalyEngin is null)
        {
            ShowMessage("Choisis l'engin concerné.", true);
            return;
        }
        var ok = await SendAsync("api/remise/maintenances", new AnomalyRequest
        {
            Engin = AnomalyEngin, Description = AnomalyDescription, Immobilisation = AnomalyLevel
        }, $"Anomalie signalée sur {AnomalyEngin}.");
        if (ok)
        {
            AnomalyDescription = string.Empty;
            AnomalyLevel = Immobilisations.Aucune;
        }
    }

    private async Task AddRepairAsync()
    {
        if (SelectedMaintenance is not { } m) return;
        var ok = await SendAsync($"api/remise/maintenances/{m.Id}/repairs", new RepairRequest
        {
            Text = RepairText, NextRevision = ToOffset(RepairNextRevision)
        }, "Réparation enregistrée.");
        if (ok) RepairText = string.Empty;
    }

    private async Task CloseMaintenanceAsync()
    {
        if (SelectedMaintenance is not { } m) return;
        var ok = await SendAsync($"api/remise/maintenances/{m.Id}/close", new CloseMaintenanceRequest
        {
            Text = RepairText, NextRevision = ToOffset(RepairNextRevision), ReturnToService = ReturnToService
        }, ReturnToService ? $"{m.Number} clôturée, engin remis en service." : $"{m.Number} clôturée.");
        if (ok) RepairText = string.Empty;
    }

    // ===== Stocks =====

    private async Task MoveStockAsync()
    {
        if (StockItem is null)
        {
            ShowMessage("Choisis le matériel.", true);
            return;
        }
        if (!int.TryParse(StockQuantity, out var quantity))
        {
            ShowMessage("Quantité invalide.", true);
            return;
        }
        var ok = await SendAsync("api/remise/stocks", new StockRequest
        {
            ItemId = StockItem.Id,
            Kind = StockKind,
            Quantity = quantity,
            Engin = StockNeedsEngin ? StockEngin ?? string.Empty : string.Empty,
            Comment = StockComment
        }, $"{StockKind} enregistrée : {quantity} {StockItem.Unit} - {StockItem.Name}.");
        if (ok)
        {
            StockComment = string.Empty;
            StockQuantity = "1";
        }
    }

    // ===== Historique =====

    private async Task LoadJournalAsync()
    {
        var session = ArgosSession.Current;
        if (session is null) return;
        try
        {
            _journal = await session.Api.GetAsync<List<JournalEntry>>("api/remise/journal?limit=3000");
            BuildJournal();
        }
        catch (Exception ex)
        {
            ShowMessage("Historique indisponible : " + ex.Message, true);
        }
    }

    private void BuildJournal()
    {
        var q = HistorySearch.Trim();
        var from = HistoryPeriod switch
        {
            "Aujourd'hui" => DateTime.Today,
            "7 derniers jours" => DateTime.Today.AddDays(-7),
            "30 derniers jours" => DateTime.Today.AddDays(-30),
            _ => DateTime.MinValue
        };
        Fill(History, _journal
            .Where(j => j.LocalAt >= from)
            .Where(j => InCategory(j.Action, HistoryCategory))
            .Where(j => q.Length == 0 || Has(j.UserName, q) || Has(j.Action, q) || Has(j.Target, q) || Has(j.Details, q))
            .Take(1000));
    }

    private static bool InCategory(string action, string category) => category switch
    {
        "Engins" => action is RemiseJournal.EnginCree or RemiseJournal.EnginModifie,
        "Statuts" => action == RemiseJournal.Disponibilite || RemiseJournal.OdeActions.Contains(action),
        "Vérifications" => action == RemiseJournal.VerificationFaite,
        "Maintenances" => action is RemiseJournal.Anomalie or RemiseJournal.Reparation or RemiseJournal.MaintenanceCloturee,
        "Stocks" => action is RemiseJournal.Mouvement or RemiseJournal.Materiel,
        _ => true
    };

    // ===== Outils =====

    private async Task<bool> SendAsync(string path, object body, string success)
    {
        var session = ArgosSession.Current;
        if (session is null || Busy) return false;
        Busy = true;
        try
        {
            var result = await session.Api.SendAsync(HttpMethod.Post, path, body);
            if (result.Ok)
                ShowMessage(success, false);
            else
                ShowMessage(result.Error ?? "Action refusée.", true);
            await session.Remise.RefreshAsync();
            return result.Ok;
        }
        finally
        {
            Busy = false;
        }
    }

    private void ShowMessage(string text, bool error)
    {
        MessageIsError = error;
        Message = text;
    }

    private static DateTimeOffset? ToOffset(DateTime? date)
        => date is { } d ? new DateTimeOffset(d.Date, TimeZoneInfo.Local.GetUtcOffset(d.Date)) : null;

    private static int CentreRank(string centre) => centre switch
    {
        EnginsCatalog.Creteil => 0,
        EnginsCatalog.MaisonsAlfort => 1,
        EnginsCatalog.Mass => 2,
        _ => 3
    };

    private static bool Has(string? value, string query)
        => !string.IsNullOrEmpty(value) && VdmsMap.Normalize(value).Contains(VdmsMap.Normalize(query), StringComparison.Ordinal);

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }

    /// <summary>Met à jour une liste de choix sans la vider si rien n'a changé (garde la sélection).</summary>
    private static void Sync(ObservableCollection<string> target, IEnumerable<string> items)
    {
        var list = items.ToList();
        if (list.SequenceEqual(target)) return;
        target.Clear();
        foreach (var item in list)
            target.Add(item);
    }

    public void Dispose()
    {
        if (ArgosSession.Current is { } session)
        {
            session.Remise.Changed -= Rebuild;
            session.Ode.Changed -= Rebuild;
        }
    }
}
