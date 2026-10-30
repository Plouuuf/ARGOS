using Argos.Services;
using Argos.ViewModels;
using Argos.ViewModels.Sigtao;

namespace Argos.Views.Apps.Sigtao;

/// <summary>Fenêtres de SIGTAO.</summary>
public static class SigtaoWindows
{
    private static Avalonia.Media.Imaging.Bitmap Icon => ImageLoader.Asset("Icons/sigtao.png");

    public static WindowSpec Dossier(DesktopViewModel desktop, string? id)
    {
        var key = "sigtao-dossier:" + (id ?? "nouveau-" + Guid.NewGuid().ToString("N")[..6]);
        return new WindowSpec(key, id is null ? "SIGTAO - Nouveau dossier militaire" : "SIGTAO - Dossier militaire", Icon,
            () => new DossierView { DataContext = new DossierViewModel(desktop, id, key) }, 980, 780);
    }

    public static WindowSpec GardePrint(DesktopViewModel desktop, Argos.Shared.Contracts.FeuilleGarde feuille)
    {
        var key = "sigtao-garde-print:" + feuille.Id + Guid.NewGuid().ToString("N")[..4];
        return new WindowSpec(key, $"Feuille de garde {feuille.Centre} - aperçu avant impression", Icon,
            () => new GardePrintView { DataContext = new GardePrintViewModel(desktop, key, feuille) }, 900, 900);
    }

    public static WindowSpec Report(DesktopViewModel desktop, string? id)
    {
        var key = "sigtao-cr:" + (id ?? "nouveau-" + Guid.NewGuid().ToString("N")[..6]);
        return new WindowSpec(key, id is null ? "SIGTAO - Nouveau compte-rendu" : "SIGTAO - Compte-rendu", Icon,
            () => new CompteRenduView { DataContext = new CompteRenduViewModel(desktop, id, key) },
            id is null ? 1240 : 820, 860);
    }
}
