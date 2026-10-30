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

    public ParametresViewModel(DesktopViewModel desktop)
    {
        _desktop = desktop;
        _page = Pages.FirstOrDefault(p => p.Id == RequestedPage) ?? Pages[0];
        RequestedPage = null;
        NavigateCommand = new RelayCommand(p => { if (Pages.FirstOrDefault(x => x.Id == p as string) is { } page) Page = page; });
        SlideshowCommand = new RelayCommand(() => WindowsSettings.Update(s => s.Wallpaper = -1));
        TestNetworkCommand = new RelayCommand(() => _ = TestNetworkAsync());
        ClearNotificationsCommand = desktop.ClearNotificationsCommand;
        WindowsSettings.Changed += OnChanged;
        _desktop.PropertyChanged += OnDesktopChanged;
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
