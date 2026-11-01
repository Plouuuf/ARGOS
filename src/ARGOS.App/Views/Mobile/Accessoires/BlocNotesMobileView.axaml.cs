using Argos.ViewModels;
using Argos.ViewModels.Accessoires;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Argos.Views.Mobile.Accessoires;

/// <summary>
/// Bloc-notes en version iPhone (app Notes) : menu « ••• » (Fichier, Édition, Format, Affichage), barre d'outils,
/// et les mêmes raccourcis clavier que la version bureau (Ctrl+N / O / S, F5, Ctrl + / − / 0).
/// </summary>
public partial class BlocNotesMobileView : UserControl
{
    public BlocNotesMobileView()
    {
        InitializeComponent();
        DateItem.Click += (_, _) => Vm?.InsertDateTime();
        DateTool.Click += (_, _) => { Vm?.InsertDateTime(); Editor.Focus(); };
        UndoItem.Click += (_, _) => { Editor.Undo(); Editor.Focus(); };
        UndoTool.Click += (_, _) => { Editor.Undo(); Editor.Focus(); };
        RedoItem.Click += (_, _) => { Editor.Redo(); Editor.Focus(); };
        RedoTool.Click += (_, _) => { Editor.Redo(); Editor.Focus(); };
        CutItem.Click += (_, _) => { Editor.Cut(); Editor.Focus(); };
        CopyItem.Click += (_, _) => { Editor.Copy(); Editor.Focus(); };
        PasteItem.Click += (_, _) => { Editor.Paste(); Editor.Focus(); };
        DeleteItem.Click += (_, _) => DeleteSelection();
        SelectAllItem.Click += (_, _) => { Editor.SelectAll(); Editor.Focus(); };
        // Une action du menu « ••• » referme le menu.
        MenuPanel.AddHandler(Button.ClickEvent, (_, _) => MenuToggle.IsChecked = false, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
    }

    private BlocNotesViewModel? Vm => DataContext as BlocNotesViewModel;

    private void DeleteSelection()
    {
        var text = Editor.Text ?? string.Empty;
        var start = Math.Clamp(Math.Min(Editor.SelectionStart, Editor.SelectionEnd), 0, text.Length);
        var end = Math.Clamp(Math.Max(Editor.SelectionStart, Editor.SelectionEnd), 0, text.Length);
        if (end > start)
        {
            Editor.Text = text[..start] + text[end..];
            Editor.CaretIndex = start;
        }
        Editor.Focus();
    }

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
