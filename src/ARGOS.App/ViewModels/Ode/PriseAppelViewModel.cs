using System.Collections.ObjectModel;
using Argos.Services;
using Argos.Shared.Contracts;
using Argos.Views.Apps.Ode;
using Avalonia.Threading;

namespace Argos.ViewModels.Ode;

/// <summary>
/// Fenêtre « Prise d'appel » (réplique de l'écran vLife) : une fiche d'appel en cours de saisie.
/// La saisie est enregistrée automatiquement ; les touches F1 à F9 qualifient l'appel.
/// </summary>
public sealed class PriseAppelViewModel : ViewModelBase, IDisposable
{
    public const string Version = "v1.8.0";

    private readonly DesktopViewModel _desktop;
    private readonly DispatcherTimer _clock;
    private CallRecord _call = new();
    private bool _loading = true;
    private bool _dirty;
    private bool _saving;
    private DateTime _lastEdit;

    private string _callerPhone = string.Empty;
    private string _implicatedPhone = string.Empty;
    private string _origin = CallOrigins.All[0];
    private string _mode = "NORMAL";
    private string _network = "ROUTIER/SITES";
    private string _streetNumber = string.Empty;
    private string? _street = string.Empty;
    private string _cstc = string.Empty;
    private string _alertAddress = string.Empty;
    private string _observations = string.Empty;
    private string? _motif = string.Empty;
    private string _reason = string.Empty;
    private string _callerName = string.Empty;
    private string _building = string.Empty;
    private string _access = string.Empty;
    private string _victimCount = string.Empty;
    private string? _priority = string.Empty;
    private string _risks = string.Empty;
    private string _instructions = string.Empty;
    private string? _finalNature = string.Empty;
    private bool _linkVisible;
    private InterventionChoice? _linkChoice;

    private string _durationText = "00:00:00";
    private string _statusTime = string.Empty;
    private string? _message;
    private bool _messageIsError;
    private bool _promptVisible;
    private string _promptTitle = string.Empty;
    private string _promptText = string.Empty;
    private string? _pendingProcedure;
    private bool _logVisible;
    private string _logText = string.Empty;

    public PriseAppelViewModel(DesktopViewModel desktop, string callId)
    {
        _desktop = desktop;
        CallId = callId;

        FKeyCommand = new RelayCommand(p => _ = OnFKeyAsync(p as string ?? string.Empty), _ => !IsClosed && !_loading);
        ConfirmLocationCommand = new RelayCommand(ConfirmLocation, () => !IsClosed);
        SaveCommand = new RelayCommand(() => _ = SaveAsync(null, true));
        TransmitCommand = new RelayCommand(() => _ = SaveAsync(CallStatuses.Transmis, true), () => !IsClosed);
        NewCallCommand = new RelayCommand(() => _ = OdeWindows.NewCallAsync(_desktop));
        CloseCommand = new RelayCommand(() => _ = CloseAsync());
        ShowLogCommand = new RelayCommand(() => LogVisible = !LogVisible);
        AddLogCommand = new RelayCommand(() => _ = AddLogAsync());
        PromptOkCommand = new RelayCommand(() => _ = ConfirmPromptAsync());
        PromptCancelCommand = new RelayCommand(() => PromptVisible = false);
        OpenInterventionCommand = new RelayCommand(OpenIntervention, () => HasIntervention);
        SansSuiteCommand = new RelayCommand(() => AskClose("SANS SUITE", "Classement sans suite"), () => !IsClosed && !_loading);
        ShowLinkCommand = new RelayCommand(ShowLink, () => !IsClosed && !_loading);
        LinkCommand = new RelayCommand(() => _ = LinkAsync());
        CancelLinkCommand = new RelayCommand(() => LinkVisible = false);
        AboutCommand = new RelayCommand(() => ShowMessage($"ADAGIO - Prise d'appel {Version}. Touches F1 à F9 : qualification de l'appel.", false));

        foreach (var nature in Natures.All)
            MotifSuggestions.Add(nature.Display);

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => Tick();
        _clock.Start();

        if (ArgosSession.Current is { } session)
            session.Ode.Changed += OnStoreChanged;

        Tick();
        _ = LoadAsync();
    }

