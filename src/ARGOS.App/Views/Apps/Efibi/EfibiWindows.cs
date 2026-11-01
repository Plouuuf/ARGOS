using Argos.Services;
using Argos.ViewModels;
using Argos.ViewModels.Efibi;

namespace Argos.Views.Apps.Efibi;

public static class EfibiWindows
{
    private static Avalonia.Media.Imaging.Bitmap Icon => ImageLoader.Asset("Icons/efibi.png");

    public static WindowSpec Record(DesktopViewModel desktop, long id)
    {
        var key = "efibi:" + id;
        return new WindowSpec(key, $"eFiBi - Fiche bilan n° {id}", Icon,
            () =>
            {
                var vm = new EfibiRecordViewModel(desktop, id, key);
                return desktop.IsCompactLayout
                    ? (Avalonia.Controls.Control)new Argos.Views.Mobile.Efibi.EfibiRecordMobileView { DataContext = vm }
                    : new EfibiRecordView { DataContext = vm };
            }, 980, 820);
    }
}
