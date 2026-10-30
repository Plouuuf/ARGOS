using System.Collections.ObjectModel;
using Argos.Services;
using Argos.Shared.Contracts;
using Argos.Views.Apps.Ode;

namespace Argos.ViewModels.Ode;

public sealed record StatusButton(string Label, bool IsCurrent, RelayCommand Command);

public sealed record EngagedRow(string Nom, string Libelle, string Status, string Since, string Role, IReadOnlyList<StatusButton> Buttons);

public sealed record OrderRow(int Number, string Text, RelayCommand PrintCommand);

public sealed record CallLinkRow(string Id, string Text, RelayCommand OpenCommand);

/// <summary>Suivi d'une intervention : statuts des engins, messages, renforts, documents, clôture.</summary>
public sealed class InterventionViewModel : ViewModelBase, IDisposable
{
    private readonly DesktopViewModel _desktop;
    private Intervention? _intervention;
    private string _message = string.Empty;
    private string? _error;
    private bool _confirmClose;

    public InterventionViewModel(DesktopViewModel desktop, string interventionId)
    {
        _desktop = desktop;
        InterventionId = interventionId;
        SendMessageCommand = new RelayCommand(() => _ = SendMessageAsync("message"));
        RenfortCommand = new RelayCommand(() => _ = RequestRenfortAsync(), () => IsActive);
        AddMeansCommand = new RelayCommand(() => _desktop.OpenWindow(OdeWindows.CreateOde(_desktop, null, InterventionId, _intervention?.Procedure)), () => IsActive);
        AskCloseCommand = new RelayCommand(() => ConfirmClose = true, () => IsActive);
        CancelCloseCommand = new RelayCommand(() => ConfirmClose = false);
        CloseInterventionCommand = new RelayCommand(() => _ = CloseInterventionAsync());
        // Main courante de l'intervention : pré-remplie depuis l'ODE (premier engin par défaut).
        WriteMcoCommand = new RelayCommand(() => _desktop.OpenWindow(
            Argos.Views.Apps.Mco.McoWindows.Editor(_desktop, null, InterventionId, null)));

        if (ArgosSession.Current is { } session)
            session.Ode.Changed += Reload;
        Reload();
    }

    public string InterventionId { get; }

