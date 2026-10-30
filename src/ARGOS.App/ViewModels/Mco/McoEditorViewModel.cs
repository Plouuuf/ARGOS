using System.Collections.ObjectModel;
using System.Text.Json;
using Argos.Services;
using Argos.Shared.Contracts;
using Argos.ViewModels.Ode;

namespace Argos.ViewModels.Mco;

/// <summary>Personnel de l'équipage (ligne modifiable).</summary>
public sealed class CrewMemberViewModel(McoPerson person, Action<CrewMemberViewModel> remove) : ViewModelBase
{
    public McoPerson Person { get; } = person;
    public string Grade { get => Person.Grade; set { Person.Grade = value ?? string.Empty; OnPropertyChanged(); } }
    public string Nom { get => Person.Nom; set { Person.Nom = value ?? string.Empty; OnPropertyChanged(); } }
    public string Prenom { get => Person.Prenom; set { Person.Prenom = value ?? string.Empty; OnPropertyChanged(); } }
    public string Fonction { get => Person.Fonction; set { Person.Fonction = value ?? string.Empty; OnPropertyChanged(); } }
    public IReadOnlyList<string> Grades => McoFonctions.Grades;
    public IReadOnlyList<string> Fonctions => McoFonctions.All;

    private RelayCommand? _remove;
    public RelayCommand RemoveCommand => _remove ??= new RelayCommand(() => remove(this));
}

/// <summary>Événement de la chronologie (ligne modifiable).</summary>
public sealed class McoEventViewModel(McoEvent ev, Action<McoEventViewModel> remove) : ViewModelBase
{
    public McoEvent Event { get; } = ev;
    public string Time { get => Event.Time; set { Event.Time = value ?? string.Empty; OnPropertyChanged(); } }
    public string Text { get => Event.Text; set { Event.Text = value ?? string.Empty; OnPropertyChanged(); } }

    private RelayCommand? _remove;
    public RelayCommand RemoveCommand => _remove ??= new RelayCommand(() => remove(this));
}

/// <summary>Fiche eFiBi trouvée pour l'intervention : consulter, ou associer à la MCO.</summary>
public sealed record EfibiLink(long Id, string Label, RelayCommand OpenCommand, RelayCommand AttachCommand);

