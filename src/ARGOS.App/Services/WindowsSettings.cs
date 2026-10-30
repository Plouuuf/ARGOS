using System.Text.Json;

namespace Argos.Services;

/// <summary>
/// Paramètres Windows simulés, propres à ce poste (fichier windows-settings.json du dossier ARGOS) :
/// fond d'écran, affichage, son, notifications. <see cref="Changed"/> prévient les écrans concernés.
/// </summary>
public sealed class WindowsSettings
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static WindowsSettings? _current;

    public static WindowsSettings Current => _current ??= Load();
    public static event Action? Changed;

    /// <summary>-1 = diaporama, sinon numéro de la photo fixe.</summary>
    public int Wallpaper { get; set; } = -1;
    public int WallpaperMinutes { get; set; } = 10;
    /// <summary>Taille du texte et des applications : 100, 110 ou 125 %.</summary>
    public int Scale { get; set; } = 100;
    public bool NightLight { get; set; }
    public int Volume { get; set; } = 60;
    public bool Muted { get; set; }
    public bool Notifications { get; set; } = true;
    public bool DoNotDisturb { get; set; }
    public bool BootSequence { get; set; } = true;

    private static string FilePath => Path.Combine(AppPaths.DataDirectory, "windows-settings.json");

    private static WindowsSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<WindowsSettings>(File.ReadAllText(FilePath), Json) is { } s)
                return s;
        }
        catch
        {
            // Fichier illisible : valeurs par défaut.
        }
        return new WindowsSettings();
    }

    /// <summary>Applique une modification, l'enregistre et prévient l'interface.</summary>
    public static void Update(Action<WindowsSettings> change)
    {
        change(Current);
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, Json));
        }
        catch
        {
            // Enregistrement impossible (dossier en lecture seule) : le réglage vaut pour la session.
        }
        Changed?.Invoke();
    }
}
