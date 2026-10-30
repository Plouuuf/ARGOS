using System.Collections.ObjectModel;
using System.Globalization;
using Argos.Services;
using Argos.Shared.Contracts;
using Argos.Views.Apps.Sigtao;
using Avalonia.Media;

namespace Argos.ViewModels.Sigtao;

/// <summary>Personnel proposé dans les listes de la feuille de garde (Id vide = poste non pourvu).</summary>
public sealed record PersonnelOption(string Id, string Label)
{
    public static readonly PersonnelOption None = new(string.Empty, "— non pourvu —");
    public override string ToString() => Label;
}

/// <summary>Un poste de la feuille de garde.</summary>
public sealed class GardeSlot : ViewModelBase
{
    private readonly Action _changed;
    private PersonnelOption _selected;

    public GardeSlot(string poste, PersonnelOption selected, ObservableCollection<PersonnelOption> options, bool editable, Action changed,
        Action<GardeSlot>? remove = null)
    {
        Poste = poste;
        _selected = selected;
        Options = options;
        IsEditable = editable;
        _changed = changed;
        if (remove is not null) RemoveCommand = new RelayCommand(() => remove(this));
    }

    public string Poste { get; }
    public ObservableCollection<PersonnelOption> Options { get; }
    public bool IsEditable { get; }
    public RelayCommand? RemoveCommand { get; }
    public bool CanRemove => RemoveCommand is not null && IsEditable;

    /// <summary>Engin du poste (vide = service ou renfort), pour l'inscription personnelle.</summary>
    public string? Engin { get; init; }
    public string? Type { get; init; }
    /// <summary>« Moi » : s'inscrire soi-même sur ce poste libre.</summary>
    public RelayCommand? SelfCommand { get; init; }
    public bool CanSelf => SelfCommand is not null && _selected.Id.Length == 0;

    public PersonnelOption Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value ?? PersonnelOption.None)) return;
            OnPropertyChanged(nameof(Display));
            OnPropertyChanged(nameof(CanSelf));
            _changed();
        }
    }

    public string Display => _selected.Id.Length == 0 ? "—" : _selected.Label;

    public GardeAffectation ToAffectation() => new() { Poste = Poste, MilitaryId = _selected.Id, Nom = _selected.Id.Length == 0 ? string.Empty : _selected.Label };
}

/// <summary>Un engin de la feuille de garde avec son équipage.</summary>
public sealed class GardeEnginVm : ViewModelBase
{
    private readonly Action _changed;
    private bool _arme;

    public GardeEnginVm(string engin, string type, string libelle, string availability, bool arme, List<GardeSlot> slots, bool editable, Action changed)
    {
        Engin = engin;
        Type = type;
        Libelle = libelle;
        Availability = availability;
        _arme = arme;
        Slots = slots;
        IsEditable = editable;
        _changed = changed;
    }

    public string Engin { get; }
    public string Type { get; }
    public string Libelle { get; }
    public string Availability { get; }
    public bool IsEditable { get; }
    public List<GardeSlot> Slots { get; }

    public bool Arme
    {
        get => _arme;
        set
        {
            if (!SetProperty(ref _arme, value)) return;
            OnPropertyChanged(nameof(ArmeText));
            OnPropertyChanged(nameof(HeaderBrush));
            _changed();
        }
    }

    public int Pourvus => Slots.Count(s => s.Selected.Id.Length > 0);
    public string ArmeText => Arme ? $"Armé · {Pourvus}/{Slots.Count}" : "NON ARMÉ (indisponible à la validation)";
    public IBrush HeaderBrush => new SolidColorBrush(Color.Parse(Arme ? "#13294B" : "#A4262C"));
    public bool HasAvailabilityWarning => Availability.Length > 0 && Availability != FleetAvailability.Disponible;

    public void Refresh()
    {
        OnPropertyChanged(nameof(Pourvus));
        OnPropertyChanged(nameof(ArmeText));
    }

    public GardeEngin ToGardeEngin() => new() { Engin = Engin, Type = Type, Arme = Arme, Postes = Slots.Select(s => s.ToAffectation()).ToList() };
}

/// <summary>Case du planning de garde.</summary>
public sealed record PlanningCell(string Text, string Tip, IBrush Background, IBrush Foreground, double Width);

