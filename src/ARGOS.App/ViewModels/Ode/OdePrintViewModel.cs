using Argos.Services;
using Argos.Views.Apps.Ode;

namespace Argos.ViewModels.Ode;

/// <summary>Imprimante virtuelle : affiche la feuille d'ordre de départ et l'« imprime » (document conservé, impression tracée).</summary>
public sealed class OdePrintViewModel : ViewModelBase
{
    private readonly DesktopViewModel _desktop;
    private OdeDocument? _document;
    private string _status = "Document prêt.";
    private int _printCount;

    public OdePrintViewModel(DesktopViewModel desktop, string interventionId, int orderNumber)
    {
        _desktop = desktop;
        InterventionId = interventionId;
        OrderNumber = orderNumber;
        PrintCommand = new RelayCommand(() => _ = PrintAsync());
        CloseCommand = new RelayCommand(() => _desktop.CloseWindow($"ode-print:{InterventionId}:{OrderNumber}"));
        OpenInterventionCommand = new RelayCommand(() => _desktop.OpenWindow(OdeWindows.Intervention(_desktop, InterventionId)));
        Load();
    }

    public string InterventionId { get; }
    public int OrderNumber { get; }

    public OdeDocument? Document { get => _document; private set => SetProperty(ref _document, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string PrintLabel => _printCount > 0 ? "Réimprimer" : "Imprimer";

    public RelayCommand PrintCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand OpenInterventionCommand { get; }

    private void Load()
    {
        var intervention = ArgosSession.Current?.Ode.FindIntervention(InterventionId);
        var order = intervention?.Orders.FirstOrDefault(o => o.Number == OrderNumber);
        if (intervention is null || order is null)
        {
            Status = "Ordre de départ introuvable.";
            return;
        }
        _printCount = order.PrintCount;
        Document = OdeDocument.From(intervention, order);
        Status = order.PrintCount > 0 ? $"Déjà imprimé {order.PrintCount} fois." : "Document en attente d'impression.";
        OnPropertyChanged(nameof(PrintLabel));
    }

    private async Task PrintAsync()
    {
        var session = ArgosSession.Current;
        if (session is null) return;
        var result = await session.Api.SendAsync(HttpMethod.Post,
            $"api/ode/interventions/{Uri.EscapeDataString(InterventionId)}/printed/{OrderNumber}");
        if (!result.Ok)
        {
            Status = result.Error ?? "Impression impossible.";
            return;
        }
        _printCount++;
        Status = $"Envoyé à l'imprimante du CS à {DateTime.Now:HH:mm:ss}.";
        OnPropertyChanged(nameof(PrintLabel));
        _desktop.ShowNotification("Imprimante ADAGIO", $"ODE {Document?.Number} imprimé.");
        _ = session.Ode.RefreshAsync();
    }
}
