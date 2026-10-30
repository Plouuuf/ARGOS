using System.Collections.ObjectModel;
using Argos.Services;
using Argos.Shared.Contracts;
using Argos.Views.Apps.Ode;

namespace Argos.ViewModels.Ode;

/// <summary>Engin proposé dans la création d'un ODE.</summary>
public sealed class EnginChoice(string nom, string status, string centre, string libelle) : ViewModelBase
{
    private bool _isSelected;

    public string Nom { get; } = nom;
    public string Status { get; } = status;
    public string Centre { get; } = centre;
    public string Libelle { get; } = libelle;
    public bool IsAvailable => EnginStatuses.IsAvailable(Status);
    public string Display => $"{Nom} - {Status}";

    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

/// <summary>
/// Création de l'ordre de départ : depuis un appel (touches F1…F8), manuellement,
/// ou en départ complémentaire (renfort) sur une intervention ouverte.
/// </summary>
public sealed class CreateOdeViewModel : ViewModelBase
{
    private readonly DesktopViewModel _desktop;
    private string _procedure;
    private string? _nature = string.Empty;
    private string _address = string.Empty;
    private string _locality = string.Empty;
    private string _building = string.Empty;
    private string _access = string.Empty;
    private string _cstc = EnginsCatalog.Creteil;
    private string _secteur = string.Empty;
    private string _callerPhone = string.Empty;
    private string _callerInfo = string.Empty;
    private string _consignes = string.Empty;
    private string _motif = string.Empty;
    private NatureCategory? _category;
    private EnginChoice? _firstEngin;
    private string? _error;
    private bool _busy;
    private bool _previewVisible;
    private OdeDocument? _preview;

    public CreateOdeViewModel(DesktopViewModel desktop, string? callId, string? interventionId, string? procedure)
    {
        _desktop = desktop;
        CallId = callId;
        InterventionId = interventionId;
        _procedure = Procedures.Find(procedure)?.Code ?? "BLANCHE";

        FillNatures();

        ValidateCommand = new RelayCommand(() => _ = ValidateAsync(), () => !Busy);
        PreviewCommand = new RelayCommand(() => { Preview = BuildPreview(); PreviewVisible = true; });
        ClosePreviewCommand = new RelayCommand(() => PreviewVisible = false);
        CancelCommand = new RelayCommand(() => _desktop.CloseWindow(OdeWindows.CreateOdeKey(CallId, InterventionId)));

        LoadEngins();
        _ = PrefillAsync();
    }

    public string? CallId { get; }
    public string? InterventionId { get; }
    public bool IsComplementary => InterventionId is not null;
    public bool IsNew => InterventionId is null;
    public bool HasCall => CallId is not null;
    public string Heading => IsComplementary ? $"Départ complémentaire - intervention {ExistingNumber}" : "Création de l'ordre de départ";
    public string Source => HasCall ? $"Depuis l'appel {CallId}" : IsComplementary ? "Renfort" : "Création manuelle";
    public string ExistingNumber { get; private set; } = string.Empty;

    public IReadOnlyList<Procedures.ProcedureDefinition> ProcedureChoices { get; } = Procedures.All.Where(p => p.Departure).ToList();
    public ObservableCollection<string> NatureSuggestions { get; } = [];
    public ObservableCollection<EnginChoice> AvailableEngins { get; } = [];
    public ObservableCollection<EnginChoice> AllEngins { get; } = [];

    public Procedures.ProcedureDefinition? SelectedProcedure
    {
        get => Procedures.Find(_procedure);
        set
        {
            if (value is null) return;
            _procedure = value.Code;
            OnPropertyChanged();
        }
    }

    /// <summary>Catégorie d'intervention (filtre des natures opérationnelles).</summary>
    public IReadOnlyList<NatureCategory?> Categories { get; } = [null, .. Natures.Categories];
    public NatureCategory? Category
    {
        get => _category;
        set { if (SetProperty(ref _category, value)) FillNatures(); }
    }

    private void FillNatures()
    {
        NatureSuggestions.Clear();
        foreach (var nature in _category?.Items ?? Natures.All)
            NatureSuggestions.Add(nature.Display);
    }