public sealed record PlanningRow(string Name, string Grade, List<PlanningCell> Cells, int Total);

public sealed record PlanningDay(string Top, string Bottom, double Width, IBrush Background, RelayCommand OpenCommand);

/// <summary>Feuille de garde du jour et planning de garde (SIGTAO).</summary>
public sealed partial class SigtaoAppViewModel
{
    private DateTime _gardeDate = DateTime.Today;
    private string _gardeCentre = FeuilleGarde.TousCentres;
    private string _gardeObservations = string.Empty;
    private string _gardeHoraires = "07h30 → 07h30 (J+1)";
    private string? _gardeLoadedKey;
    private bool _gardeDirty;
    private bool _gardeBusy;
    private FeuilleGarde? _gardeSaved;

    private DateTime _planningStart = StartOfWeek(DateTime.Today);
    private bool _planningMonth;
    private string _planningCentre = "Tous";

    public ObservableCollection<string> GardeCentres { get; } = [];
    public ObservableCollection<PersonnelOption> GardePersonnel { get; } = [];
    public ObservableCollection<GardeSlot> GardeService { get; } = [];
    public ObservableCollection<GardeEnginVm> GardeEngins { get; } = [];
    public ObservableCollection<GardeSlot> GardeReserve { get; } = [];

    public ObservableCollection<string> PlanningCentres { get; } = [];
    public ObservableCollection<PlanningDay> PlanningDays { get; } = [];
    public ObservableCollection<PlanningRow> PlanningRows { get; } = [];
    public ObservableCollection<PlanningCell> PlanningTotals { get; } = [];

    public RelayCommand GardePrevCommand { get; private set; } = null!;
    public RelayCommand GardeNextCommand { get; private set; } = null!;
    public RelayCommand GardeTodayCommand { get; private set; } = null!;
    public RelayCommand GardeSaveCommand { get; private set; } = null!;
    public RelayCommand GardeValidateCommand { get; private set; } = null!;
    public RelayCommand GardeCopyCommand { get; private set; } = null!;
    public RelayCommand GardePrintCommand { get; private set; } = null!;
    public RelayCommand GardeAddReserveCommand { get; private set; } = null!;
    public RelayCommand GardeReloadCommand { get; private set; } = null!;
    public RelayCommand PlanningPrevCommand { get; private set; } = null!;
    public RelayCommand PlanningNextCommand { get; private set; } = null!;
    public RelayCommand PlanningTodayCommand { get; private set; } = null!;
    public RelayCommand PlanningWeekCommand { get; private set; } = null!;
    public RelayCommand PlanningMonthCommand { get; private set; } = null!;

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private void InitGarde()
    {
        GardePrevCommand = new RelayCommand(() => GardeDate = GardeDate.AddDays(-1));
        GardeNextCommand = new RelayCommand(() => GardeDate = GardeDate.AddDays(1));
        GardeTodayCommand = new RelayCommand(() => GardeDate = DateTime.Today);
        GardeSaveCommand = new RelayCommand(() => _ = SaveGardeAsync(false), () => CanEditGarde);
        GardeValidateCommand = new RelayCommand(() => _ = SaveGardeAsync(true), () => CanEditGarde);
        GardeCopyCommand = new RelayCommand(CopyPreviousGarde, () => CanEditGarde);
        GardePrintCommand = new RelayCommand(() => _desktop.OpenWindow(SigtaoWindows.GardePrint(_desktop, CurrentFeuille())));
        GardeAddReserveCommand = new RelayCommand(() =>
        {
            GardeReserve.Add(NewReserveSlot(PersonnelOption.None));
            GardeChanged();
        }, () => CanEditGarde);
        GardeReloadCommand = new RelayCommand(() => LoadGarde(force: true));

        PlanningPrevCommand = new RelayCommand(() => PlanningStart = _planningMonth ? PlanningStart.AddMonths(-1) : PlanningStart.AddDays(-7));
        PlanningNextCommand = new RelayCommand(() => PlanningStart = _planningMonth ? PlanningStart.AddMonths(1) : PlanningStart.AddDays(7));
        PlanningTodayCommand = new RelayCommand(() => PlanningStart = _planningMonth ? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1) : StartOfWeek(DateTime.Today));
        PlanningWeekCommand = new RelayCommand(() => { _planningMonth = false; PlanningStart = StartOfWeek(PlanningStart); RaisePlanningMode(); });
        PlanningMonthCommand = new RelayCommand(() => { _planningMonth = true; PlanningStart = new DateTime(PlanningStart.Year, PlanningStart.Month, 1); RaisePlanningMode(); });

