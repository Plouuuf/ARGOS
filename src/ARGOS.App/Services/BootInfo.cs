using System.Runtime.InteropServices;
using Argos.Shared.Contracts;

namespace Argos.Services;

public sealed record BootLine(string Label, string Value, bool Ok = true, int DelayMs = 260)
{
    public bool HasValue => Value.Length > 0;
}

/// <summary>Écran de démarrage UEFI : vraies caractéristiques du poste quand elles sont lisibles, sinon valeurs simulées.</summary>
public static class BootInfo
{
    public static int Count => Lines().Count();

    public static IEnumerable<BootLine> Lines()
    {
        yield return new BootLine("ARGOS UEFI BIOS v2.17 - VAL-DE-MARNE SIMULATION", string.Empty, DelayMs: 420);
        yield return new BootLine("Carte mère", "BSPP-WS 8000 (CTA Créteil)");
        yield return new BootLine("Processeur", $"{RuntimeInformation.ProcessArchitecture} - {Environment.ProcessorCount} cœurs détectés", DelayMs: 380);
        yield return new BootLine("Mémoire vive", $"{Memory()} Go - test mémoire OK", DelayMs: 520);
        yield return new BootLine("Stockage", Storage());
        yield return new BootLine("Réseau", $"Carte Ethernet BSPP - serveur ARGOS {Server()}", DelayMs: 420);
        yield return new BootLine("Sécurité", "Secure Boot activé - TPM 2.0 prêt - poste whitelisté");
        yield return new BootLine("Système", $"Windows 10 Professionnel - ARGOS {BuildInfo.Label}", DelayMs: 420);
    }

    private static string Memory()
    {
        try
        {
            var bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            return bytes > 0 ? Math.Max(1, Math.Round(bytes / 1024.0 / 1024 / 1024)).ToString() : "16";
        }
        catch { return "16"; }
    }

    private static string Storage()
    {
        try
        {
            var drive = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).MaxBy(d => d.TotalSize);
            if (drive is not null)
                return $"Disque système {drive.TotalSize / 1_000_000_000} Go ({drive.AvailableFreeSpace / 1_000_000_000} Go libres)";
        }
        catch { }
        return "SSD NVMe 512 Go";
    }

    private static string Server()
    {
        try { return ClientSettings.Current.HttpBaseUri.Authority; }
        catch { return "non configuré"; }
    }
}