    /// <summary>Nature opérationnelle ; choisir une nature fixe aussi la catégorie.</summary>
    public string? Nature
    {
        get => _nature;
        set
        {
            if (!SetProperty(ref _nature, value)) return;
            var found = Natures.All.FirstOrDefault(n => string.Equals(n.Display, value?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (found is not null && _category?.Code != found.CategoryCode)
            {
                _category = Natures.Categories.FirstOrDefault(c => c.Code == found.CategoryCode);
                OnPropertyChanged(nameof(Category));
            }
        }
    }

    /// <summary>Motif du départ (précision libre : « feu de poubelle contre façade », « personne bloquée ascenseur »…).</summary>
    public string Motif { get => _motif; set => SetProperty(ref _motif, value); }
    public string Address { get => _address; set => SetProperty(ref _address, value); }
    public string Locality { get => _locality; set => SetProperty(ref _locality, value); }
    public string Building { get => _building; set => SetProperty(ref _building, value); }
    public string Access { get => _access; set => SetProperty(ref _access, value); }
    public string Cstc { get => _cstc; set => SetProperty(ref _cstc, value); }
    public string Secteur { get => _secteur; set => SetProperty(ref _secteur, value); }
    public string CallerPhone { get => _callerPhone; set => SetProperty(ref _callerPhone, value); }
    public string CallerInfo { get => _callerInfo; set => SetProperty(ref _callerInfo, value); }
    public string Consignes { get => _consignes; set => SetProperty(ref _consignes, value); }

    public EnginChoice? FirstEngin
    {
        get => _firstEngin;
        set
        {
            if (!SetProperty(ref _firstEngin, value)) return;
            // Le premier départ n'est pas aussi un engin complémentaire.
            if (value is not null) value.IsSelected = false;
        }
    }

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

    public bool Busy
    {
        get => _busy;
        private set
        {
            if (SetProperty(ref _busy, value))
                ValidateCommand.RaiseCanExecuteChanged();
        }
    }

    public bool PreviewVisible { get => _previewVisible; set => SetProperty(ref _previewVisible, value); }
    public OdeDocument? Preview { get => _preview; private set => SetProperty(ref _preview, value); }

    public RelayCommand ValidateCommand { get; }
    public RelayCommand PreviewCommand { get; }
    public RelayCommand ClosePreviewCommand { get; }
    public RelayCommand CancelCommand { get; }

    private void LoadEngins()
    {
        var snapshot = ArgosSession.Current?.Ode.Snapshot;
        AvailableEngins.Clear();
        AllEngins.Clear();
        // Parc tenu dans ADAGIO REMISE (engins créés, renommés ou retirés compris).
        foreach (var engin in snapshot?.Engins ?? [])
        {
            var choice = new EnginChoice(engin.Nom, engin.Status, engin.Centre, engin.Libelle);
            AllEngins.Add(choice);
            if (choice.IsAvailable)
                AvailableEngins.Add(choice);
        }
    }

    private async Task PrefillAsync()
    {
        var session = ArgosSession.Current;
        if (session is null) return;

        if (InterventionId is not null && session.Ode.FindIntervention(InterventionId) is { } existing)
        {
            ExistingNumber = existing.Number;
            Nature = OdeFormat.Nature(existing.NatureCode);
            Address = existing.Address;
            Locality = existing.Locality;
            Building = existing.Building;
            Access = existing.Access;
            Cstc = existing.Cstc;
            Secteur = existing.Secteur;
            CallerPhone = existing.CallerPhone;
            CallerInfo = existing.CallerInfo;
            Motif = existing.Motif;
            OnPropertyChanged(nameof(ExistingNumber));
            OnPropertyChanged(nameof(Heading));
            return;
        }

        if (CallId is null) return;
        var call = await session.Ode.LoadCallAsync(CallId);
        if (call is null) return;

        Nature = Natures.Find(call.NatureCode)?.Display ?? string.Empty;
        Address = !string.IsNullOrWhiteSpace(call.AlertAddress)
            ? call.AlertAddress
            : string.Join(" ", new[] { call.StreetNumber, call.Street }.Where(s => !string.IsNullOrWhiteSpace(s))).ToUpperInvariant();
        Locality = call.Locality;
        Building = call.Building;
        Access = call.Access;
        Cstc = string.IsNullOrWhiteSpace(call.Cstc) ? EnginsCatalog.Creteil : call.Cstc;
        CallerPhone = call.CallerPhone;
        var caller = string.Join(" - ", new[] { call.CallerName, call.VictimCount > 0 ? $"{call.VictimCount} victime(s)" : "" }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        CallerInfo = string.Join(" ** ", new[] { caller, call.Reason, call.Observations }.Where(s => !string.IsNullOrWhiteSpace(s)));
        Consignes = string.Join(" ** ", new[] { call.Instructions, string.IsNullOrWhiteSpace(call.Risks) ? "" : "RISQUES : " + call.Risks,
            string.IsNullOrWhiteSpace(call.Priority) ? "" : call.Priority }.Where(s => !string.IsNullOrWhiteSpace(s)));
        Motif = (call.Reason ?? string.Empty).Split('\n', 2)[0].Trim();
    }

    private CreateOdeRequest BuildRequest()
    {
        var nature = Natures.All.FirstOrDefault(n => string.Equals(n.Display, Nature?.Trim(), StringComparison.OrdinalIgnoreCase))
                     ?? Natures.Find(Nature?.Split(' ', 2)[0]);
        return new CreateOdeRequest
        {
            CallId = CallId,
            ExistingInterventionId = InterventionId,
            NatureCode = nature?.Code ?? string.Empty,
            Procedure = _procedure,
            Secteur = Secteur,
            Cstc = Cstc,
            Address = Address,
            Locality = Locality,
            Building = Building,
            Access = Access,
            CallerPhone = CallerPhone,
            CallerInfo = CallerInfo,
            Consignes = Consignes,
            Motif = Motif,
            FirstEngin = FirstEngin?.Nom ?? string.Empty,
            ComplementaryEngins = AvailableEngins.Where(e => e.IsSelected && e != FirstEngin).Select(e => e.Nom).ToList()
        };
    }

    private OdeDocument BuildPreview()
    {
        var request = BuildRequest();
        var intervention = new Intervention
        {
            Number = IsComplementary ? ExistingNumber : "(attribué à la validation)",
            NatureCode = request.NatureCode,
            Cstc = request.Cstc,
            Address = request.Address,
            Locality = request.Locality,
            Building = request.Building,
            CallerPhone = request.CallerPhone,
            CallerInfo = request.CallerInfo,
            Consignes = request.Consignes
        };
        var engins = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.FirstEngin)) engins.Add(request.FirstEngin);
        engins.AddRange(request.ComplementaryEngins);
        var order = new DepartureOrder
        {
            Number = IsComplementary ? 2 : 1,
            ValidatedAt = DateTimeOffset.Now,
            OperatorName = ArgosSession.Current?.OperatorName ?? string.Empty,
            Procedure = _procedure,
            Engins = engins,
            Consignes = request.Consignes
        };
        return OdeDocument.From(intervention, order);
    }

    private async Task ValidateAsync()
    {
        var session = ArgosSession.Current;
        if (session is null) return;

        var request = BuildRequest();
        if (string.IsNullOrWhiteSpace(request.FirstEngin))
        {
            Error = IsComplementary ? "Choisis l'engin à engager." : "Choisis l'engin du premier départ.";
            return;
        }
        if (IsNew && string.IsNullOrWhiteSpace(request.NatureCode))
        {
            Error = "Choisis la nature de l'intervention dans la liste.";
            return;
        }

        Busy = true;
        Error = null;
        var result = await session.Api.SendAsync(HttpMethod.Post, "api/ode/interventions", request);
        if (!result.Ok || result.Id is null)
        {
            Busy = false;
            Error = result.Error ?? "Création de l'ODE refusée.";
            LoadEngins();
            return;
        }

        await session.Ode.RefreshAsync();
        Busy = false;
        var intervention = session.Ode.FindIntervention(result.Id);
        _desktop.CloseWindow(OdeWindows.CreateOdeKey(CallId, InterventionId));
        if (CallId is not null)
            _desktop.CloseWindow(OdeWindows.PriseAppelKey(CallId));
        if (intervention is not null && intervention.Orders.Count > 0)
            _desktop.OpenWindow(OdeWindows.Print(_desktop, intervention.Id, intervention.Orders.Max(o => o.Number)));
    }
}
