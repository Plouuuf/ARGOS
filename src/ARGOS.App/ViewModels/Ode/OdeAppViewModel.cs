using System.Collections.ObjectModel;
using Argos.Services;
using Argos.Shared.Contracts;
using Argos.ViewModels.Remise;
using Argos.Views.Apps.Ode;

namespace Argos.ViewModels.Ode;

public sealed record CallRow(string Id, string Received, string Operator, string Phone, string Address, string Nature,
    string Status, RelayCommand OpenCommand);

public sealed record InterventionRow(string Number, string Created, string Nature, string Address, string Procedure,
    string Engins, string State, RelayCommand OpenCommand);

public sealed record EnginRow(string Nom, string Libelle, string Centre, string Status, string Intervention, string Since);

public sealed record StatRow(string Label, string Value, double Ratio);

/// <summary>Application ADAGIO ODE : tableau de bord, appels, interventions, archives, engins, journal, statistiques.</summary>
public sealed class OdeAppViewModel : ViewModelBase, IDisposable
{
    public enum Page { Dashboard, Calls, Interventions, Archives, Engins, Journal, Stats }

    private readonly DesktopViewModel _desktop;
    private Page _page = Page.Dashboard;
    private string _callSearch = string.Empty;
    private string _callStatus = "Tous";
    private string _archiveSearch = string.Empty;
    private string _journalSearch = string.Empty;
    private string? _error;
    private List<JournalEntry> _journal = [];

    public OdeAppViewModel(DesktopViewModel desktop)
    {
        _desktop = desktop;
        NavigateCommand = new RelayCommand(p =>
        {
            if (p is string s && Enum.TryParse<Page>(s, out var page))
                CurrentPage = page;
        });
        NewCallCommand = new RelayCommand(() => _ = OdeWindows.NewCallAsync(_desktop));
        NewOdeCommand = new RelayCommand(() => _desktop.OpenWindow(OdeWindows.CreateOde(_desktop, null, null, "BLANCHE")));
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync());

