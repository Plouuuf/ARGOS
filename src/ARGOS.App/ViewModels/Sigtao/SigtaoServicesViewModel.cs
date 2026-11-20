using System.Collections.ObjectModel;
using Argos.Services;
using Argos.Shared.Contracts;
using Avalonia.Media.Imaging;

namespace Argos.ViewModels.Sigtao;

/// <summary>Un service dans la liste des services (sous-catégorie, avec le nombre de membres).</summary>
public sealed class ServiceItemVm(string id, string label, string roles, RelayCommand openCommand) : ViewModelBase
{
    private int _count;
    private bool _isSelected;

    public string Id { get; } = id;
    public string Label { get; } = label;
    /// <summary>« Sous-officier de remise · Caporal de remise · Garde-remise ».</summary>
    public string Roles { get; } = roles;
    public RelayCommand OpenCommand { get; } = openCommand;

    public int Count
    {
        get => _count;
        set { if (SetProperty(ref _count, value)) OnPropertyChanged(nameof(CountText)); }
    }

    public string CountText => Count switch { 0 => "Aucun membre", 1 => "1 membre", _ => $"{Count} membres" };
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

/// <summary>Un militaire affecté au service affiché : grade, nom, rôle (modifiable), observations (Casernement).</summary>
public sealed class ServiceMemberVm : ViewModelBase
{
    private readonly Func<ServiceMemberVm, string, string, Task<bool>> _save;
    private string _role;
    private string _observations;
    private string _savedObservations;
    private bool _busy;

    public ServiceMemberVm(string militaryId, IReadOnlyList<string> roles, bool canEdit, bool hasObservations,
        Func<ServiceMemberVm, string, string, Task<bool>> save, Action<ServiceMemberVm> remove)
    {
        MilitaryId = militaryId;
        Roles = roles;
        CanEdit = canEdit;
        HasObservations = hasObservations;
        _save = save;
        _role = string.Empty;
        _observations = _savedObservations = string.Empty;
        RemoveCommand = new RelayCommand(() => remove(this), () => CanEdit && !_busy);
        SaveObservationsCommand = new RelayCommand(() => _ = SaveAsync(_role, _observations), () => CanEdit && !_busy && ObservationsDirty);
    }

