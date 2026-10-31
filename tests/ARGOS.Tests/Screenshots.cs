using Argos.Shared.Contracts;
using Argos.ViewModels;
using Argos.ViewModels.Ode;
using Argos.Views;
using Argos.Views.Apps.Ode;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace Argos.Tests;

/// <summary>
/// Captures d'écran des principaux écrans (dossier ARGOS_SCREENSHOTS, publié par la compilation GitHub)
/// pour comparer visuellement avec les références (prise d'appel vLife, feuille ODE).
/// </summary>
public class Screenshots
{
    private static readonly string Folder = Environment.GetEnvironmentVariable("ARGOS_SCREENSHOTS")
                                            ?? Path.Combine(Path.GetTempPath(), "argos-screenshots");

    private static void Capture(Control content, double width, double height, string name)
    {
        Directory.CreateDirectory(Folder);
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame!.Save(Path.Combine(Folder, name + ".png"));
        window.Content = null;
        window.Close();
    }

    private static CallRecord SampleCall() => new()
    {
        Id = "20261029-0007",
        Status = CallStatuses.EnCours,
        ReceivedAt = DateTimeOffset.Now.AddSeconds(-17),
        OperatorName = "BERTIER",
        Poste = "ADAGIO",
        Origin = "Téléphone urbain",
        CallerPhone = "553-235-420",
        LocationMode = "NORMAL",
        LocationNetwork = "ROUTIER/SITES",
        StreetNumber = "12",
        Street = "Impasse Guiner",
        Cstc = "CRET",
        AlertAddress = "12 IMPASSE GUINER",
        Observations = "Chantier, accès par le portail",
        NatureCode = "117",
        Reason = "Feu de structure ** Une personne bloquée au 1er étage, fumées visibles depuis la rue."
    };

    [AvaloniaFact]
    public void Prise_d_appel()
    {
        var desktop = new DesktopViewModel(new ArgosUserDto("1", "Bertier", null, [AccessLevels.Sapeur]), null, () => { }, () => { });
        var vm = new PriseAppelViewModel(desktop, "20261029-0007");
        vm.ApplyCall(SampleCall());
        Capture(new PriseAppelView { DataContext = vm }, 1180, 769, "prise-appel");

        var empty = new PriseAppelViewModel(desktop, "20261029-0008");
        empty.ApplyCall(new CallRecord { Id = "20261029-0008", ReceivedAt = DateTimeOffset.Now, OperatorName = "BERTIER", Poste = "ADAGIO" });
        Capture(new PriseAppelView { DataContext = empty }, 1180, 769, "prise-appel-vide");
    }

    [AvaloniaFact]
    public void Feuille_ODE()
    {
        var intervention = new Intervention
        {
            Number = "210-593", NatureCode = "117", Cstc = "CRET", Address = "IMPASSE GUINER", Building = "Chantier",
            Locality = "L'Echat", CallerPhone = "553-235-420", CallerInfo = "Feu de structure ** Une personne bloqué"
        };
        var order = new DepartureOrder
        {
            Number = 1, Procedure = "BLANCHE", ValidatedAt = DateTimeOffset.Now, OperatorName = "BERTIER",
            Engins = ["EPAN 24 MALF", "FPTL 202 MALF"]
        };
        Capture(new OdeDocumentView { DataContext = OdeDocument.From(intervention, order) }, 820, 900, "feuille-ode");
    }

    [AvaloniaFact]
    public void Application_ODE_et_bureau()
    {
        var desktop = new DesktopViewModel(new ArgosUserDto("1", "Bertier", null, [AccessLevels.Sapeur]), null, () => { }, () => { });
        Capture(new OdeAppView { DataContext = new OdeAppViewModel(desktop) }, 980, 640, "adagio-ode");
        Capture(new CreateOdeView { DataContext = new CreateOdeViewModel(desktop, null, null, "ROUGE") }, 1020, 730, "creation-ode");

        desktop.OpenApp(desktop.Apps.First(a => a.Id == ArgosApps.AdagioOde));
        Capture(new DesktopView { DataContext = desktop }, 1440, 900, "bureau");

        // Windows 10 : recherche, vue des tâches, centre de notifications, menu Démarrer.
        desktop.OpenApp(desktop.Apps.First(a => a.Id == ArgosApps.Calculatrice));
        desktop.ShowNotification("Nouveau message", "Cne LEROY : Planning de garde - semaine 45");
        desktop.ShowNotification("Imprimante du centre", "« ODE 210-593 n°1 » imprimé.");
        desktop.IsSearchOpen = true;
        desktop.SearchText = "ca";
        Assert.Contains(desktop.SearchResults, r => r.Title == "Calculatrice");
        Capture(new DesktopView { DataContext = desktop }, 1440, 900, "windows-recherche");
        desktop.IsTaskViewOpen = true;
        Capture(new DesktopView { DataContext = desktop }, 1440, 900, "windows-vue-des-taches");
        desktop.IsActionCenterOpen = true;
        Assert.Equal(2, desktop.Notifications.Count);
        Capture(new DesktopView { DataContext = desktop }, 1440, 900, "windows-centre-notifications");
        desktop.IsActionCenterOpen = false;
        desktop.ToggleStartMenuCommand.Execute(null);
        desktop.StartSearch = "mail";
        Assert.Single(desktop.StartResults);
        Capture(new DesktopView { DataContext = desktop }, 1440, 900, "windows-menu-demarrer");

        var settings = new Argos.ViewModels.Accessoires.ParametresViewModel(desktop);
        Capture(new Argos.Views.Apps.Accessoires.ParametresView { DataContext = settings }, 960, 640, "windows-parametres-systeme");
        settings.NavigateCommand.Execute("personnalisation");
        Assert.True(settings.IsPersonnalisation);
        Capture(new Argos.Views.Apps.Accessoires.ParametresView { DataContext = settings }, 960, 640, "windows-parametres-fond");
    }