        if (ArgosSession.Current is { } session)
        {
            session.Ode.Changed += Rebuild;
            if (!session.Ode.IsLoaded)
                _ = session.Ode.RefreshAsync();
        }
        Rebuild();
    }

    // ===== Navigation =====

    public Page CurrentPage
    {
        get => _page;
        set
        {
            if (!SetProperty(ref _page, value)) return;
            foreach (var p in Enum.GetNames<Page>())
                OnPropertyChanged("Is" + p);
            if (value == Page.Journal)
                _ = LoadJournalAsync();
            if (value == Page.Engins && Parc is null)
            {
                Parc = new GdmParcViewModel(_desktop);
                OnPropertyChanged(nameof(Parc));
            }
        }
    }

    public bool IsDashboard => CurrentPage == Page.Dashboard;
    public bool IsCalls => CurrentPage == Page.Calls;
    public bool IsInterventions => CurrentPage == Page.Interventions;
    public bool IsArchives => CurrentPage == Page.Archives;
    public bool IsEngins => CurrentPage == Page.Engins;
    public bool IsJournal => CurrentPage == Page.Journal;
    public bool IsStats => CurrentPage == Page.Stats;

    public RelayCommand NavigateCommand { get; }
    public RelayCommand NewCallCommand { get; }
    public RelayCommand NewOdeCommand { get; }
    public RelayCommand RefreshCommand { get; }

    public string OperatorName => ArgosSession.Current?.OperatorName ?? string.Empty;

    public string? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    // ===== Tableau de bord =====

    public int CallsInProgress { get; private set; }
    public int CallsWaiting { get; private set; }
    public int CallsToday { get; private set; }
    public int ActiveInterventions { get; private set; }
    public int ClosedToday { get; private set; }
    public int EnginsAvailable { get; private set; }
    public int EnginsEngaged { get; private set; }
    public int EnginsUnavailable { get; private set; }

    public ObservableCollection<CallRow> RecentCalls { get; } = [];
    public ObservableCollection<CallRow> PendingCalls { get; } = [];
    public ObservableCollection<InterventionRow> RecentInterventions { get; } = [];

    // ===== Appels =====

    public IReadOnlyList<string> CallStatusFilters { get; } = ["Tous", .. CallStatuses.All];
    public string CallSearch { get => _callSearch; set { if (SetProperty(ref _callSearch, value)) BuildCalls(); } }
    public string CallStatusFilter { get => _callStatus; set { if (SetProperty(ref _callStatus, value ?? "Tous")) BuildCalls(); } }
    public ObservableCollection<CallRow> Calls { get; } = [];
    public string CallsCount => $"{Calls.Count} appel(s)";

    // ===== Interventions et archives =====

    public ObservableCollection<InterventionRow> Active { get; } = [];
    public string ArchiveSearch { get => _archiveSearch; set { if (SetProperty(ref _archiveSearch, value)) BuildArchives(); } }
    public ObservableCollection<InterventionRow> Archives { get; } = [];
    public string ArchivesCount => $"{Archives.Count} intervention(s)";

    // ===== Engins =====

    public ObservableCollection<EnginRow> Engins { get; } = [];

    /// <summary>Parc des engins façon feuille GDM, statuts en direct (créé à la première ouverture de l'onglet).</summary>
    public GdmParcViewModel? Parc { get; private set; }

    // ===== Journal =====

    public string JournalSearch { get => _journalSearch; set { if (SetProperty(ref _journalSearch, value)) BuildJournal(); } }
    public ObservableCollection<JournalEntry> Journal { get; } = [];

    // ===== Statistiques =====

    public ObservableCollection<StatRow> StatTotals { get; } = [];
    public ObservableCollection<StatRow> StatNatures { get; } = [];
    public ObservableCollection<StatRow> StatOperators { get; } = [];

    // ===== Construction des listes =====

    private OdeSnapshot Snapshot => ArgosSession.Current?.Ode.Snapshot ?? new OdeSnapshot();

    private async Task RefreshAsync()
    {
        if (ArgosSession.Current is { } session)
            await session.Ode.RefreshAsync();
        if (CurrentPage == Page.Journal)
            await LoadJournalAsync();
    }

    private void Rebuild()
    {
        var store = ArgosSession.Current?.Ode;
        Error = store is null ? "Session fermée." : store.LastError;
        var s = Snapshot;
        var today = DateTime.Now.Date;

        CallsInProgress = s.Calls.Count(c => c.Status == CallStatuses.EnCours);
        CallsWaiting = s.Calls.Count(c => c.Status is CallStatuses.Enregistre or CallStatuses.Transmis);
        CallsToday = s.Calls.Count(c => c.ReceivedAt.ToLocalTime().Date == today);
        ActiveInterventions = s.Interventions.Count(i => i.Active);
        ClosedToday = s.Interventions.Count(i => !i.Active && i.ClosedAt?.ToLocalTime().Date == today);
        EnginsAvailable = s.Engins.Count(e => EnginStatuses.IsAvailable(e.Status));
        EnginsEngaged = s.Engins.Count(e => EnginStatuses.IsEngaged(e.Status));
        EnginsUnavailable = s.Engins.Count(e => e.Status is EnginStatuses.Indisponible or EnginStatuses.Maintenance);
        foreach (var name in new[] { nameof(CallsInProgress), nameof(CallsWaiting), nameof(CallsToday), nameof(ActiveInterventions),
                                     nameof(ClosedToday), nameof(EnginsAvailable), nameof(EnginsEngaged), nameof(EnginsUnavailable) })
            OnPropertyChanged(name);

        Fill(RecentCalls, s.Calls.OrderByDescending(c => c.ReceivedAt).Take(8).Select(CallRowOf));
        Fill(PendingCalls, s.Calls.Where(c => c.Status is CallStatuses.EnCours or CallStatuses.Enregistre or CallStatuses.Transmis)
            .OrderBy(c => c.ReceivedAt).Select(CallRowOf));
        Fill(RecentInterventions, s.Interventions.OrderByDescending(i => i.CreatedAt).Take(8).Select(InterventionRowOf));
        Fill(Active, s.Interventions.Where(i => i.Active).OrderByDescending(i => i.CreatedAt).Select(InterventionRowOf));

        Fill(Engins, s.Engins.Select(e => new EnginRow(e.Nom, e.Libelle, e.Centre, e.Status,
            e.InterventionNumber ?? "—", OdeFormat.Short(e.UpdatedAt))));

        BuildCalls();
        BuildArchives();
        BuildStats();
    }

    private void BuildCalls()
    {
        var q = CallSearch.Trim();
        var rows = Snapshot.Calls
            .Where(c => CallStatusFilter == "Tous" || c.Status == CallStatusFilter)
            .Where(c => q.Length == 0
                        || Contains(c.Id, q) || Contains(c.CallerPhone, q) || Contains(c.CallerName, q)
                        || Contains(c.AddressLine, q) || Contains(c.OperatorName, q) || Contains(OdeFormat.Nature(c.NatureCode), q))
            .OrderByDescending(c => c.ReceivedAt)
            .Select(CallRowOf);
        Fill(Calls, rows);
        OnPropertyChanged(nameof(CallsCount));
    }

    private void BuildArchives()
    {
        var q = ArchiveSearch.Trim();
        var rows = Snapshot.Interventions
            .Where(i => q.Length == 0
                        || Contains(i.Number, q) || Contains(i.Address, q) || Contains(i.Locality, q)
                        || Contains(OdeFormat.Nature(i.NatureCode), q) || Contains(i.Procedure, q)
                        || i.Engins.Any(e => Contains(e.Nom, q)))
            .OrderByDescending(i => i.CreatedAt)
            .Select(InterventionRowOf);
        Fill(Archives, rows);
        OnPropertyChanged(nameof(ArchivesCount));
    }

    private void BuildStats()
    {
        var s = Snapshot;
        var withDelay = s.Interventions
            .Select(i => (i, call: i.CallIds.Select(id => s.Calls.FirstOrDefault(c => c.Id == id)).FirstOrDefault(c => c is not null)))
            .Where(x => x.call is not null && x.i.Orders.Count > 0)
            .Select(x => (x.i.Orders.Min(o => o.ValidatedAt) - x.call!.ReceivedAt).TotalSeconds)
            .Where(d => d >= 0)
            .ToList();

        Fill(StatTotals, new[]
        {
            new StatRow("Appels reçus", s.Calls.Count.ToString(), 1),
            new StatRow("Appels classés sans suite", s.Calls.Count(c => c.Status == CallStatuses.SansSuite).ToString(), 1),
            new StatRow("Interventions (ODE)", s.Interventions.Count.ToString(), 1),
            new StatRow("Ordres de départ générés", s.Interventions.Sum(i => i.Orders.Count).ToString(), 1),
            new StatRow("Délai moyen appel → départ",
                withDelay.Count == 0 ? "—" : OdeFormat.Duration(TimeSpan.FromSeconds(withDelay.Average())), 1)
        });

        var byNature = s.Interventions.GroupBy(i => OdeFormat.Nature(i.NatureCode))
            .Select(g => (g.Key, Count: g.Count())).OrderByDescending(x => x.Count).Take(12).ToList();
        var maxNature = Math.Max(1, byNature.Select(x => x.Count).DefaultIfEmpty(1).Max());
        Fill(StatNatures, byNature.Select(x => new StatRow(x.Key, x.Count.ToString(), (double)x.Count / maxNature)));

        var byOperator = s.Calls.GroupBy(c => c.OperatorName)
            .Select(g => (g.Key, Count: g.Count())).OrderByDescending(x => x.Count).Take(12).ToList();
        var maxOp = Math.Max(1, byOperator.Select(x => x.Count).DefaultIfEmpty(1).Max());
        Fill(StatOperators, byOperator.Select(x => new StatRow(x.Key, x.Count.ToString(), (double)x.Count / maxOp)));
    }

    private async Task LoadJournalAsync()
    {
        var session = ArgosSession.Current;
        if (session is null) return;
        try
        {
            _journal = await session.Api.GetAsync<List<JournalEntry>>("api/ode/journal?limit=1000");
            BuildJournal();
        }
        catch (Exception ex)
        {
            Error = "Journal indisponible : " + ex.Message;
        }
    }

    private void BuildJournal()
    {
        var q = JournalSearch.Trim();
        Fill(Journal, _journal.Where(j => q.Length == 0 || Contains(j.UserName, q) || Contains(j.Action, q)
                                         || Contains(j.Target, q) || Contains(j.Details, q)));
    }

    private CallRow CallRowOf(CallRecord c)
    {
        var id = c.Id;
        return new CallRow(c.Id, OdeFormat.Short(c.ReceivedAt), c.OperatorName, OdeFormat.OrDash(c.CallerPhone),
            OdeFormat.OrDash(c.AddressLine), string.IsNullOrWhiteSpace(c.NatureCode) ? "—" : OdeFormat.Nature(c.NatureCode),
            c.Status, new RelayCommand(() => _desktop.OpenCall(id)));
    }

    private InterventionRow InterventionRowOf(Intervention i)
    {
        var id = i.Id;
        return new InterventionRow(i.Number, OdeFormat.Short(i.CreatedAt), OdeFormat.Nature(i.NatureCode),
            string.Join(" - ", new[] { i.Address, i.Locality }.Where(x => !string.IsNullOrWhiteSpace(x))),
            Procedures.Find(i.Procedure)?.Label ?? i.Procedure,
            string.Join(", ", i.Engins.Select(e => $"{e.Nom} ({e.Status})")),
            i.Active ? "En cours" : "Clôturée",
            new RelayCommand(() => _desktop.OpenWindow(OdeWindows.Intervention(_desktop, id))));
    }

    private static bool Contains(string? value, string query)
        => !string.IsNullOrEmpty(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }

    public void Dispose()
    {
        if (ArgosSession.Current is { } session)
            session.Ode.Changed -= Rebuild;
        Parc?.Dispose();
    }
}