    public string CallId { get; }
    public string WindowTitle => "Prise d'appel - " + Version;

    // ===== Listes =====

    public IReadOnlyList<string> Origins { get; } = CallOrigins.All;
    public IReadOnlyList<string> Modes { get; } = ["NORMAL", "DEGRADE"];
    public IReadOnlyList<string> Networks { get; } = ["ROUTIER/SITES", "AUTOROUTIER", "FERROVIAIRE", "FLUVIAL"];
    public ObservableCollection<string> MotifSuggestions { get; } = [];
    public ObservableCollection<string> StreetSuggestions { get; } = [];
    public IReadOnlyList<Procedures.ProcedureDefinition> FKeys { get; } = Procedures.All;

    // ===== Informations téléphoniques =====

    public string DecrocheText => _call.Status == CallStatuses.EnCours ? "EN COURS" : _call.Id;
    public string GroupeHoraire => _call.ReceivedAt == default ? string.Empty : OdeFormat.LongDateTime(_call.ReceivedAt);
    public string OperatorName => _call.OperatorName;
    public string Poste => _call.Poste;

    public string DurationText { get => _durationText; private set => SetProperty(ref _durationText, value); }

    /// <summary>Le chrono tourne (fond vert) tant que l'appel n'est pas terminé.</summary>
    public bool IsRunning => _call.EndedAt is null && !IsClosed;

    public string CallerPhone { get => _callerPhone; set => Edit(ref _callerPhone, value); }
    public string ImplicatedPhone { get => _implicatedPhone; set => Edit(ref _implicatedPhone, value); }
    public string Origin { get => _origin; set => Edit(ref _origin, value ?? CallOrigins.All[0]); }

    // ===== Localisation =====

    public string Mode { get => _mode; set => Edit(ref _mode, value ?? "NORMAL"); }
    public string Network { get => _network; set => Edit(ref _network, value ?? "ROUTIER/SITES"); }
    public string StreetNumber { get => _streetNumber; set => Edit(ref _streetNumber, value); }
    public string? Street { get => _street; set => Edit(ref _street, value); }
    public bool LocationConfirmed => _call.LocationConfirmed;

    // ===== Adresse de l'alerte =====

    public string Cstc { get => _cstc; set => Edit(ref _cstc, value); }
    public string AlertAddress { get => _alertAddress; set => Edit(ref _alertAddress, value); }
    public string Observations { get => _observations; set => Edit(ref _observations, value); }

    // ===== Motif =====

    /// <summary>Nature présumée (réception de l'appel).</summary>
    public string? Motif { get => _motif; set => Edit(ref _motif, value); }
    public string Reason { get => _reason; set => Edit(ref _reason, value); }

    // ===== Requérant et précisions =====

    public string CallerName { get => _callerName; set => Edit(ref _callerName, value); }
    public string Building { get => _building; set => Edit(ref _building, value); }
    public string Access { get => _access; set => Edit(ref _access, value); }
    public string VictimCount { get => _victimCount; set => Edit(ref _victimCount, value); }

    // ===== Qualification =====

    public IReadOnlyList<string> Priorities { get; } = CallPriorities.All;
    public string? Priority { get => _priority; set => Edit(ref _priority, value); }
    public string Risks { get => _risks; set => Edit(ref _risks, value); }
    public string Instructions { get => _instructions; set => Edit(ref _instructions, value); }
    /// <summary>Nature définitive (après qualification) ; vide tant que la nature présumée fait foi.</summary>
    public string? FinalNature { get => _finalNature; set => Edit(ref _finalNature, value); }

    // ===== Rattachement à une intervention existante =====

    public bool LinkVisible { get => _linkVisible; set => SetProperty(ref _linkVisible, value); }
    public ObservableCollection<InterventionChoice> LinkChoices { get; } = [];
    public InterventionChoice? LinkChoice { get => _linkChoice; set => SetProperty(ref _linkChoice, value); }
    public bool HasLinkChoices => LinkChoices.Count > 0;

    // ===== État =====