    [AvaloniaFact]
    public void Ecran_de_connexion()
    {
        var shell = new ShellView();
        var vm = (ShellViewModel)shell.DataContext!;
        if (vm.IsBootScreen)
        {
            Capture(shell, 1440, 900, "demarrage-uefi");
            vm.SkipBoot();
        }
        Assert.True(vm.IsLockScreen);
        Capture(shell, 1440, 900, "verrouillage");
    }
}

/// <summary>Captures d'ADAGIO REMISE avec le parc de départ (feuille GDM).</summary>
public class RemiseScreenshots
{
    private static readonly string Folder = Environment.GetEnvironmentVariable("ARGOS_SCREENSHOTS")
                                            ?? Path.Combine(Path.GetTempPath(), "argos-screenshots");

    private static void Capture(Control content, double width, double height, string name)
    {
        Directory.CreateDirectory(Folder);
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame!.Save(Path.Combine(Folder, name + ".png"));
        window.Content = null;
        window.Close();
    }

    private static RemiseSnapshot Sample()
    {
        var now = DateTimeOffset.Now;
        var engins = RemiseSeed.Engins(now);
        engins.Single(e => e.Nom == "FA 32 CRET").Availability = FleetAvailability.Maintenance;
        engins.Single(e => e.Nom == "CRAC 12 MASS").Availability = FleetAvailability.Indisponible;
        engins.Single(e => e.Nom == "VSAV 949 CRET").Dotation[2].Present = 1;
        engins.Single(e => e.Nom == "PSE 212 CRET").NextRevision = now.AddDays(-2);
        var items = RemiseSeed.Items(now);
        items.Single(i => i.Id == "o2-5l").Stock = 1;
        return new RemiseSnapshot
        {
            Engins = engins,
            Items = items,
            CanManage = true,
            Maintenances =
            [
                new MaintenanceRecord
                {
                    Id = "m1", Number = "MNT-0003", Engin = "FA 32 CRET", ReportedAt = now.AddHours(-5), ReportedBy = "Durand",
                    Description = "Fuite hydraulique sur la pompe", Immobilisation = Immobilisations.Totale,
                    Repairs = [new RepairEntry { At = now.AddHours(-1), By = "Durand", Text = "Joint commandé" }]
                },
                new MaintenanceRecord
                {
                    Id = "m2", Number = "MNT-0002", Engin = "VSAV 949 CRET", ReportedAt = now.AddDays(-1), ReportedBy = "Martin",
                    Description = "Vérification : Bouteille d'oxygène 5 L : 1/2", Source = "Vérification"
                }
            ],
            Verifications =
            [
                new Verification
                {
                    Id = "v1", Engin = "VSAV 949 CRET", At = now.AddDays(-1), By = "Martin",
                    Lines = [new VerificationLine { Name = "Bouteille d'oxygène 5 L", Required = 2, Counted = 1, Result = CheckResults.Present }]
                }
            ],
            Movements =
            [
                new StockMovement { Id = "s1", At = now.AddHours(-3), By = "Durand", ItemName = "Poste radio portatif ANTARES",
                    Kind = StockKinds.Entree, Quantity = 4, StockAfter = 12, Comment = "Livraison GIS 2" }
            ]
        };
    }

    [AvaloniaFact]
    public void Application_REMISE()
    {
        var user = new ArgosUserDto("1", "Durand", null, [AccessLevels.Sapeur, AccessLevels.Operateur]);
        var previous = Argos.Services.ArgosSession.Current;
        var session = new Argos.Services.ArgosSession(user, "test");
        Argos.Services.ArgosSession.Current = session;
        try
        {
            session.Remise.Apply(Sample());
            var desktop = new DesktopViewModel(user, null, () => { }, () => { });
            var vm = new Argos.ViewModels.Remise.RemiseAppViewModel(desktop);
            Capture(new Argos.Views.Apps.Remise.RemiseAppView { DataContext = vm }, 1400, 900, "remise-tableau-de-bord");
            vm.CurrentPage = Argos.ViewModels.Remise.RemiseAppViewModel.Page.Parc;
            Capture(new Argos.Views.Apps.Remise.RemiseAppView { DataContext = vm }, 1400, 800, "remise-situation-engins");
            Assert.Equal(16, vm.EnginRows.Count);

            var ode = new OdeAppViewModel(desktop) { CurrentPage = OdeAppViewModel.Page.Engins };
            Assert.NotNull(ode.Parc);
            Assert.Equal(16, ode.Parc!.Tiles.Count);
            Capture(new OdeAppView { DataContext = ode }, 1400, 1000, "ode-parc-gdm");
            vm.CurrentPage = Argos.ViewModels.Remise.RemiseAppViewModel.Page.Inventaire;
            Capture(new Argos.Views.Apps.Remise.RemiseAppView { DataContext = vm }, 1400, 800, "remise-inventaire");
            vm.VerifEngin = "VSAV 949 CRET";
            vm.CurrentPage = Argos.ViewModels.Remise.RemiseAppViewModel.Page.Verification;
            Capture(new Argos.Views.Apps.Remise.RemiseAppView { DataContext = vm }, 1400, 900, "remise-verification");
            vm.CurrentPage = Argos.ViewModels.Remise.RemiseAppViewModel.Page.Maintenance;
            vm.OpenAnomalies[0].OpenCommand.Execute(null);
            Capture(new Argos.Views.Apps.Remise.RemiseAppView { DataContext = vm }, 1400, 800, "remise-maintenance");
            vm.CurrentPage = Argos.ViewModels.Remise.RemiseAppViewModel.Page.Stocks;
            Capture(new Argos.Views.Apps.Remise.RemiseAppView { DataContext = vm }, 1400, 800, "remise-stocks");
            Assert.Equal(16, vm.TotalEngins);
            Assert.Equal(1, vm.EnMaintenance);

            var fiche = new Argos.ViewModels.Remise.FicheEnginViewModel(desktop, "VSAV 949 CRET", "test");
            Capture(new Argos.Views.Apps.Remise.FicheEnginView { DataContext = fiche }, 900, 1300, "remise-fiche-engin");
            Assert.Equal("VSAV 949", fiche.Indicatif);
            Assert.True(fiche.Dotation.Count > 10);
        }
        finally
        {
            Argos.Services.ArgosSession.Current = previous;
        }
    }
}

