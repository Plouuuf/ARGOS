using System.Collections.ObjectModel;
using Argos.Services;
using Argos.Shared.Contracts;
using Argos.ViewModels.Ode;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Argos.ViewModels.Remise;

/// <summary>Ligne de dotation modifiable dans la fiche engin.</summary>
public sealed class DotationEditViewModel(string itemId, string name, string unit, int required, int present, Action<DotationEditViewModel> remove)
    : ViewModelBase
{
    private string _required = required.ToString();

    public string ItemId { get; } = itemId;
    public string Name { get; } = name;
    public string Unit { get; } = unit;
    public int Present { get; } = present;
    public string Required { get => _required; set { if (SetProperty(ref _required, value)) OnPropertyChanged(nameof(Missing)); } }
    public int RequiredValue => int.TryParse(Required, out var n) ? Math.Max(0, n) : 0;
    public int Missing => Math.Max(0, RequiredValue - Present);
    public bool HasMissing => Missing > 0;
    private RelayCommand? _removeCommand;
    public RelayCommand RemoveCommand => _removeCommand ??= new RelayCommand(() => remove(this));
}

/// <summary>
/// Fiche engin d'ADAGIO REMISE : identification, disponibilité, révision, photo, dotation matérielle,
/// anomalies, maintenances et vérifications. Création d'un engin quand aucun nom n'est donné.
/// </summary>
public sealed class FicheEnginViewModel : ViewModelBase, IDisposable
{
    private readonly DesktopViewModel _desktop;
    private readonly string _key;
    private string? _nom;
    private bool _loaded;
    private string? _message;
    private bool _messageIsError;
    private bool _busy;

    private string _indicatif = string.Empty;
    private string _type = string.Empty;
    private string _typeGdm = string.Empty;
    private string _libelle = string.Empty;
    private string _centre = EnginsCatalog.Creteil;
    private string _code = string.Empty;
    private string _immatriculation = string.Empty;
    private string _availability = FleetAvailability.Disponible;
    private string _availabilityReason = string.Empty;
    private DateTime? _nextRevision;
    private bool _active = true;
    private string _imageData = string.Empty;
    private Bitmap? _photo;
    private ItemChoice? _newItem;
    private string _newQuantity = "1";

    public FicheEnginViewModel(DesktopViewModel desktop, string? nom, string windowKey)
    {
        _desktop = desktop;
        _nom = nom;
        _key = windowKey;
        SaveCommand = new RelayCommand(() => _ = SaveAsync(), () => CanEdit);
        CloseCommand = new RelayCommand(() => _desktop.CloseWindow(_key));
        AddLineCommand = new RelayCommand(AddLine, () => CanEdit);
        RemovePhotoCommand = new RelayCommand(() => SetImage(string.Empty), () => CanEdit);
        if (ArgosSession.Current is { } session)
        {
            session.Remise.Changed += Refresh;
            session.Ode.Changed += Refresh;
            if (!session.Remise.IsLoaded)
                _ = session.Remise.RefreshAsync();
        }
        Refresh();
    }

    public bool IsNew => _nom is null;
    public List<string> EquipageLines { get; private set; } = [];
    public string EquipageTitle { get; private set; } = string.Empty;
    public string Title => IsNew ? "Nouvel engin" : _nom!;
    public bool CanEdit => ArgosSession.Current?.Remise.Snapshot.CanManage == true;
    public bool IsReadOnly => !CanEdit;

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

    public string Indicatif { get => _indicatif; set => SetProperty(ref _indicatif, value); }
    public string Type
    {
        get => _type;
        set
        {
            if (SetProperty(ref _type, value) && _imageData.Length == 0)
                UpdatePhoto();
        }
    }
    public string TypeGdm { get => _typeGdm; set => SetProperty(ref _typeGdm, value); }
    public string Libelle { get => _libelle; set => SetProperty(ref _libelle, value); }
    public string Centre { get => _centre; set => SetProperty(ref _centre, value); }
    public string Code { get => _code; set => SetProperty(ref _code, value); }
    public string Immatriculation { get => _immatriculation; set => SetProperty(ref _immatriculation, value); }
    public bool Active { get => _active; set => SetProperty(ref _active, value); }
    public IReadOnlyList<string> Centres { get; } = [EnginsCatalog.Creteil, EnginsCatalog.MaisonsAlfort, EnginsCatalog.Mass];

    // ===== Statut =====

    public IReadOnlyList<string> Availabilities => FleetAvailability.All;
    public string Availability { get => _availability; set => SetProperty(ref _availability, value ?? FleetAvailability.Disponible); }
    public string AvailabilityReason { get => _availabilityReason; set => SetProperty(ref _availabilityReason, value); }
    public DateTime? NextRevision { get => _nextRevision; set => SetProperty(ref _nextRevision, value); }
    public string OdeStatus { get; private set; } = EnginStatuses.Disponible;
    public IBrush OdeStatusBrush { get; private set; } = RemiseColors.Disponible;
    public string OdeStatusDetail { get; private set; } = string.Empty;
    public string TypeLabel => $"{(string.IsNullOrWhiteSpace(TypeGdm) ? Type : TypeGdm).ToUpperInvariant()} BSPP";

