namespace Argos.Services;

/// <summary>Dossiers de données d'ARGOS sur chaque plateforme.</summary>
internal static class AppPaths
{
    // Windows : %LOCALAPPDATA%\VDMS\ARGOS
    // macOS   : ~/Library/Application Support/VDMS/ARGOS
    // Android / iOS : dossier privé de l'application (supprimé avec elle).
    public static string DataDirectory
    {
        get
        {
            var dir = IsMobile
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ARGOS")
                : OperatingSystem.IsMacOS()
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "VDMS", "ARGOS")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VDMS", "ARGOS");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static bool IsMobile => OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();

    public static string DiscordSessionPath => Path.Combine(DataDirectory, "discord-session.json");
}