/// <summary>Captures de la MCO (tableau de bord et rédaction pré-remplie).</summary>
public class McoScreenshots
{
    private static readonly string Folder = Environment.GetEnvironmentVariable("ARGOS_SCREENSHOTS")
                                            ?? Path.Combine(Path.GetTempPath(), "argos-screenshots");

    private static void Capture(Control content, double width, double height, string name)
    {
        Directory.CreateDirectory(Folder);
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame!.Save(Path.Combine(Folder, name + ".png"));
        window.Content = null;
        window.Close();
    }

    [AvaloniaFact]
    public void Application_MCO()
    {
        var user = new ArgosUserDto("1", "Santos", null, [AccessLevels.Sapeur, AccessLevels.Operateur]);
        var previous = Argos.Services.ArgosSession.Current;
        Argos.Services.ArgosSession.Current = new Argos.Services.ArgosSession(user, "test");
        try
        {
            var now = DateTimeOffset.Now;
            var report = new McoReport
            {
                Id = "r1", Number = "MCO-2026-0001", InterventionNumber = "210-593", Date = now, NatureCode = "117",
                Address = "12 AVENUE BOSQUET", Locality = "L'ÉCHAT", Engin = "VSAV 947 CRET",
                TimeDepart = "14:02", TimeArrivee = "14:09", TimeDisponible = "14:51",
                ChefAgres = new McoPerson { Grade = "SGT", Nom = "Santos", Prenom = "Diego", Fonction = "Chef d'agrès" },
                Crew = [new McoPerson { Grade = "CPL", Nom = "Martin", Prenom = "Paul", Fonction = "Conducteur" },
                        new McoPerson { Grade = "SAP 1CL", Nom = "Durand", Prenom = "Léa", Fonction = "Équipier" }],
                Events = [new McoEvent { Time = "14:15", Text = "Victime extraite par l'escalier" }],
                SituationArrivee = "Fumées au 1er étage, une personne à la fenêtre.",
                Actions = "Reconnaissance, extraction de la victime, ventilation.",
                VictimCount = 1, AuthorName = "Santos", CreatedAt = now, UpdatedAt = now, UpdatedBy = "Santos",
                History = [new McoHistoryEntry { At = now, By = "Santos", Action = "Création", Details = "Brouillon créé." }]
            };
            report.Synthese = McoPrefill.Synthese(report);
            Argos.Services.ArgosSession.Current.Mco.Apply(new McoSnapshot
            {
                Reports = [report, new McoReport { Id = "r2", Number = "MCO-2026-0002", InterventionNumber = "418-207",
                    Date = now.AddDays(-1), NatureCode = "112", Address = "RUE VIOLET", Engin = "PSE 212 CRET",
                    AuthorName = "Martin", Status = McoStatuses.Validee }]
            });
            var desktop = new DesktopViewModel(user, null, () => { }, () => { });
            Capture(new Argos.Views.Apps.Mco.McoAppView { DataContext = new Argos.ViewModels.Mco.McoAppViewModel(desktop) }, 1300, 800, "mco-tableau-de-bord");

            var editor = new Argos.ViewModels.Mco.McoEditorViewModel(desktop, "r1", null, null, "test");
            Assert.Equal("VSAV 947 CRET", editor.Report.Engin);
            Assert.Equal(2, editor.Crew.Count);
            Capture(new Argos.Views.Apps.Mco.McoEditorView { DataContext = editor }, 1100, 800, "mco-identification");
            editor = new Argos.ViewModels.Mco.McoEditorViewModel(desktop, "r1", null, null, "test2") { Tab = 2 };
            Capture(new Argos.Views.Apps.Mco.McoEditorView { DataContext = editor }, 1100, 800, "mco-personnel");
            editor = new Argos.ViewModels.Mco.McoEditorViewModel(desktop, "r1", null, null, "test3") { Tab = 5 };
            Capture(new Argos.Views.Apps.Mco.McoEditorView { DataContext = editor }, 1100, 800, "mco-synthese");
            editor = new Argos.ViewModels.Mco.McoEditorViewModel(desktop, "r1", null, null, "test4") { Tab = 6 };
            Assert.Empty(editor.Checks);
            Capture(new Argos.Views.Apps.Mco.McoEditorView { DataContext = editor }, 1100, 800, "mco-validation");
        }
        finally
        {
            Argos.Services.ArgosSession.Current = previous;
        }
    }
}

/// <summary>Captures de SIGTAO (tableau de bord, compagnie, comptes, annuaire, dossier, compte-rendu).</summary>
public class SigtaoScreenshots
{
    private static readonly string Folder = Environment.GetEnvironmentVariable("ARGOS_SCREENSHOTS")
                                            ?? Path.Combine(Path.GetTempPath(), "argos-screenshots");

