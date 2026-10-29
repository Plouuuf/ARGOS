using Argos.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Argos.Views;

/// <summary>
/// Fenêtre d'application sur le bureau : déplacement par la barre de titre, redimensionnement par les bords,
/// premier plan au clic. La position est gardée dans <see cref="AppWindowViewModel"/>.
/// </summary>
public partial class AppWindowControl : UserControl
{
    private const double MinWidthValue = 420;
    private const double MinHeightValue = 260;

    private enum DragMode { None, Move, ResizeRight, ResizeBottom, ResizeCorner }

    private DragMode _mode = DragMode.None;
    private Point _start;
    private Rect _origin;

    public AppWindowControl()
    {
        InitializeComponent();

        // Tout clic dans la fenêtre l'amène au premier plan (avant que le contenu ne le traite).
        AddHandler(PointerPressedEvent, (_, _) => WindowModel?.ActivateCommand.Execute(null), RoutingStrategies.Tunnel);

        TitleBar.PointerPressed += (s, e) => BeginDrag(e, DragMode.Move);
        TitleBar.DoubleTapped += (_, e) =>
        {
            WindowModel?.ToggleMaximizeCommand.Execute(null);
            e.Handled = true;
        };
        ResizeRight.PointerPressed += (_, e) => BeginDrag(e, DragMode.ResizeRight);
        ResizeBottom.PointerPressed += (_, e) => BeginDrag(e, DragMode.ResizeBottom);
        ResizeCorner.PointerPressed += (_, e) => BeginDrag(e, DragMode.ResizeCorner);
    }

    private AppWindowViewModel? WindowModel => DataContext as AppWindowViewModel;

    /// <summary>Zone de travail (Canvas) qui contient la fenêtre.</summary>
    private Control? Layer => Parent as Control;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Body.Content = WindowModel is { } window ? window.Spec.CreateContent() : null;
    }

    private void BeginDrag(PointerPressedEventArgs e, DragMode mode)
    {
        var window = WindowModel;
        var layer = Layer;
        if (window is null || layer is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        // Pas de déplacement d'une fenêtre agrandie (double-clic pour la restaurer, comme Windows).
        if (window.IsMaximized)
            return;

        // Un clic sur un bouton de la barre de titre n'est pas un déplacement.
        if (mode == DragMode.Move && e.Source is Visual source && IsInsideButton(source))
            return;

        _mode = mode;
        _start = e.GetPosition(layer);
        _origin = new Rect(window.X, window.Y, window.Width, window.Height);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var window = WindowModel;
        var layer = Layer;
        if (_mode == DragMode.None || window is null || layer is null)
            return;

        var position = e.GetPosition(layer);
        var dx = position.X - _start.X;
        var dy = position.Y - _start.Y;
        var bounds = layer.Bounds;

        switch (_mode)
        {
            case DragMode.Move:
                // La barre de titre reste toujours atteignable.
                window.X = Math.Clamp(_origin.X + dx, 80 - _origin.Width, Math.Max(0, bounds.Width - 80));
                window.Y = Math.Clamp(_origin.Y + dy, 0, Math.Max(0, bounds.Height - 31));
                break;
            case DragMode.ResizeRight:
                window.Width = Math.Max(MinWidthValue, _origin.Width + dx);
                break;
            case DragMode.ResizeBottom:
                window.Height = Math.Max(MinHeightValue, _origin.Height + dy);
                break;
            case DragMode.ResizeCorner:
                window.Width = Math.Max(MinWidthValue, _origin.Width + dx);
                window.Height = Math.Max(MinHeightValue, _origin.Height + dy);
                break;
        }
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        EndDrag(e.Pointer);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _mode = DragMode.None;
    }

    private void EndDrag(IPointer pointer)
    {
        if (_mode == DragMode.None)
            return;
        _mode = DragMode.None;
        pointer.Capture(null);
    }

    private bool IsInsideButton(Visual visual)
    {
        for (Visual? current = visual; current is not null && !ReferenceEquals(current, TitleBar); current = current.GetVisualParent())
        {
            if (current is Button)
                return true;
        }
        return false;
    }
}
