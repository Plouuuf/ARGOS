using Argos.Services;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace Argos.ViewModels;

/// <summary>Icône d'application (bureau, menu Démarrer, écran d'accueil mobile).</summary>
public sealed class AppTileViewModel : ViewModelBase
{
    private bool _isSelected;

    public AppTileViewModel(AppDefinition definition, Action<AppTileViewModel> open)
    {
        Definition = definition;
        Icon = ImageLoader.Asset(definition.IconPath);
        OpenCommand = new RelayCommand(() => open(this));
    }

    public AppDefinition Definition { get; }
    public string Id => Definition.Id;
    public string Title => Definition.Title;
    public string Subtitle => Definition.Subtitle;
    public Bitmap Icon { get; }
    public RelayCommand OpenCommand { get; }

    /// <summary>Icône sélectionnée sur le bureau (cadre bleu clair de Windows).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>Titre de section du menu Démarrer (première lettre).</summary>
public sealed record StartMenuHeader(string Letter);

/// <summary>
/// Description d'une fenêtre à ouvrir : une application du bureau, une prise d'appel, un document…
/// La clé évite d'ouvrir deux fois la même fenêtre.
/// </summary>
public sealed record WindowSpec(
    string Key,
    string Title,
    Bitmap Icon,
    Func<Control> CreateContent,
    double Width = 980,
    double Height = 640);

/// <summary>Fenêtre ouverte sur le bureau.</summary>
public sealed class AppWindowViewModel : ViewModelBase
{
    private double _x;
    private double _y;
    private double _width;
    private double _height;
    private bool _isMinimized;
    private bool _isMaximized;
    private bool _isActive;
    private int _zIndex;
    private string _title;

    public AppWindowViewModel(WindowSpec spec, double x, double y, double width, double height, DesktopViewModel desktop)
    {
        Spec = spec;
        _title = spec.Title;
        _x = x;
        _y = y;
        _width = width;
        _height = height;
        TaskbarCommand = new RelayCommand(() => desktop.OnTaskbarClick(this));
        ActivateCommand = new RelayCommand(() => desktop.Activate(this));
        MinimizeCommand = new RelayCommand(() => desktop.Minimize(this));
        ToggleMaximizeCommand = new RelayCommand(() => IsMaximized = !IsMaximized);
        CloseCommand = new RelayCommand(() => desktop.Close(this));
    }

    public WindowSpec Spec { get; }
    public string Key => Spec.Key;
    public Bitmap Icon => Spec.Icon;

    /// <summary>Titre de la barre de titre (modifiable par le contenu, ex. numéro d'appel).</summary>
    public string Title { get => _title; set => SetProperty(ref _title, value); }

    public double X { get => _x; set => SetProperty(ref _x, value); }
    public double Y { get => _y; set => SetProperty(ref _y, value); }
    public double Width { get => _width; set => SetProperty(ref _width, value); }
    public double Height { get => _height; set => SetProperty(ref _height, value); }

    public bool IsMinimized { get => _isMinimized; set => SetProperty(ref _isMinimized, value); }
    public bool IsMaximized { get => _isMaximized; set => SetProperty(ref _isMaximized, value); }

    /// <summary>Fenêtre au premier plan (titre actif, bouton surligné dans la barre des tâches).</summary>
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    public int ZIndex { get => _zIndex; set => SetProperty(ref _zIndex, value); }

    public RelayCommand TaskbarCommand { get; }
    public RelayCommand ActivateCommand { get; }
    public RelayCommand MinimizeCommand { get; }
    public RelayCommand ToggleMaximizeCommand { get; }
    public RelayCommand CloseCommand { get; }
}

/// <summary>Notification (coin inférieur droit sur ordinateur, bandeau en haut sur téléphone).</summary>
public sealed class ToastViewModel : ViewModelBase
{
    public ToastViewModel(string title, string text, Bitmap? icon, Action<ToastViewModel> dismiss)
    {
        Title = title;
        Text = text;
        Icon = icon;
        DismissCommand = new RelayCommand(() => dismiss(this));
    }

    public string Title { get; }
    public string Text { get; }
    public Bitmap? Icon { get; }
    public bool HasIcon => Icon is not null;
    public RelayCommand DismissCommand { get; }
}
