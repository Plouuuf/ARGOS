using Avalonia;

namespace Argos.Setup;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // « --uninstall » : lancé depuis « Applications installées » de Windows.
        App.Uninstall = args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));
        App.Silent = args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase));
        if (App.Silent)
        {
            try
            {
                if (App.Uninstall) SetupInstaller.Uninstall();
                else SetupInstaller.InstallAsync(SetupInstaller.DefaultInstallPath, true, true).GetAwaiter().GetResult();
                return 0;
            }
            catch { return 1; }
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();
}
