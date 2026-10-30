using Argos.ViewModels.Accessoires;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Argos.Views.Apps.Accessoires;

/// <summary>Calculatrice : le clavier (chiffres, opérateurs, Entrée, Échap, Retour) pilote aussi la calculatrice.</summary>
public partial class CalculatriceView : UserControl
{
    public CalculatriceView()
    {
        InitializeComponent();
        AddHandler(TextInputEvent, OnText, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => Focus();
        PointerPressed += (_, _) => Focus();
    }

    private CalculatriceViewModel? Vm => DataContext as CalculatriceViewModel;

    private void OnText(object? sender, TextInputEventArgs e)
    {
        if (Vm is { } vm && e.Text is { Length: 1 } t && vm.Key(t)) e.Handled = true;
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        var name = e.Key switch
        {
            Key.Enter => "Enter",
            Key.Back => "Back",
            Key.Escape => "Escape",
            Key.Delete => "Delete",
            _ => null
        };
        if (name is not null && Vm is { } vm && vm.Key(name)) e.Handled = true;
    }
}
