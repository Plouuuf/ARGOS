using System.Reflection;

namespace Argos.Shared.Contracts;

/// <summary>Numéro de compilation d'ARGOS (GitHub Actions), commun au serveur et aux postes.</summary>
public static class BuildInfo
{
    public static int Number { get; } = Read();

    public static string Version { get; } = typeof(BuildInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static string Label => Number > 0 ? $"{Version} (build {Number})" : $"{Version} (développement)";

    private static int Read()
    {
        var value = typeof(BuildInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "ArgosBuild")?.Value;
        return int.TryParse(value, out var n) ? n : 0;
    }
}

/// <summary>Version disponible au téléchargement sur le serveur ARGOS (GET /api/update).</summary>
public sealed class UpdateInfo
{
    public int Build { get; set; }
    public string Version { get; set; } = string.Empty;
    public bool Windows { get; set; }
    public bool MacOs { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public int ServerBuild { get; set; }
}

public static class UpdatePackages
{
    public const string Server = "ARGOS-server.zip";
    public const string Windows = "ARGOS-windows.zip";
    public const string MacOs = "ARGOS-macos.zip";
    public const string TagPrefix = "build-";
}