    private static void Capture(Control content, double width, double height, string name)
    {
        Directory.CreateDirectory(Folder);
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame!.Save(Path.Combine(Folder, name + ".png"));
        window.Content = null;
        window.Close();
    }

    [AvaloniaFact]
    public void Application_SIGTAO()
    {
        var user = new ArgosUserDto("1", "Santos", null, [AccessLevels.Sapeur, AccessLevels.Operateur]);
        var previous = Argos.Services.ArgosSession.Current;
        Argos.Services.ArgosSession.Current = new Argos.Services.ArgosSession(user, "test");
        try
        {
            var now = DateTimeOffset.Now;
            var me = new MilitaryRecord
            {
                Id = "m1", Nom = "Santos", Prenom = "Diego", Grade = "Sergent", Nid = "1234567", Unite = "17e Cie", Affectation = "CRET",
                Section = "2e section", Fonction = "Chef d'agrès VSAV", ArrivalDate = now.AddYears(-4).AddMonths(-3), DiscordUserId = "1",
                Email = "diego.santos@pompiersparis.fr", Telephone = "01 45 17 00 00",
                Entries =
                [
                    new DossierEntry { Kind = DossierKinds.Promotion, Title = "Sergent", Date = now.AddYears(-1), By = "Leroy" },
                    new DossierEntry { Kind = DossierKinds.Formation, Title = "Chef d'agrès VSAV", Details = "Validée au CFIS", Date = now.AddMonths(-8), By = "Leroy" }
                ]
            };
            var cr = new CompteRendu
            {
                Id = "c1", NumDoc = "2026-0007", Date = now, AuthorGrade = "Sergent", AuthorNom = "SANTOS", AuthorPrenom = "Diego",
                AuthorNid = "1234567", DestGrade = "Capitaine", DestPrenom = "Marc", DestNom = "LEROY", Objet = "Lance endommagée en intervention",
                Reference = "Intervention 210-593",
                Body = "Le 30/10/2026, lors de l'intervention **210-593**, une *lance à débit variable* a été endommagée.\n- Lance remise au magasin\n- Demande de remplacement __urgente__",
                AuthorUserId = "1", CreatedAt = now
            };
            cr.Synthese = CrText.Synthese(cr);
            Argos.Services.ArgosSession.Current.Sigtao.Apply(new SigtaoSnapshot
            {
                Military = [me,
                    new MilitaryRecord { Id = "m2", Nom = "Martin", Prenom = "Paul", Grade = "Caporal", Nid = "2345678", Fonction = "Conducteur VSAV" },
                    new MilitaryRecord { Id = "m3", Nom = "Leroy", Prenom = "Marc", Grade = "Capitaine", Nid = "3456789", Fonction = "Commandant d'unité" }],
                Accounts = [new UserAccount { Id = "a1", DiscordUserId = "1", DossierId = "m1", Grade = "Sergent", Nom = "Santos", Prenom = "Diego",
                    Nid = "1234567", Habilitations = [Habilitations.Sadmin, Habilitations.Sremise], HasPassword = true }],
                Reports = [cr],
                Gardes =
                [
                    new FeuilleGarde
                    {
                        Id = FeuilleGarde.BuildId(DateTime.Today.ToString("yyyy-MM-dd"), FeuilleGarde.TousCentres), Date = DateTime.Today.ToString("yyyy-MM-dd"), Centre = FeuilleGarde.TousCentres,
                        Statut = GardeStatuts.Validee, ValidatedBy = "Leroy", ValidatedAt = now, UpdatedBy = "Leroy", UpdatedAt = now,
                        Service = [new GardeAffectation { Poste = GardePostes.ChefDeGarde, MilitaryId = "m3", Nom = "CNE LEROY Marc" }],
                        Consignes = [new GardeConsigne { Titre = "Sport", Texte = "Footing 10h00" }, new GardeConsigne { Titre = "Manœuvre", Texte = "ARI 14h00" }],
                        Engins =
                        [
                            new GardeEngin { Engin = "VSAV 947 CRET", Type = "VSAV", Postes =
                            [
                                new GardeAffectation { Poste = "C/A", MilitaryId = "m1", Nom = "SGT SANTOS Diego" },
                                new GardeAffectation { Poste = "COND", MilitaryId = "m2", Nom = "CPL MARTIN Paul" },
                                new GardeAffectation { Poste = "EQP" },
                                new GardeAffectation { Poste = "EQP2" }
                            ] },
                            new GardeEngin { Engin = "PSE 212 CRET", Type = "PSE", Arme = false }
                        ],
                        Observations = "Exercice ARI à 14h00."
                    }
                ],
                Rights = new SigtaoRights { ManageAccounts = true, ManageCompany = true, ReadAllReports = true, ManageGarde = true, Habilitations = ["SADMIN", "SREMISE"] },
                MyDossierId = "m1", MyAccountId = "a1"
            });
            var desktop = new DesktopViewModel(user, null, () => { }, () => { });
            var vm = new Argos.ViewModels.Sigtao.SigtaoAppViewModel(desktop);
            Capture(new Argos.Views.Apps.Sigtao.SigtaoAppView { DataContext = vm }, 1300, 820, "sigtao-tableau-de-bord");
            vm.CurrentPage = Argos.ViewModels.Sigtao.SigtaoAppViewModel.Page.Compagnie;
            Capture(new Argos.Views.Apps.Sigtao.SigtaoAppView { DataContext = vm }, 1300, 700, "sigtao-compagnie");
            vm.CurrentPage = Argos.ViewModels.Sigtao.SigtaoAppViewModel.Page.Comptes;
            vm.Accounts[0].EditCommand.Execute(null);
            Capture(new Argos.Views.Apps.Sigtao.SigtaoAppView { DataContext = vm }, 1300, 760, "sigtao-comptes");
            vm.CurrentPage = Argos.ViewModels.Sigtao.SigtaoAppViewModel.Page.Annuaire;
            Capture(new Argos.Views.Apps.Sigtao.SigtaoAppView { DataContext = vm }, 1300, 700, "sigtao-annuaire");
            Assert.Equal(3, vm.Directory.Count);
            vm.CurrentPage = Argos.ViewModels.Sigtao.SigtaoAppViewModel.Page.Garde;
            Assert.Contains(vm.GardeEngins, e => e.Engin == "VSAV 947 CRET" && e.Pourvus == 2);
            Assert.Contains(vm.GardeEngins, e => e.Engin == "PSE 212 CRET" && !e.Arme);
            Capture(new Argos.Views.Apps.Sigtao.SigtaoAppView { DataContext = vm }, 1300, 900, "sigtao-feuille-de-garde");
            vm.CurrentPage = Argos.ViewModels.Sigtao.SigtaoAppViewModel.Page.Planning;
            Assert.Contains(vm.PlanningRows, r => r.Total == 1);
            Capture(new Argos.Views.Apps.Sigtao.SigtaoAppView { DataContext = vm }, 1300, 600, "sigtao-planning-semaine");
            vm.PlanningMonthCommand.Execute(null);
            Capture(new Argos.Views.Apps.Sigtao.SigtaoAppView { DataContext = vm }, 1300, 600, "sigtao-planning-mois");
            var feuille = Argos.Services.ArgosSession.Current.Sigtao.Snapshot.Gardes[0];
            Capture(new Argos.Views.Apps.Sigtao.GardePrintView { DataContext = new Argos.ViewModels.Sigtao.GardePrintViewModel(desktop, "p", feuille) },
                900, 1250, "sigtao-feuille-de-garde-impression");

            Capture(new Argos.Views.Apps.Sigtao.DossierView { DataContext = new Argos.ViewModels.Sigtao.DossierViewModel(desktop, "m1", "d") },
                980, 900, "sigtao-dossier");
            Capture(new Argos.Views.Apps.Sigtao.CompteRenduView { DataContext = new Argos.ViewModels.Sigtao.CompteRenduViewModel(desktop, "c1", "r") },
                820, 1150, "sigtao-compte-rendu");
            var draft = new Argos.ViewModels.Sigtao.CompteRenduViewModel(desktop, null, "n") { Body = cr.Body };
            Assert.Equal("SANTOS", draft.Report.AuthorNom.ToUpperInvariant());
            Capture(new Argos.Views.Apps.Sigtao.CompteRenduView { DataContext = draft }, 1240, 900, "sigtao-redaction");
        }
        finally
        {
            Argos.Services.ArgosSession.Current = previous;
        }
    }
}

