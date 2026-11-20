using Argos.Server.Services;
using Argos.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Argos.Server.Tests;

/// <summary>SIGTAO : dossiers, rubriques, comptes et habilitations, comptes-rendus, confidentialité.</summary>
public sealed class SigtaoServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "argos-sigtao-" + Guid.NewGuid().ToString("N"));
    private readonly OdeDatabase _db;
    private readonly SigtaoService _sigtao;
    private readonly RemiseService _remise;
    private static readonly ArgosUserDto Chef = new("100", "[1CL+15] SANTOS D.", "https://cdn.discordapp.com/avatars/100/abc.png",
        [AccessLevels.Sapeur, AccessLevels.App(ArgosApps.Sigtao), AccessLevels.App(ArgosApps.AdagioRemise), AccessLevels.RhAdmin]);
    private static readonly ArgosUserDto Autre = new("200", "Martin", null, [AccessLevels.Sapeur, AccessLevels.App(ArgosApps.Sigtao)]);

    public SigtaoServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new OdeDatabase(Path.Combine(_dir, "argos.db"));
        var serverOptions = new OdeServiceTests.StaticMonitor<ArgosServerOptions>(new ArgosServerOptions { AccessKey = "test" });
        var discordOptions = new OdeServiceTests.StaticMonitor<DiscordOAuthOptions>(new DiscordOAuthOptions());
        var oauth = new DiscordOAuthService(discordOptions, new AuthSessionStore(),
            new PersistentDiscordSessionStore(serverOptions), NullLogger<DiscordOAuthService>.Instance);
        var hub = new RealtimeHub(oauth, NullLogger<RealtimeHub>.Instance);
        _sigtao = new SigtaoService(_db, hub);
        _remise = new RemiseService(_db, new OdeService(_db, hub, NullLogger<OdeService>.Instance), hub);
    }

    [Fact]
    public async Task Droits_par_role_dossiers_import_et_compte_rendu()
    {
        Assert.True(_sigtao.Rights(Chef).ManageCompany);
        Assert.False(_sigtao.Rights(Autre).ManageCompany);
        Assert.True(_remise.CanManage(Chef));
        Assert.False(_remise.CanManage(Autre));
        Assert.Contains("SIGTAO", _sigtao.Rights(Autre).Habilitations);
        Assert.DoesNotContain("ADAGIO ODE", _sigtao.Rights(Autre).Habilitations);

        // Import de l'effectif : dossier relié au joueur 100, sans nom (mention Discord).
        var import = await _sigtao.ImportRosterAsync(Chef,
            "<:1CL15:1> <@100> (Diego) - NID : 0000109823\n<:CNE:2> CNE FAUBOURG (Laurent) - *Commandant d'Unité* - NM : 0000109387");
        Assert.True(import.Ok, import.Error);
        Assert.False((await _sigtao.ImportRosterAsync(Autre, "<:SAP:1> <@300> (Lucas) - NID : 1")).Ok);

        // À la connexion : nom repris du pseudo, photo = avatar Discord.
        var me = _sigtao.Snapshot(Chef).Military.Single(m => m.DiscordUserId == "100");
        Assert.Equal("SANTOS", me.Nom);
        Assert.Equal("Sapeur de 1re classe +15", me.Grade);
        Assert.Equal(Chef.AvatarUrl, me.AvatarUrl);
        Assert.Equal(me.Id, _sigtao.Snapshot(Chef).MyDossierId);
        Assert.Equal("Capitaine", _sigtao.Snapshot(Chef).Military[0].Grade);       // tri du plus haut grade

        // Réimport : mise à jour sans doublon, promotion tracée.
        Assert.True((await _sigtao.ImportRosterAsync(Chef, "<:CPL:1> <@100> (Diego) - NID : 0000109823")).Ok);
        var promoted = _sigtao.Snapshot(Chef).Military.Single(m => m.DiscordUserId == "100");
        Assert.Equal("Caporal", promoted.Grade);
        Assert.Contains(promoted.Entries, e => e.Kind == DossierKinds.Promotion);
        Assert.Equal(2, _sigtao.Snapshot(Chef).Military.Count);
        Assert.Empty(_sigtao.Snapshot(Autre).Military.Single(m => m.DiscordUserId == "100").Entries);

        Assert.False((await _sigtao.SaveMilitaryAsync(Autre, new MilitaryRecord { Nom = "X", Prenom = "Y" })).Ok);

        var cr = await _sigtao.SaveReportAsync(Autre, new CompteRendu
        {
            AuthorGrade = "Caporal", AuthorNom = "Martin", AuthorPrenom = "Paul", DestGrade = "Capitaine", DestNom = "Faubourg",
            Objet = "Perte de matériel", Body = "Lors de l'intervention, une **lance** a été endommagée. Elle a été remise au magasin."
        });
        Assert.True(cr.Ok, cr.Error);
        var saved = _sigtao.Snapshot(Autre).Reports.Single();
        Assert.Contains("lance a été endommagée", saved.Synthese);
        Assert.Matches(@"^\d{4}-\d{4}$", saved.NumDoc);
        Assert.Single(_sigtao.Snapshot(Chef).Reports);            // RH/ADMIN voit tout
        Assert.NotEmpty(_sigtao.History(Chef));
        Assert.Empty(_sigtao.History(Autre));
    }

    [Fact]
    public async Task Brouillon_ModifiableParSonRedacteurPuisTransmis()
    {
        var draft = await _sigtao.SaveReportAsync(Autre, new CompteRendu { Objet = "Brouillon", IsDraft = true });
        Assert.True(draft.Ok, draft.Error);
        var saved = _sigtao.Snapshot(Autre).Reports.Single();
        Assert.True(saved.IsDraft);
        Assert.Equal(string.Empty, saved.NumDoc);
        Assert.Empty(_sigtao.Snapshot(Chef).Reports);             // brouillon invisible des autres, même RH/ADMIN
        Assert.False((await _sigtao.SaveReportAsync(Chef, new CompteRendu { Id = saved.Id, Objet = "X", IsDraft = true })).Ok);

        saved.Body = "Texte final.";
        saved.AuthorNom = "Martin";
        saved.DestNom = "Faubourg";
        saved.IsDraft = false;
        var final = await _sigtao.SaveReportAsync(Autre, saved);
        Assert.True(final.Ok, final.Error);
        Assert.Equal(saved.Id, final.Id);
        var sent = _sigtao.Snapshot(Chef).Reports.Single();
        Assert.False(sent.IsDraft);
        Assert.Matches(@"^\d{4}-\d{4}$", sent.NumDoc);
        Assert.False((await _sigtao.SaveReportAsync(Autre, new CompteRendu { Id = sent.Id, Objet = "Modif", IsDraft = true })).Ok);
        Assert.False((await _sigtao.DeleteDraftAsync(Autre, sent.Id)).Ok);
    }

    [Fact]
    public async Task Feuille_de_garde_valide_arme_les_engins()
    {
        await _sigtao.SaveMilitaryAsync(Chef, new MilitaryRecord { Nom = "Durand", Prenom = "Léa", Grade = "Caporal" });
        await _sigtao.SaveMilitaryAsync(Chef, new MilitaryRecord { Nom = "Petit", Prenom = "Marc", Grade = "Sergent" });
        var all = _sigtao.Snapshot(Chef).Military;
        var ids = new List<string> { all.Single(m => m.Nom == "Durand").Id, all.Single(m => m.Nom == "Petit").Id };
        var date = DateTime.Today.ToString("yyyy-MM-dd");
        var fleet = _remise.Snapshot(Chef).Engins;
        var vsav = fleet.First(e => e.Nom == "VSAV 947 CRET");
        var pse = fleet.First(e => e.Nom == "PSE 212 CRET");

        FeuilleGarde Feuille(string a, string b) => new()
        {
            Date = date, Centre = "CRET",
            Service = [new GardeAffectation { Poste = GardePostes.ChefDeGarde, MilitaryId = b }],
            Engins =
            [
                new GardeEngin { Engin = vsav.Nom, Type = "VSAV", Postes = [new GardeAffectation { Poste = "C/A", MilitaryId = a }] },
                new GardeEngin { Engin = pse.Nom, Type = "PSE", Arme = false }
            ]
        };

        Assert.False((await _sigtao.SaveGardeAsync(Autre, Feuille(ids[0], ids[1]), true)).Ok);         // simple sapeur
        var deuxPostes = Feuille(ids[0], ids[1]);
        deuxPostes.Engins[0].Postes.Add(new GardeAffectation { Poste = "COND", MilitaryId = ids[0] });
        Assert.False((await _sigtao.SaveGardeAsync(Chef, deuxPostes, true)).Ok);                       // deux postes sur le même engin
        var ok = await _sigtao.SaveGardeAsync(Chef, Feuille(ids[0], ids[1]), true);
        Assert.True(ok.Ok, ok.Error);
        var saved = _sigtao.Garde(ok.Id!)!;
        Assert.True(saved.IsValidated);
        Assert.Equal(2, saved.Effectif);
        Assert.StartsWith("SGT PETIT", saved.Service[0].Nom);
        Assert.Single(_sigtao.Snapshot(Autre).Gardes);                                                   // tout le monde lit la garde

        Assert.True((await _remise.ArmFromGardeAsync(Chef, saved)).Ok);
        var after = _remise.Snapshot(Chef).Engins;
        Assert.Single(after.First(e => e.Nom == vsav.Nom).Equipage);
        var pseAfter = after.First(e => e.Nom == pse.Nom);
        Assert.Equal(FleetAvailability.Indisponible, pseAfter.Availability);
        Assert.StartsWith("Non armé", pseAfter.AvailabilityReason);

        // Garde suivante : le PSE est armé, il redevient disponible.
        saved.Engins[1].Arme = true;
        Assert.True((await _remise.ArmFromGardeAsync(Chef, saved)).Ok);
        Assert.Equal(FleetAvailability.Disponible, _remise.Snapshot(Chef).Engins.First(e => e.Nom == pse.Nom).Availability);
    }

    [Fact]
    public async Task Inscription_personnelle_dans_un_autre_centre()
    {
        await _sigtao.SaveMilitaryAsync(Chef, new MilitaryRecord { Nom = "Martin", Prenom = "Paul", Grade = "Caporal", DiscordUserId = "200" });
        var date = DateTime.Today.ToString("yyyy-MM-dd");

        var ok = await _sigtao.SelfAssignAsync(Autre, new SelfGardeRequest { Date = date, Centre = "MALF", Engin = "FPTL 202 MALF", Type = "FPTL", Poste = "COND" });
        Assert.True(ok.Ok, ok.Error);
        var malf = _sigtao.Garde(ok.Id!)!;
        Assert.Equal("CPL MARTIN Paul", malf.Engins.Single().Postes.Single(p => p.Poste == "COND").Nom);

        // Pas deux centres le même jour ; changement de poste dans le même centre autorisé.
        Assert.False((await _sigtao.SelfAssignAsync(Autre, new SelfGardeRequest { Date = date, Centre = "CRET" })).Ok);
        Assert.True((await _sigtao.SelfAssignAsync(Autre, new SelfGardeRequest { Date = date, Centre = "MALF", Poste = "Renfort" })).Ok);
        // RP : le même militaire arme un second engin.
        Assert.True((await _sigtao.SelfAssignAsync(Autre, new SelfGardeRequest { Date = date, Centre = "MALF", Engin = "EPA 84 MALF", Type = "EPA", Poste = "COND" })).Ok);
        malf = _sigtao.Garde(ok.Id!)!;
        Assert.Single(malf.Reserve);
        Assert.Equal(2, malf.Engins.Count(e => e.Postes.Any(p => p.Pourvu)));
        Assert.Equal(1, malf.Effectif);

        Assert.True((await _sigtao.SelfAssignAsync(Autre, new SelfGardeRequest { Date = date, Centre = "MALF", Retirer = true })).Ok);
        Assert.Equal(0, _sigtao.Garde(ok.Id!)!.Effectif);
        Assert.True((await _sigtao.SelfAssignAsync(Autre, new SelfGardeRequest { Date = date, Centre = "CRET" })).Ok);
    }

    [Fact]
    public async Task Services_ajout_role_observations_et_retrait()
    {
        await _sigtao.SaveMilitaryAsync(Chef, new MilitaryRecord { Nom = "Bernard", Prenom = "Hugo", Grade = "Sergent" });
        await _sigtao.SaveMilitaryAsync(Chef, new MilitaryRecord { Nom = "Leroy", Prenom = "Inès", Grade = "Caporal", Status = MilitaryStatuses.Parti });
        var all = _sigtao.Snapshot(Chef).Military;
        var hugo = all.Single(m => m.Nom == "Bernard").Id;
        var ines = all.Single(m => m.Nom == "Leroy").Id;

        // Tous les services existent dans l'instantané, vides au départ ; tout le monde les lit.
        Assert.Equal(SigtaoServices.All.Select(s => s.Id), _sigtao.Snapshot(Autre).Services.Select(s => s.Id));
        Assert.All(_sigtao.Snapshot(Autre).Services, s => Assert.Empty(s.Members));

        // Droits : gestion de la compagnie uniquement ; rôle obligatoire et propre au service ; dossier en activité.
        var request = new ServiceMemberRequest { ServiceId = SigtaoServices.Remise, MilitaryId = hugo, Role = "Sous-officier de remise" };
        Assert.False((await _sigtao.SaveServiceMemberAsync(Autre, request)).Ok);
        Assert.False((await _sigtao.SaveServiceMemberAsync(Chef, new ServiceMemberRequest { ServiceId = SigtaoServices.Remise, MilitaryId = hugo, Role = "Instructeur" })).Ok);
        Assert.False((await _sigtao.SaveServiceMemberAsync(Chef, new ServiceMemberRequest { ServiceId = "inconnu", MilitaryId = hugo, Role = "Instructeur" })).Ok);
        Assert.False((await _sigtao.SaveServiceMemberAsync(Chef, new ServiceMemberRequest { ServiceId = SigtaoServices.Remise, MilitaryId = ines, Role = "Garde-remise" })).Ok);

        var ok = await _sigtao.SaveServiceMemberAsync(Chef, request);
        Assert.True(ok.Ok, ok.Error);
        var remise = _sigtao.Snapshot(Autre).Services.Single(s => s.Id == SigtaoServices.Remise);
        Assert.Equal(("Sous-officier de remise", ""), (remise.Members.Single().Role, remise.Members.Single().Observations));

        // Changement de rôle ; un militaire peut appartenir à plusieurs services.
        Assert.True((await _sigtao.SaveServiceMemberAsync(Chef, new ServiceMemberRequest { ServiceId = SigtaoServices.Remise, MilitaryId = hugo, Role = "Caporal de remise" })).Ok);
        Assert.Equal("Caporal de remise", _sigtao.Snapshot(Chef).Services.Single(s => s.Id == SigtaoServices.Remise).Members.Single().Role);

        // Casernement : observations libres ; ignorées dans les autres services.
        var caserne = await _sigtao.SaveServiceMemberAsync(Chef, new ServiceMemberRequest
        {
            ServiceId = SigtaoServices.Casernement, MilitaryId = hugo, Role = "Effectif", Observations = "  Chambre 12, clés remises  "
        });
        Assert.True(caserne.Ok, caserne.Error);
        Assert.True((await _sigtao.SaveServiceMemberAsync(Chef, new ServiceMemberRequest
        {
            ServiceId = SigtaoServices.Instruction, MilitaryId = hugo, Role = "Instructeur", Observations = "Ignoré"
        })).Ok);
        var services = _sigtao.Snapshot(Chef).Services;
        Assert.Equal("Chambre 12, clés remises", services.Single(s => s.Id == SigtaoServices.Casernement).Members.Single().Observations);
        Assert.Equal("", services.Single(s => s.Id == SigtaoServices.Instruction).Members.Single().Observations);
        Assert.Equal(3, services.Count(s => s.Members.Any(m => m.MilitaryId == hugo)));

        // Retrait : uniquement du service visé ; second retrait refusé.
        Assert.True((await _sigtao.SaveServiceMemberAsync(Chef, new ServiceMemberRequest { ServiceId = SigtaoServices.Remise, MilitaryId = hugo, Retirer = true })).Ok);
        Assert.False((await _sigtao.SaveServiceMemberAsync(Chef, new ServiceMemberRequest { ServiceId = SigtaoServices.Remise, MilitaryId = hugo, Retirer = true })).Ok);
        services = _sigtao.Snapshot(Chef).Services;
        Assert.Empty(services.Single(s => s.Id == SigtaoServices.Remise).Members);
        Assert.Single(services.Single(s => s.Id == SigtaoServices.Casernement).Members);
        Assert.Contains(_sigtao.History(Chef), j => j.Action == SigtaoJournal.Service);
    }

    [Fact]
    public async Task Validation_de_la_garde_met_a_jour_la_derniere_garde()
    {
        await _sigtao.SaveMilitaryAsync(Chef, new MilitaryRecord { Nom = "Durand", Prenom = "Léa", Grade = "Caporal" });
        await _sigtao.SaveMilitaryAsync(Chef, new MilitaryRecord { Nom = "Petit", Prenom = "Marc", Grade = "Sergent" });
        var all = _sigtao.Snapshot(Chef).Military;
        var lea = all.Single(m => m.Nom == "Durand").Id;
        var marc = all.Single(m => m.Nom == "Petit").Id;
        string Derniere(string id) => _sigtao.Snapshot(Chef).Military.Single(m => m.Id == id).DerniereGarde;
        var j0 = DateTime.Today.ToString("yyyy-MM-dd");
        var jMoins3 = DateTime.Today.AddDays(-3).ToString("yyyy-MM-dd");

        FeuilleGarde Feuille(string date, params string[] ids) => new()
        {
            Date = date, Centre = "CRET",
            Service = ids.Select((id, i) => new GardeAffectation { Poste = GardePostes.Service[i], MilitaryId = id }).ToList()
        };

        Assert.Equal("", Derniere(lea));

        // Simple enregistrement : pas de mise à jour.
        Assert.True((await _sigtao.SaveGardeAsync(Chef, Feuille(j0, lea), false)).Ok);
        Assert.Equal("", Derniere(lea));

        // Validation : date du jour de garde sur chaque présent.
        Assert.True((await _sigtao.SaveGardeAsync(Chef, Feuille(j0, lea), true)).Ok);
        Assert.Equal(j0, Derniere(lea));
        Assert.Equal("", Derniere(marc));

        // Garde plus ancienne validée ensuite : la date ne recule pas ; l'autre présent reçoit la sienne.
        Assert.True((await _sigtao.SaveGardeAsync(Chef, Feuille(jMoins3, lea, marc), true)).Ok);
        Assert.Equal(j0, Derniere(lea));
        Assert.Equal(jMoins3, Derniere(marc));

        // L'enregistrement du dossier depuis le poste ne touche pas à la date (tenue par le serveur).
        var dossier = _sigtao.Snapshot(Chef).Military.Single(m => m.Id == lea);
        dossier.DerniereGarde = "";
        Assert.True((await _sigtao.SaveMilitaryAsync(Chef, dossier)).Ok);
        Assert.Equal(j0, Derniere(lea));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }
}

