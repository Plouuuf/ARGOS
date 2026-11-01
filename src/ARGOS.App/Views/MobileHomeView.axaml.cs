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
/// - un tableau à colonnes fixes plus large que l'écran garde sa largeur et se fait glisser horizontalement ;
///   le reste (tableaux de bord, tuiles, fiches) s'adapte à la largeur de l'écran.
/// </summary>
public partial class MobileHomeView : UserControl
{
    // Largeur maximale imposée par un tableau (au-delà, on accepte de tronquer).
    private const double MaxContentWidth = 980;
    private static readonly IBrush DefaultAccent = new SolidColorBrush(Color.Parse("#007AFF"));

    /// <summary>
    /// Une page de la pile de navigation. Les pages restent dans l'arbre visuel (masquées) tant qu'on peut y revenir :
    /// leur saisie et leur état sont conservés, comme dans une vraie app iPhone.
    /// </summary>
    private sealed class PageEntry
    {
        public required string Key { get; init; }
        public required Control Root { get; init; }
        public required Control Content { get; init; }
        public IBrush? Accent { get; init; }
        public ScrollViewer? Pan { get; init; }
        public Border? Host { get; init; }
        public double BaseWidth { get; init; }
        public double MinWidth { get; set; }
        public Grid? SidebarGrid { get; set; }
        public Control? Sidebar { get; set; }
        public Border? Backdrop { get; set; }
        public double SidebarColumn { get; set; }
        public bool MeasureScheduled { get; set; }
    }

    private readonly Panel _pages = new();
    private readonly Dictionary<string, PageEntry> _cache = new();
    private PageEntry? _current;
    private DesktopViewModel? _vm;

    public MobileHomeView()
    {
        InitializeComponent();
        MobileBody.Content = _pages;
        MenuButton.Click += (_, _) => SetDrawer(_current, !(_current?.Sidebar?.IsVisible ?? false));
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.MobileCanGoBack = null;
        }

