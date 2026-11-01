using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Argos.Setup;

public partial class MainWindow : Window
{
    private readonly bool _uninstall;
    private bool _busy;

    public MainWindow() : this(false) { }

    public MainWindow(bool uninstall)
    {
        InitializeComponent();
        _uninstall = uninstall;
        var build = SetupInstaller.Build is { } b ? $" · build {b}" : "";
        VersionText.Text = $"Version {SetupInstaller.Version}{build}";
        InstallPathBox.Text = SetupInstaller.CurrentInstallPath();

        BrowseButton.Click += OnBrowse;
        CancelButton.Click += (_, _) => Close();
        InstallButton.Click += OnInstall;
        Closing += (_, e) => e.Cancel = _busy;

        if (uninstall)
        {
            Title = "ARGOS - Désinstallation";
            TitleText.Text = "Désinstaller ARGOS";
            SubtitleText.Text = "Le programme, ses raccourcis et son entrée Windows sont retirés. Vos données (session, réglages, documents) sont conservées.";
            InstallPathBox.IsEnabled = false;
            BrowseButton.IsVisible = false;
            DesktopShortcutBox.IsVisible = StartMenuShortcutBox.IsVisible = LaunchBox.IsVisible = false;
            StatusText.Text = "Prêt à désinstaller ARGOS.";
            InstallButton.Content = "Désinstaller";
        }
        else if (SetupInstaller.IsInstalled)
        {
            TitleText.Text = "Réinstaller ARGOS";
            StatusText.Text = "ARGOS est déjà installé : il sera remplacé par cette version, vos données sont conservées.";
            InstallButton.Content = "Réinstaller";
        }
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Dossier d'installation d'ARGOS",
            AllowMultiple = false
        });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path)
            InstallPathBox.Text = Path.GetFileName(path.TrimEnd('\\', '/')).Equals("ARGOS", StringComparison.OrdinalIgnoreCase)
                ? path
                : Path.Combine(path, "ARGOS");
    }

    private async void OnInstall(object? sender, RoutedEventArgs e)
    {
        if (_uninstall)
        {
            await RunAsync(async () =>
            {
                StatusText.Text = "Désinstallation…";
                await Task.Run(SetupInstaller.Uninstall);
                Progress.Value = 100;
                StatusText.Text = "ARGOS a été désinstallé.";
            }, done: "Fermer");
            return;
        }

        var path = InstallPathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText.Text = "Choisissez un dossier d'installation.";
            return;
        }

        await RunAsync(async () =>
        {
            var progress = new Progress<SetupProgress>(p =>
            {
                Progress.Value = p.Percent;
                StatusText.Text = p.Message;
            });
            await SetupInstaller.InstallAsync(path, DesktopShortcutBox.IsChecked == true, StartMenuShortcutBox.IsChecked == true, progress);
            StatusText.Text = "ARGOS est installé et prêt à être utilisé.";
            if (LaunchBox.IsChecked == true)
            {
                var exe = Path.Combine(Path.GetFullPath(path), "ARGOS.exe");
                if (File.Exists(exe))
                    Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = true });
                Dispatcher.UIThread.Post(Close, DispatcherPriority.Background);
            }
        }, done: "Terminer");
    }

    private async Task RunAsync(Func<Task> work, string done)
    {
        SetBusy(true);
        try
        {
            await work();
            SetBusy(false);
            InstallButton.Content = done;
            InstallButton.Click -= OnInstall;
            InstallButton.Click += (_, _) => Close();
            CancelButton.IsVisible = false;
        }
        catch (UnauthorizedAccessException)
        {
            SetBusy(false);
            StatusText.Text = "Windows refuse l'écriture dans ce dossier. Gardez le dossier proposé par défaut.";
        }
        catch (Exception ex)
        {
            SetBusy(false);
            StatusText.Text = "Échec : " + ex.Message;
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        InstallButton.IsEnabled = CancelButton.IsEnabled = BrowseButton.IsEnabled = !busy;
        InstallPathBox.IsEnabled = !busy && !_uninstall;
        DesktopShortcutBox.IsEnabled = StartMenuShortcutBox.IsEnabled = LaunchBox.IsEnabled = !busy;
    }
}
