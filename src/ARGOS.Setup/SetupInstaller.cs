using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;

namespace Argos.Setup;

public sealed record SetupProgress(int Percent, string Message);

/// <summary>
/// Installation d'ARGOS pour l'utilisateur courant (sans droits Administrateur) :
/// %LOCALAPPDATA%\Programs\ARGOS\ARGOS.exe, raccourcis Bureau / menu Démarrer, entrée « Applications installées ».
/// Les données (session, réglages, documents) sont dans %LOCALAPPDATA%\VDMS\ARGOS : jamais touchées.
/// Les mises à jour suivantes sont faites par ARGOS lui-même (serveur ARGOS).
/// </summary>
public static class SetupInstaller
{
    public const string Product = "ARGOS";
    private const string Marker = ".argos-install";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ARGOS";
    private const string UninstallerName = "Desinstaller-ARGOS.exe";

    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string DefaultInstallPath => Path.Combine(Local, "Programs", Product);
    private static string InfoDir => Path.Combine(Local, "VDMS", Product);
    private static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ARGOS.lnk");
    private static string StartMenuDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", Product);

    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public static string? Build =>
        Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "ArgosBuild")?.Value is { } b && b != "0" ? b : null;

    /// <summary>Dossier d'une installation existante (pour la réparation / mise à jour), sinon le dossier par défaut.</summary>
    public static string CurrentInstallPath()
    {
        try
        {
            var file = Path.Combine(InfoDir, "install.json");
            if (File.Exists(file))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.TryGetProperty("installPath", out var p) && p.GetString() is { Length: > 0 } path && Directory.Exists(path))
                    return path;
            }
        }
        catch { }
        return DefaultInstallPath;
    }

    public static bool IsInstalled => File.Exists(Path.Combine(CurrentInstallPath(), "ARGOS.exe"));

    public static async Task InstallAsync(string installPath, bool desktopShortcut, bool startMenuShortcut, IProgress<SetupProgress>? progress = null)
    {
        installPath = Path.GetFullPath(installPath);
        ValidateInstallDirectory(installPath);

        progress?.Report(new SetupProgress(5, "Fermeture d'ARGOS…"));
        await Task.Run(WaitForClientExit);

        progress?.Report(new SetupProgress(20, "Extraction du paquet…"));
        var temp = Path.Combine(Path.GetTempPath(), "ARGOS-Setup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            await Task.Run(() => ExtractEmbeddedPayload(temp));

            progress?.Report(new SetupProgress(50, "Copie des fichiers…"));
            await Task.Run(() => ApplyWithRollback(temp, installPath));

            progress?.Report(new SetupProgress(72, "Création des raccourcis…"));
            await Task.Run(() => CreateShortcuts(installPath, desktopShortcut, startMenuShortcut));

            progress?.Report(new SetupProgress(88, "Enregistrement de l'installation…"));
            await Task.Run(() => Register(installPath));
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
        progress?.Report(new SetupProgress(100, "Installation terminée."));
    }

    private static void ValidateInstallDirectory(string folder)
    {
        if (!Directory.Exists(folder) || !Directory.EnumerateFileSystemEntries(folder).Any()) return;
        // Dossier non vide : uniquement une installation ARGOS (on n'écrase jamais un dossier quelconque).
        if (File.Exists(Path.Combine(folder, Marker)) || File.Exists(Path.Combine(folder, "ARGOS.exe"))) return;
        throw new InvalidOperationException("Ce dossier n'est pas vide et ne contient pas ARGOS. Choisissez un dossier vide.");
    }

    private static void WaitForClientExit()
    {
        foreach (var proc in Process.GetProcessesByName("ARGOS"))
        {
            using (proc)
            {
                try
                {
                    proc.CloseMainWindow();
                    if (!proc.WaitForExit(10000))
                        throw new IOException("Fermez ARGOS avant de poursuivre l'installation.");
                }
                catch (InvalidOperationException) { }
            }
        }
    }

    private static void ExtractEmbeddedPayload(string dest)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ARGOS.Setup.payload.zip")
                           ?? throw new InvalidOperationException("Paquet ARGOS absent de l'installateur.");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var root = Path.GetFullPath(dest).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var target = Path.GetFullPath(Path.Combine(dest, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Chemin interdit dans le paquet : " + entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
        if (!File.Exists(Path.Combine(dest, "ARGOS.exe")))
            throw new InvalidDataException("Le paquet ne contient pas ARGOS.exe.");
    }

    /// <summary>Copie avec sauvegarde des fichiers remplacés ; en cas d'échec, tout est restauré.</summary>
    private static void ApplyWithRollback(string payload, string folder)
    {
        Directory.CreateDirectory(folder);
        var files = Directory.GetFiles(payload, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(payload, f)).ToList();
        var backup = Path.Combine(Path.GetTempPath(), "ARGOS-Setup", "sauvegarde-" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        var done = new List<string>();
        try
        {
            foreach (var rel in files)
            {
                var dest = Path.Combine(folder, rel);
                if (File.Exists(dest))
                {
                    var saved = Path.Combine(backup, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    File.Copy(dest, saved, true);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                done.Add(rel);
                File.Copy(Path.Combine(payload, rel), dest, true);
            }
            // Restes d'une mise à jour automatique précédente.
            foreach (var old in new[] { "ARGOS.old.exe", "ARGOS.new.exe" })
                try { File.Delete(Path.Combine(folder, old)); } catch { }
            File.WriteAllText(Path.Combine(folder, Marker), $"ARGOS {Version}");
        }
        catch
        {
            foreach (var rel in done.AsEnumerable().Reverse())
            {
                try
                {
                    var saved = Path.Combine(backup, rel);
                    var dest = Path.Combine(folder, rel);
                    if (File.Exists(saved)) File.Copy(saved, dest, true);
                    else File.Delete(dest);
                }
                catch { }
            }
            throw;
        }
        finally
        {
            try { if (Directory.Exists(backup)) Directory.Delete(backup, true); } catch { }
        }

        // Désinstalleur : copie de cet installateur dans le dossier d'ARGOS.
        try
        {
            var self = Environment.ProcessPath;
            var uninstaller = Path.Combine(folder, UninstallerName);
            if (self is not null && !string.Equals(Path.GetFullPath(self), uninstaller, StringComparison.OrdinalIgnoreCase))
                File.Copy(self, uninstaller, true);
        }
        catch { }
    }

    private static void CreateShortcuts(string folder, bool desktop, bool startMenu)
    {
        var exe = Path.Combine(folder, "ARGOS.exe");
        var links = new List<(string Link, string Target, string Args, string Description)>();
        if (desktop) links.Add((DesktopLink, exe, "", "ARGOS - poste de travail BSPP (VDMS)"));
        if (startMenu)
        {
            Directory.CreateDirectory(StartMenuDir);
            links.Add((Path.Combine(StartMenuDir, "ARGOS.lnk"), exe, "", "ARGOS - poste de travail BSPP (VDMS)"));
            links.Add((Path.Combine(StartMenuDir, "Désinstaller ARGOS.lnk"), Path.Combine(folder, UninstallerName), "--uninstall", "Désinstaller ARGOS"));
        }
        if (links.Count == 0) return;

        // WScript.Shell via PowerShell : raccourcis .lnk avec l'icône d'ARGOS.
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        var script = "$s = New-Object -ComObject WScript.Shell;" + string.Concat(links.Select(l =>
            $"$l = $s.CreateShortcut({Q(l.Link)}); $l.TargetPath = {Q(l.Target)}; $l.Arguments = {Q(l.Args)}; " +
            $"$l.WorkingDirectory = {Q(folder)}; $l.IconLocation = {Q(exe + ",0")}; $l.Description = {Q(l.Description)}; $l.Save();"));
        RunPowerShell(script);
    }

    private static void RunPowerShell(string script)
    {
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
        using var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        });
        p?.WaitForExit(30000);
    }

    private static void Register(string folder)
    {
        Directory.CreateDirectory(InfoDir);
        var info = new { product = Product, installPath = folder, version = Version, build = Build, installedUtc = DateTimeOffset.UtcNow };
        File.WriteAllText(Path.Combine(InfoDir, "install.json"), JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));

        if (!OperatingSystem.IsWindows()) return;
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        var exe = Path.Combine(folder, "ARGOS.exe");
        var uninstaller = Path.Combine(folder, UninstallerName);
        key.SetValue("DisplayName", "ARGOS");
        key.SetValue("DisplayVersion", Build is null ? Version : $"{Version} (build {Build})");
        key.SetValue("Publisher", "VAL-DE-MARNE SIMULATION");
        key.SetValue("DisplayIcon", exe + ",0");
        key.SetValue("InstallLocation", folder);
        key.SetValue("UninstallString", $"\"{uninstaller}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{uninstaller}\" --uninstall --silent");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        try { key.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord); } catch { }
    }

    /// <summary>
    /// Désinstallation : programme, raccourcis et entrée Windows. Les données de l'utilisateur
    /// (%LOCALAPPDATA%\VDMS\ARGOS : session, réglages, documents) sont conservées.
    /// </summary>
    public static void Uninstall()
    {
        var folder = CurrentInstallPath();
        WaitForClientExit();

        foreach (var link in new[] { DesktopLink })
            try { File.Delete(link); } catch { }
        try { if (Directory.Exists(StartMenuDir)) Directory.Delete(StartMenuDir, true); } catch { }
        if (OperatingSystem.IsWindows())
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
        try { File.Delete(Path.Combine(InfoDir, "install.json")); } catch { }

        foreach (var name in new[] { "ARGOS.exe", "ARGOS.old.exe", "ARGOS.new.exe", Marker })
            try { File.Delete(Path.Combine(folder, name)); } catch { }

        // Le désinstalleur en cours d'exécution ne peut pas s'effacer lui-même : cmd s'en charge juste après.
        if (Directory.Exists(folder) && File.Exists(Path.Combine(folder, Marker)) == false)
        {
            Process.Start(new ProcessStartInfo("cmd.exe",
                $"/c ping -n 3 127.0.0.1 >nul & del /f /q \"{Path.Combine(folder, UninstallerName)}\" & rmdir \"{folder}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
    }
}
