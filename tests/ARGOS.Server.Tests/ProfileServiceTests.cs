using Argos.Server.Services;
using Argos.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Argos.Server.Tests;

/// <summary>Plusieurs personnages (dossiers SIGTAO) sur un même compte Discord.</summary>
public sealed class ProfileServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "argos-profils-" + Guid.NewGuid().ToString("N"));
    private readonly OdeDatabase _db;
    private readonly PersistentDiscordSessionStore _sessions;
    private readonly ProfileService _profiles;
    private readonly MailService _mail;
    private readonly string _token = "jeton-" + Guid.NewGuid().ToString("N");
    private static readonly ArgosUserDto Joueur = new("42", "Pseudo", null, [AccessLevels.Sapeur]);

    public ProfileServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new OdeDatabase(Path.Combine(_dir, "argos.db"));
        var serverOptions = new OdeServiceTests.StaticMonitor<ArgosServerOptions>(new ArgosServerOptions { AccessKey = "test" });
        _sessions = new PersistentDiscordSessionStore(serverOptions);
        _sessions.Save(_token, "42", "Pseudo", "rafraichissement");
        _profiles = new ProfileService(_db, _sessions);
        var oauth = new DiscordOAuthService(new OdeServiceTests.StaticMonitor<DiscordOAuthOptions>(new DiscordOAuthOptions()),
            new AuthSessionStore(), _sessions, NullLogger<DiscordOAuthService>.Instance, _profiles);
        _mail = new MailService(_db, new RealtimeHub(oauth, NullLogger<RealtimeHub>.Instance));

        var t = DateTimeOffset.UtcNow;
        Save(new MilitaryRecord { Id = "a", Nom = "Dupont", Prenom = "Jean", Grade = "Sergent", DiscordUserId = "42", CreatedAt = t.AddDays(-10), UpdatedAt = t });
        Save(new MilitaryRecord { Id = "b", Nom = "Martin", Prenom = "Léa", Grade = "Caporal", DiscordUserId = "42", CreatedAt = t, UpdatedAt = t.AddDays(-1) });
        Save(new MilitaryRecord { Id = "c", Nom = "Ancien", Prenom = "Paul", Grade = "Sapeur", DiscordUserId = "42", CreatedAt = t, UpdatedAt = t.AddDays(1), Status = MilitaryStatuses.Parti });
    }

    private void Save(MilitaryRecord r) => _db.SaveDoc(OdeDatabase.RemiseTables.Military, r.Id, DateTimeOffset.UtcNow, r);

    [Fact]
    public void Choix_du_profil_a_la_connexion()
    {
        // Deux dossiers actifs (le dossier « Parti » n'est pas proposé) : choix obligatoire.
        var list = _profiles.List(_token, Joueur);
        Assert.True(list.MustChoose);
        Assert.Equal(["a", "b"], list.Profiles.Select(p => p.Id));
        Assert.True(list.Profiles[0].Primary);

        // Sans choix : profil principal, identifiant Discord inchangé.
        var plain = _profiles.Apply(_token, Joueur);
        Assert.Equal("42", plain.UserId);
        Assert.Equal("a", ProfileService.DossierOf(_db, plain)?.Id);

        // Second personnage : compte distinct.
        Assert.Null(_profiles.Select(_token, Joueur, "c"));
        var second = _profiles.Select(_token, Joueur, "b")!;
        Assert.Equal("42~b", second.UserId);
        Assert.Equal("42", second.DiscordId);
        Assert.Equal("b", ProfileService.DossierOf(_db, second)?.Id);
        Assert.Contains("MARTIN", second.DisplayName);
        Assert.Equal("b", _profiles.List(_token, Joueur).SelectedId);

        // Chaque personnage a sa propre adresse et sa propre boîte.
        var mailA = _mail.Profile(plain).Address;
        var mailB = _mail.Profile(second).Address;
        Assert.Equal("jean.dupont@pompiersparis.fr", mailA);
        Assert.Equal("lea.martin@pompiersparis.fr", mailB);
        var resolver = _mail.Resolver();
        Assert.Equal("42", resolver[mailA]);
        Assert.Equal("42~b", resolver[mailB]);
        Assert.True(_mail.Identity(second).Complete);

        // Le nom affiché suit toujours le dossier, comme l'adresse.
        Assert.Equal("Jean DUPONT", _mail.Identity(plain).FullName);

        // Retour au profil principal.
        Assert.Equal("42", _profiles.Select(_token, Joueur, "a")!.UserId);
    }

    public void Dispose()
    {
        _sessions.Revoke(_token);
        try { Directory.Delete(_dir, true); } catch { }
    }
}
