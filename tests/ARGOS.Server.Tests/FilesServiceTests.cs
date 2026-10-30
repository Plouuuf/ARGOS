using Argos.Server.Services;
using Argos.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Argos.Server.Tests;

public sealed class FilesServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "argos-files-" + Guid.NewGuid().ToString("N"));
    private readonly FilesService _files;
    private static readonly ArgosUserDto Moi = new("1", "Santos", null, [AccessLevels.Sapeur]);
    private static readonly ArgosUserDto Autre = new("2", "Martin", null, [AccessLevels.Sapeur]);

    public FilesServiceTests()
    {
        Directory.CreateDirectory(_dir);
        var db = new OdeDatabase(Path.Combine(_dir, "argos.db"));
        var serverOptions = new OdeServiceTests.StaticMonitor<ArgosServerOptions>(new ArgosServerOptions { AccessKey = "test" });
        var oauth = new DiscordOAuthService(new OdeServiceTests.StaticMonitor<DiscordOAuthOptions>(new DiscordOAuthOptions()),
            new AuthSessionStore(), new PersistentDiscordSessionStore(serverOptions), NullLogger<DiscordOAuthService>.Instance);
        _files = new FilesService(db, new RealtimeHub(oauth, NullLogger<RealtimeHub>.Instance));
    }

    private static string B64(string text) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task Dossiers_fichiers_et_corbeille()
    {
        var folder = await _files.CreateFolderAsync(Moi, new FileFolderRequest { Name = "Gardes" });
        Assert.True(folder.Ok);
        var note = await _files.UploadAsync(Moi, new FileUploadRequest { ParentId = folder.Id!, Name = "notes.txt", Data = B64("bonjour") });
        Assert.True(note.Ok);
        var copy = await _files.UploadAsync(Moi, new FileUploadRequest { ParentId = folder.Id!, Name = "notes.txt", Data = B64("x") });
        Assert.Equal("notes (2).txt", _files.Snapshot(Moi).Entries.Single(e => e.Id == copy.Id).Name);

        // Le Bloc-notes réécrit le même fichier.
        Assert.True((await _files.UploadAsync(Moi, new FileUploadRequest { ReplaceId = note.Id, Data = B64("bonjour à tous") })).Ok);
        Assert.Equal("bonjour à tous", File.ReadAllText(_files.Open(Moi, note.Id!).Path!));
        Assert.Null(_files.Open(Autre, note.Id!).Entry);                       // espace personnel
        Assert.Empty(_files.Snapshot(Autre).Entries);

        Assert.False((await _files.MoveAsync(Moi, folder.Id!, new FileMoveRequest { ParentId = folder.Id! })).Ok);

        // Corbeille : le dossier emporte son contenu, puis revient avec lui.
        Assert.True((await _files.DeleteAsync(Moi, folder.Id!)).Ok);
        Assert.All(_files.Snapshot(Moi).Entries, e => Assert.True(e.Deleted));
        Assert.True((await _files.RestoreAsync(Moi, folder.Id!)).Ok);
        Assert.All(_files.Snapshot(Moi).Entries, e => Assert.False(e.Deleted));

        await _files.DeleteAsync(Moi, folder.Id!);
        Assert.True((await _files.EmptyTrashAsync(Moi)).Ok);
        Assert.Empty(_files.Snapshot(Moi).Entries);
        Assert.Equal(0, _files.Snapshot(Moi).Used);
    }

    [Fact]
    public void Noms_valides()
    {
        Assert.Equal("CR 12-03", FileKinds.CleanName("CR 12/03"[..2] + " 12-03"));
        Assert.Null(FileKinds.CleanName("  ...  "));
        Assert.Equal("a b", FileKinds.CleanName("a: b?"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
