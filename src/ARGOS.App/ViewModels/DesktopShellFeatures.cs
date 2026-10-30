using System.Collections.ObjectModel;
using System.Globalization;
using Argos.Services;
using Argos.Shared.Contracts;
using Argos.Views.Apps.Accessoires;
using Avalonia.Media.Imaging;

namespace Argos.ViewModels;

/// <summary>Notification conservée dans le centre de notifications.</summary>
public sealed class NotificationItem(string title, string text, Bitmap? icon, DateTime at, Action<NotificationItem> dismiss)
{
    public string Title { get; } = title;
    public string Text { get; } = text;
    public Bitmap? Icon { get; } = icon;
    public bool HasIcon => Icon is not null;
    public string Time { get; } = at.ToString("HH:mm", CultureInfo.GetCultureInfo("fr-FR"));
    public RelayCommand DismissCommand => new(() => dismiss(this));
}

/// <summary>Résultat de la recherche Windows (applications, fichiers de Mes documents, paramètres).</summary>
public sealed record SearchResult(string Title, string Subtitle, string Kind, Bitmap? Icon, RelayCommand OpenCommand)
{
    public bool HasIcon => Icon is not null;
}

/// <summary>Bureau Windows 10 : recherche, vue des tâches, centre de notifications, calendrier, son, alimentation.</summary>
public sealed partial class DesktopViewModel
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private bool _searchOpen, _taskViewOpen, _actionCenterOpen;
    private string _searchText = string.Empty;
    private string _startSearch = string.Empty;
    private string _longTime = string.Empty, _longDate = string.Empty;
    private DateTime _calendarDate = DateTime.Today;
    private int _unread;

    // ===== Alimentation et sessions (fournies par la coquille) =====

    public Action? RestartRequested { get; set; }
    public Action? ShutdownRequested { get; set; }
    public Action? SwitchUserRequested { get; set; }

    public RelayCommand RestartCommand => new(() => { CloseAllPanels(); CloseAllWindows(); RestartRequested?.Invoke(); });
    public RelayCommand ShutdownCommand => new(() => { CloseAllPanels(); CloseAllWindows(); ShutdownRequested?.Invoke(); });
    public RelayCommand SwitchUserCommand => new(() => { CloseAllPanels(); CloseAllWindows(); SwitchUserRequested?.Invoke(); });
    public RelayCommand OpenSettingsCommand => new(p => OpenSettings(p as string));
    public bool CanShutdown => !AppPaths.IsMobile;

    /// <summary>Fermeture des applications à l'arrêt, au redémarrage et au changement d'utilisateur.</summary>
    public void CloseAllWindows()
    {
        foreach (var w in Windows.ToList()) Close(w, force: true);
    }

    public void OpenSettings(string? page = null)
    {
        CloseAllPanels();
        Accessoires.ParametresViewModel.RequestedPage = page;
        var app = Apps.FirstOrDefault(a => a.Id == ArgosApps.Parametres);
        if (app is not null) OpenApp(app);
    }

    private void CloseAllPanels()
    {
        IsStartMenuOpen = false;
        IsSearchOpen = false;
        IsTaskViewOpen = false;
        IsActionCenterOpen = false;
    }

    public string SessionInfo => $"{DisplayName} · {LevelLabel}";

    // ===== Menu Démarrer : recherche d'un logiciel =====

    public string StartSearch
    {
        get => _startSearch;
        set
        {
            if (!SetProperty(ref _startSearch, value ?? string.Empty)) return;
            StartResults.Clear();
            var q = _startSearch.Trim();
            foreach (var a in Apps.Where(a => q.Length > 0 && (a.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                                                               || a.Subtitle.Contains(q, StringComparison.CurrentCultureIgnoreCase))))
                StartResults.Add(a);
            OnPropertyChanged(nameof(IsStartSearching));
        }
    }

    public bool IsStartSearching => StartSearch.Trim().Length > 0;
    public ObservableCollection<AppTileViewModel> StartResults { get; } = [];

    // ===== Recherche Windows (barre des tâches) =====

    public bool IsSearchOpen
    {
        get => _searchOpen;
        set
        {
            if (!SetProperty(ref _searchOpen, value)) return;
            if (value) { IsStartMenuOpen = false; IsTaskViewOpen = false; IsActionCenterOpen = false; BuildSearch(); }
        }
    }

    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value ?? string.Empty)) BuildSearch(); } }
    public ObservableCollection<SearchResult> SearchResults { get; } = [];
    public bool HasSearchResults => SearchResults.Count > 0;
    public RelayCommand ToggleSearchCommand => new(() => IsSearchOpen = !IsSearchOpen);
    public RelayCommand OpenFirstResultCommand => new(() => SearchResults.FirstOrDefault()?.OpenCommand.Execute(null));

    private static readonly (string Page, string Title, string Subtitle)[] SettingsPages =
    [
        ("systeme", "Informations système", "Paramètres › Système"),
        ("personnalisation", "Fond d'écran", "Paramètres › Personnalisation"),
        ("affichage", "Affichage et éclairage nocturne", "Paramètres › Affichage"),
        ("son", "Volume et son", "Paramètres › Son"),
        ("reseau", "Réseau et serveur ARGOS", "Paramètres › Réseau"),
        ("notifications", "Notifications", "Paramètres › Notifications"),
        ("comptes", "Compte et session", "Paramètres › Comptes")
    ];

    private void BuildSearch()
    {
        SearchResults.Clear();
        var q = SearchText.Trim();
        bool Match(string? s) => q.Length == 0 || (s?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false);

        foreach (var a in Apps.Where(a => Match(a.Title) || Match(a.Subtitle)).Take(q.Length == 0 ? 8 : 20))
        {
            var app = a;
            SearchResults.Add(new SearchResult(a.Title, a.Subtitle, "Application", a.Icon, new RelayCommand(() => { IsSearchOpen = false; OpenApp(app); })));
        }
        if (q.Length > 0)
        {
            var store = ArgosSession.Current?.Files;
            foreach (var f in (store?.Snapshot.Entries ?? []).Where(e => !e.Deleted && Match(e.Name)).Take(15))
            {
                var entry = f;
                SearchResults.Add(new SearchResult(f.Name, store!.PathOf(f.ParentId), f.IsFolder ? "Dossier" : "Fichier",
                    ImageLoader.Asset(f.IsFolder ? "Icons/explorateur.png" : FileKinds.IsText(f.Name) ? "Icons/bloc-notes.png" : "Icons/explorateur.png"),
                    new RelayCommand(() => { IsSearchOpen = false; OpenFile(entry); })));
            }
            foreach (var (page, title, subtitle) in SettingsPages.Where(p => Match(p.Title) || Match(p.Subtitle)))
            {
                var target = page;
                SearchResults.Add(new SearchResult(title, subtitle, "Paramètre", ImageLoader.Asset("Icons/parametres.png"),
                    new RelayCommand(() => OpenSettings(target))));
            }
        }
        OnPropertyChanged(nameof(HasSearchResults));
    }

    private void OpenFile(FileEntry entry)
    {
        if (!entry.IsFolder && FileKinds.IsText(entry.Name))
        {
            OpenWindow(AccessoryWindows.Notepad(this, entry.Id));
            return;
        }
        Accessoires.ExplorateurViewModel.RequestedFolder = entry.IsFolder ? entry.Id : entry.ParentId;
        var explorer = Apps.FirstOrDefault(a => a.Id == ArgosApps.Explorateur);
        if (explorer is not null) OpenApp(explorer);
    }

    // ===== Vue des tâches =====

    public bool IsTaskViewOpen
    {
        get => _taskViewOpen;
        set
        {
            if (SetProperty(ref _taskViewOpen, value) && value) { IsStartMenuOpen = false; IsSearchOpen = false; IsActionCenterOpen = false; }
        }
    }

    public RelayCommand ToggleTaskViewCommand => new(() => IsTaskViewOpen = !IsTaskViewOpen);
    public RelayCommand TaskViewSelectCommand => new(p => { if (p is AppWindowViewModel w) { IsTaskViewOpen = false; Activate(w); } });
    public RelayCommand TaskViewCloseCommand => new(p => { if (p is AppWindowViewModel w) Close(w); if (Windows.Count == 0) IsTaskViewOpen = false; });
    public bool HasWindows => Windows.Count > 0;

    // ===== Centre de notifications =====

    public ObservableCollection<NotificationItem> Notifications { get; } = [];
    public bool HasNotifications => Notifications.Count > 0;
    public int UnreadNotifications { get => _unread; private set { if (SetProperty(ref _unread, value)) OnPropertyChanged(nameof(HasUnread)); } }
    public bool HasUnread => UnreadNotifications > 0;

    public bool IsActionCenterOpen
    {
        get => _actionCenterOpen;
        set
        {
            if (!SetProperty(ref _actionCenterOpen, value)) return;
            if (value) { IsStartMenuOpen = false; IsSearchOpen = false; IsTaskViewOpen = false; UnreadNotifications = 0; }
        }
    }

    public RelayCommand ToggleActionCenterCommand => new(() => IsActionCenterOpen = !IsActionCenterOpen);
    public RelayCommand ClearNotificationsCommand => new(() => { Notifications.Clear(); OnPropertyChanged(nameof(HasNotifications)); });

    private void RecordNotification(string title, string text, Bitmap? icon)
    {
        Notifications.Insert(0, new NotificationItem(title, text, icon, DateTime.Now, n =>
        {
            Notifications.Remove(n);
            OnPropertyChanged(nameof(HasNotifications));
        }));
        while (Notifications.Count > 50) Notifications.RemoveAt(Notifications.Count - 1);
        if (!IsActionCenterOpen) UnreadNotifications++;
        OnPropertyChanged(nameof(HasNotifications));
    }

    private static bool ToastsAllowed => WindowsSettings.Current.Notifications && !WindowsSettings.Current.DoNotDisturb;

    // Commandes rapides (enregistrées dans les paramètres du poste)
    public bool DoNotDisturb { get => WindowsSettings.Current.DoNotDisturb; set => WindowsSettings.Update(s => s.DoNotDisturb = value); }
    public bool NightLight { get => WindowsSettings.Current.NightLight; set => WindowsSettings.Update(s => s.NightLight = value); }
    public double Volume { get => WindowsSettings.Current.Volume; set => WindowsSettings.Update(s => { s.Volume = (int)Math.Round(value); s.Muted = false; }); }
    public bool Muted { get => WindowsSettings.Current.Muted; set => WindowsSettings.Update(s => s.Muted = value); }
    public string VolumeLabel => Muted ? "Son coupé" : $"Haut-parleurs : {WindowsSettings.Current.Volume} %";
    public string ServerLabel => ClientSettingsAuthority();

    private static string ClientSettingsAuthority()
    {
        try { return ClientSettings.Current.HttpBaseUri.Authority; }
        catch { return "—"; }
    }

    private void OnWindowsSettingsChanged()
    {
        foreach (var n in new[] { nameof(DoNotDisturb), nameof(NightLight), nameof(Volume), nameof(Muted), nameof(VolumeLabel) })
            OnPropertyChanged(n);
    }

    // ===== Horloge et calendrier =====

    public string LongTime { get => _longTime; private set => SetProperty(ref _longTime, value); }
    public string LongDate { get => _longDate; private set => SetProperty(ref _longDate, value); }
    public DateTime CalendarDate { get => _calendarDate; set => SetProperty(ref _calendarDate, value); }
    public RelayCommand TodayCommand => new(() => { CalendarDate = DateTime.Today.AddDays(1); CalendarDate = DateTime.Today; });

    private void UpdateLongClock(DateTime now)
    {
        LongTime = now.ToString("HH:mm:ss", Fr);
        LongDate = Fr.TextInfo.ToTitleCase(now.ToString("dddd d MMMM yyyy", Fr));
    }
}
