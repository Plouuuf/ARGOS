using Argos.Server.Services;
using Argos.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Argos.Server.Tests;

public sealed class MailServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "argos-mail-" + Guid.NewGuid().ToString("N"));
    private readonly MailService _mail;
    private readonly OdeDatabase _db;
    private static readonly ArgosUserDto Santos = new("1", "Santos", null, [AccessLevels.Sapeur]);
    private static readonly ArgosUserDto Martin = new("2", "Martin", null, [AccessLevels.Sapeur]);
    private static readonly ArgosUserDto Leroy = new("3", "Leroy", null, [AccessLevels.Sapeur]);

    public MailServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new OdeDatabase(Path.Combine(_dir, "argos.db"));
        var serverOptions = new OdeServiceTests.StaticMonitor<ArgosServerOptions>(new ArgosServerOptions { AccessKey = "test" });
        var oauth = new DiscordOAuthService(new OdeServiceTests.StaticMonitor<DiscordOAuthOptions>(new DiscordOAuthOptions()),
            new AuthSessionStore(), new PersistentDiscordSessionStore(serverOptions), NullLogger<DiscordOAuthService>.Instance);
        _mail = new MailService(_db, new RealtimeHub(oauth, NullLogger<RealtimeHub>.Instance));
        _db.SaveDoc(OdeDatabase.RemiseTables.Military, "m1", DateTimeOffset.UtcNow, new MilitaryRecord
        {
            Id = "m1", Nom = "Santos", Prenom = "Diégo", Grade = "Sergent", DiscordUserId = "1"
        });
    }

    [Fact]
    public void Adresses()
    {
        Assert.Equal("diego.santos@pompiersparis.fr", MailAddresses.Build("Diégo", "Santos"));
        Assert.Equal("jean-pierre.le-gall@pompiersparis.fr", MailAddresses.Build("Jean Pierre", "Le Gall"));
        Assert.Equal(["a@b.fr", "c@d.fr"], MailAddresses.Split("Alice <A@b.fr>; c@d.fr, a@b.fr"));
        Assert.False(MailAddresses.IsValid("pas-une-adresse"));
        Assert.Equal("diego.santos@pompiersparis.fr", _mail.Snapshot(Santos).Address);   // dossier SIGTAO
        Assert.Equal("martin@pompiersparis.fr", _mail.Snapshot(Martin).Address);         // adresse attribuée
    }

    [Fact]
    public async Task Envoi_copies_cci_et_corbeille()
    {
        _mail.Snapshot(Martin);
        _mail.Snapshot(Leroy);

        var draft = await _mail.SaveDraftAsync(Santos, new MailDraftRequest
        {
            To = "martin@pompiersparis.fr", Subject = "Garde", Body = "**Bonjour**",
            NewAttachments = [new MailNewAttachment { Name = "planning.txt", Data = Convert.ToBase64String("x"u8.ToArray()) }]
        });
        Assert.True(draft.Ok);
        Assert.Single(_mail.Snapshot(Santos).Items, i => i.Folder == MailFolders.Brouillons);

        var unknown = await _mail.SendAsync(Santos, new MailDraftRequest { DraftId = draft.Item!.Id, To = "inconnu@pompiersparis.fr" });
        Assert.False(unknown.Ok);

        var sent = await _mail.SendAsync(Santos, new MailDraftRequest
        {
            DraftId = draft.Item.Id, To = "martin@pompiersparis.fr", Bcc = "leroy@pompiersparis.fr", Subject = "Garde", Body = "**Bonjour**",
            KeepAttachments = [draft.Item.Attachments[0].Id]
        });
        Assert.True(sent.Ok, sent.Error);
        var mine = _mail.Snapshot(Santos).Items;
        Assert.DoesNotContain(mine, i => i.Folder == MailFolders.Brouillons);
        Assert.Single(mine, i => i.Folder == MailFolders.Envoyes && i.Bcc.Count == 1);

        var received = _mail.Snapshot(Martin).Items.Single();
        Assert.Equal(MailFolders.Reception, received.Folder);
        Assert.False(received.Read);
        Assert.Empty(received.Bcc);                                         // la Cci reste invisible
        Assert.Contains("SANTOS", received.FromName);
        Assert.Single(_mail.Snapshot(Leroy).Items);
        Assert.NotNull(_mail.OpenAttachment(Martin, received.Attachments[0].Id).Path);
        Assert.Null(_mail.OpenAttachment(new ArgosUserDto("9", "X", null, []), received.Attachments[0].Id).Path);

        Assert.True((await _mail.UpdateAsync(Martin, received.Id, new MailFlagRequest { Read = true, Folder = MailFolders.Corbeille })).Ok);
        Assert.False((await _mail.UpdateAsync(Leroy, received.Id, new MailFlagRequest { Read = true })).Ok);   // pas sa boîte
        Assert.True((await _mail.UpdateAsync(Martin, received.Id, new MailFlagRequest { Folder = MailFolders.Reception })).Ok);
        Assert.Equal(MailFolders.Reception, _mail.Snapshot(Martin).Items.Single().Folder);
        await _mail.UpdateAsync(Martin, received.Id, new MailFlagRequest { Folder = MailFolders.Corbeille });
        await _mail.EmptyTrashAsync(Martin);
        Assert.Empty(_mail.Snapshot(Martin).Items);
        Assert.NotNull(_mail.OpenAttachment(Leroy, received.Attachments[0].Id).Path);   // encore référencée ailleurs
    }

    [Fact]
    public void Repondre_a_tous()
    {
        var item = new MailItem { From = "a@p.fr", To = ["moi@p.fr", "b@p.fr"], Cc = ["c@p.fr", "moi@p.fr"] };
        var (to, cc) = MailCompose.ReplyAll(item, "moi@p.fr");
        Assert.Equal(["a@p.fr", "b@p.fr"], to);
        Assert.Equal(["c@p.fr"], cc);
        Assert.Equal("RE: Garde", MailCompose.ReplySubject("Garde"));
        Assert.Equal("RE: Garde", MailCompose.ReplySubject("RE: Garde"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
