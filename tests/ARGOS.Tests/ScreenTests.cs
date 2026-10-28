using Argos;
using Argos.Shared.Contracts;
using Argos.ViewModels;
using Argos.Views;
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
        => AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
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

        Assert.Equal(7, vm.Windows.Count);
        vm.Windows[0].IsMaximized = true;
        vm.Minimize(vm.Windows[1]);
        vm.Close(vm.Windows[2]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(6, vm.Windows.Count);
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
}
