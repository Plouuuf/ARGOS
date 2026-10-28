using Argos.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;

namespace Argos.Views;

/// <summary>Écran de verrouillage : un clic, un appui ou une touche ouvre l'écran de connexion.</summary>
public partial class LockScreenView : UserControl
{
    public LockScreenView()
    {
        InitializeComponent();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        (DataContext as ShellViewModel)?.ShowLogin();
        e.Handled = true;
    }
}
