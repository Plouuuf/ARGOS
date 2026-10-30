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
    private string _period = Periods[0];
    private DateTime? _periodFrom = DateTime.Today.AddDays(-30);
    private DateTime? _periodTo = DateTime.Today;
    private OdeHistory? _history;
    private string _periodInfo = string.Empty;

    // ===== Période (historique des appels, archives, statistiques) =====

    public static readonly IReadOnlyList<string> Periods =
        ["Récents (300 derniers)", "Aujourd'hui", "7 derniers jours", "30 derniers jours", "12 derniers mois", "Période personnalisée"];
    public IReadOnlyList<string> PeriodChoices => Periods;
    public string Period
    {
        get => _period;
        set { if (SetProperty(ref _period, value ?? Periods[0])) { OnPropertyChanged(nameof(IsCustomPeriod)); _ = LoadPeriodAsync(); } }
    }
    public bool IsCustomPeriod => Period == Periods[^1];
    public DateTime? PeriodFrom { get => _periodFrom; set { if (SetProperty(ref _periodFrom, value) && IsCustomPeriod) _ = LoadPeriodAsync(); } }
    public DateTime? PeriodTo { get => _periodTo; set { if (SetProperty(ref _periodTo, value) && IsCustomPeriod) _ = LoadPeriodAsync(); } }
    public string PeriodInfo { get => _periodInfo; private set => SetProperty(ref _periodInfo, value); }

    private IEnumerable<CallRecord> PeriodCalls => _history?.Calls ?? Snapshot.Calls;
    private IEnumerable<Intervention> PeriodInterventions => _history?.Interventions ?? Snapshot.Interventions;

    private async Task LoadPeriodAsync()
    {
        var today = DateTime.Today;
        (DateTime From, DateTime To)? range = Period switch
        {
            "Aujourd'hui" => (today, today.AddDays(1)),
            "7 derniers jours" => (today.AddDays(-6), today.AddDays(1)),
            "30 derniers jours" => (today.AddDays(-29), today.AddDays(1)),
            "12 derniers mois" => (today.AddMonths(-12), today.AddDays(1)),
            "Période personnalisée" => ((PeriodFrom ?? today.AddDays(-30)).Date, (PeriodTo ?? today).Date.AddDays(1)),
            _ => null
        };
        if (range is null)
        {
            _history = null;
            PeriodInfo = "300 derniers appels et interventions";
        }
        else if (ArgosSession.Current is { } session)
        {
            try
            {
                var from = Uri.EscapeDataString(new DateTimeOffset(range.Value.From).ToString("O"));
                var to = Uri.EscapeDataString(new DateTimeOffset(range.Value.To).ToString("O"));
                _history = await session.Api.GetAsync<OdeHistory>($"api/ode/history?from={from}&to={to}");
                PeriodInfo = $"Du {range.Value.From:dd/MM/yyyy} au {range.Value.To.AddDays(-1):dd/MM/yyyy}"
                             + (_history.Truncated ? " (limité aux 5 000 plus récents)" : string.Empty);
            }
            catch (Exception ex)
            {
                _history = null;
                Error = "Historique indisponible : " + ex.Message;
            }
        }
        BuildCalls();
        BuildArchives();
        BuildStats();
    }

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
        PeriodInfo = "300 derniers appels et interventions";
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
    public ObservableCollection<StatRow> StatCallNatures { get; } = [];
    public ObservableCollection<StatRow> StatCategories { get; } = [];
    public ObservableCollection<StatRow> StatProcedures { get; } = [];
    public ObservableCollection<StatRow> StatOperatorOdes { get; } = [];
    public ObservableCollection<StatRow> StatDays { get; } = [];

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
        var rows = PeriodCalls
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
        var rows = PeriodInterventions
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
        var calls = PeriodCalls.ToList();
        var interventions = PeriodInterventions.ToList();
        var callById = calls.Concat(Snapshot.Calls).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());

        // Délai entre le décroché et le premier déclenchement (validation du premier ODE).
        var toDeparture = interventions
            .Select(i => (i, call: i.CallIds.Select(id => callById.GetValueOrDefault(id)).FirstOrDefault(c => c is not null)))
            .Where(x => x.call is not null && x.i.Orders.Count > 0)
            .Select(x => (x.i.Orders.Min(o => o.ValidatedAt) - x.call!.ReceivedAt).TotalSeconds)
            .Where(d => d >= 0).ToList();
        // Durée de traitement de l'appel (décroché → raccroché / clôture).
        var callDurations = calls.Where(c => c.EndedAt is not null)
            .Select(c => (c.EndedAt!.Value - c.ReceivedAt).TotalSeconds).Where(d => d >= 0).ToList();
        // Délai d'arrivée du premier engin sur les lieux (horodatage du statut « sur les lieux » dans la main courante).
        var toScene = interventions.Where(i => i.Orders.Count > 0)
            .Select(i => (start: i.Orders.Min(o => o.ValidatedAt),
                          arrival: i.Events.Where(e => e.Text.Contains(EnginStatuses.SurLesLieux, StringComparison.OrdinalIgnoreCase))
                              .Select(e => (DateTimeOffset?)e.At).Min()))
            .Where(x => x.arrival is not null).Select(x => (x.arrival!.Value - x.start).TotalSeconds).Where(d => d >= 0).ToList();
        var durations = interventions.Where(i => i.ClosedAt is not null)
            .Select(i => (i.ClosedAt!.Value - i.CreatedAt).TotalSeconds).Where(d => d >= 0).ToList();
        string Avg(List<double> values) => values.Count == 0 ? "—" : OdeFormat.Duration(TimeSpan.FromSeconds(values.Average()));

        Fill(StatTotals, new[]
        {
            new StatRow("Appels reçus", calls.Count.ToString(), 1),
            new StatRow("Appels classés sans suite", calls.Count(c => c.Status == CallStatuses.SansSuite).ToString(), 1),
            new StatRow("Appels ayant donné lieu à un départ", calls.Count(c => c.Status == CallStatuses.Intervention).ToString(), 1),
            new StatRow("Interventions", interventions.Count.ToString(), 1),
            new StatRow("Ordres de départ générés", interventions.Sum(i => i.Orders.Count).ToString(), 1),
            new StatRow("Durée moyenne de traitement d'un appel", Avg(callDurations), 1),
            new StatRow("Délai moyen appel → déclenchement", Avg(toDeparture), 1),
            new StatRow("Délai moyen déclenchement → sur les lieux", Avg(toScene), 1),
            new StatRow("Durée moyenne d'une intervention", Avg(durations), 1)
        });

        static IEnumerable<StatRow> Bars(IEnumerable<string> keys, int take = 12)
        {
            var groups = keys.GroupBy(k => k).Select(g => (g.Key, Count: g.Count())).OrderByDescending(x => x.Count).Take(take).ToList();
            var max = Math.Max(1, groups.Select(x => x.Count).DefaultIfEmpty(1).Max());
            return groups.Select(x => new StatRow(x.Key, x.Count.ToString(), (double)x.Count / max));
        }

        Fill(StatCallNatures, Bars(calls.Select(c => string.IsNullOrWhiteSpace(c.NatureCode) ? "Nature non renseignée" : OdeFormat.Nature(c.NatureCode))));
        Fill(StatNatures, Bars(interventions.Select(i => OdeFormat.Nature(i.NatureCode))));
        Fill(StatCategories, Bars(interventions.Select(i => Natures.Find(i.NatureCode)?.CategoryLabel ?? "Autre")));
        Fill(StatProcedures, Bars(interventions.Select(i => Procedures.Find(i.Procedure)?.Label ?? i.Procedure)));
        Fill(StatOperators, Bars(calls.Select(c => string.IsNullOrWhiteSpace(c.OperatorName) ? "—" : c.OperatorName)));
        Fill(StatOperatorOdes, Bars(interventions.SelectMany(i => i.Orders).Select(o => string.IsNullOrWhiteSpace(o.OperatorName) ? "—" : o.OperatorName)));
        var days = interventions.GroupBy(i => i.CreatedAt.ToLocalTime().Date).OrderByDescending(g => g.Key).Take(14)
            .Select(g => (Day: g.Key, Count: g.Count())).ToList();
        var maxDay = Math.Max(1, days.Select(d => d.Count).DefaultIfEmpty(1).Max());
        Fill(StatDays, days.Select(d => new StatRow(d.Day.ToString("ddd dd/MM", OdeFormat.French), d.Count.ToString(), (double)d.Count / maxDay)));
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
