using System.Collections.ObjectModel;
using Argos.Services;
using Argos.Shared.Contracts;
using Argos.Views.Apps.Remise;

namespace Argos.ViewModels.Remise;

/// <summary>
/// Parc des engins façon feuille GDM (ADAGIO ODE, onglet « Parc des engins ») :
/// fiches du parc (ADAGIO REMISE) avec le statut opérationnel en direct (ADAGIO ODE).
/// </summary>
public sealed class GdmParcViewModel : ViewModelBase, IDisposable
{
    public const string Tous = "Tous";
    public const string Engage = "Engagé";

    private readonly DesktopViewModel _desktop;
    private string _search = string.Empty;
    private string _centre = Tous;
    private string _status = Tous;

    public GdmParcViewModel(DesktopViewModel desktop)
    {
        _desktop = desktop;
        CentreCommand = new RelayCommand(p => CentreFilter = p as string ?? Tous);
        if (ArgosSession.Current is { } session)
        {
            session.Remise.Changed += Build;
            session.Ode.Changed += Build;
            if (!session.Remise.IsLoaded)
                _ = session.Remise.RefreshAsync();
            if (!session.Ode.IsLoaded)
                _ = session.Ode.RefreshAsync();
        }
        Build();
    }

    public string ParcSearch { get => _search; set { if (SetProperty(ref _search, value)) Build(); } }
    public string CentreFilter { get => _centre; set { if (SetProperty(ref _centre, value ?? Tous)) Build(); } }
    public string StatusFilter { get => _status; set { if (SetProperty(ref _status, value ?? Tous)) Build(); } }
    public ObservableCollection<string> Centres { get; } = [];
    public IReadOnlyList<string> StatusFilters { get; } =
        [Tous, EnginStatuses.Disponible, Engage, EnginStatuses.Indisponible, EnginStatuses.Maintenance];
    public ObservableCollection<EnginTile> Tiles { get; } = [];
    public RelayCommand CentreCommand { get; }
    public string CentreTitle => CentreFilter == Tous ? "TOUS CENTRES" : CentreFilter;
    public string CentreShort => CentreFilter == Tous ? "TOUS" : CentreFilter;
    public string ParcCount => $"{Tiles.Count} engin(s)";

    private void Build()
    {
        var remise = ArgosSession.Current?.Remise.Snapshot ?? new RemiseSnapshot();
        var ode = ArgosSession.Current?.Ode.Snapshot ?? new OdeSnapshot();
        var engins = remise.Engins.Where(e => e.Active).ToList();

        var centres = new[] { Tous }.Concat(engins.Select(e => e.Centre).Distinct().OrderBy(RemiseText.CentreRank)).ToList();
        if (!centres.SequenceEqual(Centres))
        {
            Centres.Clear();
            foreach (var c in centres) Centres.Add(c);
        }

        var q = VdmsMap.Normalize(ParcSearch);
        var tiles = engins
            .Where(e => CentreFilter == Tous || e.Centre == CentreFilter)
            .Where(e => q.Length == 0 || VdmsMap.Normalize($"{e.Nom} {e.Type} {e.Libelle} {e.Immatriculation}").Contains(q, StringComparison.Ordinal))
            .Select(e => TileOf(e, ode.Engins.FirstOrDefault(s => string.Equals(s.Nom, e.Nom, StringComparison.OrdinalIgnoreCase)),
                () => _desktop.OpenWindow(RemiseWindows.Fiche(_desktop, e.Nom))))
            .Where(t => Matches(t.Status, StatusFilter))
            .ToList();
        Tiles.Clear();
        foreach (var t in tiles) Tiles.Add(t);
        OnPropertyChanged(nameof(ParcCount));
        OnPropertyChanged(nameof(CentreTitle));
        OnPropertyChanged(nameof(CentreShort));
    }

    public static bool Matches(string status, string filter) => filter switch
    {
        Tous => true,
        Engage => EnginStatuses.IsEngaged(status) || status == EnginStatuses.DisponibleIntervention,
        EnginStatuses.Disponible => EnginStatuses.IsAvailable(status),
        _ => status == filter
    };

    public static EnginTile TileOf(FleetEngin e, EnginState? state, Action open)
    {
        var status = RemiseAppViewModel.StatusOf(e, state);
        var since = state?.UpdatedAt ?? e.AvailabilitySince;
        return new EnginTile(
            e.Nom, e.GdmTitle, e.TypeLabel, e.Rang, e.Centre,
            ImageLoader.EnginPhoto(e),
            $"{since.ToLocalTime():HH:mm - dd/MM/yyyy}    [{status}]",
            status,
            RemiseColors.ForStatus(status),
            e.NextRevision is { } r ? "Révision " + r.ToString("dd/MM/yyyy") : "Révision non planifiée",
            e.NextRevision is { } d && d.Date <= DateTime.Today.AddDays(7),
            state?.InterventionNumber is { Length: > 0 } inter ? "Intervention " + inter : string.Empty,
            new RelayCommand(open));
    }

    public void Dispose()
    {
        if (ArgosSession.Current is { } session)
        {
            session.Remise.Changed -= Build;
            session.Ode.Changed -= Build;
        }
    }
}