        if (ArgosSession.Current is { } session)
        {
            session.Remise.Changed += OnRemiseChanged;
            if (!session.Remise.IsLoaded) _ = session.Remise.RefreshAsync();
        }
    }

    private void DisposeGarde()
    {
        if (ArgosSession.Current is { } session)
            session.Remise.Changed -= OnRemiseChanged;
    }

    private void OnRemiseChanged()
    {
        BuildGarde();
        BuildPlanning();
    }

    private static DateTime StartOfWeek(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    private List<FleetEngin> Fleet => ArgosSession.Current?.Remise.Snapshot.Engins ?? [];

    // ===== Feuille de garde =====

    public bool CanEditGarde => Rights.ManageGarde && !_gardeBusy;
    public bool IsGardeReadOnly => !Rights.ManageGarde;

    public DateTime GardeDate
    {
        get => _gardeDate;
        set
        {
            if (!SetProperty(ref _gardeDate, value.Date)) return;
            OnPropertyChanged(nameof(GardeDateText));
            LoadGarde(force: true);
        }
    }

    public DateTime? GardeDateSelected
    {
        get => _gardeDate;
        set { if (value is { } v) GardeDate = v.Date; }
    }

    public string GardeDateText
    {
        get
        {
            var text = _gardeDate.ToString("dddd d MMMM yyyy", Fr);
            return char.ToUpper(text[0], Fr) + text[1..];
        }
    }

    public string GardeCentre
    {
        get => _gardeCentre;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || !SetProperty(ref _gardeCentre, value)) return;
            LoadGarde(force: true);
        }
    }

    public string GardeObservations
    {
        get => _gardeObservations;
        set { if (SetProperty(ref _gardeObservations, value ?? string.Empty)) GardeChanged(); }
    }

    public string GardeHoraires
    {
        get => _gardeHoraires;
        set { if (SetProperty(ref _gardeHoraires, value ?? string.Empty)) GardeChanged(); }
    }

    public string GardeStatus
    {
        get
        {
            if (_gardeSaved is null) return _gardeDirty ? "Nouvelle feuille - non enregistrée" : "Aucune feuille enregistrée pour ce jour";
            var at = _gardeSaved.UpdatedAt.ToLocalTime().ToString("dd/MM à HH:mm");
            var status = _gardeSaved.IsValidated
                ? $"Validée par {_gardeSaved.ValidatedBy} le {_gardeSaved.ValidatedAt?.ToLocalTime():dd/MM à HH:mm}"
                : $"En préparation · enregistrée par {_gardeSaved.UpdatedBy} le {at}";
            return _gardeDirty ? status + " · modifications non enregistrées" : status;
        }
    }

    public bool GardeIsValidated => _gardeSaved?.IsValidated == true && !_gardeDirty;
    public bool HasGardeReserve => GardeReserve.Count > 0;

    public string GardeEffectif
    {
        get
        {
            var feuille = CurrentFeuille();
            var nonArmes = feuille.Engins.Count(e => !e.Arme);
            var vacants = feuille.Engins.Where(e => e.Arme).Sum(e => e.Postes.Count(p => !p.Pourvu));
            return $"Effectif de garde : {feuille.Effectif}" +
                   (vacants > 0 ? $" · {vacants} poste(s) non pourvu(s)" : string.Empty) +
                   (nonArmes > 0 ? $" · {nonArmes} engin(s) non armé(s)" : string.Empty);
        }
    }

    private void GardeChanged()
    {
        _gardeDirty = true;
        foreach (var e in GardeEngins) e.Refresh();
        OnPropertyChanged(nameof(GardeStatus));
        OnPropertyChanged(nameof(GardeIsValidated));
        OnPropertyChanged(nameof(GardeEffectif));
        OnPropertyChanged(nameof(HasGardeReserve));
    }

    /// <summary>Après un rafraîchissement : listes à jour ; la feuille n'est rechargée que si rien n'est en cours de saisie.</summary>
    private void BuildGarde()
    {
        var centres = Fleet.Where(f => f.Active).Select(f => f.Centre).Where(c => c.Length > 0)
            .Concat(EnginsCatalog.All.Select(e => e.Centre)).Distinct().OrderBy(c => c == EnginsCatalog.Creteil ? 0 : 1).ThenBy(c => c).ToList();
        if (!centres.SequenceEqual(GardeCentres)) Fill(GardeCentres, centres);
        var planningCentres = new[] { "Tous" }.Concat(centres).ToList();
        if (!planningCentres.SequenceEqual(PlanningCentres))
        {
            Fill(PlanningCentres, planningCentres);
            OnPropertyChanged(nameof(PlanningCentre));
        }

        var options = new[] { PersonnelOption.None }.Concat(Snapshot.Military
                .Where(m => m.Status == MilitaryStatuses.Actif)
                .Select(m => new PersonnelOption(m.Id, $"{BsppGrades.Short(m.Grade)} {m.Nom.ToUpperInvariant()} {m.Prenom}".Trim()
                                                       + (string.IsNullOrWhiteSpace(m.Affectation) ? string.Empty : $" · {m.Affectation}"))))
            .ToList();
        if (!options.SequenceEqual(GardePersonnel))
        {
            if (_gardeDirty)
            {
                // Saisie en cours : on ajoute les nouveaux sans rien retirer (les sélections restent valides).
                foreach (var o in options.Where(o => !GardePersonnel.Contains(o))) GardePersonnel.Add(o);
            }
            else Fill(GardePersonnel, options);
        }

        foreach (var c in new[] { GardeSaveCommand, GardeValidateCommand, GardeCopyCommand, GardeAddReserveCommand })
            c.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanEditGarde));
        OnPropertyChanged(nameof(IsGardeReadOnly));
        LoadGarde(force: false);
    }

    private PersonnelOption Option(string? id)
        => string.IsNullOrWhiteSpace(id) ? PersonnelOption.None
            : GardePersonnel.FirstOrDefault(o => o.Id == id) ?? AddMissing(id);

    private PersonnelOption AddMissing(string id)
    {
        // Militaire parti depuis : on garde son nom tel qu'enregistré sur la feuille.
        var nom = _gardeSaved?.Affectations().FirstOrDefault(a => a.Affectation.MilitaryId == id).Affectation?.Nom ?? "Militaire inconnu";
        var option = new PersonnelOption(id, nom);
        GardePersonnel.Add(option);
        return option;
    }

    private void LoadGarde(bool force)
    {
        var key = FeuilleGarde.BuildId(_gardeDate.ToString("yyyy-MM-dd"), _gardeCentre);
        var saved = Snapshot.Gardes.FirstOrDefault(g => g.Id == key);
        if (!force && _gardeDirty && _gardeLoadedKey == key) return;
        if (!force && _gardeLoadedKey == key && ReferenceEquals(saved, _gardeSaved) && GardeEngins.Count > 0) return;
        _gardeLoadedKey = key;
        _gardeSaved = saved;
        Apply(saved ?? Blank());
        _gardeDirty = false;
        OnPropertyChanged(nameof(IAmOnGarde));
        OnPropertyChanged(nameof(CanSelfReserve));
        OnPropertyChanged(nameof(GardeStatus));
        OnPropertyChanged(nameof(GardeIsValidated));
        OnPropertyChanged(nameof(GardeEffectif));
        OnPropertyChanged(nameof(GardeDateSelected));
    }

    /// <summary>Feuille vierge : personnels de service et engins actifs du centre, avec les postes de leur type.</summary>
    private FeuilleGarde Blank() => new()
    {
        Date = _gardeDate.ToString("yyyy-MM-dd"),
        Centre = _gardeCentre,
        Service = GardePostes.Service.Select(p => new GardeAffectation { Poste = p }).ToList(),
        Engins = FleetOf(_gardeCentre).Select(f => new GardeEngin
        {
            Engin = f.Nom, Type = f.Type,
            Postes = GardePostes.For(f.Type).Select(p => new GardeAffectation { Poste = p }).ToList()
        }).ToList()
    };

    private static int CentreRank(string centre) => centre switch { EnginsCatalog.Creteil => 0, EnginsCatalog.MaisonsAlfort => 1, EnginsCatalog.Mass => 2, _ => 3 };

    private List<FleetEngin> FleetOf(string centre)
    {
        var all = centre == FeuilleGarde.TousCentres;
        var fleet = Fleet.Where(f => f.Active && (all || f.Centre == centre)).OrderBy(f => CentreRank(f.Centre)).ThenBy(f => f.Ordre).ToList();
        if (fleet.Count > 0) return fleet;
        // REMISE pas encore chargée : catalogue de départ.
        return EnginsCatalog.All.Where(e => all || e.Centre == centre)
            .Select(e => new FleetEngin { Nom = FleetEngin.BuildNom(e.Indicatif, e.Centre), Indicatif = e.Indicatif, Type = e.Type, Libelle = e.Libelle, Centre = e.Centre })
            .ToList();
    }

    private void Apply(FeuilleGarde f)
    {
        var editable = Rights.ManageGarde;
        _gardeObservations = f.Observations;
        _gardeHoraires = string.IsNullOrWhiteSpace(f.Horaires) ? "07h30 → 07h30 (J+1)" : f.Horaires;
        OnPropertyChanged(nameof(GardeObservations));
        OnPropertyChanged(nameof(GardeHoraires));

        var service = GardePostes.Service.Select(p => f.Service.FirstOrDefault(a => a.Poste == p) ?? new GardeAffectation { Poste = p })
            .Concat(f.Service.Where(a => !GardePostes.Service.Contains(a.Poste)));
        Fill(GardeService, service.Select(a => Slot(a.Poste, a.MilitaryId, editable, null, null)));

        // Engins : ceux de la feuille, puis les engins actifs du centre qui n'y figurent pas encore.
        var engins = f.Engins.ToList();
        foreach (var fe in FleetOf(f.Centre).Where(fe => !engins.Any(e => string.Equals(e.Engin, fe.Nom, StringComparison.OrdinalIgnoreCase))))
            engins.Add(new GardeEngin { Engin = fe.Nom, Type = fe.Type, Postes = GardePostes.For(fe.Type).Select(p => new GardeAffectation { Poste = p }).ToList() });
        Fill(GardeEngins, engins.Select(e =>
        {
            var fleet = Fleet.FirstOrDefault(x => string.Equals(x.Nom, e.Engin, StringComparison.OrdinalIgnoreCase));
            var availability = fleet is null ? string.Empty
                : fleet.Availability == FleetAvailability.Disponible ? FleetAvailability.Disponible
                : $"{fleet.Availability}{(string.IsNullOrWhiteSpace(fleet.AvailabilityReason) ? string.Empty : " - " + fleet.AvailabilityReason)}";
            var slots = e.Postes.Select(a => Slot(a.Poste, a.MilitaryId, editable, e.Engin, e.Type)).ToList();
            return new GardeEnginVm(e.Engin, e.Type, fleet?.Libelle ?? string.Empty, availability, e.Arme, slots, editable, GardeChanged);
        }));

        Fill(GardeReserve, f.Reserve.Select(a => NewReserveSlot(Option(a.MilitaryId))));
        OnPropertyChanged(nameof(HasGardeReserve));
    }

    private GardeSlot Slot(string poste, string? militaryId, bool editable, string? engin, string? type)
    {
        return new GardeSlot(poste, Option(militaryId), GardePersonnel, editable, GardeChanged)
        {
            Engin = engin,
            Type = type,
            SelfCommand = HasMe ? new RelayCommand(() => _ = SelfAsync(engin, type, poste, false)) : null
        };
    }

    // ===== Inscription personnelle (son centre ou un autre CS) =====

    public bool IAmOnGarde => Snapshot.MyDossierId is { } me && (_gardeSaved?.Affectations().Any(a => a.Affectation.MilitaryId == me) ?? false);
    public bool CanSelfReserve => HasMe && !IAmOnGarde;
    public RelayCommand GardeSelfReserveCommand => new(() => _ = SelfAsync(null, null, "Renfort", false));
    public RelayCommand GardeSelfRemoveCommand => new(() => _ = SelfAsync(null, null, string.Empty, true));

    private async Task SelfAsync(string? engin, string? type, string poste, bool retirer)
    {
        var session = ArgosSession.Current;
        if (session is null || _gardeBusy) return;
        if (_gardeDirty && Rights.ManageGarde)
        {
            ShowMessage("Enregistre d'abord la feuille en cours de saisie.", true);
            return;
        }
        _gardeBusy = true;
        try
        {
            var result = await session.Api.SendAsync(HttpMethod.Post, "api/sigtao/gardes/moi", new SelfGardeRequest
            {
                Date = _gardeDate.ToString("yyyy-MM-dd"), Centre = _gardeCentre, Engin = engin, Type = type, Poste = poste, Retirer = retirer
            });
            if (!result.Ok)
            {
                ShowMessage(result.Error ?? "Inscription refusée.", true);
                return;
            }
            await session.Sigtao.RefreshAsync();
            LoadGarde(force: true);
            ShowMessage(retirer ? $"Tu es retiré de la garde du {_gardeDate:dd/MM}."
                : $"Inscrit sur la garde du {_gardeDate:dd/MM} : {(engin is null ? poste : $"{poste} {engin}")}." +
                  (_gardeSaved?.IsValidated == true ? " La feuille était déjà validée : l'encadrement la revalidera pour armer l'engin." : string.Empty), false);
        }
        finally
        {
            _gardeBusy = false;
        }
    }

    private GardeSlot NewReserveSlot(PersonnelOption selected)
        => new("Renfort", selected, GardePersonnel, Rights.ManageGarde, GardeChanged, slot =>
        {
            GardeReserve.Remove(slot);
            GardeChanged();
        });

    private FeuilleGarde CurrentFeuille() => new()
    {
        Id = FeuilleGarde.BuildId(_gardeDate.ToString("yyyy-MM-dd"), _gardeCentre),
        Date = _gardeDate.ToString("yyyy-MM-dd"),
        Centre = _gardeCentre,
        Horaires = _gardeHoraires,
        Observations = _gardeObservations,
        Service = GardeService.Select(s => s.ToAffectation()).ToList(),
        Engins = GardeEngins.Select(e => e.ToGardeEngin()).ToList(),
        Reserve = GardeReserve.Where(r => r.Selected.Id.Length > 0).Select(r => r.ToAffectation()).ToList(),
        Statut = _gardeSaved?.Statut ?? GardeStatuts.Brouillon,
        ValidatedBy = _gardeSaved?.ValidatedBy ?? string.Empty,
        ValidatedAt = _gardeSaved?.ValidatedAt,
        UpdatedBy = _gardeSaved?.UpdatedBy ?? string.Empty,
        UpdatedAt = _gardeSaved?.UpdatedAt ?? DateTimeOffset.Now
    };

    /// <summary>Reprend les affectations de la dernière feuille enregistrée du centre (même poste, même engin).</summary>
    private void CopyPreviousGarde()
    {
        var date = _gardeDate.ToString("yyyy-MM-dd");
        var previous = Snapshot.Gardes.Where(g => g.Centre == _gardeCentre && string.CompareOrdinal(g.Date, date) < 0)
            .OrderByDescending(g => g.Date).FirstOrDefault();
        if (previous is null)
        {
            ShowMessage("Aucune feuille de garde précédente pour ce centre.", true);
            return;
        }
        foreach (var s in GardeService)
            if (previous.Service.FirstOrDefault(a => a.Poste == s.Poste) is { Pourvu: true } a) s.Selected = Option(a.MilitaryId);
        foreach (var e in GardeEngins)
        {
            var pe = previous.Engins.FirstOrDefault(x => string.Equals(x.Engin, e.Engin, StringComparison.OrdinalIgnoreCase));
            if (pe is null) continue;
            e.Arme = pe.Arme;
            foreach (var s in e.Slots)
                if (pe.Postes.FirstOrDefault(a => a.Poste == s.Poste) is { Pourvu: true } a) s.Selected = Option(a.MilitaryId);
        }
        Fill(GardeReserve, previous.Reserve.Select(a => NewReserveSlot(Option(a.MilitaryId))));
        GardeChanged();
        var day = DateTime.ParseExact(previous.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        ShowMessage($"Garde du {day:dd/MM/yyyy} reprise : vérifie les présents puis enregistre.", false);
    }

    private async Task SaveGardeAsync(bool valider)
    {
        var session = ArgosSession.Current;
        if (session is null || _gardeBusy) return;
        _gardeBusy = true;
        GardeSaveCommand.RaiseCanExecuteChanged();
        GardeValidateCommand.RaiseCanExecuteChanged();
        try
        {
            var result = await session.Api.SendAsync(HttpMethod.Post, "api/sigtao/gardes", new SaveGardeRequest { Feuille = CurrentFeuille(), Valider = valider });
            if (!result.Ok)
            {
                ShowMessage(result.Error ?? "Enregistrement refusé.", true);
                return;
            }
            _gardeDirty = false;
            await session.Sigtao.RefreshAsync();
            if (valider) await session.Remise.RefreshAsync();
            LoadGarde(force: true);
            var text = valider
                ? $"Feuille de garde du {_gardeDate:dd/MM/yyyy} validée : engins armés dans ADAGIO REMISE."
                : $"Feuille de garde du {_gardeDate:dd/MM/yyyy} enregistrée.";
            if (!string.IsNullOrWhiteSpace(result.Error)) text += " Attention : " + result.Error;
            ShowMessage(text, !string.IsNullOrWhiteSpace(result.Error));
            if (valider) _desktop.ShowNotification("SIGTAO", $"Garde du {_gardeDate:dd/MM} validée.");
        }
        finally
        {
            _gardeBusy = false;
            GardeSaveCommand.RaiseCanExecuteChanged();
            GardeValidateCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Ouvre la feuille d'un jour (clic sur une colonne du planning).</summary>
    private void OpenGardeDay(DateTime day)
    {
        _gardeDate = day.Date;
        OnPropertyChanged(nameof(GardeDateText));
        LoadGarde(force: true);
        CurrentPage = Page.Garde;
    }

    // ===== Planning de garde =====

    public DateTime PlanningStart
    {
        get => _planningStart;
        set
        {
            if (!SetProperty(ref _planningStart, value.Date)) return;
            BuildPlanning();
        }
    }

    public string PlanningCentre
    {
        get => _planningCentre;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || !SetProperty(ref _planningCentre, value)) return;
            BuildPlanning();
        }
    }

    public bool IsPlanningWeek => !_planningMonth;
    public bool IsPlanningMonth => _planningMonth;

    public string PlanningTitle
    {
        get
        {
            if (_planningMonth)
            {
                var m = _planningStart.ToString("MMMM yyyy", Fr);
                return char.ToUpper(m[0], Fr) + m[1..];
            }
            var end = _planningStart.AddDays(6);
            return $"Semaine du {_planningStart:dd/MM} au {end:dd/MM/yyyy}";
        }
    }

    public string PlanningSummary { get; private set; } = string.Empty;

    private void RaisePlanningMode()
    {
        OnPropertyChanged(nameof(IsPlanningWeek));
        OnPropertyChanged(nameof(IsPlanningMonth));
        BuildPlanning();
    }

    private static readonly IBrush CellEmpty = Brushes.White;
    private static readonly IBrush CellValidated = new SolidColorBrush(Color.Parse("#DCE8F7"));
    private static readonly IBrush CellDraft = new SolidColorBrush(Color.Parse("#FFF4CE"));
    private static readonly IBrush CellService = new SolidColorBrush(Color.Parse("#E3D7F2"));
    private static readonly IBrush TextDark = new SolidColorBrush(Color.Parse("#13294B"));
    private static readonly IBrush TextMuted = new SolidColorBrush(Color.Parse("#8A96A5"));
    private static readonly IBrush DayNormal = new SolidColorBrush(Color.Parse("#DDE3EA"));
    private static readonly IBrush DayWeekend = new SolidColorBrush(Color.Parse("#C9D1DC"));
    private static readonly IBrush DayToday = new SolidColorBrush(Color.Parse("#F4C7CE"));

    private void BuildPlanning()
    {
        var days = _planningMonth
            ? Enumerable.Range(0, DateTime.DaysInMonth(_planningStart.Year, _planningStart.Month)).Select(i => _planningStart.AddDays(i)).ToList()
            : Enumerable.Range(0, 7).Select(i => _planningStart.AddDays(i)).ToList();
        var width = _planningMonth ? 46 : 116;
        var keys = days.Select(d => d.ToString("yyyy-MM-dd")).ToList();
        var gardes = Snapshot.Gardes.Where(g => keys.Contains(g.Date) && (_planningCentre == "Tous" || g.Centre == _planningCentre)).ToList();

        Fill(PlanningDays, days.Select(d =>
        {
            var day = d;
            var weekend = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var top = _planningMonth ? d.ToString("ddd", Fr)[..2].ToUpperInvariant() : d.ToString("dddd", Fr);
            var bottom = _planningMonth ? d.Day.ToString() : d.ToString("dd/MM");
            return new PlanningDay(top, bottom, width, d == DateTime.Today ? DayToday : weekend ? DayWeekend : DayNormal,
                new RelayCommand(() => OpenGardeDay(day)));
        }));

        // Où est chaque militaire chaque jour : (jour, militaire) → affectations (RP : plusieurs engins possibles).
        var map = new Dictionary<(string, string), List<(GardeAffectation A, string? Engin, FeuilleGarde F)>>();
        foreach (var g in gardes)
            foreach (var (a, engin) in g.Affectations())
            {
                if (!map.TryGetValue((g.Date, a.MilitaryId), out var list)) map[(g.Date, a.MilitaryId)] = list = [];
                list.Add((a, engin, g));
            }

        string Indicatif(string? engin, string centre) => engin is null ? string.Empty
            : Fleet.FirstOrDefault(f => string.Equals(f.Nom, engin, StringComparison.OrdinalIgnoreCase))?.Indicatif ?? engin.Replace(" " + centre, string.Empty);

        var rows = new List<PlanningRow>();
        foreach (var m in Snapshot.Military.Where(m => m.Status == MilitaryStatuses.Actif))
        {
            var total = 0;
            var cells = keys.Select(k =>
            {
                if (!map.TryGetValue((k, m.Id), out var hits)) return new PlanningCell(string.Empty, string.Empty, CellEmpty, TextMuted, width);
                total++;
                var parts = hits.Select(h =>
                {
                    var indicatif = Indicatif(h.Engin, h.F.Centre);
                    return (Short: GardePostes.Short(h.A.Poste), Long: indicatif.Length == 0 ? h.A.Poste : $"{GardePostes.Short(h.A.Poste)} · {indicatif}",
                        Tip: $"{h.F.Centre} - {h.A.Poste}{(indicatif.Length > 0 ? " " + indicatif : string.Empty)}");
                }).ToList();
                var text = _planningMonth ? string.Join("+", parts.Select(p => p.Short)) : string.Join(" / ", parts.Select(p => p.Long));
                var first = hits[0];
                var tip = string.Join("\n", parts.Select(p => p.Tip)) + (first.F.IsValidated ? "\n(garde validée)" : "\n(en préparation)");
                var bg = !first.F.IsValidated ? CellDraft : hits.All(h => h.Engin is null) ? CellService : CellValidated;
                return new PlanningCell(text, tip, bg, TextDark, width);
            }).ToList();
            rows.Add(new PlanningRow($"{m.Nom.ToUpperInvariant()} {m.Prenom}".Trim(), BsppGrades.Short(m.Grade), cells, total));
        }
        Fill(PlanningRows, rows);

        Fill(PlanningTotals, keys.Select(k =>
        {
            var count = gardes.Where(g => g.Date == k).Sum(g => g.Effectif);
            var unarmed = gardes.Where(g => g.Date == k).Sum(g => g.Engins.Count(e => !e.Arme));
            return new PlanningCell(count == 0 ? "—" : count.ToString(), unarmed > 0 ? $"{unarmed} engin(s) non armé(s)" : $"{count} personnel(s) de garde",
                unarmed > 0 ? CellDraft : CellEmpty, unarmed > 0 ? new SolidColorBrush(Color.Parse("#A4262C")) : TextDark, width);
        }));

        PlanningSummary = $"{gardes.Count} feuille(s) de garde · {gardes.Count(g => g.IsValidated)} validée(s) · " +
                          $"{rows.Count(r => r.Total > 0)} militaire(s) de garde sur la période. Clic sur un jour : ouvrir sa feuille de garde.";
        OnPropertyChanged(nameof(PlanningSummary));
        OnPropertyChanged(nameof(PlanningTitle));
    }
}