    // ===== Photo =====

    public Bitmap? Photo { get => _photo; private set { SetProperty(ref _photo, value); OnPropertyChanged(nameof(HasPhoto)); } }
    public bool HasPhoto => Photo is not null;

    /// <summary>Photo choisie par l'utilisateur (déjà réduite), en base 64.</summary>
    public void SetImage(string base64)
    {
        _imageData = base64;
        UpdatePhoto();
        RemovePhotoCommand.RaiseCanExecuteChanged();
    }

    private void UpdatePhoto()
        => Photo = ImageLoader.EnginPhoto(new FleetEngin { Type = Type.Trim(), ImageData = _imageData });

    // ===== Dotation =====

    public ObservableCollection<DotationEditViewModel> Dotation { get; } = [];
    public ObservableCollection<ItemChoice> ItemChoices { get; } = [];
    public ItemChoice? NewItem { get => _newItem; set => SetProperty(ref _newItem, value); }
    public string NewQuantity { get => _newQuantity; set => SetProperty(ref _newQuantity, value); }
    public string DotationSummary => Dotation.Count == 0
        ? "Aucune dotation définie (la dotation type sera appliquée à la création)."
        : $"{Dotation.Count} équipement(s) · {Dotation.Count(d => d.HasMissing)} incomplet(s)";

    // ===== Suivi =====

    public ObservableCollection<AnomalyRow> Anomalies { get; } = [];
    public ObservableCollection<AnomalyRow> History { get; } = [];
    public ObservableCollection<VerificationRow> Verifications { get; } = [];
    public bool HasAnomalies => Anomalies.Count > 0;

    public RelayCommand SaveCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand AddLineCommand { get; }
    public RelayCommand RemovePhotoCommand { get; }

    // ===== Chargement =====

