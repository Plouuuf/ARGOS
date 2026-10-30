using Argos.ViewModels;
using Argos.ViewModels.Accessoires;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Argos.Views.Apps.Accessoires;

/// <summary>Bloc-notes : raccourcis Ctrl+N / O / S, F5 (heure et date), Ctrl + / − / 0 (zoom).</summary>
public partial class BlocNotesView : UserControl
{
    public BlocNotesView()
    {
        InitializeComponent();
        DateItem.Click += (_, _) => Vm?.InsertDateTime();
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => Editor.Focus();
    }

    private BlocNotesViewModel? Vm => DataContext as BlocNotesViewModel;

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm) return;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        RelayCommand? command = (ctrl, e.Key) switch
        {
            (true, Key.N) => vm.NewCommand,
            (true, Key.O) => vm.OpenCommand,
            (true, Key.S) when e.KeyModifiers.HasFlag(KeyModifiers.Shift) => vm.SaveAsCommand,
            (true, Key.S) => vm.SaveCommand,
            (true, Key.OemPlus or Key.Add) => vm.ZoomInCommand,
            (true, Key.OemMinus or Key.Subtract) => vm.ZoomOutCommand,
            (true, Key.D0 or Key.NumPad0) => vm.ZoomResetCommand,
            _ => null
        };
        if (command is not null)
        {
            command.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            vm.InsertDateTime();
            e.Handled = true;
        }
    }
}
