using Argos.Services;
using Argos.Shared.Contracts;
using Argos.ViewModels;
using Argos.ViewModels.Ode;

namespace Argos.Views.Apps.Ode;

/// <summary>Fenêtres d'ADAGIO ODE (prise d'appel, création d'ODE, suivi d'intervention, impression).</summary>
public static class OdeWindows
{
    private static Avalonia.Media.Imaging.Bitmap Icon => ImageLoader.Asset("Icons/adagio-ode.png");

    public static string PriseAppelKey(string callId) => "prise-appel:" + callId;

    public static WindowSpec PriseAppel(DesktopViewModel desktop, string callId)
        => new(PriseAppelKey(callId), "Prise d'appel - " + PriseAppelViewModel.Version, Icon,
            () => new PriseAppelView { DataContext = new PriseAppelViewModel(desktop, callId) },
            1180, 800);

    /// <summary>Nouvelle fiche d'appel (décroché manuel), ouverte aussitôt dans la prise d'appel.</summary>
    public static async Task NewCallAsync(DesktopViewModel desktop)
    {
        var session = ArgosSession.Current;
        if (session is null) return;
        try
        {
            var call = await session.Api.PostAsync<CallRecord>("api/ode/calls");
            _ = session.Ode.RefreshAsync();
            desktop.OpenWindow(PriseAppel(desktop, call.Id));
        }
        catch (Exception ex)
        {
            desktop.ShowNotification("ADAGIO ODE", "Création de l'appel impossible : " + ex.Message, Icon);
        }
    }

    public static string CreateOdeKey(string? callId, string? interventionId)
        => "ode-creation:" + (callId ?? interventionId ?? "manuel");

    public static WindowSpec CreateOde(DesktopViewModel desktop, string? callId, string? interventionId, string? procedure)
        => new(CreateOdeKey(callId, interventionId),
            interventionId is null ? "ADAGIO ODE - Création de l'ordre de départ" : "ADAGIO ODE - Départ complémentaire",
            Icon,
            () => new CreateOdeView { DataContext = new CreateOdeViewModel(desktop, callId, interventionId, procedure) },
            1020, 760);

    public static string InterventionKey(string id) => "intervention:" + id;

    public static WindowSpec Intervention(DesktopViewModel desktop, string interventionId)
        => new(InterventionKey(interventionId), "ADAGIO ODE - Suivi d'intervention", Icon,
            () => new InterventionView { DataContext = new InterventionViewModel(desktop, interventionId) },
            1040, 720);

    public static WindowSpec Print(DesktopViewModel desktop, string interventionId, int orderNumber)
        => new($"ode-print:{interventionId}:{orderNumber}", "Imprimante ADAGIO - Ordre de départ", Icon,
            () => new OdePrintView { DataContext = new OdePrintViewModel(desktop, interventionId, orderNumber) },
            900, 860);
}
