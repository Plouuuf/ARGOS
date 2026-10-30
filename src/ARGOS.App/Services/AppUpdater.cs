using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;
using Argos.Shared.Contracts;

namespace Argos.Services;

/// <summary>
/// Mise à jour automatique des postes (Windows et macOS) depuis le serveur ARGOS.
/// Le fichier en cours d'exécution est remplacé sur place (Windows : renommage de l'exe, macOS : remplacement du .app) :
/// la nouvelle version démarre au prochain lancement, ou tout de suite via <see cref="Restart"/>.
/// </summary>
public static class AppUpdater
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Compilation déjà installée sur place pendant cette session (en attente de redémarrage).</summary>
    public static int Installed { get; private set; }

    private static string Platform => OperatingSystem.IsWindows() ? "windows" : "macos";

    public static bool IsSupported =>
        !AppPaths.IsMobile && (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        && BuildInfo.Number > 0 && Environment.ProcessPath is { Length: > 0 } path
        && (!OperatingSystem.IsMacOS() || path.Contains(".app/Contents/MacOS/", StringComparison.Ordinal));

    /// <summary>Supprime les restes d'une mise à jour précédente (ancien exe ou ancienne app).</summary>
    public static void Cleanup()
    {
        if (!IsSupported) return;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var old = OldExe;
                if (File.Exists(old)) File.Delete(old);
            }
            else if (AppBundle is { } bundle && Directory.Exists(bundle + ".old"))
            {
                Directory.Delete(bundle + ".old", true);
            }
        }
        catch
        {
            // Encore verrouillé : on réessaiera au prochain lancement.
        }
    }

    /// <summary>Version proposée par le serveur, si elle est plus récente que celle installée.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        if (!IsSupported) return null;
        using var http = Client(TimeSpan.FromSeconds(15));
        var info = await http.GetFromJsonAsync<UpdateInfo>("api/update", Json, ct);
        if (info is null || info.Build <= Math.Max(BuildInfo.Number, Installed)) return null;
        return (OperatingSystem.IsWindows() ? info.Windows : info.MacOs) ? info : null;
    }

    /// <summary>Télécharge et installe la version. Renvoie un message d'erreur, ou null si tout s'est bien passé.</summary>
    public static async Task<string?> InstallAsync(UpdateInfo info, CancellationToken ct = default)
    {
        if (!IsSupported) return "Mise à jour automatique indisponible sur ce poste.";
        await Gate.WaitAsync(ct);
        try
        {
            if (Installed >= info.Build) return null;
            using var http = Client(TimeSpan.FromMinutes(10));
            var zip = Path.Combine(Path.GetTempPath(), $"argos-maj-{info.Build}.zip");
            await using (var source = await http.GetStreamAsync($"api/update/{Platform}", ct))
            await using (var target = File.Create(zip))
                await source.CopyToAsync(target, ct);

            if (OperatingSystem.IsWindows()) InstallWindows(zip);
            else await InstallMacAsync(zip, ct);

            try { File.Delete(zip); } catch { /* fichier temporaire */ }
            Installed = info.Build;
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return "Mise à jour impossible : ARGOS n'a pas le droit d'écrire dans son dossier. Déplace-le (Bureau, Documents…) ou mets-le à jour à la main.";
        }
        catch (Exception ex)
        {
            return "Mise à jour impossible : " + ex.Message;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Relance ARGOS sur la nouvelle version.</summary>
    public static void Restart()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
            else if (AppBundle is { } bundle)
                Process.Start("/bin/sh", ["-c", $"sleep 1; /usr/bin/open -n \"{bundle}\""]);
        }
        finally
        {
            App.Quit();
        }
    }

    private static HttpClient Client(TimeSpan timeout) => new() { BaseAddress = ClientSettings.Current.HttpBaseUri, Timeout = timeout };

    // ===== Windows : l'exe en cours d'exécution peut être renommé, puis remplacé. =====

    private static string OldExe => Path.ChangeExtension(Environment.ProcessPath!, ".old.exe");

    private static void InstallWindows(string zip)
    {
        var current = Environment.ProcessPath!;
        var fresh = Path.ChangeExtension(current, ".new.exe");
        using (var archive = ZipFile.OpenRead(zip))
        {
            var entry = archive.Entries.FirstOrDefault(e => e.Name.Equals("ARGOS.exe", StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException("paquet Windows sans ARGOS.exe.");
            entry.ExtractToFile(fresh, true);
        }
        if (File.Exists(OldExe)) File.Delete(OldExe);
        File.Move(current, OldExe);
        try
        {
            File.Move(fresh, current);
        }
        catch
        {
            File.Move(OldExe, current);                 // on remet l'ancien en place
            throw;
        }
    }

    // ===== macOS : le paquet ARGOS.app est remplacé (l'ancien reste utilisable jusqu'à la fermeture). =====

    private static string? AppBundle
    {
        get
        {
            var path = Environment.ProcessPath ?? string.Empty;
            var index = path.IndexOf(".app/Contents/MacOS/", StringComparison.Ordinal);
            return index < 0 ? null : path[..(index + 4)];
        }
    }

    private static async Task InstallMacAsync(string zip, CancellationToken ct)
    {
        var bundle = AppBundle ?? throw new InvalidOperationException("ARGOS.app introuvable.");
        var parent = Path.GetDirectoryName(bundle)!;
        var staging = Path.Combine(parent, ".argos-maj");
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        // ditto conserve les droits d'exécution et la signature du paquet.
        await RunAsync("/usr/bin/ditto", ["-x", "-k", zip, staging], ct);
        var fresh = Directory.GetDirectories(staging, "*.app").FirstOrDefault() ?? throw new InvalidDataException("paquet macOS sans ARGOS.app.");
        await RunAsync("/usr/bin/xattr", ["-cr", fresh], ct, ignoreErrors: true);
        var old = bundle + ".old";
        if (Directory.Exists(old)) Directory.Delete(old, true);
        Directory.Move(bundle, old);
        try
        {
            Directory.Move(fresh, bundle);
        }
        catch
        {
            Directory.Move(old, bundle);
            throw;
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { /* dossier temporaire */ }
        }
    }

    private static async Task RunAsync(string file, string[] args, CancellationToken ct, bool ignoreErrors = false)
    {
        using var process = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, RedirectStandardError = true })
                            ?? throw new InvalidOperationException(file + " introuvable.");
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0 && !ignoreErrors)
            throw new InvalidOperationException($"{Path.GetFileName(file)} : {await process.StandardError.ReadToEndAsync(ct)}");
    }
}