/// <summary>Capture d'une fiche eFiBi (données fictives, sans base).</summary>
public class EfibiScreenshots
{
    [AvaloniaFact]
    public void Fiche_eFiBi()
    {
        var folder = Environment.GetEnvironmentVariable("ARGOS_SCREENSHOTS") ?? Path.Combine(Path.GetTempPath(), "argos-screenshots");
        Directory.CreateDirectory(folder);
        var desktop = new DesktopViewModel(new ArgosUserDto("1", "Santos", null, [AccessLevels.Sapeur]), null, () => { }, () => { });
        var record = new EfibiRecord
        {
            Summary = new EfibiSummary { Id = 42, ShareCode = "K7P2QX", InterventionNumber = "210-593", VictimName = "Jean TEST",
                TransmissionStatus = "Transmis", Author = "ABC12345", CreatedAt = DateTimeOffset.Now.AddHours(-2),
                UpdatedAt = DateTimeOffset.Now, StartedAt = DateTimeOffset.Now.AddHours(-2), TransmittedAt = DateTimeOffset.Now.AddHours(-1) }
        };
        EfibiPayload.Parse("""
            {"sections":{
              "identityForm":{"title":"IDENTITÉ","controls":[{"index":0,"label":"Nom","type":"text","value":"TEST"},{"index":1,"label":"Prénom","type":"text","value":"Jean"},{"index":2,"label":"Âge","type":"number","value":"42"},{"index":3,"label":"Téléphone","type":"text","value":""}],"lists":{"allergieList":[{"text":"Pénicilline"}],"traitementList":[]},"choices":[]},
              "circulatoryForm":{"title":"BILAN CIRCULATOIRE","controls":[{"index":0,"label":"Pouls","type":"number","value":"96"},{"index":1,"label":"Tension","type":"text","value":"13/8"},{"index":2,"label":"Saturation","type":"number","value":"97"}],"lists":[],"choices":[]},
              "regulationForm":{"title":"CONTACT RÉGULATION","controls":[{"index":0,"label":"Médecin régulateur","type":"text","value":"Dr Martin"},{"index":1,"label":"Destination","type":"text","value":"CHU Henri Mondor"}],"lists":{"regTimeline":[]},"choices":[]}
            }}
            """, record);
        var vm = new Argos.ViewModels.Efibi.EfibiRecordViewModel(desktop, 42, "t");
        vm.Apply(record);
        var window = new Window { Width = 980, Height = 900, Content = new Argos.Views.Apps.Efibi.EfibiRecordView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(Path.Combine(folder, "efibi-fiche.png"));
        window.Content = null;
        window.Close();
        Assert.Equal(3, vm.Sections.Count);
    }
}

/// <summary>Captures ILIAS (contenu fictif).</summary>
public class IliasScreenshots
{
    private static readonly string Folder = Environment.GetEnvironmentVariable("ARGOS_SCREENSHOTS")
                                            ?? Path.Combine(Path.GetTempPath(), "argos-screenshots");

