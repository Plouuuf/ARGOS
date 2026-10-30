using Argos.Services;
using Argos.ViewModels;
using Argos.ViewModels.Mco;

namespace Argos.Views.Apps.Mco;

/// <summary>Fenêtres de la MCO.</summary>
public static class McoWindows
{
    private static Avalonia.Media.Imaging.Bitmap Icon => ImageLoader.Asset("Icons/mco.png");

    /// <summary>MCO existante (<paramref name="reportId"/>) ou nouvelle MCO, éventuellement pré-remplie depuis une intervention.</summary>
    public static WindowSpec Editor(DesktopViewModel desktop, string? reportId, string? interventionId, string? engin)
    {
        var key = "mco:" + (reportId ?? (interventionId is null ? "nouvelle-" + Guid.NewGuid().ToString("N")[..6] : "inter-" + interventionId + ":" + engin));
        var title = reportId is null ? "MCO - Nouvelle main courante" : "MCO - Main courante";
        return new WindowSpec(key, title, Icon,
            () => new McoEditorView { DataContext = new McoEditorViewModel(desktop, reportId, interventionId, engin, key) },
            1100, 800);
    }
}