    public bool IsClosed => _call.Status is CallStatuses.Intervention or CallStatuses.SansSuite;
    public bool IsEditable => !IsClosed;
    public bool HasIntervention => !string.IsNullOrWhiteSpace(_call.InterventionId);

    public string ClosedBanner => _call.Status switch
    {
        CallStatuses.Intervention => $"Appel traité : départ engagé (procédure {Procedures.Find(_call.Procedure)?.Label}).",
        CallStatuses.SansSuite => $"Appel classé sans suite : {_call.ClosingReason}",
        _ => string.Empty
    };

    public string StatusDate => OdeFormat.StatusDate(DateTime.Now);
    public string StatusTime { get => _statusTime; private set => SetProperty(ref _statusTime, value); }
    public string StatusOperator => ArgosSession.Current?.OperatorName ?? _call.OperatorName;
    public string Platform => "Plate-forme Active : \\PRODUCTION";

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

    // Demande de motif (F5 INU, F9 adresse inexistante)
    public bool PromptVisible { get => _promptVisible; set => SetProperty(ref _promptVisible, value); }
    public string PromptTitle { get => _promptTitle; private set => SetProperty(ref _promptTitle, value); }
    public string PromptText { get => _promptText; set => SetProperty(ref _promptText, value); }

    // Main courante de l'appel
    public bool LogVisible { get => _logVisible; set => SetProperty(ref _logVisible, value); }
    public string LogText { get => _logText; set => SetProperty(ref _logText, value); }
    public ObservableCollection<OdeEvent> Events { get; } = [];

    public RelayCommand FKeyCommand { get; }
    public RelayCommand ConfirmLocationCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand TransmitCommand { get; }
    public RelayCommand NewCallCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand ShowLogCommand { get; }
    public RelayCommand AddLogCommand { get; }
    public RelayCommand PromptOkCommand { get; }
    public RelayCommand PromptCancelCommand { get; }
    public RelayCommand OpenInterventionCommand { get; }
    public RelayCommand AboutCommand { get; }
    public RelayCommand SansSuiteCommand { get; }
    public RelayCommand ShowLinkCommand { get; }
    public RelayCommand LinkCommand { get; }
    public RelayCommand CancelLinkCommand { get; }

    // ===== Chargement =====

    private async Task LoadAsync()
    {
        var session = ArgosSession.Current;
        if (session is null)
        {
            ShowMessage("Session fermée.", true);
            return;
        }

        var call = await session.Ode.LoadCallAsync(CallId);
        if (call is null)
        {
            ShowMessage("Fiche d'appel introuvable.", true);
            return;
        }

        ApplyCall(call);
        RefreshStreetSuggestions(session.Ode.Snapshot);
    }

    /// <summary>Affiche une fiche d'appel (chargement, et aperçu dans les tests d'affichage).</summary>
    public void ApplyCall(CallRecord call)
    {
        _call = call;
        _loading = true;
        _callerPhone = call.CallerPhone;
        _implicatedPhone = call.ImplicatedPhone;
        _origin = string.IsNullOrWhiteSpace(call.Origin) ? CallOrigins.All[0] : call.Origin;
        _mode = call.LocationMode;
        _network = call.LocationNetwork;
        _streetNumber = call.StreetNumber;
        _street = call.Street;
        _cstc = call.Cstc;
        _alertAddress = call.AlertAddress;
        _observations = call.Observations;
        _motif = Natures.Find(string.IsNullOrEmpty(call.PresumedNatureCode) ? call.NatureCode : call.PresumedNatureCode)?.Display ?? string.Empty;
        _finalNature = Natures.Find(call.FinalNatureCode)?.Display ?? string.Empty;
        _reason = call.Reason;
        _callerName = call.CallerName;
        _building = call.Building;
        _access = call.Access;
        _victimCount = call.VictimCount > 0 ? call.VictimCount.ToString() : string.Empty;
        _priority = call.Priority;
        _risks = call.Risks;
        _instructions = call.Instructions;
        _loading = false;

        RaiseAll();
    }

