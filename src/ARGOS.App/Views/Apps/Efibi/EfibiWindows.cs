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
            () => new EfibiRecordView { DataContext = new EfibiRecordViewModel(desktop, id, key) }, 980, 820);
    }
}
