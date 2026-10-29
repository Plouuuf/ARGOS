using System.Globalization;
using Argos.Services;
using Argos.Shared.Contracts;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Argos.ViewModels;

public enum ShellScreen
{
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

        _ = InitializeAsync();
    }

    // ===== Écrans =====

    public ShellScreen Screen
    {
        get => _screen;
        private set
        {
            if (!SetProperty(ref _screen, value)) return;
            OnPropertyChanged(nameof(IsLockScreen));
            OnPropertyChanged(nameof(IsLoginScreen));
            OnPropertyChanged(nameof(IsWelcomeScreen));
            OnPropertyChanged(nameof(IsDesktopScreen));
            OnPropertyChanged(nameof(IsWallpaperBlurred));
            OnPropertyChanged(nameof(ShowDesktopLayout));
            OnPropertyChanged(nameof(ShowMobileLayout));
        }
    }

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
                OnPropertyChanged(nameof(Wallpaper));
        }
    }

    public bool ShowDesktopLayout => IsDesktopScreen && !IsCompact;
    public bool ShowMobileLayout => IsDesktopScreen && IsCompact;

    public Bitmap Wallpaper => ImageLoader.Asset(IsPortrait ? "Wallpapers/portrait.jpg" : "Wallpapers/landscape.jpg");

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
        if (Screen != ShellScreen.Lock) return;
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
        Desktop = new DesktopViewModel(user, Avatar, LockSession, () => _ = SignOutAsync());
        Desktop.IsOnline = _realtime.IsConnected;
        _realtime.Start(_auth.SessionToken!);
        _revalidation.Start();

        // Même durée que l'animation « Bienvenue » de Windows.
        await Task.Delay(TimeSpan.FromSeconds(1.6));
        if (Screen == ShellScreen.Welcome)
            Screen = ShellScreen.Desktop;
    }

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