    private void OnStoreChanged()
    {
        var session = ArgosSession.Current;
        if (session is null) return;
        RefreshStreetSuggestions(session.Ode.Snapshot);

        // Seuls les champs gérés par le serveur sont repris (fin d'appel, statut, événements) :
        // la saisie en cours de l'opérateur n'est jamais écrasée.
        var fresh = session.Ode.FindCall(CallId);
        if (fresh is null || _loading) return;
        _call.Status = fresh.Status;
        _call.EndedAt = fresh.EndedAt;
        _call.InterventionId = fresh.InterventionId;
        _call.Procedure = fresh.Procedure;
        _call.ClosingReason = fresh.ClosingReason;
        _call.Id = fresh.Id;
        _call.Events = fresh.Events;
        RaiseState();
    }

    private void RefreshStreetSuggestions(OdeSnapshot snapshot)
    {
        // Plan VDMS d'abord (voie - quartier), puis les lieux déjà saisis qui n'y figurent pas (sites, ERP…).
        var map = VdmsMap.Streets.Select(s => s.Display).ToList();
        var known = snapshot.Calls.Select(c => c.Street)
            .Concat(snapshot.Interventions.Select(i => i.Address))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Where(s => VdmsMap.FindStreet(s) is null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.Create(OdeFormat.French, true));
        var all = map.Concat(known).ToList();
        if (all.SequenceEqual(StreetSuggestions)) return;
        StreetSuggestions.Clear();
        foreach (var s in all) StreetSuggestions.Add(s);
    }

    private void Tick()
    {
        StatusTime = DateTime.Now.ToString("HH:mm:ss", OdeFormat.French);
        if (_call.ReceivedAt != default)
            DurationText = OdeFormat.Duration((_call.EndedAt ?? DateTimeOffset.UtcNow) - _call.ReceivedAt);

        // Enregistrement automatique 2 s après la dernière frappe.
        if (_dirty && !_saving && DateTime.UtcNow - _lastEdit > TimeSpan.FromSeconds(2))
            _ = SaveAsync(null, false);
    }

    // ===== Saisie =====