        _vm = DataContext as DesktopViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            _vm.MobileCanGoBack = CanGoBack;
        }
        UpdateBody();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesktopViewModel.MobileApp))
            UpdateBody();
    }

    private bool CanGoBack()
        => _current?.Content.DataContext is not ICloseGuard guard || guard.TryClose();

    private void UpdateBody()
    {
        // Pages qui ne sont plus dans la pile : retirées (leur vue libère alors son modèle).
        var alive = new HashSet<string>(_vm?.MobileStackKeys ?? []);
        if (_vm?.MobileApp is { } shown) alive.Add(shown.Key);
        foreach (var gone in _cache.Values.Where(p => !alive.Contains(p.Key)).ToList())
        {
            _cache.Remove(gone.Key);
            _pages.Children.Remove(gone.Root);
        }

        if (_vm?.MobileApp is not { } page)
        {
            _current = null;
            MenuButton.IsVisible = false;
            return;
        }

        if (!_cache.TryGetValue(page.Key, out var entry))
        {
            entry = Create(page);
            _cache[page.Key] = entry;
            _pages.Children.Add(entry.Root);
        }
        foreach (var p in _cache.Values) p.Root.IsVisible = ReferenceEquals(p, entry);
        _current = entry;
        BackLabel.Foreground = entry.Accent ?? DefaultAccent;
        BackIcon.Foreground = entry.Accent ?? DefaultAccent;
        MenuButton.IsVisible = entry.Sidebar is not null;
    }

    private PageEntry Create(WindowSpec page)
    {
        var content = page.CreateContent();
        var accent = Argos.Views.Mobile.MobilePage.GetAccent(content);
        // Vue iPhone dédiée : affichée telle quelle.
        if (Argos.Views.Mobile.MobilePage.GetIsNative(content))
            return new PageEntry { Key = page.Key, Root = content, Content = content, Accent = accent };

        // Vue bureau adaptée : menu latéral en tiroir, tableaux larges glissés horizontalement ;
        // fenêtre de saisie : mise en page fixe gardée lisible.
        var baseWidth = _vm!.Apps.Any(a => a.Id == page.Key) ? 0 : Math.Min(page.Width, MaxContentWidth);
        var host = new Border { Child = content, HorizontalAlignment = HorizontalAlignment.Left };
        var pan = new ScrollViewer
        {
            Content = host,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        var entry = new PageEntry
        {
            Key = page.Key, Root = pan, Content = content, Accent = accent, Pan = pan, Host = host,
            BaseWidth = baseWidth, MinWidth = baseWidth
        };
        pan.SizeChanged += (_, _) => FitHost(entry);

        void OnLoaded(object? s, RoutedEventArgs e)
        {
            content.Loaded -= OnLoaded;
            FindSidebar(entry);
            if (ReferenceEquals(_current, entry)) MenuButton.IsVisible = entry.Sidebar is not null;
            MeasureTables(entry);
        }
        content.Loaded += OnLoaded;
        // Changement de rubrique, d'onglet ou de filtre : la largeur utile peut changer.
        content.AddHandler(Button.ClickEvent, (_, _) => ScheduleMeasure(entry), RoutingStrategies.Bubble, handledEventsToo: true);
        return entry;
    }

    private static void ScheduleMeasure(PageEntry entry)
    {
        if (entry.MeasureScheduled) return;
        entry.MeasureScheduled = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            entry.MeasureScheduled = false;
            MeasureTables(entry);
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>Largeur minimale = la plus large ligne de tableau visible ; sinon la largeur de l'écran.</summary>
    private static void MeasureTables(PageEntry entry)
    {
        double need = 0;
        foreach (var g in entry.Content.GetSelfAndVisualDescendants().OfType<Grid>())
        {
            if (g.ColumnDefinitions.Count < 3 || !g.IsEffectivelyVisible || ReferenceEquals(g, entry.SidebarGrid)) continue;
            var fixedWidth = g.ColumnDefinitions.Where(c => c.Width.IsAbsolute).Sum(c => c.Width.Value);
            var stars = g.ColumnDefinitions.Count(c => c.Width.IsStar);
            if (fixedWidth < 300) continue;
            need = Math.Max(need, fixedWidth + stars * 90 + 48);
        }
        entry.MinWidth = Math.Max(entry.BaseWidth, Math.Min(need, MaxContentWidth));
        FitHost(entry);
    }

    private static void FitHost(PageEntry entry)
    {
        if (entry.Pan is null || entry.Host is null) return;
        var viewport = entry.Pan.Bounds.Width;
        if (viewport <= 0) return;
        entry.Host.Width = Math.Max(viewport, entry.MinWidth);
        entry.Host.Height = entry.Pan.Bounds.Height;
    }

    private void FindSidebar(PageEntry entry)
    {
        var grid = entry.Content.GetSelfAndVisualDescendants().OfType<Grid>().FirstOrDefault(g =>
            g.ColumnDefinitions.Count >= 2
            && g.ColumnDefinitions[0].Width is { IsAbsolute: true, Value: >= 150 and <= 300 }
            && g.ColumnDefinitions[1].Width.IsStar
            && g.Children.Any(c => Grid.GetColumn(c) == 0)
            && g.Children.Any(c => Grid.GetColumn(c) == 1));
        if (grid is null) return;
        var sidebar = grid.Children.First(c => Grid.GetColumn(c) == 0);

        entry.SidebarGrid = grid;
        entry.Sidebar = sidebar;
        entry.SidebarColumn = grid.ColumnDefinitions[0].Width.Value;
        grid.ColumnDefinitions[0].Width = new GridLength(0);

        // Tiroir : le menu passe au-dessus du contenu, sur un voile sombre.
        var backdrop = new Border { Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)), IsVisible = false, ZIndex = 50 };
        Grid.SetColumnSpan(backdrop, grid.ColumnDefinitions.Count);
        Grid.SetRowSpan(backdrop, Math.Max(1, grid.RowDefinitions.Count));
        backdrop.PointerPressed += (_, e) => { SetDrawer(entry, false); e.Handled = true; };
        grid.Children.Add(backdrop);
        entry.Backdrop = backdrop;

        Grid.SetColumnSpan(sidebar, grid.ColumnDefinitions.Count);
        sidebar.Width = Math.Min(entry.SidebarColumn + 40, 300);
        sidebar.HorizontalAlignment = HorizontalAlignment.Left;
        sidebar.ZIndex = 51;
        sidebar.IsVisible = false;
        // Un choix dans le menu le referme.
        sidebar.AddHandler(Button.ClickEvent, (_, _) => SetDrawer(entry, false), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private static void SetDrawer(PageEntry? entry, bool open)
    {
        if (entry?.Sidebar is null || entry.Backdrop is null) return;
        entry.Sidebar.IsVisible = open;
        entry.Backdrop.IsVisible = open;
        // Le tiroir s'ouvre toujours à gauche de l'écran, même si le contenu a glissé.
        if (open && entry.Pan is not null) entry.Pan.Offset = new Vector(0, entry.Pan.Offset.Y);
    }
}