    private static void Capture(Control content, double width, double height, string name)
    {
        Directory.CreateDirectory(Folder);
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(Path.Combine(Folder, name + ".png"));
        window.Content = null;
        window.Close();
    }

    [AvaloniaFact]
    public void Application_ILIAS()
    {
        var user = new ArgosUserDto("1", "Santos", null, [AccessLevels.Sapeur, AccessLevels.RhAdmin]);
        var previous = Argos.Services.ArgosSession.Current;
        Argos.Services.ArgosSession.Current = new Argos.Services.ArgosSession(user, "test");
        try
        {
            var now = DateTimeOffset.Now;
            var quiz = new IliasQuiz
            {
                Id = "q1", Title = "Prise d'appel 18 - procédures ADAGIO", Description = "Évaluation finale.", TimeLimitMinutes = 10, PassPercent = 75,
                Questions =
                [
                    new() { Text = "Quelle touche classe un appel pour « INU » ?", Options = ["F1", "F5", "F9"], Correct = 1 },
                    new() { Text = "Quelle procédure correspond à F3 ?", Options = ["Blanche", "Orange", "Rouge"], Correct = 2 }
                ]
            };
            Argos.Services.ArgosSession.Current.Ilias.Apply(new IliasSnapshot
            {
                CanAdmin = true,
                ServerTime = now,
                Categories =
                [
                    new() { Id = "reg", Name = "Documents réglementaires", Description = "Notes de service et règlements." },
                    new() { Id = "ope", Name = "Opérationnel", Description = "Doctrines et fiches réflexes.", Order = 1 },
                    new() { Id = "suap", ParentId = "ope", Name = "Secours à victimes", Description = "Référentiels SUAP.", Order = 2 }
                ],
                Documents =
                [
                    new() { Id = "d1", CategoryId = "suap", Title = "Référentiel bilan SUAP", Description = "Conduite du bilan d'une victime.",
                        FileName = "bilan.pdf", Size = 812_000, Version = 3, PublishedAt = now.AddMonths(-3), UpdatedAt = now.AddDays(-2) },
                    new() { Id = "d2", CategoryId = "reg", Title = "Règlement intérieur de la compagnie", FileName = "ri.pdf", Size = 240_000,
                        PublishedAt = now.AddMonths(-6), UpdatedAt = now.AddMonths(-1) }
                ],
                Formations =
                [
                    new() { Id = "f1", Title = "Prise d'appel au CTA", Description = "Traiter un appel 18 avec ADAGIO.", Objectives = "Traiter un appel de bout en bout.",
                        Prerequisites = "Accès ADAGIO ODE.", QuizId = "q1",
                        Modules = [new() { Id = "m1", Title = "La prise d'appel", Content = "Décrocher, se présenter, recueillir le numéro." },
                                   new() { Id = "m2", Title = "Localiser l'événement", Content = "Saisir la voie, le numéro, confirmer.", DocumentId = "d1" }] }
                ],
                Quizzes = [quiz],
                MyProgress = [new() { Id = "1:f1", UserId = "1", FormationId = "f1", CompletedModules = ["m1"], StartedAt = now.AddDays(-1), UpdatedAt = now }],
                MyResults = [new() { Id = "r1", UserId = "1", QuizId = "q1", QuizTitle = quiz.Title, Score = 2, Total = 2, Passed = true, At = now.AddDays(-1) }],
                RecentDocuments = ["d1"]
            });
            var desktop = new DesktopViewModel(user, null, () => { }, () => { });
            var vm = new Argos.ViewModels.Ilias.IliasAppViewModel(desktop);
            Capture(new Argos.Views.Apps.Ilias.IliasAppView { DataContext = vm }, 1280, 800, "ilias-accueil");
            vm.CurrentPage = Argos.ViewModels.Ilias.IliasAppViewModel.Page.Bibliotheque;
            Assert.Equal(2, vm.Documents.Count);
            Capture(new Argos.Views.Apps.Ilias.IliasAppView { DataContext = vm }, 1280, 700, "ilias-bibliotheque");
            vm.CurrentPage = Argos.ViewModels.Ilias.IliasAppViewModel.Page.Suivi;
            Capture(new Argos.Views.Apps.Ilias.IliasAppView { DataContext = vm }, 1280, 700, "ilias-suivi");
            vm.CurrentPage = Argos.ViewModels.Ilias.IliasAppViewModel.Page.Administration;
            Capture(new Argos.Views.Apps.Ilias.IliasAppView { DataContext = vm }, 1280, 1500, "ilias-administration");
            Capture(new Argos.Views.Apps.Ilias.FormationView { DataContext = new Argos.ViewModels.Ilias.FormationViewModel(desktop, "f1", null) },
                900, 760, "ilias-formation");
            Capture(new Argos.Views.Apps.Ilias.QuizView { DataContext = new Argos.ViewModels.Ilias.QuizRunViewModel(desktop, "q1", "k") },
                860, 640, "ilias-questionnaire");
        }
        finally
        {
            Argos.Services.ArgosSession.Current = previous;
        }
    }
}

/// <summary>Captures de la messagerie (messages fictifs).</summary>
public class MailScreenshots
{
    private static readonly string Folder = Environment.GetEnvironmentVariable("ARGOS_SCREENSHOTS")
                                            ?? Path.Combine(Path.GetTempPath(), "argos-screenshots");

    private static void Capture(Control content, double width, double height, string name)
    {
        Directory.CreateDirectory(Folder);
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(Path.Combine(Folder, name + ".png"));
        window.Content = null;
        window.Close();
    }

