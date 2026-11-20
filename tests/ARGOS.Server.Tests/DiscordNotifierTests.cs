using Argos.Server.Services;
using Argos.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Argos.Server.Tests;

/// <summary>Notifications Discord : préférences, décision d'envoi (sans réseau), déclencheurs mail et garde.</summary>
public sealed class DiscordNotifierTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "argos-notif-" + Guid.NewGuid().ToString("N"));
    private readonly OdeDatabase _db;
    private readonly NotificationPreferencesService _preferences;
    private readonly FakeMessenger _messenger = new();
    private readonly HashSet<string> _online = [];
    private readonly DiscordNotifier _notifier;
    private readonly RealtimeHub _hub;

    private static readonly ArgosUserDto Santos = new("1", "Santos", null, [AccessLevels.Sapeur]);
    private static readonly ArgosUserDto Martin = new("2", "Martin", null, [AccessLevels.Sapeur]);
    private static readonly ArgosUserDto Chef = new("100", "Chef", null,
        [AccessLevels.Sapeur, AccessLevels.App(ArgosApps.Sigtao), AccessLevels.RhAdmin]);

    public DiscordNotifierTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new OdeDatabase(Path.Combine(_dir, "argos.db"));
        _preferences = new NotificationPreferencesService(_db);
        _notifier = new DiscordNotifier(_preferences, _messenger, id => _online.Contains(id), NullLogger<DiscordNotifier>.Instance);
        var serverOptions = new OdeServiceTests.StaticMonitor<ArgosServerOptions>(new ArgosServerOptions { AccessKey = "test" });
        var oauth = new DiscordOAuthService(new OdeServiceTests.StaticMonitor<DiscordOAuthOptions>(new DiscordOAuthOptions()),
            new AuthSessionStore(), new PersistentDiscordSessionStore(serverOptions), NullLogger<DiscordOAuthService>.Instance);
        _hub = new RealtimeHub(oauth, NullLogger<RealtimeHub>.Instance);
    }

    [Fact]
    public void Preferences_par_defaut_et_enregistrement()
    {
        var defaut = _preferences.Get("1");
        Assert.True(defaut.Enabled && defaut.Mail && defaut.Garde && defaut.CompteRendu);

        Assert.True(_preferences.Save("1", new NotificationPreferences { Enabled = true, Mail = false, Garde = true, CompteRendu = false }).Ok);
        var saved = _preferences.Get("1");
        Assert.False(saved.Mail);
        Assert.True(saved.Garde);
        Assert.False(saved.CompteRendu);
        Assert.True(_preferences.Get("2").Mail);                      // un autre compte garde les valeurs par défaut
        Assert.False(_preferences.Save("", new NotificationPreferences()).Ok);

        Assert.False(new NotificationPreferences { Enabled = false }.Allows(NotificationKind.Garde));
        Assert.True(new NotificationPreferences { Enabled = false }.Allows(NotificationKind.Test));
    }

    [Fact]
    public async Task Decision_preferences_et_connexion()
    {
        // Tout activé, ARGOS fermé : envoi au compte Discord de base, même depuis un profil secondaire.
        Assert.True(await _notifier.NotifyAsync("5~dossier9", NotificationKind.Mail, "Titre", "Texte"));
        Assert.Equal("5", _messenger.Sent.Single().DiscordId);
        Assert.Equal(DiscordNotifier.ColorOf(NotificationKind.Mail), _messenger.Sent.Single().Message.Color);

        // ARGOS ouvert : rien.
        _online.Add("5");
        Assert.False(await _notifier.NotifyAsync("5", NotificationKind.Mail, "Titre", "Texte"));
        _online.Clear();

        // Type désactivé : rien ; les autres types passent.
        _preferences.Save("5", new NotificationPreferences { Mail = false });
        Assert.False(await _notifier.NotifyAsync("5", NotificationKind.Mail, "Titre", "Texte"));
        Assert.True(await _notifier.NotifyAsync("5", NotificationKind.Garde, "Titre", "Texte"));

        // Interrupteur général coupé : rien du tout.
        _preferences.Save("5", new NotificationPreferences { Enabled = false });
        Assert.False(await _notifier.NotifyAsync("5", NotificationKind.Garde, "Titre", "Texte"));
        Assert.Equal(2, _messenger.Sent.Count);

        // Test depuis les Paramètres : envoyé même ARGOS ouvert et notifications coupées.
        _online.Add("5");
        Assert.True((await _notifier.SendTestAsync(new ArgosUserDto("5~dossier9", "Lea", null, []))).Ok);
        Assert.Equal("5", _messenger.Sent.Last().DiscordId);
    }

    [Fact]
    public async Task Sans_bot_rien_ne_part_et_le_test_explique_pourquoi()
    {
        _messenger.IsConfigured = false;
        _notifier.Notify("5", NotificationKind.Mail, "Titre", "Texte");
        Assert.Equal(0, await _notifier.FlushAsync());
        Assert.False(await _notifier.NotifyAsync("5", NotificationKind.Mail, "Titre", "Texte"));
        var test = await _notifier.SendTestAsync(Martin);
        Assert.False(test.Ok);
        Assert.Contains("pas encore configuré", test.Error);
        Assert.Empty(_messenger.Sent);
    }

    [Fact]
    public async Task Echec_d_envoi_renvoie_le_message_clair()
    {
        _messenger.Failure = "Discord refuse le message : autorise les messages privés.";
        var test = await _notifier.SendTestAsync(Martin);
        Assert.False(test.Ok);
        Assert.Equal(_messenger.Failure, test.Error);
    }

    [Fact]
    public async Task Mail_notifie_les_destinataires_pas_l_expediteur()
    {
        var mail = new MailService(_db, _hub, _notifier);
        mail.Snapshot(Santos);
        mail.Snapshot(Martin);
        // Boîte d'un profil secondaire du joueur 4.
        _db.SaveDoc(OdeDatabase.RemiseTables.MailProfiles, "4~d2", DateTimeOffset.UtcNow,
            new MailProfile { UserId = "4~d2", Address = "lea.durand@pompiersparis.fr", Name = "CPL DURAND Léa" });

        var sent = await mail.SendAsync(Santos, new MailDraftRequest
        {
            To = "martin@pompiersparis.fr; lea.durand@pompiersparis.fr", Cc = "santos@pompiersparis.fr", Subject = "Planning"
        });
        Assert.True(sent.Ok, sent.Error);
        Assert.Equal(2, await _notifier.FlushAsync());

        Assert.Equal(["2", "4"], _messenger.Sent.Select(s => s.DiscordId).Order().ToArray());
        Assert.All(_messenger.Sent, s => Assert.StartsWith("📧 Nouveau mail de", s.Message.Title));
        Assert.All(_messenger.Sent, s => Assert.Contains("Planning", s.Message.Description));
        var secondaire = _messenger.Sent.Single(s => s.DiscordId == "4");
        Assert.Contains("lea.durand@pompiersparis.fr", secondaire.Message.Description);
        Assert.DoesNotContain(_messenger.Sent, s => s.DiscordId == "1");
    }

    [Fact]
    public async Task Garde_validee_un_message_par_personne_sans_doublon()
    {
        var sigtao = new SigtaoService(_db, _hub, _notifier);
        await sigtao.SaveMilitaryAsync(Chef, new MilitaryRecord { Nom = "Durand", Prenom = "Léa", Grade = "Caporal", DiscordUserId = "300" });
        await sigtao.SaveMilitaryAsync(Chef, new MilitaryRecord { Nom = "Petit", Prenom = "Marc", Grade = "Sergent", DiscordUserId = "400" });
        await sigtao.SaveMilitaryAsync(Chef, new MilitaryRecord { Nom = "Sans", Prenom = "Discord", Grade = "Sapeur" });
        var all = sigtao.Snapshot(Chef).Military;
        var lea = all.Single(m => m.Nom == "Durand").Id;
        var marc = all.Single(m => m.Nom == "Petit").Id;
        var sans = all.Single(m => m.Nom == "Sans").Id;
        var date = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");

        FeuilleGarde Feuille(string marcPoste) => new()
        {
            Date = date, Centre = "CRET",
            Service = [new GardeAffectation { Poste = GardePostes.ChefDeGarde, MilitaryId = lea }],
            Engins =
            [
                new GardeEngin
                {
                    Engin = "VSAV 947 CRET", Type = "VSAV",
                    Postes =
                    [
                        new GardeAffectation { Poste = "C/A", MilitaryId = lea },
                        new GardeAffectation { Poste = marcPoste, MilitaryId = marc },
                        new GardeAffectation { Poste = "EQP2", MilitaryId = sans }
                    ]
                }
            ]
        };

        // Simple enregistrement : aucune notification.
        Assert.True((await sigtao.SaveGardeAsync(Chef, Feuille("COND"), false)).Ok);
        Assert.Equal(0, await _notifier.FlushAsync());

        // Validation : un message par militaire relié à Discord, postes regroupés.
        Assert.True((await sigtao.SaveGardeAsync(Chef, Feuille("COND"), true)).Ok);
        Assert.Equal(2, await _notifier.FlushAsync());
        var leaMessage = _messenger.Sent.Single(s => s.DiscordId == "300").Message;
        Assert.StartsWith("🚒", leaMessage.Title);
        Assert.Contains(GardePostes.ChefDeGarde, leaMessage.Description);
        Assert.Contains("C/A VSAV 947 CRET", leaMessage.Description);
        Assert.Contains("COND VSAV 947 CRET", _messenger.Sent.Single(s => s.DiscordId == "400").Message.Description);

        // Nouvelle validation sans changement : rien ; changement de poste pour Marc : lui seul est prévenu.
        Assert.True((await sigtao.SaveGardeAsync(Chef, Feuille("COND"), true)).Ok);
        Assert.Equal(0, await _notifier.FlushAsync());
        Assert.True((await sigtao.SaveGardeAsync(Chef, Feuille("EQP"), true)).Ok);
        Assert.Equal(1, await _notifier.FlushAsync());
        Assert.Equal("400", _messenger.Sent.Last().DiscordId);
        Assert.Contains("EQP VSAV 947 CRET", _messenger.Sent.Last().Message.Description);
    }

    [Fact]
    public async Task Compte_rendu_transmis_notifie_le_destinataire()
    {
        var sigtao = new SigtaoService(_db, _hub, _notifier);
        await sigtao.SaveMilitaryAsync(Chef, new MilitaryRecord { Nom = "Faubourg", Prenom = "Laurent", Grade = "Capitaine", DiscordUserId = "500" });
        var auteur = new ArgosUserDto("2", "Martin", null, [AccessLevels.Sapeur, AccessLevels.App(ArgosApps.Sigtao)]);

        Assert.True((await sigtao.SaveReportAsync(auteur, new CompteRendu { Objet = "Brouillon", Body = "x", IsDraft = true })).Ok);
        Assert.Equal(0, await _notifier.FlushAsync());

        var ok = await sigtao.SaveReportAsync(auteur, new CompteRendu
        {
            Objet = "Retard", Body = "Texte.", AuthorGrade = "SAP", AuthorNom = "Martin", DestGrade = "CNE", DestNom = "FAUBOURG"
        });
        Assert.True(ok.Ok, ok.Error);
        Assert.Equal(1, await _notifier.FlushAsync());
        Assert.Equal("500", _messenger.Sent.Single().DiscordId);
        Assert.Contains("Retard", _messenger.Sent.Single().Message.Description);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch { /* fichier encore ouvert par SQLite : sans importance */ }
    }

    private sealed class FakeMessenger : IDiscordDirectMessenger
    {
        public bool IsConfigured { get; set; } = true;
        public string? Failure { get; set; }
        public List<(string DiscordId, DiscordDirectMessage Message)> Sent { get; } = [];

        public Task<DirectMessageResult> SendAsync(string discordId, DiscordDirectMessage message, CancellationToken ct = default)
        {
            if (Failure is not null)
                return Task.FromResult(new DirectMessageResult(false, Failure));
            Sent.Add((discordId, message));
            return Task.FromResult(new DirectMessageResult(true));
        }
    }
}