    public string MilitaryId { get; }
    public IReadOnlyList<string> Roles { get; }
    public bool CanEdit { get; }
    public bool IsReadOnly => !CanEdit;
    public bool HasObservations { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand SaveObservationsCommand { get; }

    public string Grade { get; private set; } = string.Empty;
    public string GradeShort { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public bool IsInactive { get; private set; }
    public Bitmap? GradeIcon => ImageLoader.Grade(Grade);

    /// <summary>Rôle dans le service : le choix dans la liste est enregistré aussitôt.</summary>
    public string Role
    {
        get => _role;
        set
        {
            // La liste déroulante peut renvoyer null pendant la reconstruction : on l'ignore.
            if (string.IsNullOrWhiteSpace(value) || !Roles.Contains(value) || value == _role) return;
            var previous = _role;
            _role = value;
            OnPropertyChanged();
            _ = ChangeRoleAsync(previous, value);
        }
    }

    public string Observations
    {
        get => _observations;
        set
        {
            if (!SetProperty(ref _observations, value ?? string.Empty)) return;
            OnPropertyChanged(nameof(ObservationsDirty));
            SaveObservationsCommand.RaiseCanExecuteChanged();
        }
    }

    public bool ObservationsDirty => _observations.Trim() != _savedObservations.Trim();

    /// <summary>Mise à jour depuis le serveur ; les observations en cours de saisie ne sont pas écrasées.</summary>
    public void Update(MilitaryRecord? record, ServiceMember member)
    {
        Grade = record?.Grade ?? string.Empty;
        GradeShort = record is null ? string.Empty : BsppGrades.Short(record.Grade) + record.Echelon;
        Name = record is null ? "Militaire inconnu" : $"{record.Nom.ToUpperInvariant()} {record.Prenom}".Trim();
        Status = record?.Status ?? string.Empty;
        IsInactive = record is not null && record.Status != MilitaryStatuses.Actif;
        foreach (var n in new[] { nameof(Grade), nameof(GradeShort), nameof(Name), nameof(Status), nameof(IsInactive), nameof(GradeIcon) })
            OnPropertyChanged(n);
        if (_role != member.Role)
        {
            _role = member.Role;
            OnPropertyChanged(nameof(Role));
        }
        var dirty = ObservationsDirty;
        _savedObservations = member.Observations;
        if (!dirty && _observations != member.Observations)
        {
            _observations = member.Observations;
            OnPropertyChanged(nameof(Observations));
        }
        OnPropertyChanged(nameof(ObservationsDirty));
        SaveObservationsCommand.RaiseCanExecuteChanged();
    }

    private async Task ChangeRoleAsync(string previous, string role)
    {
        if (await SaveAsync(role, _savedObservations)) return;
        _role = previous;                       // refusé : on revient au rôle enregistré
        OnPropertyChanged(nameof(Role));
    }

    private async Task<bool> SaveAsync(string role, string observations)
    {
        if (_busy) return false;
        _busy = true;
        RemoveCommand.RaiseCanExecuteChanged();
        SaveObservationsCommand.RaiseCanExecuteChanged();
        try
        {
            var ok = await _save(this, role, observations);
            if (ok && observations == _observations)
            {
                _savedObservations = observations;
                OnPropertyChanged(nameof(ObservationsDirty));
            }
            return ok;
        }
        finally
        {
            _busy = false;
            RemoveCommand.RaiseCanExecuteChanged();
            SaveObservationsCommand.RaiseCanExecuteChanged();
        }
    }
}

/// <summary>Services de la compagnie : liste des services, membres du service choisi, ajout, rôle, retrait.</summary>
public sealed partial class SigtaoAppViewModel
{
    private string? _serviceId;
    private MilitaryChoice? _serviceNewMember;
    private string? _serviceNewRole;
    private bool _serviceBusy;

    public ObservableCollection<ServiceItemVm> ServicesList { get; } = [];
    public ObservableCollection<ServiceMemberVm> ServiceMembers { get; } = [];
    /// <summary>Dossiers en activité pas encore membres du service affiché.</summary>
    public ObservableCollection<MilitaryChoice> ServiceCandidates { get; } = [];

    public RelayCommand ServiceBackCommand { get; private set; } = null!;
    public RelayCommand ServiceAddCommand { get; private set; } = null!;

    private SigtaoServiceDefinition? SelectedService => SigtaoServices.Find(_serviceId);

    public bool HasSelectedService => SelectedService is not null;
    public string SelectedServiceTitle => SelectedService?.Label ?? string.Empty;
    public bool SelectedServiceHasObservations => SelectedService?.HasObservations == true;
    public IReadOnlyList<string> ServiceRoles => SelectedService?.Roles ?? Array.Empty<string>();
    public bool CanEditServices => Rights.ManageCompany;
    public bool HasServiceMembers => ServiceMembers.Count > 0;
    public string ServiceMembersCount => ServiceMembers.Count switch
    {
        0 => "Aucun militaire affecté",
        1 => "1 militaire affecté",
        var n => $"{n} militaires affectés"
    };

    public MilitaryChoice? ServiceNewMember
    {
        get => _serviceNewMember;
        set { if (SetProperty(ref _serviceNewMember, value)) ServiceAddCommand.RaiseCanExecuteChanged(); }
    }

    public string? ServiceNewRole
    {
        get => _serviceNewRole;
        set { if (SetProperty(ref _serviceNewRole, value)) ServiceAddCommand.RaiseCanExecuteChanged(); }
    }

    private void InitServices()
    {
        foreach (var def in SigtaoServices.All)
        {
            var id = def.Id;
            ServicesList.Add(new ServiceItemVm(def.Id, def.Label, string.Join(" · ", def.Roles), new RelayCommand(() => OpenService(id))));
        }
        ServiceBackCommand = new RelayCommand(() => OpenService(null));
        ServiceAddCommand = new RelayCommand(() => _ = AddServiceMemberAsync(),
            () => CanEditServices && !_serviceBusy && HasSelectedService && ServiceNewMember is not null
                  && ServiceNewRole is { } role && ServiceRoles.Contains(role));
    }

    private void OpenService(string? id)
    {
        if (_serviceId == id) return;
        _serviceId = id;
        ServiceNewMember = null;
        ServiceNewRole = SelectedService?.Roles.LastOrDefault();          // rôle de base du service par défaut
        ServiceMembers.Clear();
        foreach (var n in new[] { nameof(HasSelectedService), nameof(SelectedServiceTitle), nameof(SelectedServiceHasObservations), nameof(ServiceRoles) })
            OnPropertyChanged(n);
        BuildServices();
    }

    private void BuildServices()
    {
        var s = Snapshot;
        var stored = s.Services.ToDictionary(x => x.Id);
        var military = s.Military.ToDictionary(m => m.Id);
        foreach (var item in ServicesList)
        {
            item.Count = stored.TryGetValue(item.Id, out var doc) ? doc.Members.Count(m => military.ContainsKey(m.MilitaryId)) : 0;
            item.IsSelected = item.Id == _serviceId;
        }
        OnPropertyChanged(nameof(CanEditServices));

        var def = SelectedService;
        if (def is null)
        {
            ServiceMembers.Clear();
            ServiceCandidates.Clear();
            RaiseServiceMembers();
            return;
        }

        // Membres : ordre des rôles du service, puis grade (du plus haut au plus bas, comme l'effectif).
        var members = (stored.TryGetValue(def.Id, out var current) ? current.Members : new List<ServiceMember>())
            .Where(m => military.ContainsKey(m.MilitaryId))
            .Select(m => (Member: m, Record: military[m.MilitaryId]))
            .OrderBy(x => RoleRank(def, x.Member.Role))
            .ThenByDescending(x => BsppGrades.Rank(x.Record.Grade))
            .ThenBy(x => x.Record.Nom)
            .ToList();

        // On garde les lignes existantes (saisie d'observations en cours) et on ne reconstruit la liste que si elle change.
        var existing = ServiceMembers.ToDictionary(v => v.MilitaryId);
        var rows = members.Select(x =>
        {
            if (!existing.TryGetValue(x.Member.MilitaryId, out var vm) || vm.CanEdit != CanEditServices)
                vm = new ServiceMemberVm(x.Member.MilitaryId, def.Roles, CanEditServices, def.HasObservations, SaveServiceMemberAsync, RemoveServiceMember);
            vm.Update(x.Record, x.Member);
            return vm;
        }).ToList();
        if (!rows.SequenceEqual(ServiceMembers)) Fill(ServiceMembers, rows);

        var taken = members.Select(x => x.Member.MilitaryId).ToHashSet();
        var candidates = s.Military.Where(m => m.Status == MilitaryStatuses.Actif && !taken.Contains(m.Id))
            .Select(m => new MilitaryChoice(m.Id, m.FullName)).ToList();
        if (!candidates.SequenceEqual(ServiceCandidates))
        {
            var keep = ServiceNewMember?.Id;
            Fill(ServiceCandidates, candidates);
            ServiceNewMember = ServiceCandidates.FirstOrDefault(c => c.Id == keep);
        }
        RaiseServiceMembers();
    }

    private static int RoleRank(SigtaoServiceDefinition def, string role)
    {
        var index = def.Roles.ToList().IndexOf(role);
        return index < 0 ? def.Roles.Count : index;
    }

    private void RaiseServiceMembers()
    {
        OnPropertyChanged(nameof(HasServiceMembers));
        OnPropertyChanged(nameof(ServiceMembersCount));
        ServiceAddCommand.RaiseCanExecuteChanged();
    }

    private async Task AddServiceMemberAsync()
    {
        var session = ArgosSession.Current;
        if (session is null || SelectedService is not { } def || ServiceNewMember is not { } who || ServiceNewRole is not { } role) return;
        _serviceBusy = true;
        ServiceAddCommand.RaiseCanExecuteChanged();
        try
        {
            var result = await session.Api.SendAsync(HttpMethod.Post, "api/sigtao/services",
                new ServiceMemberRequest { ServiceId = def.Id, MilitaryId = who.Id, Role = role });
            ShowMessage(result.Ok ? $"{who.Label} ajouté à « {def.Label} » ({role})." : result.Error ?? "Ajout refusé.", !result.Ok);
            if (result.Ok) ServiceNewMember = null;
            await session.Sigtao.RefreshAsync();
        }
        finally
        {
            _serviceBusy = false;
            ServiceAddCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task<bool> SaveServiceMemberAsync(ServiceMemberVm member, string role, string observations)
    {
        var session = ArgosSession.Current;
        if (session is null || SelectedService is not { } def) return false;
        var result = await session.Api.SendAsync(HttpMethod.Post, "api/sigtao/services",
            new ServiceMemberRequest { ServiceId = def.Id, MilitaryId = member.MilitaryId, Role = role, Observations = observations });
        ShowMessage(result.Ok ? $"{member.GradeShort} {member.Name} : « {def.Label} » mis à jour." : result.Error ?? "Modification refusée.", !result.Ok);
        await session.Sigtao.RefreshAsync();
        return result.Ok;
    }

    private void RemoveServiceMember(ServiceMemberVm member) => _ = RemoveServiceMemberAsync(member);

    private async Task RemoveServiceMemberAsync(ServiceMemberVm member)
    {
        var session = ArgosSession.Current;
        if (session is null || SelectedService is not { } def) return;
        var result = await session.Api.SendAsync(HttpMethod.Post, "api/sigtao/services",
            new ServiceMemberRequest { ServiceId = def.Id, MilitaryId = member.MilitaryId, Retirer = true });
        ShowMessage(result.Ok ? $"{member.GradeShort} {member.Name} retiré de « {def.Label} »." : result.Error ?? "Retrait refusé.", !result.Ok);
        await session.Sigtao.RefreshAsync();
    }
}
