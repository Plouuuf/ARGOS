using Argos.Shared.Contracts;
using Argos.ViewModels;
using Argos.ViewModels.Ode;
using Argos.ViewModels.Mco;
using Argos.ViewModels.Remise;
using Argos.Views.Apps.Ode;
using Argos.Views.Apps.Mco;
using Argos.Views.Apps.Remise;
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
        ArgosApps.Mco => new McoAppView { DataContext = new McoAppViewModel(desktop) },
        ArgosApps.AdagioRemise => new RemiseAppView { DataContext = new RemiseAppViewModel(desktop) },
        _ => new PlaceholderAppView { DataContext = app }
    };
}
