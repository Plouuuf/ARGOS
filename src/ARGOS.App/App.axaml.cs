using Argos.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Argos;

/// <summary>
/// Application Avalonia commune à toutes les plateformes.
/// Ordinateur (Windows / macOS) : une fenêtre plein écran, <see cref="MainWindow"/>.
/// Téléphone / tablette (iOS / Android) : une seule vue, <see cref="ShellView"/>.
/// </summary>
public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new ShellView();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static void Quit(int exitCode = 0)
        => (Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(exitCode);
}
