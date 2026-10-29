using Argos.Server.Services;
using Argos.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Argos.Server.Tests;

/// <summary>Déroulé complet d'ADAGIO ODE : appel → ODE → statuts → renfort → clôture.</summary>
public sealed class OdeServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "argos-tests-" + Guid.NewGuid().ToString("N"));
    private readonly OdeService _service;
    private readonly OdeDatabase _db;
    private static readonly ArgosUserDto Operator = new("42", "Bertier", null, [AccessLevels.Sapeur, AccessLevels.Operateur]);

    public OdeServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new OdeDatabase(Path.Combine(_dir, "argos.db"));
        var serverOptions = new StaticMonitor<ArgosServerOptions>(new ArgosServerOptions { AccessKey = "test" });
        var discordOptions = new StaticMonitor<DiscordOAuthOptions>(new DiscordOAuthOptions());
        var oauth = new DiscordOAuthService(discordOptions, new AuthSessionStore(),
            new PersistentDiscordSessionStore(serverOptions), NullLogger<DiscordOAuthService>.Instance);
        var hub = new RealtimeHub(oauth, NullLogger<RealtimeHub>.Instance);
        _service = new OdeService(_db, hub, NullLogger<OdeService>.Instance);
    }

    [Fact]
    public async Task Appel_puis_ODE_puis_cloture()
    {
        var call = await _service.CreateCallAsync(Operator, "553-235-420");
        Assert.Equal(CallStatuses.EnCours, call.Status);
        Assert.Equal("BERTIER", call.OperatorName);

        call.Street = "Impasse Guiner";
        call.NatureCode = "117";
        call.Reason = "Feu de structure";
        var update = await _service.UpdateCallAsync(Operator, call, null);
        Assert.True(update.Ok);
        Assert.Contains(_service.GetCall(call.Id)!.Events, e => e.Kind == "modification");

        // Engin inconnu ou absent : refus.
        var refused = await _service.CreateOdeAsync(Operator, new CreateOdeRequest { CallId = call.Id, NatureCode = "117", Address = "X" });
        Assert.False(refused.Result.Ok);

        var (result, intervention) = await _service.CreateOdeAsync(Operator, new CreateOdeRequest
        {
            CallId = call.Id,
            NatureCode = "117",
            Procedure = "ROUGE",
            Address = "IMPASSE GUINER",
            FirstEngin = "EPAN 24 MALF",
            ComplementaryEngins = ["FPTL 202 MALF"]
        });
        Assert.True(result.Ok, result.Error);
        Assert.NotNull(intervention);
        Assert.Matches(@"^\d{3}-\d{3}$", intervention!.Number);
        Assert.Equal(CallStatuses.Intervention, _service.GetCall(call.Id)!.Status);

        var snapshot = _service.Snapshot();
        Assert.Equal(EnginStatuses.Alerte, snapshot.Engins.Single(e => e.Nom == "EPAN 24 MALF").Status);

        // Un engin engagé ne peut pas partir sur une autre intervention.
        var busy = await _service.CreateOdeAsync(Operator, new CreateOdeRequest { NatureCode = "301", Address = "Y", FirstEngin = "EPAN 24 MALF" });
        Assert.False(busy.Result.Ok);

        Assert.True((await _service.SetEnginStatusAsync(Operator, intervention.Id, "EPAN 24 MALF", EnginStatuses.SurLesLieux)).Ok);

        // Renfort sur la même intervention.
        var renfort = await _service.CreateOdeAsync(Operator, new CreateOdeRequest
        {
            ExistingInterventionId = intervention.Id,
            FirstEngin = "VSAV 947 CRET"
        });
        Assert.True(renfort.Result.Ok, renfort.Result.Error);
        Assert.Equal(2, renfort.Intervention!.Orders.Count);

        Assert.True((await _service.CloseInterventionAsync(Operator, intervention.Id)).Ok);
        snapshot = _service.Snapshot();
        Assert.All(snapshot.Engins, e => Assert.Equal(EnginStatuses.Disponible, e.Status));
        Assert.False(snapshot.Interventions.Single(i => i.Id == intervention.Id).Active);

        var journal = _service.Journal(100);
        Assert.Contains(journal, j => j.Action == "Création d'ODE");
        Assert.Contains(journal, j => j.Action == "Clôture d'intervention");
    }

    [Fact]
    public async Task Appel_sans_suite_reste_dans_l_historique()
    {
        var call = await _service.CreateCallAsync(Operator);
        var result = await _service.CloseWithoutDepartureAsync(Operator, call.Id, "F9", "Adresse introuvable");
        Assert.True(result.Ok);
        var stored = _service.GetCall(call.Id)!;
        Assert.Equal(CallStatuses.SansSuite, stored.Status);
        Assert.Equal("Adresse introuvable", stored.ClosingReason);
        Assert.Contains(_service.Snapshot().Calls, c => c.Id == call.Id);
    }

    [Fact]
    public async Task Decroche_SIMCALL_cree_une_seule_fiche()
    {
        var answered = new SimcallCallAnswered("sim-1", "42", "Bertier", "555-0101", DateTimeOffset.UtcNow);
        var first = await _service.OnSimcallAnsweredAsync(answered, Operator);
        var second = await _service.OnSimcallAnsweredAsync(answered, Operator);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal("555-0101", first.CallerPhone);

        await _service.OnSimcallEndedAsync(new SimcallCallEnded("sim-1", DateTimeOffset.UtcNow));
        Assert.NotNull(_service.GetCall(first.Id)!.EndedAt);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    internal sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
