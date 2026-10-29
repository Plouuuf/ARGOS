using System.Collections.ObjectModel;
using System.Globalization;
using Argos.Services;
using Argos.Shared.Contracts;
using Argos.Views.Apps;
using Argos.Views.Apps.Ode;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Argos.ViewModels;

/// <summary>
/// Bureau Windows 10 (ordinateur, iPad) et écran d'accueil (téléphone) : applications, fenêtres,
/// barre des tâches, menu Démarrer et notifications.
/// </summary>
public sealed class DesktopViewModel : ViewModelBase
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private const double DefaultWidth = 980;
    private const double DefaultHeight = 640;

    private readonly Action _lock;
    private readonly Action _signOut;
    private int _nextZ = 1;
    private int _cascade;

    private string _displayName;
    private string _levelLabel;
    private Bitmap? _avatar;
    private bool _isStartMenuOpen;
    private bool _isOnline;
    private string _trayTime = string.Empty;
    private string _trayDate = string.Empty;
    private WindowSpec? _mobileApp;
    private readonly Stack<WindowSpec> _mobileStack = new();

    public DesktopViewModel(ArgosUserDto user, Bitmap? avatar, Action lockSession, Action signOut)
    {
        _lock = lockSession;
        _signOut = signOut;
        _displayName = user.DisplayName;
        _levelLabel = AccessLevels.HighestLabel(user.Levels);
        _avatar = avatar;

        foreach (var definition in AppCatalog.All)
            Apps.Add(new AppTileViewModel(definition, OpenApp));

        // Menu Démarrer : liste alphabétique avec une lettre par section, comme Windows 10.
        foreach (var group in Apps.OrderBy(a => a.Title, StringComparer.Create(French, true))
                     .GroupBy(a => char.ToUpper(a.Title[0], French).ToString()))
        {
            StartMenuEntries.Add(new StartMenuHeader(group.Key));
            foreach (var app in group)
                StartMenuEntries.Add(app);
        }

        ToggleStartMenuCommand = new RelayCommand(() => IsStartMenuOpen = !IsStartMenuOpen);
        CloseStartMenuCommand = new RelayCommand(() => IsStartMenuOpen = false);
        LockCommand = new RelayCommand(() => { IsStartMenuOpen = false; _lock(); });
        SignOutCommand = new RelayCommand(() => { IsStartMenuOpen = false; _signOut(); });
        ShowDesktopCommand = new RelayCommand(ShowDesktop);
        MobileBackCommand = new RelayCommand(() => MobileApp = _mobileStack.Count > 0 ? _mobileStack.Pop() : null);

        UpdateClock(DateTime.Now);
    }

    public ObservableCollection<AppTileViewModel> Apps { get; } = [];
    public ObservableCollection<object> StartMenuEntries { get; } = [];
    public ObservableCollection<AppWindowViewModel> Windows { get; } = [];
    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    public string DisplayName
    {
        get => _displayName;
        private set => SetProperty(ref _displayName, value);
    }

    public string LevelLabel
    {
        get => _levelLabel;
        private set => SetProperty(ref _levelLabel, value);
    }

    public Bitmap? Avatar
    {
        get => _avatar;
        private set
        {
            if (SetProperty(ref _avatar, value))
                OnPropertyChanged(nameof(HasAvatar));
        }
    }

    public bool HasAvatar => Avatar is not null;

    public bool IsStartMenuOpen
    {
        get => _isStartMenuOpen;
        set => SetProperty(ref _isStartMenuOpen, value);
    }

    /// <summary>Connexion temps réel au serveur (icône réseau de la barre des tâches).</summary>
    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            if (SetProperty(ref _isOnline, value))
                OnPropertyChanged(nameof(NetworkTooltip));
        }
    }

    public string NetworkTooltip => IsOnline ? "Serveur ARGOS : connecté" : "Serveur ARGOS : connexion en cours…";

    public string TrayTime
    {
        get => _trayTime;
        private set => SetProperty(ref _trayTime, value);
    }

    public string TrayDate
    {
        get => _trayDate;
        private set => SetProperty(ref _trayDate, value);
    }

    /// <summary>Application ouverte en plein écran sur téléphone (null = écran d'accueil).</summary>
    public WindowSpec? MobileApp
    {
        get => _mobileApp;
        private set
        {
            if (SetProperty(ref _mobileApp, value))
                OnPropertyChanged(nameof(IsMobileAppOpen));
        }
    }

    public bool IsMobileAppOpen => MobileApp is not null;

    public RelayCommand ToggleStartMenuCommand { get; }
    public RelayCommand CloseStartMenuCommand { get; }
    public RelayCommand LockCommand { get; }
    public RelayCommand SignOutCommand { get; }
    public RelayCommand ShowDesktopCommand { get; }
    public RelayCommand MobileBackCommand { get; }

    /// <summary>Taille de la zone des fenêtres, fournie par la vue (sert à placer les nouvelles fenêtres).</summary>
    public double WorkAreaWidth { get; set; } = 1280;
    public double WorkAreaHeight { get; set; } = 760;

    /// <summary>Vrai quand le poste affiche l'interface téléphone.</summary>
    public bool IsCompactLayout { get; set; }

    // ===== Applications et fenêtres =====

    public void OpenApp(AppTileViewModel app)
        => OpenWindow(new WindowSpec(app.Id, app.Title, app.Icon, () => AppContentFactory.Create(app, this)));

    /// <summary>
    /// Ouvre une fenêtre (ou la ramène au premier plan si sa clé est déjà ouverte).
    /// Sur téléphone, la page s'affiche en plein écran ; « retour » revient à la page précédente.
    /// </summary>
    public void OpenWindow(WindowSpec spec)
    {
        IsStartMenuOpen = false;
        SelectIcon(null);

        if (IsCompactLayout)
        {
            if (MobileApp is not null && MobileApp.Key != spec.Key)
                _mobileStack.Push(MobileApp);
            MobileApp = spec;
            return;
        }

        var existing = Windows.FirstOrDefault(w => w.Key == spec.Key);
        if (existing is not null)
        {
            Activate(existing);
            return;
        }

        var width = Math.Min(spec.Width, Math.Max(480, WorkAreaWidth - 40));
        var height = Math.Min(spec.Height, Math.Max(360, WorkAreaHeight - 20));
        var offset = 28 * (_cascade++ % 8);
        var x = Math.Max(0, Math.Min((WorkAreaWidth - width) / 2 - 120 + offset, WorkAreaWidth - width));
        var y = Math.Max(0, Math.Min((WorkAreaHeight - height) / 2 - 80 + offset, WorkAreaHeight - height));

        var window = new AppWindowViewModel(spec, x, y, width, height, this);
        Windows.Add(window);
        Activate(window);
    }

    /// <summary>Ferme la fenêtre de cette clé, si elle est ouverte.</summary>
    public void CloseWindow(string key)
    {
        if (IsCompactLayout)
        {
            if (MobileApp?.Key == key)
                MobileApp = _mobileStack.Count > 0 ? _mobileStack.Pop() : null;
            return;
        }
        var window = Windows.FirstOrDefault(w => w.Key == key);
        if (window is not null)
            Close(window);
    }

    public AppWindowViewModel? FindWindow(string key) => Windows.FirstOrDefault(w => w.Key == key);

    public void Activate(AppWindowViewModel window)
    {
        window.IsMinimized = false;
        window.ZIndex = ++_nextZ;
        foreach (var w in Windows)
            w.IsActive = ReferenceEquals(w, window);
    }

    public void Minimize(AppWindowViewModel window)
    {
        window.IsMinimized = true;
        window.IsActive = false;
        ActivateTopmost();
    }

    public void Close(AppWindowViewModel window)
    {
        Windows.Remove(window);
        if (window.IsActive)
            ActivateTopmost();
    }

    /// <summary>Clic sur une fenêtre dans la barre des tâches : réduit la fenêtre active, sinon l'amène au premier plan.</summary>
    public void OnTaskbarClick(AppWindowViewModel window)
    {
        IsStartMenuOpen = false;
        if (window.IsActive && !window.IsMinimized)
            Minimize(window);
        else
            Activate(window);
    }

    public void SelectIcon(AppTileViewModel? app)
    {
        foreach (var a in Apps)
            a.IsSelected = ReferenceEquals(a, app);
    }

    /// <summary>Clic sur le fond d'écran : ferme le menu Démarrer et désélectionne les icônes.</summary>
    public void OnDesktopBackgroundPressed()
    {
        IsStartMenuOpen = false;
        SelectIcon(null);
        foreach (var w in Windows)
            w.IsActive = false;
    }

    private void ShowDesktop()
    {
        IsStartMenuOpen = false;
        foreach (var w in Windows)
        {
            w.IsMinimized = true;
            w.IsActive = false;
        }
    }

    private void ActivateTopmost()
    {
        var top = Windows.Where(w => !w.IsMinimized).MaxBy(w => w.ZIndex);
        foreach (var w in Windows)
            w.IsActive = ReferenceEquals(w, top);
    }

    // ===== Session, horloge, notifications =====

    public void UpdateUser(ArgosUserDto user, Bitmap? avatar)
    {
        DisplayName = user.DisplayName;
        LevelLabel = AccessLevels.HighestLabel(user.Levels);
        if (avatar is not null)
            Avatar = avatar;
    }

    public void UpdateClock(DateTime now)
    {
        TrayTime = now.ToString("HH:mm", French);
        TrayDate = now.ToString("dd/MM/yyyy", French);
    }

    public void HandleRealtime(RealtimeMessage message)
    {
        switch (message.Type)
        {
            case RealtimeTypes.Notify:
                var app = message.AppId is null ? null : Apps.FirstOrDefault(a => a.Id == message.AppId);
                ShowNotification(message.Title ?? "ARGOS", message.Text ?? string.Empty, app?.Icon);
                break;

            case RealtimeTypes.OdeChanged:
                _ = ArgosSession.Current?.Ode.RefreshAsync();
                break;

            case RealtimeTypes.RemiseChanged:
                _ = ArgosSession.Current?.Remise.RefreshAsync();
                break;

            case RealtimeTypes.OpenCall when !string.IsNullOrWhiteSpace(message.Text):
                // Décroché d'un 18 sur SIMCALL : la prise d'appel s'ouvre d'elle-même.
                _ = ArgosSession.Current?.Ode.RefreshAsync();
                OpenCall(message.Text!);
                ShowNotification("Appel 18", "Prise d'appel ouverte.", Apps.FirstOrDefault(a => a.Id == ArgosApps.AdagioOde)?.Icon);
                break;
        }
    }

    /// <summary>Ouvre la fenêtre « Prise d'appel » d'une fiche d'appel.</summary>
    public void OpenCall(string callId) => OpenWindow(OdeWindows.PriseAppel(this, callId));

    public void ShowNotification(string title, string text, Bitmap? icon = null)
    {
        var toast = new ToastViewModel(title, text, icon, t => Toasts.Remove(t));
        Toasts.Add(toast);
        while (Toasts.Count > 3)
            Toasts.RemoveAt(0);

        DispatcherTimer.RunOnce(() => Toasts.Remove(toast), TimeSpan.FromSeconds(7));
    }
}
