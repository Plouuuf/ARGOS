using Argos.Server.Services;
using Argos.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Argos.Server.Tests;

/// <summary>MCO : ODE → brouillon pré-rempli → modifications tracées → validation figée → archivage.</summary>
public sealed class McoServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "argos-mco-" + Guid.NewGuid().ToString("N"));
    private readonly OdeDatabase _db;
    private readonly OdeService _ode;
    private readonly McoService _mco;
    private static readonly ArgosUserDto User = new("9", "Santos", null, [AccessLevels.Sapeur, AccessLevels.Operateur]);

    public McoServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new OdeDatabase(Path.Combine(_dir, "argos.db"));
        var serverOptions = new OdeServiceTests.StaticMonitor<ArgosServerOptions>(new ArgosServerOptions { AccessKey = "test" });
        var discordOptions = new OdeServiceTests.StaticMonitor<DiscordOAuthOptions>(new DiscordOAuthOptions());
        var oauth = new DiscordOAuthService(discordOptions, new AuthSessionStore(),
            new PersistentDiscordSessionStore(serverOptions), NullLogger<DiscordOAuthService>.Instance);
        var hub = new RealtimeHub(oauth, NullLogger<RealtimeHub>.Instance);
        _ode = new OdeService(_db, hub, NullLogger<OdeService>.Instance);
        _mco = new McoService(_db, hub);
    }

    [Fact]
    public async Task Cycle_complet_d_une_MCO()
    {
        var (ok, intervention) = await _ode.CreateOdeAsync(User, new CreateOdeRequest
        {
            NatureCode = "117", Address = "AVENUE BOSQUET", Locality = "L'ÉCHAT",
            FirstEngin = "VSAV 947 CRET", ComplementaryEngins = ["PSE 212 CRET"]
        });
        Assert.True(ok.Ok, ok.Error);
        await _ode.SetEnginStatusAsync(User, intervention!.Id, "VSAV 947 CRET", EnginStatuses.Parti);
        await _ode.SetEnginStatusAsync(User, intervention.Id, "VSAV 947 CRET", EnginStatuses.SurLesLieux);
        intervention = _ode.GetIntervention(intervention.Id)!;

        var report = new McoReport();
        McoPrefill.FromIntervention(report, intervention, "VSAV 947 CRET");
        Assert.Equal("VSAV 947 CRET", report.Engin);
        Assert.Equal(new[] { "PSE 212 CRET" }, report.OtherEngins);
        Assert.NotEmpty(report.TimeDepart);
        Assert.NotEmpty(report.TimeArrivee);

        var created = await _mco.SaveAsync(User, report);
        Assert.True(created.Ok, created.Error);
        var saved = _mco.Get(created.Id!)!;
        Assert.StartsWith("MCO-", saved.Number);
        Assert.Equal(McoStatuses.Brouillon, saved.Status);

        // Validation refusée tant que la MCO est incomplète.
        Assert.False((await _mco.ValidateAsync(User, saved.Id)).Ok);

        saved.ChefAgres = new McoPerson { Grade = "SGT", Nom = "Santos", Prenom = "Diego" };
        saved.Crew = [new McoPerson { Grade = "CPL", Nom = "Martin", Fonction = "Conducteur" }];
        saved.SituationArrivee = "Fumées au 1er étage";
        saved.Synthese = McoPrefill.Synthese(saved);
        Assert.Contains("Effectif : 2", saved.Synthese);
        Assert.True((await _mco.SaveAsync(User, saved)).Ok);
        Assert.Contains(_mco.Get(saved.Id)!.History, h => h.Action == "Modification" && h.Details.Contains("chef d'agrès"));

        var validated = await _mco.ValidateAsync(User, saved.Id);
        Assert.True(validated.Ok, validated.Error);
        saved.Actions = "tentative de modification";
        Assert.False((await _mco.SaveAsync(User, saved)).Ok);
        Assert.True((await _mco.ArchiveAsync(User, saved.Id)).Ok);
        Assert.Equal(McoStatuses.Archivee, _mco.Get(saved.Id)!.Status);
        Assert.Single(_mco.Snapshot().Reports);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }
}
