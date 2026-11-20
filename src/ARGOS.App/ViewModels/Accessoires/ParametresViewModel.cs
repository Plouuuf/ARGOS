using System.Diagnostics;
using System.Runtime.InteropServices;
using Argos.Services;
using Argos.Shared.Contracts;
using Avalonia.Media.Imaging;

namespace Argos.ViewModels.Accessoires;

public sealed record SettingsPage(string Id, string Title, string Description);

public sealed record SettingsNav(string Id, string Title, bool Selected, RelayCommand Command);

public sealed record WallpaperChoice(int Index, Bitmap Image, string Label, bool Selected, RelayCommand SelectCommand);

/// <summary>Paramètres Windows : système, personnalisation, affichage, son, réseau, notifications, comptes.</summary>
public sealed class ParametresViewModel : ViewModelBase, IDisposable
{
    public static string? RequestedPage { get; set; }

    private readonly DesktopViewModel _desktop;
    private SettingsPage _page;
    private string _networkTest = string.Empty;
    private NotificationPreferences _discord = new();
    private bool _discordLoaded;
    private bool _discordBusy;
    private string _discordStatus = string.Empty;
    private readonly SemaphoreSlim _discordSave = new(1, 1);

    public ParametresViewModel(DesktopViewModel desktop)
    {
        _desktop = desktop;
        _page = Pages.FirstOrDefault(p => p.Id == RequestedPage) ?? Pages[0];
        RequestedPage = null;
        NavigateCommand = new RelayCommand(p => { if (Pages.FirstOrDefault(x => x.Id == p as string) is { } page) Page = page; });
        SlideshowCommand = new RelayCommand(() => WindowsSettings.Update(s => s.Wallpaper = -1));
        TestNetworkCommand = new RelayCommand(() => _ = TestNetworkAsync());
        ClearNotificationsCommand = desktop.ClearNotificationsCommand;
        DiscordTestCommand = new RelayCommand(() => _ = SendDiscordTestAsync(), () => _discordLoaded && !_discordBusy);
        WindowsSettings.Changed += OnChanged;
        _desktop.PropertyChanged += OnDesktopChanged;
        _ = LoadDiscordAsync();
    }

    public IReadOnlyList<SettingsPage> Pages { get; } =
    [
        new("systeme", "Système", "Informations sur l'ordinateur, version"),
        new("personnalisation", "Personnalisation", "Fond d'écran, diaporama"),
        new("affichage", "Affichage", "Taille du texte, éclairage nocturne"),
        new("son", "Son", "Volume, son coupé"),
        new("reseau", "Réseau", "Serveur ARGOS, état de la connexion"),
        new("notifications", "Notifications", "Bannières, ne pas déranger"),
        new("comptes", "Comptes", "Session ouverte, changement d'utilisateur")
    ];

    public SettingsPage Page
    {
        get => _page;
        set
        {
            if (!SetProperty(ref _page, value)) return;
            foreach (var p in Pages) OnPropertyChanged("Is" + char.ToUpper(p.Id[0]) + p.Id[1..]);
            OnPropertyChanged(nameof(NavItems));
        }
    }

    public IReadOnlyList<SettingsNav> NavItems => Pages.Select(p => new SettingsNav(p.Id, p.Title, p.Id == Page.Id,
        new RelayCommand(() => Page = p))).ToList();

    public bool IsSysteme => Page.Id == "systeme";
    public bool IsPersonnalisation => Page.Id == "personnalisation";
    public bool IsAffichage => Page.Id == "affichage";
    public bool IsSon => Page.Id == "son";
    public bool IsReseau => Page.Id == "reseau";
    public bool IsNotifications => Page.Id == "notifications";
    public bool IsComptes => Page.Id == "comptes";
    public RelayCommand NavigateCommand { get; }

    // ===== Système =====

    public string MachineName => "POSTE-" + Environment.MachineName.ToUpperInvariant();
    public string ArgosVersion => BuildInfo.Label;
    public string OsVersion => "Windows 10 Professionnel (simulé) sur " + RuntimeInformation.OSDescription;
    public IReadOnlyList<BootLine> Hardware { get; } = BootInfo.Lines().Skip(1).ToList();

    // ===== Personnalisation =====

