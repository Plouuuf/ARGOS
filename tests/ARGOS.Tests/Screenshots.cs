using Argos.Shared.Contracts;
using Argos.ViewModels;
using Argos.ViewModels.Ode;
using Argos.Views;
using Argos.Views.Apps.Ode;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace Argos.Tests;

/// <summary>
/// Captures d'écran des principaux écrans (dossier ARGOS_SCREENSHOTS, publié par la compilation GitHub)
/// pour comparer visuellement avec les références (prise d'appel vLife, feuille ODE).
/// </summary>
public class Screenshots
{
    private static readonly string Folder = Environment.GetEnvironmentVariable("ARGOS_SCREENSHOTS")
                                            ?? Path.Combine(Path.GetTempPath(), "argos-screenshots");

    private static void Capture(Control content, double width, double height, string name)
    {
        Directory.CreateDirectory(Folder);
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame!.Save(Path.Combine(Folder, name + ".png"));
        window.Close();
    }

    private static CallRecord SampleCall() => new()
    {
        Id = "20261029-0007",
        Status = CallStatuses.EnCours,
        ReceivedAt = DateTimeOffset.Now.AddSeconds(-17),
        OperatorName = "BERTIER",
        Poste = "ADAGIO",
        Origin = "Téléphone urbain",
        CallerPhone = "553-235-420",
        LocationMode = "NORMAL",
        LocationNetwork = "ROUTIER/SITES",
        StreetNumber = "12",
        Street = "Impasse Guiner",
        Cstc = "CRET",
        AlertAddress = "12 IMPASSE GUINER",
        Observations = "Chantier, accès par le portail",
        NatureCode = "117",
        Reason = "Feu de structure ** Une personne bloquée au 1er étage, fumées visibles depuis la rue."
    };

    [AvaloniaFact]
    public void Prise_d_appel()
    {
        var desktop = new DesktopViewModel(new ArgosUserDto("1", "Bertier", null, [AccessLevels.Sapeur]), null, () => { }, () => { });
        var vm = new PriseAppelViewModel(desktop, "20261029-0007");
        vm.ApplyCall(SampleCall());
        Capture(new PriseAppelView { DataContext = vm }, 1180, 769, "prise-appel");

        var empty = new PriseAppelViewModel(desktop, "20261029-0008");
        empty.ApplyCall(new CallRecord { Id = "20261029-0008", ReceivedAt = DateTimeOffset.Now, OperatorName = "BERTIER", Poste = "ADAGIO" });
        Capture(new PriseAppelView { DataContext = empty }, 1180, 769, "prise-appel-vide");
    }

    [AvaloniaFact]
    public void Feuille_ODE()
    {
        var intervention = new Intervention
        {
            Number = "210-593", NatureCode = "117", Cstc = "CRET", Address = "IMPASSE GUINER", Building = "Chantier",
            Locality = "L'Echat", CallerPhone = "553-235-420", CallerInfo = "Feu de structure ** Une personne bloqué"
        };
        var order = new DepartureOrder
        {
            Number = 1, Procedure = "BLANCHE", ValidatedAt = DateTimeOffset.Now, OperatorName = "BERTIER",
            Engins = ["EPAN 24 MALF", "FPTL 202 MALF"]
        };
        Capture(new OdeDocumentView { DataContext = OdeDocument.From(intervention, order) }, 820, 900, "feuille-ode");
    }

    [AvaloniaFact]
    public void Application_ODE_et_bureau()
    {
        var desktop = new DesktopViewModel(new ArgosUserDto("1", "Bertier", null, [AccessLevels.Sapeur]), null, () => { }, () => { });
        Capture(new OdeAppView { DataContext = new OdeAppViewModel(desktop) }, 980, 640, "adagio-ode");
        Capture(new CreateOdeView { DataContext = new CreateOdeViewModel(desktop, null, null, "ROUGE") }, 1020, 730, "creation-ode");

        desktop.OpenApp(desktop.Apps[0]);
        Capture(new DesktopView { DataContext = desktop }, 1440, 900, "bureau");
    }

    [AvaloniaFact]
    public void Ecran_de_connexion()
    {
        var shell = new ShellView();
        Capture(shell, 1440, 900, "verrouillage");
    }
}
