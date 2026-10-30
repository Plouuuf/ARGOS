using Argos.Services;
using Argos.ViewModels;
using Argos.ViewModels.Accessoires;

namespace Argos.Views.Apps.Accessoires;

public static class AccessoryWindows
{
    /// <summary>Bloc-notes sur un fichier de Mes documents (une fenêtre par fichier).</summary>
    public static WindowSpec Notepad(DesktopViewModel desktop, string fileId)
    {
        var key = "bloc-notes:" + fileId;
        var name = ArgosSession.Current?.Files.Find(fileId)?.Name ?? "Document";
        return new WindowSpec(key, name + " - Bloc-notes", ImageLoader.Asset("Icons/bloc-notes.png"),
            () => new BlocNotesView { DataContext = new BlocNotesViewModel(desktop, key, fileId) }, 760, 560);
    }
}
