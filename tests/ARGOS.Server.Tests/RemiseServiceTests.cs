using Argos.Server.Services;
using Argos.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Argos.Server.Tests;

/// <summary>ADAGIO REMISE : parc, vérification, anomalie, maintenance, stocks, synchronisation avec l'ODE.</summary>
public sealed class RemiseServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "argos-remise-" + Guid.NewGuid().ToString("N"));
    private readonly OdeDatabase _db;
    private readonly OdeService _ode;
    private readonly RemiseService _remise;
    private static readonly ArgosUserDto Chef = new("7", "Durand", null, [AccessLevels.Sapeur, AccessLevels.Operateur]);
    private static readonly ArgosUserDto Sapeur = new("8", "Martin", null, [AccessLevels.Sapeur]);

    public RemiseServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new OdeDatabase(Path.Combine(_dir, "argos.db"));
        var serverOptions = new OdeServiceTests.StaticMonitor<ArgosServerOptions>(new ArgosServerOptions { AccessKey = "test" });
        var discordOptions = new OdeServiceTests.StaticMonitor<DiscordOAuthOptions>(new DiscordOAuthOptions());
        var oauth = new DiscordOAuthService(discordOptions, new AuthSessionStore(),
            new PersistentDiscordSessionStore(serverOptions), NullLogger<DiscordOAuthService>.Instance);
        var hub = new RealtimeHub(oauth, NullLogger<RealtimeHub>.Instance);
        _ode = new OdeService(_db, hub, NullLogger<OdeService>.Instance);
        _remise = new RemiseService(_db, _ode, hub);
    }

    [Fact]
    public void Parc_initial_de_la_feuille_GDM()
    {
        var state = _remise.Snapshot(Chef);
        Assert.Equal(EnginsCatalog.All.Count, state.Engins.Count);
        Assert.NotEmpty(state.Items);
        var vsav = state.Engins.Single(e => e.Nom == "VSAV 949 CRET");
        Assert.Equal("VSAV BSPP", vsav.TypeLabel);
        Assert.Equal("2", vsav.Rang);
        Assert.NotEmpty(vsav.Dotation);
        Assert.Equal("SPVL BSPP", state.Engins.Single(e => e.Type == "VLROGC").TypeLabel);
        Assert.True(state.CanManage);
        Assert.False(_remise.Snapshot(Sapeur).CanManage);
        Assert.Equal("EPAN 24 MALF (EPAN_1_MALF)", _ode.Snapshot().Engins.Single(e => e.Nom == "EPAN 24 MALF").NomOde);
    }

    [Fact]
    public async Task Anomalie_totale_bloque_l_ODE_puis_remise_en_service()
    {
        var report = await _remise.ReportAnomalyAsync(Sapeur, new AnomalyRequest
        {
            Engin = "VSAV 947 CRET", Description = "Fuite de liquide de frein", Immobilisation = Immobilisations.Totale
        });
        Assert.True(report.Ok, report.Error);
        Assert.Equal(EnginStatuses.Maintenance, _ode.Snapshot().Engins.Single(e => e.Nom == "VSAV 947 CRET").Status);

        var refused = await _ode.CreateOdeAsync(Chef, new CreateOdeRequest { NatureCode = "117", Address = "RUE X", FirstEngin = "VSAV 947 CRET" });
        Assert.False(refused.Result.Ok);

        Assert.False((await _remise.CloseMaintenanceAsync(Sapeur, report.Id!, new CloseMaintenanceRequest())).Ok);
        var close = await _remise.CloseMaintenanceAsync(Chef, report.Id!, new CloseMaintenanceRequest
        {
            Text = "Flexible remplacé", NextRevision = DateTimeOffset.UtcNow.AddDays(90)
        });
        Assert.True(close.Ok, close.Error);
        Assert.Equal(EnginStatuses.Disponible, _ode.Snapshot().Engins.Single(e => e.Nom == "VSAV 947 CRET").Status);
        Assert.Equal(FleetAvailability.Disponible, _remise.Snapshot(Chef).Engins.Single(e => e.Nom == "VSAV 947 CRET").Availability);
    }

    [Fact]
    public async Task Engin_engage_ne_peut_pas_passer_indisponible()
    {
        var (ok, _) = await _ode.CreateOdeAsync(Chef, new CreateOdeRequest { NatureCode = "117", Address = "RUE X", FirstEngin = "PSE 212 CRET" });
        Assert.True(ok.Ok, ok.Error);
        var pse = _remise.Snapshot(Chef).Engins.Single(e => e.Nom == "PSE 212 CRET");
        pse.Availability = FleetAvailability.Indisponible;
        var result = await _remise.SaveEnginAsync(Chef, new SaveEnginRequest { OriginalNom = pse.Nom, Engin = pse });
        Assert.False(result.Ok);
        Assert.Equal(FleetAvailability.Disponible, _remise.Snapshot(Chef).Engins.Single(e => e.Nom == "PSE 212 CRET").Availability);
    }

    [Fact]
    public async Task Creation_renommage_et_engagement_d_un_nouvel_engin()
    {
        var created = await _remise.SaveEnginAsync(Chef, new SaveEnginRequest
        {
            Engin = new FleetEngin { Indicatif = "vsav 950", Type = "vsav", Centre = "cret", Libelle = "Secours et assistance aux victimes" }
        });
        Assert.True(created.Ok, created.Error);
        Assert.Equal("VSAV 950 CRET", created.Id);
        Assert.Contains(_ode.Snapshot().Engins, e => e.Nom == "VSAV 950 CRET");

        var engin = _remise.Snapshot(Chef).Engins.Single(e => e.Nom == "VSAV 950 CRET");
        Assert.NotEmpty(engin.Dotation);
        engin.Indicatif = "VSAV 951";
        var renamed = await _remise.SaveEnginAsync(Chef, new SaveEnginRequest { OriginalNom = "VSAV 950 CRET", Engin = engin });
        Assert.True(renamed.Ok, renamed.Error);
        Assert.DoesNotContain(_ode.Snapshot().Engins, e => e.Nom == "VSAV 950 CRET");

        var (ode, _) = await _ode.CreateOdeAsync(Chef, new CreateOdeRequest { NatureCode = "117", Address = "RUE X", FirstEngin = "VSAV 951 CRET" });
        Assert.True(ode.Ok, ode.Error);
    }

    [Fact]
    public async Task Verification_avec_manquant_ouvre_une_anomalie()
    {
        var vsav = _remise.Snapshot(Chef).Engins.Single(e => e.Nom == "VSAV 949 CRET");
        var lines = vsav.Dotation.Select(d => new VerificationLine { ItemId = d.ItemId, Counted = d.Required, Result = CheckResults.Present }).ToList();
        lines[0].Result = CheckResults.Absent;
        var result = await _remise.VerifyAsync(Sapeur, new VerificationRequest { Engin = vsav.Nom, Lines = lines });
        Assert.True(result.Ok, result.Error);

        var state = _remise.Snapshot(Chef);
        Assert.Single(state.Verifications);
        Assert.Equal(1, state.Verifications[0].Missing);
        Assert.Contains(state.Maintenances, m => m.Engin == vsav.Nom && m.Source == "Vérification" && m.IsOpen);
        Assert.Equal(0, state.Engins.Single(e => e.Nom == vsav.Nom).Dotation[0].Present);
    }

    [Fact]
    public async Task Mouvements_de_stock()
    {
        var item = _remise.Snapshot(Chef).Items.Single(i => i.Id == "radio");
        var stock = item.Stock;
        Assert.True((await _remise.MoveStockAsync(Chef, new StockRequest { ItemId = "radio", Kind = StockKinds.Entree, Quantity = 5 })).Ok);
        Assert.False((await _remise.MoveStockAsync(Chef, new StockRequest { ItemId = "radio", Kind = StockKinds.Sortie, Quantity = 999 })).Ok);
        Assert.True((await _remise.MoveStockAsync(Chef, new StockRequest
        {
            ItemId = "radio", Kind = StockKinds.Affectation, Quantity = 2, Engin = "VSAV 947 CRET"
        })).Ok);
        Assert.False((await _remise.MoveStockAsync(Sapeur, new StockRequest { ItemId = "radio", Kind = StockKinds.Entree, Quantity = 1 })).Ok);

        var state = _remise.Snapshot(Chef);
        Assert.Equal(stock + 3, state.Items.Single(i => i.Id == "radio").Stock);
        Assert.Equal(2, state.Movements.Count);
        Assert.Equal(4, state.Engins.Single(e => e.Nom == "VSAV 947 CRET").Dotation.Single(d => d.ItemId == "radio").Present);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }
}