/// <summary>Intervention ADAGIO ODE proposée pour rattacher la MCO.</summary>
public sealed record InterventionChoice(string Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Rédaction d'une MCO : identification, horodatage, engin et personnel, déroulement, bilan, synthèse,
/// validation. Pré-remplie depuis l'intervention ADAGIO ODE ; figée une fois validée.
/// </summary>
public sealed class McoEditorViewModel : ViewModelBase, IDisposable, ICloseGuard
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly DesktopViewModel _desktop;
    private readonly string _key;
    private McoReport _report = new();
    private string? _message;
    private bool _messageIsError;
    private bool _busy;
    private bool _dirty;
    private int _tab;
    private InterventionChoice? _selectedIntervention;
    private string _natureText = string.Empty;
    private string _efibiText = string.Empty;
    private string _otherEnginsText = string.Empty;

    public McoEditorViewModel(DesktopViewModel desktop, string? reportId, string? interventionId, string? engin, string windowKey)
    {
        _desktop = desktop;
        _key = windowKey;
        SaveCommand = new RelayCommand(() => _ = SaveAsync(), () => IsEditable);
        ValidateCommand = new RelayCommand(() => _ = ValidateAsync(), () => IsEditable && !IsNew);
        ArchiveCommand = new RelayCommand(() => _ = ArchiveAsync(), () => _report.Status == McoStatuses.Validee);
        CloseCommand = new RelayCommand(RequestClose);
        SaveAndCloseCommand = new RelayCommand(() => _ = SaveAndCloseAsync());
        DiscardCloseCommand = new RelayCommand(() => { ConfirmClose = false; _desktop.CloseWindow(_key); });
        CancelCloseCommand = new RelayCommand(() => ConfirmClose = false);
        SetChefFromSigtaoCommand = new RelayCommand(SetChefFromSigtao, () => IsEditable);
        AddCrewFromSigtaoCommand = new RelayCommand(AddCrewFromSigtao, () => IsEditable);
        _autosave = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _autosave.Tick += (_, _) => _ = AutoSaveAsync();
        _autosave.Start();
        PrefillCommand = new RelayCommand(Prefill, () => IsEditable);
        GenerateCommand = new RelayCommand(GenerateSynthese, () => IsEditable);
        AddCrewCommand = new RelayCommand(() => AddCrew(new McoPerson { Fonction = "Équipier" }), () => IsEditable);
        AddEventCommand = new RelayCommand(() => AddEvent(new McoEvent { Time = DateTime.Now.ToString("HH:mm") }), () => IsEditable);
        NextTabCommand = new RelayCommand(() => Tab = Math.Min(6, Tab + 1));
        PreviousTabCommand = new RelayCommand(() => Tab = Math.Max(0, Tab - 1));

        var session = ArgosSession.Current;
        if (session is not null)
        {
            session.Ode.Changed += LoadChoices;
            if (!session.Ode.IsLoaded) _ = session.Ode.RefreshAsync();
            session.Sigtao.Changed += LoadPersonnel;
            if (!session.Sigtao.IsLoaded) _ = session.Sigtao.RefreshAsync();
        }
        LoadChoices();
        LoadPersonnel();

        var existing = reportId is null ? null : session?.Mco.Find(reportId);
        if (existing is not null)
        {
            Load(Clone(existing));
        }
        else
        {
            var report = new McoReport { Date = DateTimeOffset.Now };
            var intervention = interventionId is null ? null : session?.Ode.FindIntervention(interventionId);
            if (intervention is not null)
                McoPrefill.FromIntervention(report, intervention, engin);
            Load(report);
            _selectedIntervention = InterventionChoices.FirstOrDefault(c => c.Id == report.InterventionId);
            OnPropertyChanged(nameof(SelectedIntervention));
            _dirty = intervention is not null;
        }
    }

    // ===== État =====

    public McoReport Report { get => _report; private set => SetProperty(ref _report, value); }
    public bool IsNew => string.IsNullOrEmpty(_report.Id);
    public bool IsEditable => _report.IsEditable;
    public string Title => IsNew ? "Nouvelle MCO" : $"{_report.Number} - {_report.Status}";
    public string StatusLine => _report.Status switch
    {
        McoStatuses.Brouillon => IsNew ? "Brouillon non enregistré" : $"Brouillon · modifié le {OdeFormat.Short(_report.UpdatedAt)} par {_report.UpdatedBy}",
        McoStatuses.Validee => $"Validée le {OdeFormat.Short(_report.ValidatedAt ?? _report.UpdatedAt)} par {_report.ValidatedBy} · lecture seule",
        _ => $"Archivée le {OdeFormat.Short(_report.ArchivedAt ?? _report.UpdatedAt)} · lecture seule"
    };

    public int Tab
    {
        get => _tab;
        set
        {
            if (!SetProperty(ref _tab, value)) return;
            RefreshChronologie();
            if (value == 6) RefreshChecks();
        }
    }

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

    // ===== Identification =====

    public ObservableCollection<InterventionChoice> InterventionChoices { get; } = [];
    public InterventionChoice? SelectedIntervention { get => _selectedIntervention; set => SetProperty(ref _selectedIntervention, value); }
    public ObservableCollection<string> EnginChoices { get; } = [];
    public IReadOnlyList<string> NatureChoices { get; } = Natures.All.Select(n => n.Display).ToList();

    public string NatureText
    {
        get => _natureText;
        set
        {
            if (!SetProperty(ref _natureText, value)) return;
            var code = (value ?? string.Empty).Split(" - ")[0].Trim();
            _report.NatureCode = Natures.Find(code)?.Code ?? code;
            Touch();
        }
    }

    public DateTime? ReportDate
    {
        get => _report.Date == default ? null : _report.Date.ToLocalTime().Date;
        set
        {
            if (value is { } d) _report.Date = new DateTimeOffset(d.Date, TimeZoneInfo.Local.GetUtcOffset(d.Date));
            OnPropertyChanged();
            Touch();
        }
    }

    // ===== Engin et personnel =====

    public ObservableCollection<CrewMemberViewModel> Crew { get; } = [];
    public IReadOnlyList<string> Grades => McoFonctions.Grades;
    public string OtherEnginsText { get => _otherEnginsText; set { if (SetProperty(ref _otherEnginsText, value)) Touch(); } }
    public string Effectif => $"Effectif total : {(string.IsNullOrWhiteSpace(_report.ChefAgres.Nom) ? 0 : 1) + Crew.Count(c => !string.IsNullOrWhiteSpace(c.Nom))}";

    // ===== Horodatage =====

    public ObservableCollection<McoEventViewModel> Events { get; } = [];
    public ObservableCollection<string> Chronologie { get; } = [];

    // ===== Bilan =====

    public string VictimText
    {
        get => _report.VictimCount.ToString();
        set
        {
            _report.VictimCount = int.TryParse(value, out var n) ? Math.Max(0, n) : 0;
            OnPropertyChanged();
            Touch();
        }
    }

    public string EfibiText { get => _efibiText; set { if (SetProperty(ref _efibiText, value)) Touch(); } }

    /// <summary>Fiches eFiBi déjà enregistrées pour ce numéro d'intervention (lecture seule).</summary>
    public ObservableCollection<EfibiLink> EfibiMatches { get; } = [];
    public string EfibiSearchStatus { get => _efibiStatus; private set => SetProperty(ref _efibiStatus, value); }
    private string _efibiStatus = string.Empty;
    public RelayCommand FindEfibiCommand => _findEfibi ??= new RelayCommand(() => _ = FindEfibiAsync());
    private RelayCommand? _findEfibi;

    private async Task FindEfibiAsync()
    {
        var session = ArgosSession.Current;
        if (session is null) return;
        if (string.IsNullOrWhiteSpace(_report.InterventionNumber))
        {
            EfibiSearchStatus = "Renseigne d'abord le numéro d'intervention (onglet Identification).";
            return;
        }
        try
        {
            var page = await session.Api.PostAsync<EfibiPage>("api/efibi/search",
                new EfibiQuery { Intervention = _report.InterventionNumber, PageSize = 50 });
            EfibiMatches.Clear();
            foreach (var f in page.Items)
            {
                var id = f.Id;
                EfibiMatches.Add(new EfibiLink(id, $"Fiche {f.Id} · {(string.IsNullOrWhiteSpace(f.VictimName) ? "victime non renseignée" : f.VictimName)} · {f.TransmissionStatus}",
                    new RelayCommand(() => _desktop.OpenWindow(Argos.Views.Apps.Efibi.EfibiWindows.Record(_desktop, id))),
                    new RelayCommand(() =>
                    {
                        var ids = EfibiText.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                        if (!ids.Contains(id.ToString())) ids.Add(id.ToString());
                        EfibiText = string.Join(", ", ids);
                    })));
            }
            EfibiSearchStatus = page.Error ?? (page.Items.Count == 0 ? "Aucune fiche eFiBi pour cette intervention." : $"{page.Items.Count} fiche(s) trouvée(s).");
        }
        catch (Exception ex)
        {
            EfibiSearchStatus = ex.Message.Contains("403") ? "Le rôle Discord eFIBI est nécessaire pour consulter les fiches." : "eFiBi injoignable.";
        }
    }

    // ===== Validation =====

    public ObservableCollection<string> Checks { get; } = [];
    public bool IsComplete => Checks.Count == 0;
    public IReadOnlyList<McoHistoryEntry> History => _report.History.AsEnumerable().Reverse().ToList();
    public string AuthorLine => IsNew
        ? $"Rédacteur : {ArgosSession.Current?.OperatorName}"
        : $"Rédacteur : {_report.AuthorName} · créée le {OdeFormat.Short(_report.CreatedAt)}";
    public string ValidationLine => _report.ValidatedAt is { } at
        ? $"Validée le {at.ToLocalTime():dd/MM/yyyy à HH:mm} par {_report.ValidatedBy}"
        : "Non validée";

    public RelayCommand SaveCommand { get; }
    public RelayCommand ValidateCommand { get; }
    public RelayCommand ArchiveCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand PrefillCommand { get; }
    public RelayCommand GenerateCommand { get; }
    public RelayCommand AddCrewCommand { get; }
    public RelayCommand AddEventCommand { get; }
    public RelayCommand NextTabCommand { get; }
    public RelayCommand PreviousTabCommand { get; }

    // ===== Chargement =====

    private void LoadChoices()
    {
        var snapshot = ArgosSession.Current?.Ode.Snapshot ?? new OdeSnapshot();
        var choices = snapshot.Interventions.OrderByDescending(i => i.CreatedAt)
            .Select(i => new InterventionChoice(i.Id,
                $"{i.Number} · {i.CreatedAt.ToLocalTime():dd/MM HH:mm} · {OdeFormat.Nature(i.NatureCode)} · {i.Address}"))
            .ToList();
        if (!choices.SequenceEqual(InterventionChoices))
        {
            var keep = SelectedIntervention?.Id ?? _report.InterventionId;
            InterventionChoices.Clear();
            foreach (var c in choices) InterventionChoices.Add(c);
            _selectedIntervention = InterventionChoices.FirstOrDefault(c => c.Id == keep);
            OnPropertyChanged(nameof(SelectedIntervention));
        }
        var engins = snapshot.Engins.Select(e => e.Nom).ToList();
        if (engins.Count == 0) engins = EnginsCatalog.All.Select(e => e.Nom).ToList();
        if (!engins.SequenceEqual(EnginChoices))
        {
            EnginChoices.Clear();
            foreach (var e in engins) EnginChoices.Add(e);
        }
    }

    private void Load(McoReport report)
    {
        Report = report;
        _savedJson = JsonSerializer.Serialize(report, Json);
        _natureText = Natures.Find(report.NatureCode)?.Display ?? report.NatureCode;
        _efibiText = string.Join(", ", report.EfibiIds);
        _otherEnginsText = string.Join("\n", report.OtherEngins);
        Crew.Clear();
        foreach (var p in report.Crew) Crew.Add(new CrewMemberViewModel(p, RemoveCrew));
        Events.Clear();
        foreach (var e in report.Events) Events.Add(new McoEventViewModel(e, RemoveEvent));
        _selectedIntervention = InterventionChoices.FirstOrDefault(c => c.Id == report.InterventionId);
        foreach (var name in new[] { nameof(NatureText), nameof(EfibiText), nameof(OtherEnginsText), nameof(ReportDate),
                                     nameof(VictimText), nameof(SelectedIntervention), nameof(IsNew), nameof(IsEditable),
                                     nameof(Title), nameof(StatusLine), nameof(History), nameof(AuthorLine),
                                     nameof(ValidationLine), nameof(Effectif) })
            OnPropertyChanged(name);
        foreach (var c in new[] { SaveCommand, ValidateCommand, ArchiveCommand, PrefillCommand, GenerateCommand, AddCrewCommand, AddEventCommand })
            c.RaiseCanExecuteChanged();
        RefreshChronologie();
        RefreshChecks();
    }

    /// <summary>Les rubriques reliées directement au compte-rendu (Report.X) : un changement suffit à marquer la MCO modifiée.</summary>
    public void Touch() => _dirty = true;

    // ===== Actions =====

    private void Prefill()
    {
        var session = ArgosSession.Current;
        var intervention = SelectedIntervention is null ? null : session?.Ode.FindIntervention(SelectedIntervention.Id);
        if (intervention is null)
        {
            ShowMessage("Choisis l'intervention ADAGIO ODE à reprendre.", true);
            return;
        }
        Sync();
        var copy = Clone(_report);
        McoPrefill.FromIntervention(copy, intervention, string.IsNullOrWhiteSpace(copy.Engin) ? null : copy.Engin);
        Load(copy);
        _dirty = true;
        ShowMessage($"Informations de l'intervention {intervention.Number} reprises depuis ADAGIO ODE.", false);
    }

    private void GenerateSynthese()
    {
        Sync();
        var copy = Clone(_report);
        copy.Synthese = McoPrefill.Synthese(copy);
        Load(copy);
        _dirty = true;
        ShowMessage("Synthèse générée : tu peux la modifier librement avant validation.", false);
    }

    private void AddCrew(McoPerson person)
    {
        Crew.Add(new CrewMemberViewModel(person, RemoveCrew));
        OnPropertyChanged(nameof(Effectif));
        _dirty = true;
    }

    private void RemoveCrew(CrewMemberViewModel member)
    {
        if (!IsEditable) return;
        Crew.Remove(member);
        OnPropertyChanged(nameof(Effectif));
        _dirty = true;
    }

    private void AddEvent(McoEvent ev)
    {
        Events.Add(new McoEventViewModel(ev, RemoveEvent));
        _dirty = true;
    }

    private void RemoveEvent(McoEventViewModel ev)
    {
        if (!IsEditable) return;
        Events.Remove(ev);
        RefreshChronologie();
        _dirty = true;
    }

    /// <summary>Recopie les listes modifiables dans le compte-rendu.</summary>
    private void Sync()
    {
        _report.Crew = Crew.Select(c => c.Person).ToList();
        _report.Events = Events.Select(e => e.Event).ToList();
        _report.EfibiIds = EfibiText.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        _report.OtherEngins = OtherEnginsText.Split(new[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        _report.InterventionId = SelectedIntervention?.Id ?? _report.InterventionId;
    }

    public void RefreshChronologie()
    {
        Sync();
        var items = new List<(string Time, string Text)>
        {
            (_report.TimeDepart, "Départ du centre"), (_report.TimeArrivee, "Arrivée sur les lieux"),
            (_report.TimeDebutOperations, "Début des opérations"), (_report.TimeFinOperations, "Fin des opérations"),
            (_report.TimeDisponible, "Disponible"), (_report.TimeRetour, "Retour au centre")
        };
        items.AddRange(_report.Events.Select(e => (e.Time, e.Text)));
        Chronologie.Clear();
        foreach (var (time, text) in items.Where(i => !string.IsNullOrWhiteSpace(i.Time) && !string.IsNullOrWhiteSpace(i.Text))
                     .OrderBy(i => i.Time, StringComparer.Ordinal))
            Chronologie.Add($"{time}  {text}");
    }

    private void RefreshChecks()
    {
        Sync();
        Checks.Clear();
        foreach (var m in McoPrefill.Missing(_report))
            Checks.Add("À compléter : " + m);
        OnPropertyChanged(nameof(IsComplete));
    }

    private async Task<bool> SaveAsync(bool quiet = false)
    {
        var session = ArgosSession.Current;
        if (session is null || _busy) return false;
        _busy = true;
        try
        {
            Sync();
            var result = await session.Api.SendAsync(HttpMethod.Post, "api/mco/reports", _report);
            if (!result.Ok)
            {
                ShowMessage(result.Error ?? "Enregistrement refusé.", true);
                return false;
            }
            await session.Mco.RefreshAsync();
            if (result.Id is { } id && session.Mco.Find(id) is { } saved)
                Load(Clone(saved));
            _dirty = false;
            if (!quiet) ShowMessage($"{_report.Number} enregistrée en brouillon.", false);
            return true;
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ValidateAsync()
    {
        var session = ArgosSession.Current;
        if (session is null) return;
        if (!await SaveAsync(quiet: true)) return;
        RefreshChecks();
        var result = await session.Api.SendAsync(HttpMethod.Post, $"api/mco/reports/{_report.Id}/validate");
        await session.Mco.RefreshAsync();
        if (session.Mco.Find(_report.Id) is { } saved)
            Load(Clone(saved));
        ShowMessage(result.Ok ? $"{_report.Number} validée définitivement." : result.Error ?? "Validation refusée.", !result.Ok);
    }

    private async Task ArchiveAsync()
    {
        var session = ArgosSession.Current;
        if (session is null) return;
        var result = await session.Api.SendAsync(HttpMethod.Post, $"api/mco/reports/{_report.Id}/archive");
        await session.Mco.RefreshAsync();
        if (session.Mco.Find(_report.Id) is { } saved)
            Load(Clone(saved));
        ShowMessage(result.Ok ? $"{_report.Number} archivée." : result.Error ?? "Archivage refusé.", !result.Ok);
    }

    private void ShowMessage(string text, bool error)
    {
        MessageIsError = error;
        Message = text;
    }

    private static McoReport Clone(McoReport r)
        => JsonSerializer.Deserialize<McoReport>(JsonSerializer.Serialize(r, Json), Json) ?? new McoReport();

    public bool HasUnsavedChanges => IsEditable && (_dirty || IsModified());

    // ===== Fermeture : ne pas perdre une MCO non enregistrée =====

    private string _savedJson = string.Empty;
    private bool _confirmClose;
    private readonly Avalonia.Threading.DispatcherTimer _autosave;
    public bool ConfirmClose { get => _confirmClose; set => SetProperty(ref _confirmClose, value); }
    public RelayCommand SaveAndCloseCommand { get; }
    public RelayCommand DiscardCloseCommand { get; }
    public RelayCommand CancelCloseCommand { get; }

    private bool IsModified()
    {
        Sync();
        return JsonSerializer.Serialize(_report, Json) != _savedJson;
    }

    /// <summary>Croix de la fenêtre : propose d'enregistrer le brouillon modifié.</summary>
    public bool TryClose()
    {
        if (!HasUnsavedChanges) return true;
        ConfirmClose = true;
        return false;
    }

    private void RequestClose()
    {
        if (HasUnsavedChanges) ConfirmClose = true;
        else _desktop.CloseWindow(_key);
    }

    private async Task SaveAndCloseAsync()
    {
        ConfirmClose = false;
        if (await SaveAsync(quiet: true)) _desktop.CloseWindow(_key);
    }

    /// <summary>Brouillon enregistré automatiquement chaque minute s'il a été modifié.</summary>
    private async Task AutoSaveAsync()
    {
        if (_busy || !HasUnsavedChanges || ConfirmClose) return;
        if (string.IsNullOrWhiteSpace(_report.InterventionNumber) && string.IsNullOrWhiteSpace(_report.Address)) return;
        if (await SaveAsync(quiet: true))
            ShowMessage($"Brouillon enregistré automatiquement à {DateTime.Now:HH:mm}.", false);
    }

    // ===== Personnel repris de SIGTAO =====

    public sealed record PersonnelChoice(string Grade, string Nom, string Prenom, string Fonction, string Label)
    {
        public override string ToString() => Label;
    }

    public ObservableCollection<PersonnelChoice> PersonnelChoices { get; } = [];
    private PersonnelChoice? _personnel;
    public PersonnelChoice? SelectedPersonnel { get => _personnel; set => SetProperty(ref _personnel, value); }
    public RelayCommand SetChefFromSigtaoCommand { get; }
    public RelayCommand AddCrewFromSigtaoCommand { get; }

    private void LoadPersonnel()
    {
        var records = ArgosSession.Current?.Sigtao.Snapshot.Military ?? [];
        PersonnelChoices.Clear();
        foreach (var r in records.Where(r => r.Status == MilitaryStatuses.Actif && !string.IsNullOrWhiteSpace(r.Nom))
                     .OrderBy(r => BsppGrades.Rank(r.Grade)).ThenBy(r => r.Nom))
        {
            var grade = BsppGrades.Short(r.Grade) + r.Echelon;
            PersonnelChoices.Add(new PersonnelChoice(grade, r.Nom.ToUpperInvariant(), r.Prenom, r.Fonction,
                $"{grade} {r.Nom.ToUpperInvariant()} {r.Prenom}" + (string.IsNullOrWhiteSpace(r.Fonction) ? "" : $" - {r.Fonction}")));
        }
    }

    private void SetChefFromSigtao()
    {
        if (SelectedPersonnel is not { } p) return;
        _report.ChefAgres.Grade = p.Grade;
        _report.ChefAgres.Nom = p.Nom;
        _report.ChefAgres.Prenom = p.Prenom;
        OnPropertyChanged(nameof(Report));
        var current = Report;
        Report = new McoReport();
        Report = current;
        Touch();
        OnPropertyChanged(nameof(Effectif));
        SelectedPersonnel = null;
    }

    private void AddCrewFromSigtao()
    {
        if (SelectedPersonnel is not { } p) return;
        AddCrew(new McoPerson { Grade = p.Grade, Nom = p.Nom, Prenom = p.Prenom, Fonction = "Équipier" });
        OnPropertyChanged(nameof(Effectif));
        SelectedPersonnel = null;
    }

    public void Dispose()
    {
        _autosave.Stop();
        if (ArgosSession.Current is { } session)
        {
            session.Ode.Changed -= LoadChoices;
            session.Sigtao.Changed -= LoadPersonnel;
        }
    }
}