    [AvaloniaFact]
    public void Application_Messagerie()
    {
        var user = new ArgosUserDto("1", "Santos", null, [AccessLevels.Sapeur]);
        var previous = Argos.Services.ArgosSession.Current;
        Argos.Services.ArgosSession.Current = new Argos.Services.ArgosSession(user, "test");
        try
        {
            var now = DateTimeOffset.Now;
            var me = "diego.santos@pompiersparis.fr";
            Argos.Services.ArgosSession.Current.Mail.Apply(new MailSnapshot
            {
                Address = me, Name = "Sgt SANTOS Diego", Signature = "Sgt SANTOS Diego\nChef d'agrès VSAV - CS Créteil",
                Items =
                [
                    new() { Id = "i1", OwnerId = "1", Folder = MailFolders.Reception, From = "marc.leroy@pompiersparis.fr", FromName = "Cne LEROY Marc",
                        To = [me, "paul.martin@pompiersparis.fr"], Cc = ["chef.cs@pompiersparis.fr"], Subject = "Planning de garde - semaine 45",
                        Body = "Bonjour à tous,\n\nVous trouverez ci-joint le **planning de garde** de la semaine 45.\n- Relève à *7h30*\n- Rassemblement __obligatoire__ à 8h\n\nCordialement,\nCne LEROY",
                        Importance = MailImportance.Haute, SentAt = now.AddMinutes(-25), UpdatedAt = now,
                        Attachments = [new() { Id = "a1", Name = "planning-s45.pdf", Size = 182_000 }] },
                    new() { Id = "i2", OwnerId = "1", Folder = MailFolders.Reception, From = "paul.martin@pompiersparis.fr", FromName = "Cpl MARTIN Paul",
                        To = [me], Subject = "Échange de garde", Body = "Salut, tu serais d'accord pour échanger samedi contre dimanche ?",
                        Read = true, Flagged = true, SentAt = now.AddDays(-1), UpdatedAt = now },
                    new() { Id = "i3", OwnerId = "1", Folder = MailFolders.Envoyes, From = me, FromName = "Sgt SANTOS Diego",
                        To = ["marc.leroy@pompiersparis.fr"], Subject = "CR intervention 210-593", Body = "Mon capitaine,\nci-joint le compte-rendu.", Read = true,
                        SentAt = now.AddDays(-2), UpdatedAt = now },
                    new() { Id = "i4", OwnerId = "1", Folder = MailFolders.Brouillons, From = me, To = [], Subject = "Demande de stage", Body = "Brouillon…",
                        Read = true, UpdatedAt = now }
                ],
                Contacts = [new() { Id = "c1", OwnerId = "1", Name = "Secrétariat CS Créteil", Address = "secretariat.cret@pompiersparis.fr" }],
                Directory =
                [
                    new() { Address = "marc.leroy@pompiersparis.fr", Name = "Cne LEROY Marc", Fonction = "Commandant d'unité", Unite = "17e Cie" },
                    new() { Address = "paul.martin@pompiersparis.fr", Name = "Cpl MARTIN Paul", Fonction = "Conducteur VSAV", Unite = "17e Cie" },
                    new() { Address = me, Name = "Sgt SANTOS Diego", Fonction = "Chef d'agrès VSAV", Unite = "17e Cie" }
                ]
            });
            var desktop = new DesktopViewModel(user, null, () => { }, () => { });
            var vm = new Argos.ViewModels.Mail.MailAppViewModel(desktop);
            Assert.Equal(2, vm.Messages.Count);
            Assert.Equal(1, vm.Folders.First(f => f.Id == MailFolders.Reception).Unread);
            vm.Select("i1");
            Assert.True(vm.HasAttachments);
            Capture(new Argos.Views.Apps.Mail.MailAppView { DataContext = vm }, 1300, 760, "mail-reception");
            vm.ShowContacts = true;
            Capture(new Argos.Views.Apps.Mail.MailAppView { DataContext = vm }, 1300, 640, "mail-contacts");

            var compose = new Argos.ViewModels.Mail.MailComposeViewModel(desktop, "k", new Argos.ViewModels.Mail.MailComposeInit
            {
                To = "marc.leroy@pompiersparis.fr", Subject = "RE: Planning de garde - semaine 45",
                Body = "Bien reçu mon capitaine.\n\nSgt SANTOS Diego" + MailCompose.Quote(Argos.Services.ArgosSession.Current.Mail.Find("i1")!),
                Attachments = [new() { Id = "a1", Name = "planning-s45.pdf", Size = 182_000 }]
            }, autoSave: false);
            Assert.Single(compose.Request().KeepAttachments);
            Capture(new Argos.Views.Apps.Mail.MailComposeView { DataContext = compose }, 900, 720, "mail-redaction");
        }
        finally
        {
            Argos.Services.ArgosSession.Current = previous;
        }
    }
}

/// <summary>Accessoires Windows : Calculatrice (calculs), Explorateur, Bloc-notes, Corbeille (captures).</summary>
public class AccessoiresTests
{
    private static readonly string Folder = Environment.GetEnvironmentVariable("ARGOS_SCREENSHOTS")
                                            ?? Path.Combine(Path.GetTempPath(), "argos-screenshots");

    private static void Capture(Control content, double width, double height, string name)
    {
        Directory.CreateDirectory(Folder);
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(Path.Combine(Folder, name + ".png"));
        window.Content = null;
        window.Close();
    }

    private static string Run(string keys)
    {
        var calc = new Argos.ViewModels.Accessoires.CalculatriceViewModel();
        foreach (var k in keys.Split(' ')) calc.Key(k);
        return calc.Display;
    }

