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

    public string? Motif { get => _motif; set => Edit(ref _motif, value); }
    public string Reason { get => _reason; set => Edit(ref _reason, value); }

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
        _motif = Natures.Find(call.NatureCode)?.Display ?? string.Empty;
        _reason = call.Reason;
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
        var known = snapshot.Calls.Select(c => c.Street)
            .Concat(snapshot.Interventions.Select(i => i.Address))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.Create(OdeFormat.French, true))
            .ToList();
        if (known.SequenceEqual(StreetSuggestions)) return;
        StreetSuggestions.Clear();
        foreach (var s in known) StreetSuggestions.Add(s);
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

    private CallRecord BuildRecord()
    {
        var nature = Natures.All.FirstOrDefault(n => string.Equals(n.Display, Motif?.Trim(), StringComparison.OrdinalIgnoreCase))
                     ?? Natures.Find(Motif?.Split(' ', 2)[0]);
        return new CallRecord
        {
            Id = CallId,
            Origin = Origin,
            CallerPhone = CallerPhone,
            ImplicatedPhone = ImplicatedPhone,
            CallerName = _call.CallerName,
            LocationMode = Mode,
            LocationNetwork = Network,
            StreetNumber = StreetNumber,
            Street = Street ?? string.Empty,
            LocationConfirmed = _call.LocationConfirmed,
            Cstc = Cstc,
            AlertAddress = AlertAddress,
            Locality = _call.Locality,
            Building = _call.Building,
            Access = _call.Access,
            Observations = Observations,
            NatureCode = nature?.Code ?? string.Empty,
            Reason = Reason,
            VictimCount = _call.VictimCount,
            Priority = _call.Priority,
            Risks = _call.Risks
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

        _call.LocationConfirmed = true;
        AlertAddress = string.Join(" ", new[] { StreetNumber.Trim(), street }.Where(s => s.Length > 0)).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(Cstc))
            Cstc = EnginsCatalog.Creteil;
        OnPropertyChanged(nameof(LocationConfirmed));
        ShowMessage("Localisation confirmée.", false);
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
        _pendingProcedure = procedure.Key;
        PromptTitle = $"{procedure.Key} - {procedure.Label} : appel clos sans départ";
        PromptText = procedure.Label;
        PromptVisible = true;
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
    }

    private void RaiseAll()
    {
        foreach (var name in new[]
                 {
                     nameof(CallerPhone), nameof(ImplicatedPhone), nameof(Origin), nameof(Mode), nameof(Network),
                     nameof(StreetNumber), nameof(Street), nameof(Cstc), nameof(AlertAddress), nameof(Observations),
                     nameof(Motif), nameof(Reason), nameof(Poste), nameof(StatusOperator)
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
