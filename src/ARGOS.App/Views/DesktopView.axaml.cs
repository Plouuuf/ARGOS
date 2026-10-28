using System.Collections.Specialized;
using System.ComponentModel;
using Argos.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;

namespace Argos.Views;

/// <summary>
/// Bureau Windows 10. Les fenêtres sont posées sur un Canvas et suivent leur <see cref="AppWindowViewModel"/>
/// (position, taille, réduite, agrandie, premier plan).
/// </summary>
public partial class DesktopView : UserControl
{
    private readonly Dictionary<AppWindowViewModel, AppWindowControl> _controls = new();
    private DesktopViewModel? _vm;

    public DesktopView()
    {
        InitializeComponent();

        Backdrop.PointerPressed += (_, _) => _vm?.OnDesktopBackgroundPressed();
        DesktopIcons.AddHandler(TappedEvent, OnIconTapped);
        DesktopIcons.AddHandler(DoubleTappedEvent, OnIconDoubleTapped);
        WindowLayer.SizeChanged += (_, _) =>
        {
            UpdateWorkArea();
            foreach (var (window, control) in _controls)
                ApplyLayout(window, control);
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm is not null)
        {
            _vm.Windows.CollectionChanged -= OnWindowsChanged;
            foreach (var window in _controls.Keys.ToList())
                RemoveWindow(window);
        }

        _vm = DataContext as DesktopViewModel;
        if (_vm is null)
            return;

        _vm.Windows.CollectionChanged += OnWindowsChanged;
        foreach (var window in _vm.Windows)
            AddWindow(window);
        UpdateWorkArea();
    }

    private void UpdateWorkArea()
    {
        if (_vm is null || WindowLayer.Bounds.Width <= 0)
            return;
        _vm.WorkAreaWidth = WindowLayer.Bounds.Width;
        _vm.WorkAreaHeight = WindowLayer.Bounds.Height;
    }

    private void OnWindowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var window in _controls.Keys.ToList())
                RemoveWindow(window);
            if (_vm is not null)
                foreach (var window in _vm.Windows)
                    AddWindow(window);
            return;
        }

        if (e.OldItems is not null)
            foreach (AppWindowViewModel window in e.OldItems)
                RemoveWindow(window);
        if (e.NewItems is not null)
            foreach (AppWindowViewModel window in e.NewItems)
                AddWindow(window);
    }

    private void AddWindow(AppWindowViewModel window)
    {
        if (_controls.ContainsKey(window))
            return;
        var control = new AppWindowControl { DataContext = window };
        _controls[window] = control;
        WindowLayer.Children.Add(control);
        window.PropertyChanged += OnWindowPropertyChanged;
        ApplyLayout(window, control);
    }

    private void RemoveWindow(AppWindowViewModel window)
    {
        if (!_controls.Remove(window, out var control))
            return;
        window.PropertyChanged -= OnWindowPropertyChanged;
        WindowLayer.Children.Remove(control);
    }

    private void OnWindowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is AppWindowViewModel window && _controls.TryGetValue(window, out var control))
            ApplyLayout(window, control);
    }

    private void ApplyLayout(AppWindowViewModel window, AppWindowControl control)
    {
        control.IsVisible = !window.IsMinimized;
        control.ZIndex = window.ZIndex;

        if (window.IsMaximized)
        {
            Canvas.SetLeft(control, 0);
            Canvas.SetTop(control, 0);
            control.Width = WindowLayer.Bounds.Width;
            control.Height = WindowLayer.Bounds.Height;
        }
        else
        {
            Canvas.SetLeft(control, window.X);
            Canvas.SetTop(control, window.Y);
            control.Width = window.Width;
            control.Height = window.Height;
        }
    }

    // ===== Icônes du bureau : un clic sélectionne, un double-clic ouvre (comme Windows). =====

    private void OnIconTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control { DataContext: AppTileViewModel app })
        {
            _vm?.SelectIcon(app);
            if (_vm is not null) _vm.IsStartMenuOpen = false;
        }
    }

    private void OnIconDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control { DataContext: AppTileViewModel app })
            _vm?.OpenApp(app);
    }
}
