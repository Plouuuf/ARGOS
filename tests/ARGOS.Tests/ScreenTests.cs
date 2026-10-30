using Argos;
using Argos.Shared.Contracts;
using Argos.ViewModels;
using Argos.Views;
using Argos.ViewModels.Ode;
using Argos.Views.Apps.Ode;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Argos.Tests.TestAppBuilder))]

namespace Argos.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Chaque écran est chargé dans une fenêtre sans écran : une exception = test en échec.</summary>
public class ScreenTests
{
    private static readonly ArgosUserDto TestUser =
        new("1", "Sapeur Test", null, [AccessLevels.Sapeur, AccessLevels.Operateur]);

    private static Window Show(Control content, double width = 1440, double height = 900)
    {
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void Fenetre_principale_se_charge()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<ShellView>(window.Content);
    }

    [AvaloniaFact]
    public void Ecran_de_connexion_se_charge()
    {
        var shell = new ShellView();
        Show(shell);
        var vm = Assert.IsType<ShellViewModel>(shell.DataContext);
        vm.ShowLogin();
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsLoginScreen);
    }

    [AvaloniaFact]
    public void Bureau_fenetres_menu_et_notifications()
    {
        var vm = new DesktopViewModel(TestUser, null, () => { }, () => { });
        Show(new DesktopView { DataContext = vm });

        foreach (var app in vm.Apps)
            vm.OpenApp(app);
        vm.IsStartMenuOpen = true;
        vm.ShowNotification("ADAGIO ODE", "Nouvel ordre de départ");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Argos.Services.AppCatalog.All.Count, vm.Windows.Count);
        vm.Windows[0].IsMaximized = true;
        vm.Minimize(vm.Windows[1]);
        vm.Close(vm.Windows[2]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Argos.Services.AppCatalog.All.Count - 1, vm.Windows.Count);
    }

    [AvaloniaFact]
    public void Interface_telephone()
    {
        var vm = new DesktopViewModel(TestUser, null, () => { }, () => { }) { IsCompactLayout = true };
        Show(new MobileHomeView { DataContext = vm }, 390, 844);

        vm.OpenApp(vm.Apps[0]);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsMobileAppOpen);
        vm.MobileBackCommand.Execute(null);
        Assert.False(vm.IsMobileAppOpen);
    }

    [AvaloniaFact]
    public void Ecrans_de_bienvenue_et_verrouillage()
    {
        var shell = new ShellViewModel();
        Show(new WelcomeView { DataContext = shell });
        Show(new LockScreenView { DataContext = shell });
        Show(new LoginView { DataContext = shell });
    }

    [AvaloniaFact]
    public void Fenetres_ADAGIO_ODE_se_chargent_sans_session()
    {
        var desktop = new DesktopViewModel(TestUser, null, () => { }, () => { });
        Show(new DesktopView { DataContext = desktop });

        desktop.OpenWindow(OdeWindows.PriseAppel(desktop, "20261029-0001"));
        desktop.OpenWindow(OdeWindows.CreateOde(desktop, "20261029-0001", null, "ROUGE"));
        desktop.OpenWindow(OdeWindows.CreateOde(desktop, null, "abc", "BLANCHE"));
        desktop.OpenWindow(OdeWindows.Intervention(desktop, "abc"));
        desktop.OpenWindow(OdeWindows.Print(desktop, "abc", 1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(5, desktop.Windows.Count);

        // Même clé : la fenêtre existante revient au premier plan, pas de doublon.
        desktop.OpenCall("20261029-0001");
        Assert.Equal(5, desktop.Windows.Count);

        foreach (var w in desktop.Windows.ToList())
            desktop.Close(w);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(desktop.Windows);
    }

    [AvaloniaFact]
    public void Prise_d_appel_touches_F_sans_session()
    {
        var desktop = new DesktopViewModel(TestUser, null, () => { }, () => { });
        var vm = new PriseAppelViewModel(desktop, "X");
        Show(new PriseAppelView { DataContext = vm }, 1180, 770);
        Assert.Equal("Prise d'appel - v1.8.0", vm.WindowTitle);
        Assert.Equal(9, vm.FKeys.Count);
        Assert.False(vm.HandleKey("F12"));
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Feuille_ODE_reprend_le_modele()
    {
        var intervention = new Intervention
        {
            Number = "210-593", NatureCode = "117", Cstc = "CRET", Address = "IMPASSE GUINER", Building = "Chantier",
            Locality = "L'Echat", CallerPhone = "553-235-420", CallerInfo = "Feu de structure ** Une personne bloquée"
        };
        var order = new DepartureOrder
        {
            Number = 1, Procedure = "BLANCHE", ValidatedAt = DateTimeOffset.Now, OperatorName = "BERTIER",
            Engins = ["EPAN 24 MALF", "FPTL 202 MALF"]
        };
        var doc = OdeDocument.From(intervention, order);
        Assert.Equal("Mission BSPP - Procédure BLANCHE", doc.MissionTitle);
        Assert.Equal("PREMIERS DEPARTS - INCENDIE", doc.DepartTitle);
        Assert.Equal("EPAN 24 MALF (EPAN_1_MALF)", doc.MainEngin);
        Assert.Equal("FPTL 202 MALF (FPTL_1_MALF)", doc.Complementary);
        Assert.Equal("L'ECHAT", doc.Locality);
        Assert.Equal("IMPASSE GUINER (Chantier)", doc.Address);
        Show(new OdeDocumentView { DataContext = doc }, 900, 1100);
    }

    [AvaloniaFact]
    public void Natures_et_engins()
    {
        Assert.True(Natures.All.Count > 150);
        Assert.Equal("Feu de cheminée", Natures.Find("112")?.Label);
        Assert.Equal(25, EnginsCatalog.All.Count);
        Assert.NotNull(EnginsCatalog.Find("VSAV 947 CRET"));
        Assert.NotNull(EnginsCatalog.Find("PSE 212"));
    }
}

