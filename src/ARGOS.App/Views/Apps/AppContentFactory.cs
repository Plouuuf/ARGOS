using Argos.Shared.Contracts;
using Argos.ViewModels;
using Argos.ViewModels.Ode;
using Argos.Views.Apps.Ode;
using Avalonia.Controls;

namespace Argos.Views.Apps;

/// <summary>
/// Crée le contenu d'une application (dans une fenêtre du bureau ou en plein écran sur téléphone).
/// Les applications pas encore développées affichent une page provisoire.
/// </summary>
public static class AppContentFactory
{
    public static Control Create(AppTileViewModel app, DesktopViewModel desktop) => app.Id switch
    {
        ArgosApps.AdagioOde => new OdeAppView { DataContext = new OdeAppViewModel(desktop) },
        _ => new PlaceholderAppView { DataContext = app }
    };
}