    private void Edit<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!SetProperty(ref field, value, name) || _loading || IsClosed)
            return;
        _dirty = true;
        _lastEdit = DateTime.UtcNow;
    }

    private static NatureDefinition? NatureOf(string? text)
        => Natures.All.FirstOrDefault(n => string.Equals(n.Display, text?.Trim(), StringComparison.OrdinalIgnoreCase))
           ?? Natures.Find(text?.Split(' ', 2)[0]);

    private CallRecord BuildRecord()
    {
        var presumed = NatureOf(Motif);
        var final = NatureOf(FinalNature);
        var nature = final ?? presumed;
        return new CallRecord
        {
            Id = CallId,
            Origin = Origin,
            CallerPhone = CallerPhone,
            ImplicatedPhone = ImplicatedPhone,
            CallerName = CallerName,
            LocationMode = Mode,
            LocationNetwork = Network,
            StreetNumber = StreetNumber,
            Street = Street ?? string.Empty,
            LocationConfirmed = _call.LocationConfirmed,
            Cstc = Cstc,
            AlertAddress = AlertAddress,
            Locality = _call.Locality,
            Building = Building,
            Access = Access,
            Observations = Observations,
            PresumedNatureCode = presumed?.Code ?? string.Empty,
            FinalNatureCode = final?.Code ?? string.Empty,
            NatureCode = nature?.Code ?? string.Empty,
            Reason = Reason,
            VictimCount = int.TryParse(VictimCount, out var victims) ? Math.Max(0, victims) : 0,
            Priority = Priority ?? string.Empty,
            Risks = Risks,
            Instructions = Instructions
        };
    }

    /// <summary>Enregistre la fiche (et change son statut si demandé).</summary>
    public async Task<bool> SaveAsync(string? status, bool explicitSave)
    {
        var session = ArgosSession.Current;
        if (session is null || _loading) return false;
        if (IsClosed && status is null && !explicitSave) return true;

        _saving = true;
        _dirty = false;
        var record = BuildRecord();
        var path = "api/ode/calls/" + Uri.EscapeDataString(CallId) + (status is null ? string.Empty : "?status=" + Uri.EscapeDataString(status));
        var result = await session.Api.SendAsync(HttpMethod.Put, path, record);
        _saving = false;

        if (!result.Ok)
        {
            _dirty = true;
            ShowMessage(result.Error ?? "Enregistrement impossible.", true);
            return false;
        }

        _call.NatureCode = record.NatureCode;
        if (status is not null)
        {
            _call.Status = status;
            RaiseState();
        }
        if (explicitSave)
            ShowMessage(status == CallStatuses.Transmis ? "Appel transmis pour traitement." : "Fiche d'appel enregistrée.", false);
        return true;
    }

    private void ConfirmLocation()
    {
        var street = (Street ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(street))
        {
            ShowMessage("Renseigne la voie, le site ou la commune avant de confirmer.", true);
            return;
        }

        var number = StreetNumber.Trim();
        var known = VdmsMap.FindStreet(street);
        _call.LocationConfirmed = true;
        if (known is not null)
        {
            Street = known.Display;
            _call.Locality = known.District.ToUpperInvariant();
            AlertAddress = string.Join(" ", new[] { number, known.Name }.Where(s => s.Length > 0)).ToUpperInvariant();
        }
        else
        {
            _call.Locality = string.Empty;
            AlertAddress = string.Join(" ", new[] { number, street }.Where(s => s.Length > 0)).ToUpperInvariant();
        }
        if (string.IsNullOrWhiteSpace(Cstc))
            Cstc = EnginsCatalog.Creteil;
        OnPropertyChanged(nameof(LocationConfirmed));
        if (known is null)
            ShowMessage("Voie absente du plan VDMS : localisation enregistrée telle quelle (F9 si l'adresse n'existe pas).", true);
        else
            ShowMessage($"Localisation confirmée : {known.Name}, {known.District}.", false);
    }

    // ===== Touches F1 à F9 =====

    private async Task OnFKeyAsync(string key)
    {
        var procedure = Procedures.Find(key);
        if (procedure is null || IsClosed) return;

        if (procedure.Departure)
        {
            if (!await SaveAsync(null, false)) return;
            _desktop.OpenWindow(OdeWindows.CreateOde(_desktop, CallId, null, procedure.Code));
            return;
        }

        // Appel clos sans départ : on demande le motif.
        AskClose(procedure.Key, $"{procedure.Key} - {procedure.Label} : appel clos sans départ", procedure.Label);
    }

    private void AskClose(string procedure, string title, string text = "")
    {
        if (IsClosed) return;
        _pendingProcedure = procedure;
        PromptTitle = title;
        PromptText = text;
        PromptVisible = true;
    }

    // ===== Rattachement =====

    private void ShowLink()
    {
        LinkChoices.Clear();
        foreach (var i in (ArgosSession.Current?.Ode.Snapshot.Interventions ?? []).Where(i => i.Active).OrderByDescending(i => i.CreatedAt))
            LinkChoices.Add(new InterventionChoice(i.Id, $"{i.Number} - {OdeFormat.Nature(i.NatureCode)} - {i.Address}"));
        LinkChoice = LinkChoices.FirstOrDefault();
        OnPropertyChanged(nameof(HasLinkChoices));
        LinkVisible = true;
    }

    private async Task LinkAsync()
    {
        var session = ArgosSession.Current;
        if (session is null || LinkChoice is not { } choice) return;
        if (!await SaveAsync(null, false)) return;
        var result = await session.Api.SendAsync(HttpMethod.Post, $"api/ode/calls/{Uri.EscapeDataString(CallId)}/link",
            new LinkCallRequest(choice.Id));
        LinkVisible = false;
        if (!result.Ok)
        {
            ShowMessage(result.Error ?? "Rattachement refusé.", true);
            return;
        }
        _call.Status = CallStatuses.Intervention;
        _call.InterventionId = choice.Id;
        RaiseState();
        ShowMessage($"Appel rattaché à l'intervention {choice.Label.Split(' ')[0]}.", false);
    }

    /// <summary>Touche du clavier : F1…F9 déclenchent la procédure correspondante.</summary>
    public bool HandleKey(string keyName)
    {
        if (PromptVisible || Procedures.Find(keyName) is null || !FKeyCommand.CanExecute(keyName))
            return false;
        FKeyCommand.Execute(keyName);
        return true;
    }

    private async Task ConfirmPromptAsync()
    {
        var session = ArgosSession.Current;
        if (session is null || _pendingProcedure is null) return;
        if (string.IsNullOrWhiteSpace(PromptText))
        {
            ShowMessage("Indique le motif du classement.", true);
            return;
        }
        if (!await SaveAsync(null, false)) return;

        var result = await session.Api.SendAsync(HttpMethod.Post,
            $"api/ode/calls/{Uri.EscapeDataString(CallId)}/sans-suite",
            new SansSuiteRequest(_pendingProcedure, PromptText));
        PromptVisible = false;
        if (!result.Ok)
        {
            ShowMessage(result.Error ?? "Action refusée.", true);
            return;
        }
        _call.Status = CallStatuses.SansSuite;
        _call.Procedure = _pendingProcedure;
        _call.ClosingReason = PromptText;
        _call.EndedAt ??= DateTimeOffset.UtcNow;
        RaiseState();
        ShowMessage("Appel classé sans suite.", false);
    }

    // ===== Main courante de l'appel =====

    private async Task AddLogAsync()
    {
        var session = ArgosSession.Current;
        if (session is null || string.IsNullOrWhiteSpace(LogText)) return;
        var result = await session.Api.SendAsync(HttpMethod.Post,
            $"api/ode/calls/{Uri.EscapeDataString(CallId)}/events", new TextRequest(LogText));
        if (!result.Ok)
        {
            ShowMessage(result.Error ?? "Ajout impossible.", true);
            return;
        }
        LogText = string.Empty;
    }

    private void OpenIntervention()
    {
        if (!string.IsNullOrWhiteSpace(_call.InterventionId))
            _desktop.OpenWindow(OdeWindows.Intervention(_desktop, _call.InterventionId!));
    }

    private async Task CloseAsync()
    {
        if (_dirty)
            await SaveAsync(null, false);
        _desktop.CloseWindow(OdeWindows.PriseAppelKey(CallId));
    }

    private void ShowMessage(string text, bool error)
    {
        MessageIsError = error;
        Message = text;
        DispatcherTimer.RunOnce(() => { if (Message == text) Message = null; }, TimeSpan.FromSeconds(error ? 8 : 4));
    }

    private void RaiseState()
    {
        foreach (var name in new[]
                 {
                     nameof(DecrocheText), nameof(IsClosed), nameof(IsEditable), nameof(IsRunning), nameof(ClosedBanner),
                     nameof(HasIntervention), nameof(LocationConfirmed), nameof(GroupeHoraire), nameof(OperatorName)
                 })
            OnPropertyChanged(name);

        Events.Clear();
        foreach (var e in _call.Events.OrderByDescending(e => e.At))
            Events.Add(e);

        FKeyCommand.RaiseCanExecuteChanged();
        ConfirmLocationCommand.RaiseCanExecuteChanged();
        TransmitCommand.RaiseCanExecuteChanged();
        OpenInterventionCommand.RaiseCanExecuteChanged();
        SansSuiteCommand.RaiseCanExecuteChanged();
        ShowLinkCommand.RaiseCanExecuteChanged();
    }

    private void RaiseAll()
    {
        foreach (var name in new[]
                 {
                     nameof(CallerPhone), nameof(ImplicatedPhone), nameof(Origin), nameof(Mode), nameof(Network),
                     nameof(StreetNumber), nameof(Street), nameof(Cstc), nameof(AlertAddress), nameof(Observations),
                     nameof(Motif), nameof(Reason), nameof(Poste), nameof(StatusOperator), nameof(CallerName), nameof(Building),
                     nameof(Access), nameof(VictimCount), nameof(Priority), nameof(Risks), nameof(Instructions), nameof(FinalNature)
                 })
            OnPropertyChanged(name);
        RaiseState();
    }

    public void Dispose()
    {
        _clock.Stop();
        if (ArgosSession.Current is { } session)
            session.Ode.Changed -= OnStoreChanged;
        if (_dirty)
            _ = SaveAsync(null, false);
    }
}

public sealed record InterventionChoice(string Id, string Label)
{
    public override string ToString() => Label;
}
