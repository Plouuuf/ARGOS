using Argos.Services;
using Argos.ViewModels;
using Argos.ViewModels.Remise;

namespace Argos.Views.Apps.Remise;

/// <summary>Fenêtres d'ADAGIO REMISE.</summary>
public static class RemiseWindows
{
    private static Avalonia.Media.Imaging.Bitmap Icon => ImageLoader.Asset("Icons/adagio-remise.png");

    /// <summary>Fiche d'un engin, ou création d'un engin quand <paramref name="nom"/> est null.</summary>
    public static WindowSpec Fiche(DesktopViewModel desktop, string? nom)
    {
        var key = "remise-fiche:" + (nom ?? "nouveau-" + Guid.NewGuid().ToString("N")[..6]);
        return new WindowSpec(key, nom is null ? "ADAGIO REMISE - Nouvel engin" : "ADAGIO REMISE - Fiche engin " + nom, Icon,
            () => new FicheEnginView { DataContext = new FicheEnginViewModel(desktop, nom, key) },
            900, 760);
    }
}
