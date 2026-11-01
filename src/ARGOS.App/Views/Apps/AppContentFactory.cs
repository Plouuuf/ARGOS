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
    public static Control Create(AppTileViewModel app, DesktopViewModel desktop)
        => desktop.IsCompactLayout && CreateMobile(app, desktop) is { } mobile ? mobile : CreateDesktop(app, desktop);

    /// <summary>Version iPhone dédiée (Views/Mobile), quand elle existe ; sinon la vue bureau est adaptée automatiquement.</summary>
    private static Control? CreateMobile(AppTileViewModel app, DesktopViewModel desktop) => app.Id switch
    {
        ArgosApps.AdagioOde => new Argos.Views.Mobile.Ode.OdeMobileView { DataContext = new OdeAppViewModel(desktop) },
        ArgosApps.Mco => new Argos.Views.Mobile.Mco.McoMobileView { DataContext = new McoAppViewModel(desktop) },
        ArgosApps.AdagioRemise => new Argos.Views.Mobile.Remise.RemiseMobileView { DataContext = new RemiseAppViewModel(desktop) },
        ArgosApps.Efibi => new Argos.Views.Mobile.Efibi.EfibiMobileView { DataContext = new EfibiAppViewModel(desktop) },
        ArgosApps.Ilias => new Argos.Views.Mobile.Ilias.IliasMobileView { DataContext = new IliasAppViewModel(desktop) },
        ArgosApps.Sigtao => new Argos.Views.Mobile.Sigtao.SigtaoMobileView { DataContext = new SigtaoAppViewModel(desktop) },
        ArgosApps.Mail => new Argos.Views.Mobile.Mail.MailMobileView { DataContext = new MailAppViewModel(desktop) },
        ArgosApps.Explorateur => new Argos.Views.Mobile.Accessoires.ExplorateurMobileView { DataContext = new ExplorateurViewModel(desktop) },
        ArgosApps.BlocNotes => new Argos.Views.Mobile.Accessoires.BlocNotesMobileView { DataContext = new BlocNotesViewModel(desktop, app.Id, null) },
        ArgosApps.Calculatrice => new Argos.Views.Mobile.Accessoires.CalculatriceMobileView { DataContext = new CalculatriceViewModel() },
        ArgosApps.Corbeille => new Argos.Views.Mobile.Accessoires.CorbeilleMobileView { DataContext = new CorbeilleViewModel() },
        ArgosApps.Imprimante => new Argos.Views.Mobile.Accessoires.ImprimanteMobileView { DataContext = new ImprimanteViewModel(desktop) },
        ArgosApps.Parametres => new Argos.Views.Mobile.Accessoires.ParametresMobileView { DataContext = new ParametresViewModel(desktop) },
        _ => null
    };

    private static Control CreateDesktop(AppTileViewModel app, DesktopViewModel desktop) => app.Id switch
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
