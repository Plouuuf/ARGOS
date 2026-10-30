using Argos.Server.Services;
using Argos.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Argos.Server.Tests;

public sealed class IliasServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "argos-ilias-" + Guid.NewGuid().ToString("N"));
    private readonly IliasService _ilias;
    private static readonly ArgosUserDto Admin = new("1", "Faubourg", null, [AccessLevels.Sapeur, AccessLevels.App(ArgosApps.Ilias), AccessLevels.RhAdmin]);
    private static readonly ArgosUserDto Sapeur = new("2", "Martin", null, [AccessLevels.Sapeur, AccessLevels.App(ArgosApps.Ilias)]);

    public IliasServiceTests()
    {
        Directory.CreateDirectory(_dir);
        var db = new OdeDatabase(Path.Combine(_dir, "argos.db"));
        var serverOptions = new OdeServiceTests.StaticMonitor<ArgosServerOptions>(new ArgosServerOptions { AccessKey = "test" });
        var oauth = new DiscordOAuthService(new OdeServiceTests.StaticMonitor<DiscordOAuthOptions>(new DiscordOAuthOptions()),
            new AuthSessionStore(), new PersistentDiscordSessionStore(serverOptions), NullLogger<DiscordOAuthService>.Instance);
        _ilias = new IliasService(db, new RealtimeHub(oauth, NullLogger<RealtimeHub>.Instance));
    }

    [Fact]
    public async Task Formation_questionnaire_et_droits()
    {
        var snap = _ilias.Snapshot(Sapeur);
        Assert.NotEmpty(snap.Categories);
        Assert.False(snap.CanAdmin);
        var quiz = snap.Quizzes.Single();
        Assert.All(quiz.Questions, q => Assert.Equal(-1, q.Correct));          // réponses cachées
        Assert.True(_ilias.Snapshot(Admin).Quizzes.Single().Questions.All(q => q.Correct >= 0));

        var formation = snap.Formations.Single();
        foreach (var m in formation.Modules)
            Assert.True((await _ilias.SetModuleAsync(Sapeur, formation.Id, new IliasModuleRequest { ModuleId = m.Id })).Ok);
        Assert.Null(_ilias.Snapshot(Sapeur).MyProgress.Single().CompletedAt);  // évaluation pas encore réussie

        var result = await _ilias.SubmitAsync(Sapeur, quiz.Id, new IliasSubmitRequest { Answers = [1, 0, 1, 2], DurationSeconds = 120 });
        Assert.NotNull(result);
        Assert.Equal(4, result!.Score);
        Assert.True(result.Passed);
        Assert.NotNull(_ilias.Snapshot(Sapeur).MyProgress.Single().CompletedAt);

        Assert.False((await _ilias.SaveCategoryAsync(Sapeur, new IliasCategory { Name = "X" })).Ok);
        Assert.True((await _ilias.SaveDocumentAsync(Admin, new IliasDocumentRequest
        {
            Document = new IliasDocument { Title = "Note de service", CategoryId = "reglementaire", FileName = "note.pdf", Audience = IliasAudiences.RhAdmin },
            FileBase64 = Convert.ToBase64String("%PDF-1.4"u8.ToArray())
        })).Ok);
        Assert.Empty(_ilias.Snapshot(Sapeur).Documents);                        // public RH / ADMIN
        Assert.Single(_ilias.Snapshot(Admin).Documents);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }
}
