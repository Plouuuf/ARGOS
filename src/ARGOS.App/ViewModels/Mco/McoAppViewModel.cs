using System.Collections.ObjectModel;
using Argos.Services;
using Argos.Shared.Contracts;
using Argos.ViewModels.Ode;
using Argos.Views.Apps.Mco;

namespace Argos.ViewModels.Mco;

public sealed record McoRow(string Number, string Intervention, string Date, string Nature, string Address, string Engin,
    string Author, string Status, RelayCommand OpenCommand);

public sealed record InterventionMcoRow(string Number, string Date, string Nature, string Address, string Engins,
    string McoState, bool HasMco, RelayCommand WriteCommand);

/// <summary>MCO : tableau de bord des mains courantes, recherche, interventions à rédiger.</summary>
public sealed class McoAppViewModel : ViewModelBase, IDisposable
{
    public const string Tous = "Tous";

    private readonly DesktopViewModel _desktop;
    private string _search = string.Empty;
    private string _status = Tous;
    private string _engin = Tous;

    public McoAppViewModel(DesktopViewModel desktop)
    {
        _desktop = desktop;
        NewCommand = new RelayCommand(() => _desktop.OpenWindow(McoWindows.Editor(_desktop, null, null, null)));
        RefreshCommand = new RelayCommand(() =>
        {
            _ = ArgosSession.Current?.Mco.RefreshAsync();
            _ = ArgosSession.Current?.Ode.RefreshAsync();
        });
        if (ArgosSession.Current is { } session)
        {
            session.Mco.Changed += Build;
            session.Ode.Changed += Build;
            if (!session.Mco.IsLoaded) _ = session.Mco.RefreshAsync();
            if (!session.Ode.IsLoaded) _ = session.Ode.RefreshAsync();
        }
        Build();
    }

    public RelayCommand NewCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public string OperatorName => ArgosSession.Current?.OperatorName ?? string.Empty;
    public string? Error => ArgosSession.Current?.Mco.LastError;
    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    public int Drafts { get; private set; }
    public int Validated { get; private set; }
    public int Archived { get; private set; }
    public int ToWrite { get; private set; }

    public string Search { get => _search; set { if (SetProperty(ref _search, value)) Build(); } }
    public string StatusFilter { get => _status; set { if (SetProperty(ref _status, value ?? Tous)) Build(); } }
    public string EnginFilter { get => _engin; set { if (SetProperty(ref _engin, value ?? Tous)) Build(); } }
    public IReadOnlyList<string> StatusFilters { get; } = [Tous, .. McoStatuses.All];
    public ObservableCollection<string> EnginFilters { get; } = [];
    public ObservableCollection<McoRow> Reports { get; } = [];
    public ObservableCollection<InterventionMcoRow> Interventions { get; } = [];
    public string ReportsCount => $"{Reports.Count} MCO";

    private void Build()
    {
        var reports = ArgosSession.Current?.Mco.Snapshot.Reports ?? [];
        var interventions = ArgosSession.Current?.Ode.Snapshot.Interventions ?? [];

        Drafts = reports.Count(r => r.Status == McoStatuses.Brouillon);
        Validated = reports.Count(r => r.Status == McoStatuses.Validee);
        Archived = reports.Count(r => r.Status == McoStatuses.Archivee);
        ToWrite = interventions.Count(i => !reports.Any(r => r.InterventionId == i.Id || r.InterventionNumber == i.Number));
        foreach (var n in new[] { nameof(Drafts), nameof(Validated), nameof(Archived), nameof(ToWrite), nameof(Error), nameof(HasError) })
            OnPropertyChanged(n);

        var engins = new[] { Tous }.Concat(reports.Select(r => r.Engin).Where(e => e.Length > 0).Distinct().Order()).ToList();
        if (!engins.SequenceEqual(EnginFilters))
        {
            EnginFilters.Clear();
            foreach (var e in engins) EnginFilters.Add(e);
        }

        // Recherche : numéro d'intervention ou de MCO, adresse, nature, date (« 30/10 »), engin, rédacteur.
        var q = VdmsMap.Normalize(Search);
        Reports.Clear();
        foreach (var r in reports
                     .Where(r => StatusFilter == Tous || r.Status == StatusFilter)
                     .Where(r => EnginFilter == Tous || r.Engin == EnginFilter)
                     .Where(r => q.Length == 0 || VdmsMap.Normalize(string.Join(" ", r.Number, r.InterventionNumber, r.Address, r.Locality,
                         OdeFormat.Nature(r.NatureCode), r.Date.ToLocalTime().ToString("dd/MM/yyyy"), r.Engin, r.AuthorName)).Contains(q))
                     .OrderByDescending(r => r.Date).ThenByDescending(r => r.CreatedAt))
        {
            var id = r.Id;
            Reports.Add(new McoRow(r.Number, OdeFormat.OrDash(r.InterventionNumber), r.Date.ToLocalTime().ToString("dd/MM/yyyy"),
                string.IsNullOrWhiteSpace(r.NatureCode) ? "—" : OdeFormat.Nature(r.NatureCode),
                OdeFormat.OrDash(string.Join(" - ", new[] { r.Address, r.Locality }.Where(s => !string.IsNullOrWhiteSpace(s)))),
                OdeFormat.OrDash(r.Engin), r.AuthorName, r.Status,
                new RelayCommand(() => _desktop.OpenWindow(McoWindows.Editor(_desktop, id, null, null)))));
        }
        OnPropertyChanged(nameof(ReportsCount));

        Interventions.Clear();
        foreach (var i in interventions.OrderByDescending(i => i.CreatedAt).Take(15))
        {
            var linked = reports.Where(r => r.InterventionId == i.Id || r.InterventionNumber == i.Number).ToList();
            var id = i.Id;
            Interventions.Add(new InterventionMcoRow(i.Number, OdeFormat.Short(i.CreatedAt), OdeFormat.Nature(i.NatureCode),
                i.Address, string.Join(", ", i.Engins.Select(e => e.Nom)),
                linked.Count == 0 ? "Aucune MCO" : string.Join(", ", linked.Select(l => $"{l.Engin} ({l.Status.ToLowerInvariant()})")),
                linked.Count > 0,
                new RelayCommand(() => _desktop.OpenWindow(McoWindows.Editor(_desktop, null, id, null)))));
        }
    }

    public void Dispose()
    {
        if (ArgosSession.Current is { } session)
        {
            session.Mco.Changed -= Build;
            session.Ode.Changed -= Build;
        }
    }
}