    public IReadOnlyList<WallpaperChoice> Wallpapers => Enumerable.Range(0, ShellViewModel.WallpaperCount)
        .Select(i => new WallpaperChoice(i, ShellViewModel.WallpaperThumbnail(i), $"Photo {i + 1}", WindowsSettings.Current.Wallpaper == i,
            new RelayCommand(() => WindowsSettings.Update(s => s.Wallpaper = i)))).ToList();
    public bool IsSlideshow => WindowsSettings.Current.Wallpaper < 0;
    public RelayCommand SlideshowCommand { get; }
    public IReadOnlyList<int> Intervals { get; } = [1, 5, 10, 30, 60];
    public int Interval { get => WindowsSettings.Current.WallpaperMinutes; set => WindowsSettings.Update(s => s.WallpaperMinutes = value); }

    // ===== Affichage =====

    public IReadOnlyList<string> Scales { get; } = ["100 % (recommandé)", "110 %", "125 %"];
    public string Scale
    {
        get => WindowsSettings.Current.Scale switch { 110 => Scales[1], 125 => Scales[2], _ => Scales[0] };
        set => WindowsSettings.Update(s => s.Scale = value == Scales[1] ? 110 : value == Scales[2] ? 125 : 100);
    }
    public bool NightLight { get => WindowsSettings.Current.NightLight; set => WindowsSettings.Update(s => s.NightLight = value); }
    public bool BootSequence { get => WindowsSettings.Current.BootSequence; set => WindowsSettings.Update(s => s.BootSequence = value); }

    // ===== Son =====

    public double Volume { get => WindowsSettings.Current.Volume; set => WindowsSettings.Update(s => { s.Volume = (int)Math.Round(value); s.Muted = false; }); }
    public bool Muted { get => WindowsSettings.Current.Muted; set => WindowsSettings.Update(s => s.Muted = value); }
    public string VolumeText => Muted ? "Son coupé" : $"{WindowsSettings.Current.Volume} %";

    // ===== Réseau =====

    public string ServerUrl
    {
        get
        {
            try { return ClientSettings.Current.HttpBaseUri.ToString(); }
            catch { return "Adresse invalide"; }
        }
    }
    public string ConnectionState => _desktop.IsOnline ? "Connecté (temps réel actif)" : "Connexion en cours…";
    public string NetworkTest { get => _networkTest; private set => SetProperty(ref _networkTest, value); }
    public RelayCommand TestNetworkCommand { get; }

    private async Task TestNetworkAsync()
    {
        NetworkTest = "Test en cours…";
        try
        {
            using var http = new HttpClient { BaseAddress = ClientSettings.Current.HttpBaseUri, Timeout = TimeSpan.FromSeconds(8) };
            var watch = Stopwatch.StartNew();
            using var response = await http.GetAsync("health");
            watch.Stop();
            NetworkTest = response.IsSuccessStatusCode
                ? $"Serveur ARGOS joignable en {watch.ElapsedMilliseconds} ms."
                : $"Le serveur répond avec une erreur ({(int)response.StatusCode}).";
        }
        catch (Exception ex)
        {
            NetworkTest = "Serveur injoignable : " + ex.Message;
        }
    }

    // ===== Notifications =====

    public bool Notifications { get => WindowsSettings.Current.Notifications; set => WindowsSettings.Update(s => s.Notifications = value); }
    public bool DoNotDisturb { get => WindowsSettings.Current.DoNotDisturb; set => WindowsSettings.Update(s => s.DoNotDisturb = value); }
    public RelayCommand ClearNotificationsCommand { get; }

    // ===== Notifications Discord (message privé du bot ARGOS quand l'application est fermée) =====

    public string DiscordHelp =>
        "Tu reçois un message privé du bot ARGOS sur Discord quand ARGOS est fermé. Autorise les messages privés des membres du serveur VDMS.";

    /// <summary>Vrai une fois les préférences lues sur le serveur (les cases restent grisées avant).</summary>
    public bool DiscordLoaded { get => _discordLoaded; private set { if (SetProperty(ref _discordLoaded, value)) RaiseDiscord(); } }

    public bool DiscordEnabled { get => _discord.Enabled; set => SetDiscord(p => p.Enabled = value, _discord.Enabled != value); }
    public bool DiscordMail { get => _discord.Mail; set => SetDiscord(p => p.Mail = value, _discord.Mail != value); }
    public bool DiscordGarde { get => _discord.Garde; set => SetDiscord(p => p.Garde = value, _discord.Garde != value); }
    public bool DiscordCompteRendu { get => _discord.CompteRendu; set => SetDiscord(p => p.CompteRendu = value, _discord.CompteRendu != value); }

    /// <summary>Les cases par type ne servent que si l'interrupteur général est activé.</summary>
    public bool DiscordDetailsEnabled => _discordLoaded && _discord.Enabled;

