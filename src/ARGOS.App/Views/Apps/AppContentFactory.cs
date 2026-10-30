using Argos.Shared.Contracts;
using Argos.ViewModels;
using Argos.ViewModels.Ode;
using Argos.ViewModels.Efibi;
using Argos.ViewModels.Ilias;
using Argos.ViewModels.Mail;
using Argos.ViewModels.Accessoires;
using Argos.ViewModels.Mco;
using Argos.ViewModels.Remise;
using Argos.ViewModels.Sigtao;
using Argos.Views.Apps.Ode;
using Argos.Views.Apps.Efibi;
using Argos.Views.Apps.Ilias;
using Argos.Views.Apps.Mail;
using Argos.Views.Apps.Accessoires;
using Argos.Views.Apps.Mco;
using Argos.Views.Apps.Remise;
using Argos.Views.Apps.Sigtao;
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
        ArgosApps.Efibi => new EfibiAppView { DataContext = new EfibiAppViewModel(desktop) },
        ArgosApps.Ilias => new IliasAppView { DataContext = new IliasAppViewModel(desktop) },
        ArgosApps.Mail => new MailAppView { DataContext = new MailAppViewModel(desktop) },
        ArgosApps.Sigtao => new SigtaoAppView { DataContext = new SigtaoAppViewModel(desktop) },
        ArgosApps.Mco => new McoAppView { DataContext = new McoAppViewModel(desktop) },
        ArgosApps.AdagioRemise => new RemiseAppView { DataContext = new RemiseAppViewModel(desktop) },
        ArgosApps.Explorateur => new ExplorateurView { DataContext = new ExplorateurViewModel(desktop) },
        ArgosApps.BlocNotes => new BlocNotesView { DataContext = new BlocNotesViewModel(desktop, app.Id, null) },
        ArgosApps.Calculatrice => new CalculatriceView { DataContext = new CalculatriceViewModel() },
        ArgosApps.Corbeille => new CorbeilleView { DataContext = new CorbeilleViewModel() },
        ArgosApps.Imprimante => new ImprimanteView { DataContext = new ImprimanteViewModel(desktop) },
        ArgosApps.Parametres => new ParametresView { DataContext = new ParametresViewModel(desktop) },
        _ => new PlaceholderAppView { DataContext = app }
    };
}