    private void Refresh()
    {
        var store = ArgosSession.Current?.Remise;
        if (store is null) return;
        var snapshot = store.Snapshot;

        var choices = snapshot.Items.Where(i => i.Active).OrderBy(i => i.Name, StringComparer.Create(OdeFormat.French, true))
            .Select(i => new ItemChoice(i.Id, i.Name, i.Unit)).ToList();
        if (!choices.SequenceEqual(ItemChoices))
        {
            ItemChoices.Clear();
            foreach (var c in choices) ItemChoices.Add(c);
        }

        var engin = _nom is null ? null : store.FindEngin(_nom);
        if (!_loaded && (engin is not null || _nom is null) && store.IsLoaded)
        {
            _loaded = true;
            Load(engin);
        }
        else if (engin is not null)
        {
            // Quantités présentes mises à jour par une vérification ou un mouvement de stock.
            foreach (var line in Dotation.ToList())
            {
                var present = engin.Dotation.FirstOrDefault(d => d.ItemId == line.ItemId)?.Present ?? line.Present;
                if (present != line.Present)
                    Dotation[Dotation.IndexOf(line)] = new DotationEditViewModel(line.ItemId, line.Name, line.Unit,
                        line.RequiredValue, present, RemoveLine);
            }
        }

        var state = _nom is null ? null : ArgosSession.Current?.Ode.FindEngin(_nom);
        if (engin is not null)
        {
            OdeStatus = RemiseAppViewModel.StatusOf(engin, state);
            OdeStatusBrush = RemiseColors.ForStatus(OdeStatus);
            OdeStatusDetail = state?.InterventionNumber is { Length: > 0 } inter
                ? $"Intervention {inter} · depuis {OdeFormat.Short(state.UpdatedAt)}"
                : $"Depuis {OdeFormat.Short(state?.UpdatedAt ?? engin.AvailabilitySince)}"
                  + (string.IsNullOrWhiteSpace(engin.AvailabilityReason) ? string.Empty : " · " + engin.AvailabilityReason);
            OnPropertyChanged(nameof(OdeStatus));
            OnPropertyChanged(nameof(OdeStatusBrush));
            OnPropertyChanged(nameof(OdeStatusDetail));

            // Équipage armé par la feuille de garde SIGTAO.
            EquipageLines = engin.Equipage.Select(a => $"{a.Poste} : {a.Nom}").ToList();
            EquipageTitle = engin.EquipageGarde.Length == 0
                ? "Aucune feuille de garde validée n'a encore armé cet engin (SIGTAO › Feuille de garde)."
                : DateTime.TryParseExact(engin.EquipageGarde, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var garde)
                    ? $"Garde du {garde:dd/MM/yyyy}{(EquipageLines.Count == 0 ? " : engin non armé." : string.Empty)}"
                    : engin.EquipageGarde;
            OnPropertyChanged(nameof(EquipageLines));
            OnPropertyChanged(nameof(EquipageTitle));

            var maintenances = snapshot.Maintenances.Where(m => string.Equals(m.Engin, _nom, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.ReportedAt).ToList();
            Fill(Anomalies, maintenances.Where(m => m.IsOpen).Select(RowOf));
            Fill(History, maintenances.Where(m => !m.IsOpen).Select(RowOf));
            Fill(Verifications, snapshot.Verifications.Where(v => string.Equals(v.Engin, _nom, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(v => v.At).Take(15)
                .Select(v => new VerificationRow(OdeFormat.Short(v.At), v.Engin, v.By,
                    RemiseText.Result(v), v.Conforme, v.Observations)));
            OnPropertyChanged(nameof(HasAnomalies));
        }

        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(IsReadOnly));
        SaveCommand.RaiseCanExecuteChanged();
        AddLineCommand.RaiseCanExecuteChanged();
        RemovePhotoCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(DotationSummary));
    }

    private AnomalyRow RowOf(MaintenanceRecord m)
        => new(m.Number, m.Engin, OdeFormat.Short(m.ReportedAt), m.Immobilisation, m.Description, m.Status, m.ReportedBy, m.IsOpen,
            new RelayCommand(() => { }));

    private void Load(FleetEngin? e)
    {
        Indicatif = e?.Indicatif ?? string.Empty;
        _type = e?.Type ?? string.Empty;
        OnPropertyChanged(nameof(Type));
        TypeGdm = e?.TypeGdm ?? string.Empty;
        Libelle = e?.Libelle ?? string.Empty;
        Centre = e?.Centre ?? EnginsCatalog.Creteil;
        Code = e?.Code ?? string.Empty;
        Immatriculation = e?.Immatriculation ?? string.Empty;
        Availability = e?.Availability ?? FleetAvailability.Disponible;
        AvailabilityReason = e?.AvailabilityReason ?? string.Empty;
        NextRevision = e?.NextRevision?.ToLocalTime().Date;
        Active = e?.Active ?? true;
        _imageData = e?.ImageData ?? string.Empty;
        UpdatePhoto();

        Dotation.Clear();
        if (e is not null)
        {
            var store = ArgosSession.Current?.Remise;
            foreach (var d in e.Dotation)
            {
                var item = store?.FindItem(d.ItemId);
                Dotation.Add(new DotationEditViewModel(d.ItemId, item?.Name ?? d.ItemId, item?.Unit ?? string.Empty,
                    d.Required, d.Present, RemoveLine));
            }
        }
        OnPropertyChanged(nameof(DotationSummary));
    }

    private void AddLine()
    {
        if (NewItem is null) return;
        if (!int.TryParse(NewQuantity, out var qty) || qty < 0)
        {
            ShowMessage("Quantité requise invalide.", true);
            return;
        }
        var existing = Dotation.FirstOrDefault(d => d.ItemId == NewItem.Id);
        if (existing is not null)
            existing.Required = qty.ToString();
        else
            Dotation.Add(new DotationEditViewModel(NewItem.Id, NewItem.Name, NewItem.Unit, qty, 0, RemoveLine));
        NewItem = null;
        NewQuantity = "1";
        OnPropertyChanged(nameof(DotationSummary));
    }

    private void RemoveLine(DotationEditViewModel line)
    {
        if (!CanEdit) return;
        Dotation.Remove(line);
        OnPropertyChanged(nameof(DotationSummary));
    }

    private async Task SaveAsync()
    {
        var session = ArgosSession.Current;
        if (session is null || _busy) return;
        _busy = true;
        try
        {
            var original = _nom is null ? null : session.Remise.FindEngin(_nom);
            var engin = new FleetEngin
            {
                Indicatif = Indicatif,
                Type = Type,
                TypeGdm = TypeGdm,
                Libelle = Libelle,
                Centre = Centre,
                Code = Code,
                Immatriculation = Immatriculation,
                Availability = Availability,
                AvailabilityReason = Availability == FleetAvailability.Disponible ? string.Empty : AvailabilityReason,
                NextRevision = NextRevision is { } d ? new DateTimeOffset(d.Date, TimeZoneInfo.Local.GetUtcOffset(d.Date)) : null,
                Active = Active,
                ImageData = _imageData,
                Dotation = Dotation.Select(l => new DotationLine
                {
                    ItemId = l.ItemId,
                    Required = l.RequiredValue,
                    Present = original?.Dotation.FirstOrDefault(o => o.ItemId == l.ItemId)?.Present ?? l.Present
                }).ToList()
            };
            var result = await session.Api.SendAsync(HttpMethod.Post, "api/remise/engins",
                new SaveEnginRequest { OriginalNom = _nom, Engin = engin });
            if (result.Id is { Length: > 0 } saved && (result.Ok || _nom is not null))
            {
                _nom = saved;
                OnPropertyChanged(nameof(IsNew));
                OnPropertyChanged(nameof(Title));
            }
            ShowMessage(result.Ok ? "Fiche enregistrée." : result.Error ?? "Enregistrement refusé.", !result.Ok);
            await session.Remise.RefreshAsync();
            await session.Ode.RefreshAsync();
        }
        finally
        {
            _busy = false;
        }
    }

    private void ShowMessage(string text, bool error)
    {
        MessageIsError = error;
        Message = text;
    }

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }

    public void Dispose()
    {
        if (ArgosSession.Current is { } session)
        {
            session.Remise.Changed -= Refresh;
            session.Ode.Changed -= Refresh;
        }
    }
}
