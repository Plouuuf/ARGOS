using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Argos.Shared.Contracts;

namespace Argos.Server.Services;

public sealed class UpdateOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>Dépôt GitHub dont les « Releases » contiennent les compilations (build-N).</summary>
    public string Repository { get; set; } = "Plouuuf/ARGOS";
    /// <summary>Jeton GitHub en lecture (obligatoire seulement si le dépôt est privé).</summary>
    public string GitHubToken { get; set; } = string.Empty;
    public int CheckMinutes { get; set; } = 10;
    /// <summary>Le serveur s'installe lui-même (Windows) quand une nouvelle compilation est publiée.</summary>
    public bool AutoInstallServer { get; set; } = true;
}

/// <summary>
/// Mise à jour automatique : lit la dernière Release GitHub, garde les paquets des postes (servis par /api/update)
/// et, sur le VPS Windows, installe la nouvelle version du serveur puis le redémarre (retour arrière si elle ne démarre pas).
/// </summary>
public sealed class UpdateService(IConfiguration configuration, RealtimeHub hub, ILogger<UpdateService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static string Dir { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "updates");
    private string CurrentFile => Path.Combine(Dir, "current.json");
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private UpdateOptions Options => configuration.GetSection("Update").Get<UpdateOptions>() ?? new UpdateOptions();

    /// <summary>Paquets disponibles pour les postes.</summary>
    public UpdateInfo Current()
    {
        try
        {
            if (File.Exists(CurrentFile) && JsonSerializer.Deserialize<UpdateInfo>(File.ReadAllText(CurrentFile), Json) is { } info)
            {
                info.ServerBuild = BuildInfo.Number;
                return info;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Fichier de mise à jour illisible.");
        }
        return new UpdateInfo { ServerBuild = BuildInfo.Number, Version = BuildInfo.Version };
    }

    public string? PackagePath(string platform)
    {
        var info = Current();
        var name = platform switch { "windows" => UpdatePackages.Windows, "macos" => UpdatePackages.MacOs, _ => null };
        if (name is null || info.Build == 0) return null;
        var path = Path.Combine(Dir, UpdatePackages.TagPrefix + info.Build, name);
        return File.Exists(path) ? path : null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = Options;
            if (options.Enabled)
            {
                try
                {
                    await CheckAsync(options, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Vérification des mises à jour impossible : {Message}", ex.Message);
                    Trace("ERREUR : " + ex);
                }
            }
            try { await Task.Delay(TimeSpan.FromMinutes(Math.Max(2, options.CheckMinutes)), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private HttpRequestMessage Request(UpdateOptions options, string url, bool binary)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ARGOS-Server", BuildInfo.Version));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(binary ? "application/octet-stream" : "application/vnd.github+json"));
        if (!string.IsNullOrWhiteSpace(options.GitHubToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.GitHubToken.Trim());
        return request;
    }

    private async Task CheckAsync(UpdateOptions options, CancellationToken ct)
    {
        using var response = await _http.SendAsync(Request(options, $"https://api.github.com/repos/{options.Repository}/releases/latest", false), ct);
        if (!response.IsSuccessStatusCode)
        {
            Trace($"GitHub a répondu {(int)response.StatusCode} sur releases/latest ({options.Repository}).");
            logger.LogWarning("GitHub ({Status}) : aucune version publiée lisible{Hint}.", (int)response.StatusCode,
                string.IsNullOrWhiteSpace(options.GitHubToken) ? " (dépôt privé ? renseigner Update:GitHubToken)" : string.Empty);
            return;
        }
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        if (!tag.StartsWith(UpdatePackages.TagPrefix, StringComparison.Ordinal) || !int.TryParse(tag[UpdatePackages.TagPrefix.Length..], out var build))
            return;
        var assets = root.GetProperty("assets").EnumerateArray()
            .ToDictionary(a => a.GetProperty("name").GetString() ?? string.Empty, a => a.GetProperty("url").GetString() ?? string.Empty);
        var version = root.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
        var published = root.TryGetProperty("published_at", out var p) && p.TryGetDateTimeOffset(out var at) ? at : DateTimeOffset.UtcNow;

        // Paquets des postes, servis par le serveur (les postes n'ont pas besoin d'accéder à GitHub).
        var current = Current();
        if (build > current.Build)
        {
            var folder = Path.Combine(Dir, UpdatePackages.TagPrefix + build);
            Directory.CreateDirectory(folder);
            var info = new UpdateInfo { Build = build, Version = version, PublishedAt = published };
            foreach (var (name, setter) in new (string, Action<UpdateInfo>)[]
                     {
                         (UpdatePackages.Windows, i => i.Windows = true),
                         (UpdatePackages.MacOs, i => i.MacOs = true)
                     })
            {
                if (!assets.TryGetValue(name, out var url)) continue;
                await DownloadAsync(options, url, Path.Combine(folder, name), ct);
                setter(info);
            }
            File.WriteAllText(CurrentFile, JsonSerializer.Serialize(info, Json));
            foreach (var old in Directory.GetDirectories(Dir, UpdatePackages.TagPrefix + "*"))
                if (!string.Equals(Path.GetFileName(old), UpdatePackages.TagPrefix + build, StringComparison.Ordinal))
                    try { Directory.Delete(old, true); } catch { /* fichier en cours d'envoi : supprimé la prochaine fois */ }
            logger.LogInformation("Postes : version {Version} (build {Build}) disponible.", version, build);
        }

        Trace($"Dernière version : {tag} ; serveur en service : build {BuildInfo.Number} ; postes : build {Current().Build} ; " +
              $"auto-installation {(options.AutoInstallServer ? "activée" : "désactivée")}, Windows : {OperatingSystem.IsWindows()}, " +
              $"paquet serveur : {assets.ContainsKey(UpdatePackages.Server)}, déjà tentée : {AlreadyTried(build)}.");

        // Le serveur lui-même (VPS Windows, compilation officielle uniquement).
        if (options.AutoInstallServer && OperatingSystem.IsWindows() && BuildInfo.Number > 0 && build > BuildInfo.Number
            && assets.TryGetValue(UpdatePackages.Server, out var serverUrl) && !AlreadyTried(build))
        {
            var zip = Path.Combine(Dir, $"serveur-{build}.zip");
            await DownloadAsync(options, serverUrl, zip, ct);
            File.WriteAllText(Path.Combine(Dir, "tentative.txt"), build.ToString());
            logger.LogWarning("Installation du serveur ARGOS build {Build} : redémarrage.", build);
            Trace($"Installation du serveur build {build} lancée (dossier {AppContext.BaseDirectory}).");
            await hub.BroadcastAsync(new RealtimeMessage(RealtimeTypes.Notify, Title: "Mise à jour d'ARGOS",
                Text: "Le serveur redémarre pour se mettre à jour : reconnexion automatique dans quelques secondes."), ct);
            InstallServer(zip);
        }
    }

    /// <summary>Journal des vérifications (data\updates\verification.log) pour diagnostiquer la mise à jour du VPS.</summary>
    private static void Trace(string text)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var file = Path.Combine(Dir, "verification.log");
            if (File.Exists(file) && new FileInfo(file).Length > 300_000) File.Delete(file);
            File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}");
        }
        catch
        {
            // Journal facultatif.
        }
    }

    private bool AlreadyTried(int build)
    {
        var file = Path.Combine(Dir, "tentative.txt");
        return File.Exists(file) && File.ReadAllText(file).Trim() == build.ToString();
    }

    private async Task DownloadAsync(UpdateOptions options, string url, string target, CancellationToken ct)
    {
        using var response = await _http.SendAsync(Request(options, url, true), HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var temp = target + ".part";
        await using (var file = File.Create(temp))
            await response.Content.CopyToAsync(file, ct);
        File.Move(temp, target, true);
    }

    /// <summary>
    /// Script lancé par une tâche planifiée distincte (SYSTEM) : il survit à l'arrêt du serveur,
    /// remplace les fichiers (jamais data\ ni appsettings.Local.json), redémarre, et revient en arrière si besoin.
    /// </summary>
    private void InstallServer(string zip)
    {
        var root = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var script = Path.Combine(Dir, "maj-serveur.ps1");
        var log = Path.Combine(Dir, "maj-serveur.log");
        File.WriteAllText(script, $$"""
            $ErrorActionPreference = "Continue"
            Start-Transcript -Path '{{log}}' -Append | Out-Null
            $Root = '{{root}}'
            $Zip = '{{zip}}'
            $Task = 'ARGOS Server'
            $Exe = Join-Path $Root 'ARGOS.Server.exe'
            $Tmp = Join-Path $env:TEMP 'argos-maj'
            Start-Sleep -Seconds 3
            Remove-Item $Tmp -Recurse -Force -ErrorAction SilentlyContinue
            Expand-Archive -Path $Zip -DestinationPath $Tmp -Force
            if (-not (Test-Path (Join-Path $Tmp 'ARGOS.Server.exe'))) { Write-Output 'Paquet invalide.'; Stop-Transcript; exit 1 }
            Remove-Item (Join-Path $Tmp 'appsettings.Local.json') -Force -ErrorAction SilentlyContinue
            Stop-ScheduledTask -TaskName $Task -ErrorAction SilentlyContinue
            Get-Process -Name 'ARGOS.Server' -ErrorAction SilentlyContinue | Stop-Process -Force
            for ($i = 0; $i -lt 30 -and (Get-Process -Name 'ARGOS.Server' -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Seconds 1 }
            Copy-Item $Exe (Join-Path $Root 'ARGOS.Server.old.exe') -Force
            Copy-Item -Path (Join-Path $Tmp '*') -Destination $Root -Recurse -Force
            Start-ScheduledTask -TaskName $Task
            Start-Sleep -Seconds 20
            if (-not (Get-Process -Name 'ARGOS.Server' -ErrorAction SilentlyContinue)) {
                Write-Output 'La nouvelle version ne démarre pas : retour à la précédente.'
                Stop-ScheduledTask -TaskName $Task -ErrorAction SilentlyContinue
                Copy-Item (Join-Path $Root 'ARGOS.Server.old.exe') $Exe -Force
                Start-ScheduledTask -TaskName $Task
            } else { Write-Output 'Mise à jour installée.' }
            Remove-Item $Tmp -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item $Zip -Force -ErrorAction SilentlyContinue
            Stop-Transcript | Out-Null
            """, new UTF8Encoding(true));

        const string task = "ARGOS Mise a jour";
        Run("schtasks", $"/Create /TN \"{task}\" /SC ONCE /ST 00:00 /SD 01/01/2030 /RU SYSTEM /RL HIGHEST /F " +
                        $"/TR \"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \\\"{script}\\\"\"");
        Run("schtasks", $"/Run /TN \"{task}\"");
    }

    private void Run(string file, string args)
    {
        using var process = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
        process?.WaitForExit(30_000);
        if (process is { ExitCode: not 0 })
        {
            var error = process.StandardError.ReadToEnd();
            logger.LogWarning("{File} {Args} : {Error}", file, args, error);
            Trace($"{file} {args} a échoué (code {process.ExitCode}) : {error.Trim()}");
        }
        else Trace($"{file} {args.Split(' ').FirstOrDefault()} : OK");
    }

    public override void Dispose()
    {
        _http.Dispose();
        base.Dispose();
    }
}