    [Fact]
    public void Calculatrice_comme_Windows()
    {
        Assert.Equal("5", Run("2 + 3 ="));
        Assert.Equal("20", Run("2 + 3 * 4 ="));                // de gauche à droite, comme la calculatrice standard
        Assert.Equal("15", Run("5 + = ="));                      // « 5 + = » donne 10, « = » répété ajoute 5
        Assert.Equal("1 234 567", Run("1 2 3 4 5 6 7").Replace(' ', ' ').Replace(' ', ' '));
        Assert.Equal("0,5", Run("1 / 2 ="));
        Assert.Equal("Division par zéro impossible", Run("8 / 0 ="));
        Assert.Equal("10", Run("2 0 0 + 5 %"));
        var calc = new Argos.ViewModels.Accessoires.CalculatriceViewModel();
        calc.Key("9"); calc.Function("sqrt");
        Assert.Equal("3", calc.Display);
        calc.Memory("MS"); calc.Key("Escape"); calc.Memory("MR");
        Assert.Equal("3", calc.Display);
    }

    [Fact]
    public void BlocNotes_decode_les_anciens_fichiers()
    {
        Assert.Equal("é", Argos.ViewModels.Accessoires.BlocNotesViewModel.Decode([0xE9]));          // Windows-1252 / Latin-1
        Assert.Equal("é", Argos.ViewModels.Accessoires.BlocNotesViewModel.Decode([0xEF, 0xBB, 0xBF, 0xC3, 0xA9]));
    }

    [AvaloniaFact]
    public void Captures_accessoires()
    {
        var user = new ArgosUserDto("1", "Santos", null, [AccessLevels.Sapeur]);
        var previous = Argos.Services.ArgosSession.Current;
        Argos.Services.ArgosSession.Current = new Argos.Services.ArgosSession(user, "test");
        try
        {
            var now = DateTimeOffset.Now;
            Argos.Services.ArgosSession.Current.Files.Apply(new FilesSnapshot
            {
                Used = 3_400_000, Quota = 200L * 1024 * 1024,
                Entries =
                [
                    new() { Id = "f1", OwnerId = "1", Name = "Gardes", IsFolder = true, UpdatedAt = now.AddDays(-3) },
                    new() { Id = "f2", OwnerId = "1", Name = "Formations", IsFolder = true, UpdatedAt = now.AddDays(-9) },
                    new() { Id = "f3", OwnerId = "1", ParentId = "f1", Name = "Novembre", IsFolder = true, UpdatedAt = now },
                    new() { Id = "d1", OwnerId = "1", Name = "Notes de garde.txt", Size = 2_300, UpdatedAt = now.AddHours(-2) },
                    new() { Id = "d2", OwnerId = "1", Name = "Planning S45.pdf", Size = 182_000, UpdatedAt = now.AddDays(-1) },
                    new() { Id = "d3", OwnerId = "1", Name = "Inventaire VSAV.csv", Size = 8_400, UpdatedAt = now.AddDays(-5) },
                    new() { Id = "t1", OwnerId = "1", Name = "Brouillon CR.txt", Size = 900, Deleted = true, DeletedAt = now.AddMinutes(-40), UpdatedAt = now }
                ]
            });
            var desktop = new DesktopViewModel(user, null, () => { }, () => { });
            var explorer = new Argos.ViewModels.Accessoires.ExplorateurViewModel(desktop);
            Assert.Equal(5, explorer.Items.Count);
            explorer.Select("d1");
            Capture(new Argos.Views.Apps.Accessoires.ExplorateurView { DataContext = explorer }, 940, 600, "accessoire-explorateur");

            var notes = new Argos.ViewModels.Accessoires.BlocNotesViewModel(desktop, "n", null)
            {
                Text = "Garde du 30/10\r\n- Relève 7h30\r\n- VSAV 1 : contrôle des bouteilles O2\r\n- Vérifier le stock de pansements"
            };
            Assert.StartsWith("*", notes.Title);
            Capture(new Argos.Views.Apps.Accessoires.BlocNotesView { DataContext = notes }, 760, 520, "accessoire-bloc-notes");

            var calc = new Argos.ViewModels.Accessoires.CalculatriceViewModel();
            foreach (var k in "1 2 5 * 4 =".Split(' ')) calc.Key(k);
            Capture(new Argos.Views.Apps.Accessoires.CalculatriceView { DataContext = calc }, 340, 540, "accessoire-calculatrice");

            Argos.Services.ArgosSession.Current.Printer.Apply(new PrinterSnapshot
            {
                Jobs =
                [
                    new() { Id = "p1", Title = "ODE 210-594 n°1", OwnerId = "1", OwnerName = "Santos", Status = PrintStatuses.EnAttente, CreatedAt = now },
                    new() { Id = "p2", Title = "ODE 210-593 n°2", OwnerId = "2", OwnerName = "Martin", Status = PrintStatuses.Imprime,
                        CreatedAt = now.AddMinutes(-12), PrintedAt = now.AddMinutes(-12), HasImage = true, Size = 240_000, Reprint = true }
                ]
            });
            var printer = new Argos.ViewModels.Accessoires.ImprimanteViewModel(desktop);
            Assert.Equal(2, printer.Jobs.Count);
            printer.Select("p1");
            Assert.True(printer.CanDelete);
            Capture(new Argos.Views.Apps.Accessoires.ImprimanteView { DataContext = printer }, 1000, 500, "accessoire-imprimante");

            var bin = new Argos.ViewModels.Accessoires.CorbeilleViewModel();
            Assert.Single(bin.Items);
            Capture(new Argos.Views.Apps.Accessoires.CorbeilleView { DataContext = bin }, 760, 420, "accessoire-corbeille");
        }
        finally
        {
            Argos.Services.ArgosSession.Current = previous;
        }
    }
}
