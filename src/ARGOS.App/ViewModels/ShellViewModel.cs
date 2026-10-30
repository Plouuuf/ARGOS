using System.Collections.ObjectModel;
using System.Globalization;
using Argos.Services;
using Argos.Shared.Contracts;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Argos.ViewModels;

public enum ShellScreen
{
    Boot,
    Lock,
    Login,
    Welcome,
    Desktop
}

/// <summary>
/// Enchaînement des écrans, comme sous Windows 10 : verrouillage → connexion → « Bienvenue » → bureau.
/// La connexion passe par Discord (whitelist par rôle, vérifiée par le serveur ARGOS).
/// </summary>
public sealed class ShellViewModel : ViewModelBase
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly TimeSpan RevalidationInterval = TimeSpan.FromMinutes(5);

    private readonly DiscordAuthService _auth = new();
    private readonly RealtimeClient _realtime = new();
    private readonly DispatcherTimer _clock;
    private readonly DispatcherTimer _revalidation;

    private CancellationTokenSource? _signInCts;
    private ShellScreen _screen = ShellScreen.Lock;
    private bool _isCompact;
    private bool _isPortrait;
    private string _timeText = string.Empty;
    private string _dateText = string.Empty;
    private string _displayName = "ARGOS";
    private string _subtitle = "Brigade de sapeurs-pompiers de Paris";
    private Bitmap? _avatar;
    private bool _hasKnownAccount;
    private bool _sessionReady;
    private bool _isBusy;
    private string? _statusText;
    private string? _errorText;
    private DesktopViewModel? _desktop;
    private string? _updateText;
    private bool _updating;
    private readonly DispatcherTimer _updates;
    private readonly DispatcherTimer _wallpaperTimer;

    public ShellViewModel()
    {
        ShowLoginCommand = new RelayCommand(ShowLogin);
        BackToLockCommand = new RelayCommand(BackToLock);
        SignInCommand = new RelayCommand(() => _ = SignInAsync(), () => !IsBusy);
        CancelSignInCommand = new RelayCommand(CancelSignIn);
        UseOtherAccountCommand = new RelayCommand(() => _ = UseOtherAccountAsync(), () => !IsBusy);
        QuitCommand = new RelayCommand(() => App.Quit());

        _realtime.ConnectionChanged += connected => Dispatcher.UIThread.Post(() =>
        {
            if (Desktop is not null) Desktop.IsOnline = connected;
        });
        _realtime.MessageReceived += message => Dispatcher.UIThread.Post(() => Desktop?.HandleRealtime(message));
        _realtime.SessionDenied += reason => Dispatcher.UIThread.Post(() => _ = ForceSignOutAsync(reason));

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => UpdateClock();
        _clock.Start();
        UpdateClock();

        _revalidation = new DispatcherTimer { Interval = RevalidationInterval };
        _revalidation.Tick += (_, _) => _ = RevalidateAsync();

        // Fond d'écran : diaporama (photo au hasard, puis la suivante à intervalle régulier) ou photo fixe (Paramètres).
        var settings = WindowsSettings.Current;
        ShowWallpaper(settings.Wallpaper >= 0 ? settings.Wallpaper : Random.Shared.Next(Wallpapers.Length), fade: false);
        _wallpaperTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(Math.Max(1, settings.WallpaperMinutes)) };
        _wallpaperTimer.Tick += (_, _) => NextWallpaper();
        if (settings.Wallpaper < 0) _wallpaperTimer.Start();
        WindowsSettings.Changed += OnWindowsSettingsChanged;

        // Séquence de démarrage UEFI, puis écran de verrouillage.
        if (settings.BootSequence)
        {
            _screen = ShellScreen.Boot;
            _ = RunBootAsync();
        }

        // Mise à jour automatique : au démarrage, puis toutes les 20 minutes.
        AppUpdater.Cleanup();
        _updates = new DispatcherTimer { Interval = TimeSpan.FromMinutes(20) };
        _updates.Tick += (_, _) => _ = CheckUpdateAsync();
        _updates.Start();

        _ = InitializeAsync();
    }

    /// <summary>Message de mise à jour (écran de verrouillage).</summary>
    public string? UpdateText
    {
        get => _updateText;
        private set
        {
            if (SetProperty(ref _updateText, value))
                OnPropertyChanged(nameof(HasUpdateText));
        }
    }

    public bool HasUpdateText => !string.IsNullOrWhiteSpace(UpdateText);

    public string VersionText => "ARGOS " + BuildInfo.Label;

    private async Task CheckUpdateAsync()
    {
        if (!AppUpdater.IsSupported || _updating) return;
        _updating = true;
        try
        {
            var info = await AppUpdater.CheckAsync();
            if (info is null) return;
            var idle = Screen is ShellScreen.Lock or ShellScreen.Login && !IsBusy;
            if (idle) UpdateText = $"Mise à jour d'ARGOS ({info.Version})…";
            var error = await AppUpdater.InstallAsync(info);
            idle = Screen is ShellScreen.Lock or ShellScreen.Login && !IsBusy;
            if (error is not null)
            {
                if (idle) UpdateText = error;
                else Desktop?.ShowNotification("Mise à jour d'ARGOS", error);
                return;
            }
            if (idle)
            {
                // Personne n'est connecté : on redémarre tout de suite sur la nouvelle version.
                UpdateText = "Mise à jour installée : redémarrage…";
                await Task.Delay(1200);
                AppUpdater.Restart();
            }
            else
            {
                Desktop?.ShowNotification("ARGOS mis à jour",
                    $"{info.Version} est installée : elle s'ouvrira au prochain lancement d'ARGOS.");
            }
        }
        catch
        {
            // Serveur injoignable ou sans mise à jour : nouvel essai plus tard.
        }
        finally
        {
            _updating = false;
        }
    }

    // ===== Écrans =====

    public ShellScreen Screen
    {
        get => _screen;
        private set
        {
            if (!SetProperty(ref _screen, value)) return;
            OnPropertyChanged(nameof(IsBootScreen));
            OnPropertyChanged(nameof(IsLockScreen));
            OnPropertyChanged(nameof(IsLoginScreen));
            OnPropertyChanged(nameof(IsWelcomeScreen));
            OnPropertyChanged(nameof(IsDesktopScreen));
            OnPropertyChanged(nameof(IsWallpaperBlurred));
            OnPropertyChanged(nameof(ShowDesktopLayout));
            OnPropertyChanged(nameof(ShowMobileLayout));
        }
    }

    public bool IsBootScreen => Screen == ShellScreen.Boot;
    public bool IsLockScreen => Screen == ShellScreen.Lock;
    public bool IsLoginScreen => Screen == ShellScreen.Login;
    public bool IsWelcomeScreen => Screen == ShellScreen.Welcome;
    public bool IsDesktopScreen => Screen == ShellScreen.Desktop;

    /// <summary>Fond flouté sur l'écran de connexion et pendant « Bienvenue », comme Windows 10.</summary>
    public bool IsWallpaperBlurred => Screen is ShellScreen.Login or ShellScreen.Welcome;

    // ===== Disposition (ordinateur / téléphone) =====

    /// <summary>Écran étroit (téléphone) : interface mobile au lieu du bureau Windows.</summary>
    public bool IsCompact
    {
        get => _isCompact;
        set
        {
            if (!SetProperty(ref _isCompact, value)) return;
            OnPropertyChanged(nameof(ShowDesktopLayout));
            OnPropertyChanged(nameof(ShowMobileLayout));
        }
    }

    public bool IsPortrait
    {
        get => _isPortrait;
        set
        {
            if (SetProperty(ref _isPortrait, value))
                ShowWallpaper(_wallpaperIndex, fade: false);
        }
    }

    public bool ShowDesktopLayout => IsDesktopScreen && !IsCompact;
    public bool ShowMobileLayout => IsDesktopScreen && IsCompact;

    // ===== Fond d'écran changeant (bureau et écran de verrouillage) =====

    /// <summary>Photos du diaporama : paysage (ordinateur) et portrait (téléphone), dans le même ordre.</summary>
    private static readonly string[] Wallpapers = ["", "-2", "-3", "-4"];
    public static readonly TimeSpan WallpaperInterval = TimeSpan.FromMinutes(10);
    private int _wallpaperIndex;
    private Bitmap? _wallpaperA, _wallpaperB;
    private bool _showA = true;

    private static Bitmap WallpaperOf(int index, bool portrait)
        => ImageLoader.Asset($"Wallpapers/{(portrait ? "portrait" : "landscape")}{Wallpapers[index % Wallpapers.Length]}.jpg");

    /// <summary>Deux calques superposés : la nouvelle photo apparaît en fondu au-dessus de l'ancienne.</summary>
    public Bitmap? WallpaperA { get => _wallpaperA; private set => SetProperty(ref _wallpaperA, value); }
    public Bitmap? WallpaperB { get => _wallpaperB; private set => SetProperty(ref _wallpaperB, value); }
    public bool ShowWallpaperA { get => _showA; private set { if (SetProperty(ref _showA, value)) OnPropertyChanged(nameof(ShowWallpaperB)); } }
    public bool ShowWallpaperB => !_showA;
    public Bitmap Wallpaper => WallpaperOf(_wallpaperIndex, IsPortrait);

    public void NextWallpaper() => ShowWallpaper(_wallpaperIndex + 1, fade: true);

    private void ShowWallpaper(int index, bool fade)
    {
        _wallpaperIndex = index % Wallpapers.Length;
        var image = WallpaperOf(_wallpaperIndex, IsPortrait);
        if (!fade)
        {
            if (ShowWallpaperA) WallpaperA = image; else WallpaperB = image;
        }
        else if (ShowWallpaperA)
        {
            WallpaperB = image;
            ShowWallpaperA = false;
        }
        else
        {
            WallpaperA = image;
            ShowWallpaperA = true;
        }
        OnPropertyChanged(nameof(Wallpaper));
    }

    /// <summary>Bouton d'arrêt (quitter ARGOS) : sur ordinateur seulement.</summary>
    public bool CanQuit => !AppPaths.IsMobile;

    // ===== Horloge =====

    public string TimeText
    {
        get => _timeText;
        private set => SetProperty(ref _timeText, value);
    }

    public string DateText
    {
        get => _dateText;
        private set => SetProperty(ref _dateText, value);
    }

    // ===== Connexion =====

    public string DisplayName
    {
        get => _displayName;
        private set => SetProperty(ref _displayName, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
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

    /// <summary>Un compte Discord est déjà enregistré sur cet appareil.</summary>
    public bool HasKnownAccount
    {
        get => _hasKnownAccount;
        private set
        {
            if (!SetProperty(ref _hasKnownAccount, value)) return;
            OnPropertyChanged(nameof(SignInLabel));
        }
    }

    public string SignInLabel => HasKnownAccount ? "Se connecter" : "Se connecter avec Discord";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            SignInCommand.RaiseCanExecuteChanged();
            UseOtherAccountCommand.RaiseCanExecuteChanged();
        }
    }

    public string? StatusText
    {
        get => _statusText;
        private set
        {
            if (SetProperty(ref _statusText, value))
                OnPropertyChanged(nameof(HasStatus));
        }
    }

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

    public string? ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public DesktopViewModel? Desktop
    {
        get => _desktop;
        private set => SetProperty(ref _desktop, value);
    }

    public RelayCommand ShowLoginCommand { get; }
    public RelayCommand BackToLockCommand { get; }
    public RelayCommand SignInCommand { get; }
    public RelayCommand CancelSignInCommand { get; }
    public RelayCommand UseOtherAccountCommand { get; }
    public RelayCommand QuitCommand { get; }

    /// <summary>Ouverture d'une adresse dans le navigateur, fournie par la vue (TopLevel.Launcher).</summary>
    public Func<Uri, Task<bool>>? OpenUrl { get; set; }

    // ===== Déroulé =====

    private async Task InitializeAsync()
    {
        _ = CheckUpdateAsync();
        var saved = _auth.PeekSavedUser();
        if (saved is not null)
            await ApplyUserAsync(saved);

        try
        {
            _sessionReady = await _auth.TryResumeAsync();
            if (_auth.User is not null)
                await ApplyUserAsync(_auth.User);
            else
                ResetIdentity();
        }
        catch
        {
            // Serveur injoignable : on réessaiera au clic sur « Se connecter ».
            _sessionReady = false;
        }
    }

    public void ShowLogin()
    {
        if (Screen == ShellScreen.Boot) _bootSkipped = true;
        if (Screen is not (ShellScreen.Lock or ShellScreen.Boot)) return;
        ErrorText = null;
        Screen = ShellScreen.Login;
    }

    private void BackToLock()
    {
        if (Screen != ShellScreen.Login || IsBusy) return;
        Screen = ShellScreen.Lock;
    }

    private async Task SignInAsync()
    {
        if (IsBusy) return;
        ErrorText = null;
        IsBusy = true;
        _signInCts = new CancellationTokenSource();
        var ct = _signInCts.Token;

        try
        {
            // 1. Compte déjà enregistré : simple revalidation des rôles.
            if (HasKnownAccount)
            {
                StatusText = "Vérification du compte Discord…";
                _sessionReady = await _auth.TryResumeAsync(ct);
                if (_sessionReady && _auth.User is not null)
                {
                    await EnterDesktopAsync();
                    return;
                }
                ResetIdentity();
            }

            // 2. Nouvelle connexion Discord dans le navigateur.
            StatusText = "Connexion au serveur ARGOS…";
            var start = await _auth.StartAsync(ct);

            var opened = OpenUrl is not null && await OpenUrl(new Uri(start.AuthorizeUrl));
            if (!opened)
                throw new InvalidOperationException("Impossible d'ouvrir le navigateur pour la connexion Discord.");

            StatusText = "Valide la connexion dans ton navigateur…";
            var result = await _auth.WaitForApprovalAsync(start.SessionId, ct);
            if (result.Status == "approved" && _auth.User is not null)
            {
                _sessionReady = true;
                await EnterDesktopAsync();
                return;
            }

            ErrorText = result.Error ?? "La connexion Discord n'a pas abouti.";
        }
        catch (OperationCanceledException)
        {
            ErrorText = null;
        }
        catch (HttpRequestException)
        {
            ErrorText = "Le serveur ARGOS est injoignable. Vérifie ta connexion internet.";
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            StatusText = null;
            IsBusy = false;
            _signInCts?.Dispose();
            _signInCts = null;
        }
    }

    private void CancelSignIn() => _signInCts?.Cancel();

    private async Task UseOtherAccountAsync()
    {
        await _auth.ForgetAsync();
        _sessionReady = false;
        ResetIdentity();
        await SignInAsync();
    }

    private async Task EnterDesktopAsync()
    {
        var user = _auth.User!;
        await ApplyUserAsync(user);

        Screen = ShellScreen.Welcome;
        StatusText = null;

        ArgosSession.Current = new ArgosSession(user, _auth.SessionToken!);
        _ = ArgosSession.Current.Ode.RefreshAsync();
        Desktop = new DesktopViewModel(user, Avatar, LockSession, () => _ = SignOutAsync())
        {
            RestartRequested = () => _ = RestartAsync(),
            ShutdownRequested = () => _ = ShutdownAsync(),
            SwitchUserRequested = () => _ = SwitchUserAsync()
        };
        Desktop.IsOnline = _realtime.IsConnected;
        _realtime.Start(_auth.SessionToken!);
        _revalidation.Start();

        // Même durée que l'animation « Bienvenue » de Windows.
        await Task.Delay(TimeSpan.FromSeconds(1.6));
        if (Screen == ShellScreen.Welcome)
            Screen = ShellScreen.Desktop;
    }

    // ===== Alimentation et sessions =====

    private bool _bootSkipped;
    private int _bootProgress;
    public ObservableCollection<BootLine> BootLines { get; } = [];
    public bool BootLoadingWindows { get; private set; }
    public int BootProgress { get => _bootProgress; private set => SetProperty(ref _bootProgress, value); }

    /// <summary>Séquence UEFI simulée (processeur, mémoire, stockage, réseau, sécurité), puis chargement de Windows.</summary>
    public async Task RunBootAsync()
    {
        _bootSkipped = false;
        BootLines.Clear();
        BootLoadingWindows = false;
        OnPropertyChanged(nameof(BootLoadingWindows));
        BootProgress = 0;
        foreach (var line in BootInfo.Lines())
        {
            if (_bootSkipped) break;
            BootLines.Add(line);
            BootProgress = (int)(100.0 * BootLines.Count / BootInfo.Count);
            await Task.Delay(line.DelayMs);
        }
        if (!_bootSkipped)
        {
            BootLoadingWindows = true;
            OnPropertyChanged(nameof(BootLoadingWindows));
            await Task.Delay(1800);
        }
        if (Screen == ShellScreen.Boot) Screen = ShellScreen.Lock;
    }

    public void SkipBoot()
    {
        if (Screen != ShellScreen.Boot) return;
        _bootSkipped = true;
        Screen = ShellScreen.Lock;
    }

    /// <summary>Redémarrer : ferme les applications et la session, réaffiche la séquence de démarrage.</summary>
    public async Task RestartAsync()
    {
        await SignOutAsync();
        Screen = ShellScreen.Boot;
        await RunBootAsync();
    }

    /// <summary>Arrêter : ferme les applications puis ARGOS (sur téléphone, retour au verrouillage).</summary>
    public async Task ShutdownAsync()
    {
        await SignOutAsync();
        if (CanQuit) App.Quit();
    }

    /// <summary>Changer d'utilisateur : ferme la session et propose un autre compte Discord.</summary>
    public async Task SwitchUserAsync()
    {
        await SignOutAsync();
        Screen = ShellScreen.Login;
        await UseOtherAccountAsync();
    }

    private void OnWindowsSettingsChanged()
    {
        var s = WindowsSettings.Current;
        _wallpaperTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, s.WallpaperMinutes));
        if (s.Wallpaper < 0) { if (!_wallpaperTimer.IsEnabled) _wallpaperTimer.Start(); }
        else
        {
            _wallpaperTimer.Stop();
            if (s.Wallpaper != _wallpaperIndex) ShowWallpaper(s.Wallpaper, fade: true);
        }
        OnPropertyChanged(nameof(DisplayScale));
        OnPropertyChanged(nameof(NightLight));
    }

    public static int WallpaperCount => Wallpapers.Length;
    public static Bitmap WallpaperThumbnail(int index) => WallpaperOf(index, false);
    public double DisplayScale => WindowsSettings.Current.Scale / 100.0;
    public bool NightLight => WindowsSettings.Current.NightLight;

    /// <summary>Verrouiller : retour à l'écran de verrouillage, la session reste enregistrée.</summary>
    private void LockSession() => _ = SignOutAsync();

    /// <summary>Se déconnecter : ferme le bureau, garde le compte enregistré sur l'appareil.</summary>
    private async Task SignOutAsync()
    {
        _revalidation.Stop();
        await _realtime.StopAsync();
        Desktop = null;
        ArgosSession.Current = null;
        ErrorText = null;
        Screen = ShellScreen.Lock;
    }

    /// <summary>Le serveur a refusé la session (rôle retiré, session expirée) : retour à la connexion.</summary>
    private async Task ForceSignOutAsync(string reason)
    {
        await SignOutAsync();
        await _auth.ForgetAsync();
        _sessionReady = false;
        ResetIdentity();
        Screen = ShellScreen.Login;
        ErrorText = reason;
    }

    private async Task RevalidateAsync()
    {
        if (Screen != ShellScreen.Desktop && Screen != ShellScreen.Welcome) return;
        try
        {
            var result = await _auth.RevalidateAsync();
            if (result.Valid && result.User is not null)
            {
                await ApplyUserAsync(result.User);
                Desktop?.UpdateUser(result.User, Avatar);
            }
            else if (result.Reauthenticate)
            {
                await ForceSignOutAsync(result.Error ?? "Ta session ARGOS a pris fin.");
            }
        }
        catch
        {
            // Serveur momentanément injoignable : la session reste ouverte.
        }
    }

    private async Task ApplyUserAsync(ArgosUserDto user)
    {
        HasKnownAccount = true;
        DisplayName = user.DisplayName;
        Subtitle = AccessLevels.HighestLabel(user.Levels) + " · BSPP";
        Avatar = await ImageLoader.AvatarAsync(user.AvatarUrl) ?? Avatar;
    }

    private void ResetIdentity()
    {
        HasKnownAccount = false;
        DisplayName = "ARGOS";
        Subtitle = "Brigade de sapeurs-pompiers de Paris";
        Avatar = null;
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        TimeText = now.ToString("HH:mm", French);
        DateText = now.ToString("dddd d MMMM", French);
        Desktop?.UpdateClock(now);
    }
}