    public string DiscordStatus { get => _discordStatus; private set { if (SetProperty(ref _discordStatus, value)) OnPropertyChanged(nameof(HasDiscordStatus)); } }
    public bool HasDiscordStatus => _discordStatus.Length > 0;
    public RelayCommand DiscordTestCommand { get; }

    private async Task LoadDiscordAsync()
    {
        var session = ArgosSession.Current;
        if (session is null) return;
        try
        {
            _discord = await session.Api.GetAsync<NotificationPreferences>("api/me/notifications");
            DiscordLoaded = true;
        }
        catch
        {
            DiscordStatus = "Réglages des notifications Discord indisponibles (serveur injoignable ou ancien).";
        }
    }

    private void SetDiscord(Action<NotificationPreferences> change, bool changed)
    {
        if (!_discordLoaded || !changed) return;
        change(_discord);
        RaiseDiscord();
        var copy = new NotificationPreferences
        {
            Enabled = _discord.Enabled, Mail = _discord.Mail, Garde = _discord.Garde, CompteRendu = _discord.CompteRendu
        };
        _ = SaveDiscordAsync(copy);
    }

    /// <summary>Enregistrement à chaque changement (un envoi à la fois, dans l'ordre des clics).</summary>
    private async Task SaveDiscordAsync(NotificationPreferences preferences)
    {
        var session = ArgosSession.Current;
        if (session is null) return;
        await _discordSave.WaitAsync();
        try
        {
            var result = await session.Api.SendAsync(HttpMethod.Post, "api/me/notifications", preferences);
            DiscordStatus = result.Ok ? string.Empty : "Enregistrement impossible : " + (result.Error ?? "erreur du serveur.");
        }
        finally
        {
            _discordSave.Release();
        }
    }

    private async Task SendDiscordTestAsync()
    {
        var session = ArgosSession.Current;
        if (session is null || _discordBusy) return;
        _discordBusy = true;
        DiscordTestCommand.RaiseCanExecuteChanged();
        DiscordStatus = "Envoi du message de test…";
        try
        {
            var result = await session.Api.SendAsync(HttpMethod.Post, "api/me/notifications/test");
            DiscordStatus = result.Ok
                ? "Message envoyé : regarde tes messages privés Discord."
                : result.Error ?? "L'envoi du message de test a échoué.";
        }
        finally
        {
            _discordBusy = false;
            DiscordTestCommand.RaiseCanExecuteChanged();
        }
    }

    private void RaiseDiscord()
    {
        foreach (var n in new[] { nameof(DiscordEnabled), nameof(DiscordMail), nameof(DiscordGarde), nameof(DiscordCompteRendu), nameof(DiscordDetailsEnabled) })
            OnPropertyChanged(n);
        DiscordTestCommand.RaiseCanExecuteChanged();
    }

    // ===== Comptes =====

    public string DisplayName => _desktop.DisplayName;
    public string LevelLabel => _desktop.LevelLabel;
    public Bitmap? Avatar => _desktop.Avatar;
    public bool HasAvatar => _desktop.HasAvatar;
    public string UserId => ArgosSession.Current?.User.UserId ?? "—";
    public string Roles
    {
        get
        {
            var levels = ArgosSession.Current?.User.Levels ?? [];
            var apps = levels.Where(l => l.StartsWith("app:", StringComparison.Ordinal))
                .Select(l => AppCatalog.Find(l[4..])?.Title ?? l[4..]).ToList();
            var others = levels.Where(l => !l.StartsWith("app:", StringComparison.Ordinal)).Select(AccessLevels.Label).ToList();
            return string.Join(" · ", others.Concat(apps.Select(a => "Accès " + a)));
        }
    }
    public RelayCommand LockCommand => _desktop.LockCommand;
    public RelayCommand SignOutCommand => _desktop.SignOutCommand;
    public RelayCommand SwitchUserCommand => _desktop.SwitchUserCommand;

    private void OnChanged()
    {
        foreach (var n in new[] { nameof(Wallpapers), nameof(IsSlideshow), nameof(Interval), nameof(Scale), nameof(NightLight), nameof(BootSequence),
                                  nameof(Volume), nameof(Muted), nameof(VolumeText), nameof(Notifications), nameof(DoNotDisturb) })
            OnPropertyChanged(n);
    }

    private void OnDesktopChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesktopViewModel.IsOnline)) OnPropertyChanged(nameof(ConnectionState));
    }

    public void Dispose()
    {
        WindowsSettings.Changed -= OnChanged;
        _desktop.PropertyChanged -= OnDesktopChanged;
    }
}
