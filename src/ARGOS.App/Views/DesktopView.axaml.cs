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
            _vm.PropertyChanged -= OnPanelsChanged;
            foreach (var window in _controls.Keys.ToList())
                RemoveWindow(window);
        }

        _vm = DataContext as DesktopViewModel;
        if (_vm is null)
            return;

        _vm.Windows.CollectionChanged += OnWindowsChanged;
        _vm.PropertyChanged += OnPanelsChanged;
        foreach (var window in _vm.Windows)
            AddWindow(window);
        UpdateWorkArea();
    }

    /// <summary>Recherche et menu Démarrer : la saisie part directement dans la zone de recherche.</summary>
    private void OnPanelsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesktopViewModel.IsSearchOpen) && _vm?.IsSearchOpen == true)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => SearchBox.Focus());
        if (e.PropertyName == nameof(DesktopViewModel.IsStartMenuOpen) && _vm?.IsStartMenuOpen == true)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => StartSearchBox.Focus());
    }

    private void OnTaskViewBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender) && _vm is not null) _vm.IsTaskViewOpen = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_vm is not null && e.Key == Key.Escape && (_vm.IsSearchOpen || _vm.IsTaskViewOpen || _vm.IsActionCenterOpen || _vm.IsStartMenuOpen))
        {
            _vm.OnDesktopBackgroundPressed();
            e.Handled = true;
            return;
        }
        if (_vm is not null && e.Key == Key.Enter && _vm.IsSearchOpen)
        {
            _vm.OpenFirstResultCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (_vm is not null && e.Key == Key.Enter && _vm.IsStartMenuOpen && _vm.StartResults.FirstOrDefault() is { } first)
        {
            first.OpenCommand.Execute(null);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
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
