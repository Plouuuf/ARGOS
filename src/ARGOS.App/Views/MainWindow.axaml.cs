using Avalonia.Controls;
using Avalonia.Input;

namespace Argos.Views;

/// <summary>
/// Fenêtre unique sur ordinateur : ARGOS occupe tout l'écran, comme un vrai poste CTA.
/// F11 bascule entre plein écran et fenêtre.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            WindowState = WindowState == WindowState.FullScreen ? WindowState.Maximized : WindowState.FullScreen;
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }
}
