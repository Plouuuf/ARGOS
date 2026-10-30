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
    private static readonly ArgosUserDto Chef = new("100", "Santos", null, [AccessLevels.Sapeur, AccessLevels.Operateur]);
    private static readonly ArgosUserDto Autre = new("200", "Martin", null, [AccessLevels.Sapeur, AccessLevels.Operateur]);

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
    public async Task Dossier_compte_habilitations_et_compte_rendu()
    {
        // Mise en route : sans aucun compte, les opérateurs administrent.
        Assert.True(_sigtao.Rights(Chef).ManageAccounts);

        var created = await _sigtao.SaveMilitaryAsync(Chef, new MilitaryRecord
        {
            Nom = "Santos", Prenom = "Diego", Nid = "1234567", Grade = "Sergent", DiscordUserId = "100", Fonction = "Chef d'agrès"
        });
        Assert.True(created.Ok, created.Error);
        Assert.False((await _sigtao.SaveMilitaryAsync(Chef, new MilitaryRecord { Nom = "X", Prenom = "Y", Nid = "1234567" })).Ok);

        Assert.True((await _sigtao.AddEntryAsync(Chef, created.Id!, new DossierEntry
        {
            Kind = DossierKinds.Promotion, Title = "Sergent-chef", Date = DateTimeOffset.UtcNow
        })).Ok);
        Assert.Equal("Sergent-chef", _sigtao.Snapshot(Chef).Military.Single().Grade);

        var account = await _sigtao.SaveAccountAsync(Chef, new UserAccount
        {
            DiscordUserId = "100", DossierId = created.Id!, Habilitations = [Habilitations.Sadmin, Habilitations.Sremise, Habilitations.Srh]
        });
        Assert.True(account.Ok, account.Error);
        Assert.Equal("Santos", _sigtao.Snapshot(Chef).Accounts.Single().Nom);

        // Un administrateur existe : les autres opérateurs perdent l'administration et la gestion du parc.
        Assert.False(_sigtao.Rights(Autre).ManageAccounts);
        Assert.False(_remise.CanManage(Autre));
        Assert.True(_remise.CanManage(Chef));
        Assert.Empty(_sigtao.Snapshot(Autre).Accounts);
        Assert.Empty(_sigtao.Snapshot(Autre).Military.Single().Entries);

        // Garde-fou : on ne retire pas sa propre habilitation SADMIN.
        Assert.False((await _sigtao.SaveAccountAsync(Chef, new UserAccount { Id = account.Id!, DiscordUserId = "100", Habilitations = [] })).Ok);

        var reset = await _sigtao.ResetAccessAsync(Chef, account.Id!);
        Assert.True(reset.Ok);
        Assert.Equal(10, reset.TemporaryPassword!.Length);
        var stored = SigtaoAccess.Accounts(_db).Single();
        Assert.True(SigtaoService.CheckPassword(stored, reset.TemporaryPassword));
        Assert.False(SigtaoService.CheckPassword(stored, "mauvais"));
        Assert.True(_sigtao.Snapshot(Chef).Accounts.Single().HasPassword);

        var cr = await _sigtao.SaveReportAsync(Autre, new CompteRendu
        {
            AuthorGrade = "Caporal", AuthorNom = "Martin", AuthorPrenom = "Paul", DestGrade = "Capitaine", DestNom = "Leroy",
            Objet = "Perte de matériel", Body = "Lors de l'intervention, une **lance** a été endommagée. Elle a été remise au magasin."
        });
        Assert.True(cr.Ok, cr.Error);
        var saved = _sigtao.Snapshot(Autre).Reports.Single();
        Assert.Contains("lance a été endommagée", saved.Synthese);
        Assert.Matches(@"^\d{4}-\d{4}$", saved.NumDoc);
        Assert.Single(_sigtao.Snapshot(Chef).Reports);            // l'administrateur voit tout
        Assert.NotEmpty(_sigtao.History(Chef));
        Assert.Empty(_sigtao.History(Autre));
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
        Assert.Equal(("Caporal-chef", "+15"), (list[2].Grade, list[2].Echelon));
        Assert.Equal(("Enzo", "0000109978"), (list[3].Prenom, list[3].Nid));
        Assert.Equal(("Sapeur de 1re classe", "+15", "Diego"), (list[4].Grade, list[4].Echelon, list[4].Prenom));
        Assert.Equal("Sapeur de 2e classe", list[5].Grade);
        Assert.Equal("SANTOS", RosterImport.NameFromNickname("[1CL+15] SANTOS D."));
    }
}
