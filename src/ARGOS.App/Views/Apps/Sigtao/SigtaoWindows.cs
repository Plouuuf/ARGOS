using Argos.Services;
using Argos.ViewModels;
using Argos.ViewModels.Sigtao;
using Argos.Views.Mobile.Sigtao;

namespace Argos.Views.Apps.Sigtao;

/// <summary>Fenêtres de SIGTAO.</summary>
public static class SigtaoWindows
{
    private static Avalonia.Media.Imaging.Bitmap Icon => ImageLoader.Asset("Icons/sigtao.png");

    public static WindowSpec Dossier(DesktopViewModel desktop, string? id)
    {
        var key = "sigtao-dossier:" + (id ?? "nouveau-" + Guid.NewGuid().ToString("N")[..6]);
        return new WindowSpec(key, id is null ? "SIGTAO - Nouveau dossier militaire" : "SIGTAO - Dossier militaire", Icon,
            () =>
            {
                var vm = new DossierViewModel(desktop, id, key);
                return desktop.IsCompactLayout ? (Avalonia.Controls.Control)new DossierMobileView { DataContext = vm } : new DossierView { DataContext = vm };
            }, 980, 780);
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
            () =>
            {
                var vm = new CompteRenduViewModel(desktop, id, key);
                return desktop.IsCompactLayout ? (Avalonia.Controls.Control)new CompteRenduMobileView { DataContext = vm } : new CompteRenduView { DataContext = vm };
            },
            id is null ? 1240 : 820, 860);
    }
}
