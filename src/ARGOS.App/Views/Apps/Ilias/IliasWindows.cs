using Argos.Services;
using Argos.ViewModels;
using Argos.ViewModels.Ilias;

namespace Argos.Views.Apps.Ilias;

public static class IliasWindows
{
    private static Avalonia.Media.Imaging.Bitmap Icon => ImageLoader.Asset("Icons/ilias.png");

    /// <summary>Ouverture des documents depuis les fenêtres de formation (fournie par l'application ILIAS ouverte).</summary>
    public static Func<string, Task>? DocumentOpener { get; set; }

    public static WindowSpec Formation(DesktopViewModel desktop, string id)
        => new("ilias-formation:" + id, "ILIAS - Formation", Icon,
            () =>
            {
                var vm = new FormationViewModel(desktop, id, DocumentOpener);
                return desktop.IsCompactLayout
                    ? (Avalonia.Controls.Control)new Argos.Views.Mobile.Ilias.FormationMobileView { DataContext = vm }
                    : new FormationView { DataContext = vm };
            }, 900, 760);

    public static WindowSpec Quiz(DesktopViewModel desktop, string id)
    {
        var key = "ilias-quiz:" + id;
        return new WindowSpec(key, "ILIAS - Questionnaire", Icon,
            () =>
            {
                var vm = new QuizRunViewModel(desktop, id, key);
                return desktop.IsCompactLayout
                    ? (Avalonia.Controls.Control)new Argos.Views.Mobile.Ilias.QuizMobileView { DataContext = vm }
                    : new QuizView { DataContext = vm };
            }, 860, 780);
    }
}
