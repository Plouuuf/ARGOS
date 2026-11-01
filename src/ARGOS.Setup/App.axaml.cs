using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Argos.Setup;

public partial class App : Application
{
    public static bool Uninstall { get; set; }
    public static bool Silent { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(Uninstall);
        base.OnFrameworkInitializationCompleted();
    }
}