    public string Number => _intervention?.Number ?? "…";
    public string Nature => OdeFormat.Nature(_intervention?.NatureCode);
    public string Procedure => "Procédure " + (Procedures.Find(_intervention?.Procedure)?.Label ?? "—");
    public string Address => _intervention is null ? string.Empty
        : string.Join(" - ", new[] { _intervention.Address, _intervention.Locality, _intervention.Building }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string Created => _intervention is null ? string.Empty : $"Créée le {OdeFormat.OdeDateTime(_intervention.CreatedAt)} par {_intervention.CreatedBy}";
    public string Requerant => _intervention is null ? string.Empty
        : $"Requérant : {OdeFormat.OrDash(_intervention.CallerPhone)}  ·  {OdeFormat.OrDash(_intervention.CallerInfo)}";
    public string Consignes => OdeFormat.OrDash(_intervention?.Consignes);
    public bool IsActive => _intervention?.Active == true;
    public string StateLabel => IsActive ? "EN COURS" : $"CLÔTURÉE {(_intervention?.ClosedAt is { } c ? OdeFormat.OdeDateTime(c) : string.Empty)}";

    public ObservableCollection<EngagedRow> Engins { get; } = [];
    public ObservableCollection<OdeEvent> Events { get; } = [];
    public ObservableCollection<OrderRow> Orders { get; } = [];
    public ObservableCollection<CallLinkRow> Calls { get; } = [];

    public string Message { get => _message; set => SetProperty(ref _message, value); }

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
    public bool ConfirmClose { get => _confirmClose; set => SetProperty(ref _confirmClose, value); }

    public RelayCommand SendMessageCommand { get; }
    public RelayCommand RenfortCommand { get; }
    public RelayCommand AddMeansCommand { get; }
    public RelayCommand AskCloseCommand { get; }
    public RelayCommand CancelCloseCommand { get; }
    public RelayCommand CloseInterventionCommand { get; }
    public RelayCommand WriteMcoCommand { get; }

    private void Reload()
    {
        var store = ArgosSession.Current?.Ode;
        _intervention = store?.FindIntervention(InterventionId);
        if (_intervention is null)
        {
            Error = store?.LastError ?? "Intervention introuvable.";
            return;
        }

        Engins.Clear();
        foreach (var e in _intervention.Engins.OrderBy(e => e.OrderNumber).ThenByDescending(e => e.FirstDeparture))
        {
            var nom = e.Nom;
            var buttons = EnginStatuses.Operationnels
                .Select(s => new StatusButton(s, s == e.Status, new RelayCommand(() => _ = SetStatusAsync(nom, s), () => IsActive)))
                .ToList();
            Engins.Add(new EngagedRow(
                e.Nom,
                ArgosSession.Current?.Ode.FindEngin(e.Nom)?.Libelle ?? EnginsCatalog.Find(e.Nom)?.Libelle ?? string.Empty,
                e.Status,
                "depuis " + OdeFormat.Time(e.StatusAt),
                e.FirstDeparture ? "1er départ" : $"Complémentaire (ODE n°{e.OrderNumber})",
                buttons));
        }

        Events.Clear();
        foreach (var ev in _intervention.Events.OrderByDescending(ev => ev.At))
            Events.Add(ev);

        Orders.Clear();
        foreach (var o in _intervention.Orders.OrderBy(o => o.Number))
        {
            var number = o.Number;
            Orders.Add(new OrderRow(number,
                $"ODE n°{o.Number} · {OdeFormat.OdeDateTime(o.ValidatedAt)} · {string.Join(", ", o.Engins)}" + (o.PrintCount > 0 ? $" · imprimé {o.PrintCount}×" : string.Empty),
                new RelayCommand(() => _desktop.OpenWindow(OdeWindows.Print(_desktop, InterventionId, number)))));
        }

        Calls.Clear();
        foreach (var id in _intervention.CallIds)
        {
            var call = store!.FindCall(id);
            var callId = id;
            Calls.Add(new CallLinkRow(id,
                call is null ? $"Appel {id}" : $"Appel {id} · {OdeFormat.Short(call.ReceivedAt)} · {OdeFormat.OrDash(call.CallerPhone)}",
                new RelayCommand(() => _desktop.OpenCall(callId))));
        }

        Error = null;
        foreach (var name in new[] { nameof(Number), nameof(Nature), nameof(Procedure), nameof(Address), nameof(Created),
                                     nameof(Requerant), nameof(Consignes), nameof(IsActive), nameof(StateLabel) })
            OnPropertyChanged(name);
        RenfortCommand.RaiseCanExecuteChanged();
        AddMeansCommand.RaiseCanExecuteChanged();
        AskCloseCommand.RaiseCanExecuteChanged();
    }

    private async Task SetStatusAsync(string nom, string status)
    {
        var session = ArgosSession.Current;
        if (session is null) return;
        var result = await session.Api.SendAsync(HttpMethod.Post,
            $"api/ode/interventions/{Uri.EscapeDataString(InterventionId)}/status", new EnginStatusRequest(nom, status));
        Error = result.Ok ? null : result.Error;
    }

    private async Task<bool> SendMessageAsync(string kind)
    {
        var session = ArgosSession.Current;
        if (session is null || string.IsNullOrWhiteSpace(Message)) return false;
        var result = await session.Api.SendAsync(HttpMethod.Post,
            $"api/ode/interventions/{Uri.EscapeDataString(InterventionId)}/messages?kind={kind}", new TextRequest(Message));
        if (!result.Ok)
        {
            Error = result.Error;
            return false;
        }
        Message = string.Empty;
        Error = null;
        return true;
    }

    /// <summary>Demande de renfort : tracée dans la main courante, puis ouverture du départ complémentaire.</summary>
    private async Task RequestRenfortAsync()
    {
        if (string.IsNullOrWhiteSpace(Message))
            Message = "Demande de renfort.";
        if (await SendMessageAsync("renfort"))
            _desktop.OpenWindow(OdeWindows.CreateOde(_desktop, null, InterventionId, _intervention?.Procedure));
    }

    private async Task CloseInterventionAsync()
    {
        ConfirmClose = false;
        var session = ArgosSession.Current;
        if (session is null) return;
        var result = await session.Api.SendAsync(HttpMethod.Post,
            $"api/ode/interventions/{Uri.EscapeDataString(InterventionId)}/close");
        Error = result.Ok ? null : result.Error;
    }

    public void Dispose()
    {
        if (ArgosSession.Current is { } session)
            session.Ode.Changed -= Reload;
    }
}
