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

        desktop.OpenApp(desktop.Apps[0]);
        Capture(new DesktopView { DataContext = desktop }, 1440, 900, "bureau");
    }

    [AvaloniaFact]
    public void Ecran_de_connexion()
    {
        var shell = new ShellView();
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
