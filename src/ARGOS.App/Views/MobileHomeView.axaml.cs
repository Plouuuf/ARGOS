using System.ComponentModel;
using Argos.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Argos.Views;

/// <summary>
/// Interface téléphone : écran d'accueil avec les applications, chacune ouverte en plein écran.
/// Mode téléphone des applications prévues pour le bureau :
/// - le menu latéral (première colonne fixe de 150 à 300 px) devient un tiroir ouvert par le bouton ☰ ;
/// - le contenu garde une largeur minimale lisible (tableaux) et se fait glisser horizontalement si l'écran est plus étroit.
/// </summary>
public partial class MobileHomeView : UserControl
{
    // Largeur minimale du contenu : au-delà, les tableaux à colonnes fixes seraient tronqués.
    private const double MaxContentWidth = 860;

    private DesktopViewModel? _vm;
    private ScrollViewer? _pan;
    private Border? _host;
    private double _minWidth;
    private Grid? _sidebarGrid;
    private Control? _sidebar;
    private Border? _backdrop;
    private GridLength _sidebarColumn;

    public MobileHomeView()
    {
        InitializeComponent();
        MenuButton.Click += (_, _) => SetDrawer(!(_sidebar?.IsVisible ?? false));
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = DataContext as DesktopViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        UpdateBody();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesktopViewModel.MobileApp))
            UpdateBody();
    }

    private void UpdateBody()
    {
        _sidebarGrid = null;
        _sidebar = null;
        _backdrop = null;
        MenuButton.IsVisible = false;
        if (_vm?.MobileApp is not { } page)
        {
            MobileBody.Content = null;
            return;
        }

        var content = page.CreateContent();
        _minWidth = Math.Min(page.Width, MaxContentWidth);
        _host = new Border { Child = content, HorizontalAlignment = HorizontalAlignment.Left };
        _pan = new ScrollViewer
        {
            Content = _host,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        _pan.SizeChanged += (_, _) => FitHost();
        MobileBody.Content = _pan;

        // Le menu latéral n'est connu qu'une fois l'arbre visuel construit.
        void OnLoaded(object? s, RoutedEventArgs e)
        {
            content.Loaded -= OnLoaded;
            FindSidebar(content);
            FitHost();
        }
        content.Loaded += OnLoaded;
    }

    /// <summary>Largeur du contenu : celle de l'écran, sans descendre sous le minimum lisible de l'application.</summary>
    private void FitHost()
    {
        if (_pan is null || _host is null) return;
        var viewport = _pan.Bounds.Width;
        if (viewport <= 0) return;
        var sidebar = _sidebarGrid is null ? 0 : _sidebarColumn.Value;
        var min = Math.Max(0, _minWidth - sidebar);
        _host.Width = Math.Max(viewport, min);
        _host.Height = _pan.Bounds.Height;
    }

    private void FindSidebar(Control root)
    {
        var grid = root.GetSelfAndVisualDescendants().OfType<Grid>().FirstOrDefault(g =>
            g.ColumnDefinitions.Count >= 2
            && g.ColumnDefinitions[0].Width is { IsAbsolute: true, Value: >= 150 and <= 300 }
            && g.ColumnDefinitions[1].Width.IsStar
            && g.Children.Any(c => Grid.GetColumn(c) == 0)
            && g.Children.Any(c => Grid.GetColumn(c) == 1));
        if (grid is null) return;
        var sidebar = grid.Children.First(c => Grid.GetColumn(c) == 0);

        _sidebarGrid = grid;
        _sidebar = sidebar;
        _sidebarColumn = grid.ColumnDefinitions[0].Width;
        grid.ColumnDefinitions[0].Width = new GridLength(0);

        // Tiroir : le menu passe au-dessus du contenu, sur un voile sombre.
        _backdrop = new Border { Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)), IsVisible = false, ZIndex = 50 };
        Grid.SetColumnSpan(_backdrop, grid.ColumnDefinitions.Count);
        Grid.SetRowSpan(_backdrop, Math.Max(1, grid.RowDefinitions.Count));
        _backdrop.PointerPressed += (_, e) => { SetDrawer(false); e.Handled = true; };
        grid.Children.Add(_backdrop);

        Grid.SetColumnSpan(sidebar, grid.ColumnDefinitions.Count);
        sidebar.Width = Math.Min(_sidebarColumn.Value + 40, 300);
        sidebar.HorizontalAlignment = HorizontalAlignment.Left;
        sidebar.ZIndex = 51;
        sidebar.IsVisible = false;
        // Un choix dans le menu le referme.
        sidebar.AddHandler(Button.ClickEvent, (_, _) => SetDrawer(false), RoutingStrategies.Bubble, handledEventsToo: true);

        MenuButton.IsVisible = true;
    }

    private void SetDrawer(bool open)
    {
        if (_sidebar is null || _backdrop is null) return;
        _sidebar.IsVisible = open;
        _backdrop.IsVisible = open;
        // Le tiroir s'ouvre toujours à gauche de l'écran, même si le contenu a glissé.
        if (open && _pan is not null) _pan.Offset = new Vector(0, _pan.Offset.Y);
    }
}
