using Argos.Server.Services;
using Argos.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Argos.Server.Tests;

public sealed class PrinterServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "argos-print-" + Guid.NewGuid().ToString("N"));
    private readonly PrinterService _printer;
    private readonly FilesService _files;
    private static readonly ArgosUserDto Moi = new("1", "Santos", null, [AccessLevels.Sapeur]);
    private static readonly ArgosUserDto Autre = new("2", "Martin", null, [AccessLevels.Sapeur]);

    public PrinterServiceTests()
    {
        Directory.CreateDirectory(_dir);
        PrinterService.Dir = Path.Combine(_dir, "print");
        PrinterService.Step = TimeSpan.FromMilliseconds(20);
        var db = new OdeDatabase(Path.Combine(_dir, "argos.db"));
        var serverOptions = new OdeServiceTests.StaticMonitor<ArgosServerOptions>(new ArgosServerOptions { AccessKey = "test" });
        var oauth = new DiscordOAuthService(new OdeServiceTests.StaticMonitor<DiscordOAuthOptions>(new DiscordOAuthOptions()),
            new AuthSessionStore(), new PersistentDiscordSessionStore(serverOptions), NullLogger<DiscordOAuthService>.Instance);
        var hub = new RealtimeHub(oauth, NullLogger<RealtimeHub>.Instance);
        _files = new FilesService(db, hub);
        _printer = new PrinterService(db, hub, _files, NullLogger<PrinterService>.Instance);
    }

    [Fact]
    public async Task File_impression_conservation_et_droits()
    {
        var job = await _printer.SubmitAsync(Moi, new PrintJob { Title = "ODE 210-593 n°1", InterventionNumber = "210-593" }, [1, 2, 3]);
        Assert.Equal(PrintStatuses.EnAttente, job.Status);
        Assert.NotNull(_printer.ImagePath(job.Id));
        // Copie rangée dans Mes documents › Impressions.
        var entries = _files.Snapshot(Moi).Entries;
        Assert.Contains(entries, e => e.IsFolder && e.Name == "Impressions");
        Assert.Contains(entries, e => e.Id == job.FileId);

        for (var i = 0; i < 50 && _printer.Snapshot(Moi).Jobs.Single().Status != PrintStatuses.Imprime; i++)
            await Task.Delay(20);
        Assert.Equal(PrintStatuses.Imprime, _printer.Snapshot(Moi).Jobs.Single().Status);

        Assert.True((await _printer.ReprintAsync(Autre, job.Id)).Ok);
        Assert.Equal(2, _printer.Snapshot(Moi).Jobs.Count);
        Assert.False((await _printer.DeleteAsync(Autre, job.Id)).Ok);      // pas son document
        Assert.True((await _printer.DeleteAsync(Moi, job.Id)).Ok);
        Assert.Single(_printer.Snapshot(Moi).Jobs);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