public sealed class RosterImportTests
{
    private const string Roster = """
        **CORPS DES OFFICIERS**

        <:CNE:1544832422741549118> CNE FAUBOURG (Laurent) - *Commandant d'Unité* - NM : 0000109387
        <:SCH:1544832342600843386> <@765246238059790376> (Kamron) - *Chef de Centre Adjoint CS CRET* - NID : 0000109942
        <:CCH15:1544832227366412369> <@383609121754185741> (Alexandre) - NID : 0000109476
        <:CCH:1544832202154704907> <@501436869024808973> (Enzo - NID : 0000109978
        <:1CL15:1544832148413095967> <@560037927695024139> (Diego) - NID : 0000109823
        <:SAP:1544840069347803258> <@396005111345971200> (Matthieu) - NID : 0000109921
        """;

    [Fact]
    public void Lit_le_tableau_d_effectif()
    {
        var list = RosterImport.Parse(Roster);
        Assert.Equal(6, list.Count);
        var cne = list[0];
        Assert.Equal(("Capitaine", "FAUBOURG", "Laurent", "0000109387", "Commandant d'Unité"),
            (cne.Grade, cne.Nom, cne.Prenom, cne.Matricule, cne.Fonction));
        Assert.Equal(("Sergent-chef", "765246238059790376", "CRET"), (list[1].Grade, list[1].DiscordUserId, list[1].Affectation));
        Assert.Equal(("Caporal-chef +15", ""), (list[2].Grade, list[2].Echelon));
        Assert.Equal(("Enzo", "0000109978"), (list[3].Prenom, list[3].Nid));
        Assert.Equal(("Sapeur de 1re classe +15", "Diego"), (list[4].Grade, list[4].Prenom));
        Assert.Equal("1CL+15", BsppGrades.Short(list[4].Grade));
        Assert.Equal("Sapeur de 2e classe", list[5].Grade);
        Assert.Equal("SANTOS", RosterImport.NameFromNickname("[1CL+15] SANTOS D."));
    }
}
