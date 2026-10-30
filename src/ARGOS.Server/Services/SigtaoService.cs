using System.Security.Cryptography;
using Argos.Shared.Contracts;
using static Argos.Server.Services.OdeDatabase;

namespace Argos.Server.Services;

/// <summary>Compte tel qu'enregistré : le hachage du mot de passe ne quitte jamais le serveur.</summary>
public sealed class StoredAccount
{
    public UserAccount Account { get; set; } = new();
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
}

/// <summary>
/// Habilitations SIGTAO lues dans les comptes. Tant qu'aucun compte ne porte une habilitation,
/// les opérateurs la possèdent (mise en route du serveur sans se bloquer soi-même).
/// </summary>
public static class SigtaoAccess
{
    public static List<StoredAccount> Accounts(OdeDatabase db) => db.Docs<StoredAccount>(RemiseTables.Accounts);

    public static UserAccount? AccountOf(OdeDatabase db, string discordUserId)
        => Accounts(db).Select(a => a.Account).FirstOrDefault(a => a.DiscordUserId == discordUserId);

    public static bool Has(OdeDatabase db, ArgosUserDto user, string habilitation)
    {
        if (user.Levels.Contains(AccessLevels.Administration))
            return true;
        var accounts = Accounts(db).Select(a => a.Account).ToList();
        var mine = accounts.FirstOrDefault(a => a.DiscordUserId == user.UserId);
        if (mine is { Active: true } && mine.Habilitations.Contains(habilitation))
            return true;
        if (mine is { Active: false })
            return false;
        var nobodyHasIt = !accounts.Any(a => a.Active && a.Habilitations.Contains(habilitation));
        return nobodyHasIt && user.Levels.Any(l => l is AccessLevels.Operateur or AccessLevels.Encadrement);
    }
}

/// <summary>SIGTAO : dossiers militaires, comptes et habilitations, comptes-rendus, annuaire, historique.</summary>
public sealed class SigtaoService(OdeDatabase db, RealtimeHub hub)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Droits SIGTAO : le rôle Discord « Accès RH/ADMIN » gère la compagnie et lit tous les comptes-rendus.</summary>
    public SigtaoRights Rights(ArgosUserDto user)
    {
        var rh = AccessLevels.IsRhAdmin(user.Levels);
        return new SigtaoRights
        {
            ManageAccounts = rh,
            ManageCompany = rh,
            ReadAllReports = rh,
            ManageGarde = rh || user.Levels.Contains(AccessLevels.Encadrement) || user.Levels.Contains(AccessLevels.Administration),
            Habilitations = AppCatalogLabels.Where(a => AccessLevels.CanUse(user.Levels, a.Id)).Select(a => a.Label)
                .Concat(rh ? new[] { "RH / ADMIN" } : Array.Empty<string>()).ToList()
        };
    }

    private static readonly (string Id, string Label)[] AppCatalogLabels =
    [
        (ArgosApps.AdagioOde, "ADAGIO ODE"), (ArgosApps.AdagioRemise, "ADAGIO REMISE"), (ArgosApps.Efibi, "eFIBI"),
        (ArgosApps.Ilias, "ILIAS"), (ArgosApps.Mail, "Mail"), (ArgosApps.Mco, "MCO"), (ArgosApps.Sigtao, "SIGTAO")
    ];

    public SigtaoSnapshot Snapshot(ArgosUserDto user)
    {
        var rights = Rights(user);
        var accounts = SigtaoAccess.Accounts(db).Select(a => a.Account).ToList();
        var mine = accounts.FirstOrDefault(a => a.DiscordUserId == user.UserId);
        var military = db.Docs<MilitaryRecord>(RemiseTables.Military).ToList();

        // Dossier importé sans nom : le nom de famille est repris du pseudo Discord à la première connexion.
        foreach (var m in military.Where(m => m.DiscordUserId == user.UserId && string.IsNullOrWhiteSpace(m.Nom)))
        {
            var nom = RosterImport.NameFromNickname(user.DisplayName);
            if (nom.Length == 0) continue;
            m.Nom = nom;
            m.UpdatedAt = DateTimeOffset.UtcNow;
            m.UpdatedBy = "ARGOS";
            db.SaveDoc(RemiseTables.Military, m.Id, m.UpdatedAt, m);
            Journal(user, SigtaoJournal.DossierModifie, m.FullName, "nom repris du pseudo Discord");
        }
        // Photo par défaut : l'avatar Discord du joueur, mis à jour à chaque connexion.
        foreach (var m in military.Where(m => m.DiscordUserId == user.UserId && !string.IsNullOrWhiteSpace(user.AvatarUrl)
                                              && m.AvatarUrl != user.AvatarUrl))
        {
            m.AvatarUrl = user.AvatarUrl!;
            db.SaveDoc(RemiseTables.Military, m.Id, DateTimeOffset.UtcNow, m);
        }
        military = military.OrderBy(m => GradeRank(m.Grade)).ThenBy(m => m.Nom).ThenBy(m => m.Prenom).ToList();
        var myDossier = military.FirstOrDefault(m => m.Id == mine?.DossierId) ?? military.FirstOrDefault(m => m.DiscordUserId == user.UserId);

        // Rubriques du dossier (carrière, sanctions, entretiens) : gestion de la compagnie ou intéressé seulement.
        if (!rights.ManageCompany)
            foreach (var m in military.Where(m => m.Id != myDossier?.Id))
                m.Entries = [];

        var reports = db.Docs<CompteRendu>(RemiseTables.CrReports, 2000)
            .Where(r => r.AuthorUserId == user.UserId || (rights.ReadAllReports && !r.IsDraft))
            .ToList();

        return new SigtaoSnapshot
        {
            Military = military,
            Accounts = rights.ManageAccounts ? accounts : accounts.Where(a => a.DiscordUserId == user.UserId).ToList(),
            Reports = reports,
            Gardes = Gardes(DateTime.Today.AddDays(-62), DateTime.Today.AddDays(62)),
            Rights = rights,
            MyDossierId = myDossier?.Id,
            MyAccountId = mine?.Id,
            ServerTime = DateTimeOffset.UtcNow
        };
    }

    public List<JournalEntry> History(ArgosUserDto user)
    {
        var rights = Rights(user);
        if (!rights.ManageCompany && !rights.ManageAccounts)
            return [];
        return db.Journal(3000).Where(j => SigtaoJournal.Actions.Contains(j.Action)).ToList();
    }

    // ===== Dossiers militaires =====

    public async Task<OdeResult> SaveMilitaryAsync(ArgosUserDto user, MilitaryRecord incoming)
    {
        if (!Rights(user).ManageCompany)
            return new(false, "Habilitation insuffisante (gestion de la compagnie).");
        incoming.Nom = (incoming.Nom ?? string.Empty).Trim();
        incoming.Prenom = (incoming.Prenom ?? string.Empty).Trim();
        incoming.Nid = (incoming.Nid ?? string.Empty).Trim();
        if (incoming.Nom.Length == 0 && incoming.Prenom.Length == 0)
            return new(false, "Nom ou prénom obligatoire.");
        if (incoming.PhotoData.Length > 1_500_000)
            return new(false, "Photo trop lourde (1 Mo maximum).");
        if (!MilitaryStatuses.All.Contains(incoming.Status))
            incoming.Status = MilitaryStatuses.Actif;

        await _gate.WaitAsync();
        try
        {
            var all = db.Docs<MilitaryRecord>(RemiseTables.Military);
            if (incoming.Nid.Length > 0 && all.Any(m => m.Id != incoming.Id && m.Nid == incoming.Nid))
                return new(false, $"Le NID {incoming.Nid} est déjà attribué.");
            var existing = string.IsNullOrWhiteSpace(incoming.Id) ? null : all.FirstOrDefault(m => m.Id == incoming.Id);
            var now = DateTimeOffset.UtcNow;
            incoming.UpdatedAt = now;
            incoming.UpdatedBy = user.DisplayName;
            if (existing is null)
            {
                incoming.Id = Guid.NewGuid().ToString("N");
                incoming.CreatedAt = now;
                incoming.Entries = [];
                db.SaveDoc(RemiseTables.Military, incoming.Id, now, incoming);
                Journal(user, SigtaoJournal.DossierCree, incoming.FullName, $"NID {Or(incoming.Nid)} - {incoming.Unite} {incoming.Affectation}");
            }
            else
            {
                var changes = Changes(existing, incoming);
                incoming.CreatedAt = existing.CreatedAt;
                incoming.Entries = existing.Entries;          // les rubriques s'ajoutent une par une (traçabilité)
                db.SaveDoc(RemiseTables.Military, incoming.Id, now, incoming);
                if (changes.Count > 0)
                    Journal(user, SigtaoJournal.DossierModifie, incoming.FullName, string.Join(" ; ", changes));
            }
            await NotifyAsync();
            return new(true, null, incoming.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Ajoute une rubrique au dossier ; promotion, affectation et mutation mettent la fiche à jour.</summary>
    public async Task<OdeResult> AddEntryAsync(ArgosUserDto user, string dossierId, DossierEntry entry)
    {
        if (!Rights(user).ManageCompany)
            return new(false, "Habilitation insuffisante (gestion de la compagnie).");
        if (!DossierKinds.All.Contains(entry.Kind) || string.IsNullOrWhiteSpace(entry.Title))
            return new(false, "Choisis la rubrique et renseigne l'intitulé.");
        await _gate.WaitAsync();
        try
        {
            var record = db.GetDoc<MilitaryRecord>(RemiseTables.Military, dossierId);
            if (record is null) return new(false, "Dossier introuvable.");
            var now = DateTimeOffset.UtcNow;
            entry.Id = Guid.NewGuid().ToString("N")[..10];
            entry.Title = entry.Title.Trim();
            entry.Details = (entry.Details ?? string.Empty).Trim();
            entry.By = user.DisplayName;
            entry.RecordedAt = now;
            if (entry.Date == default) entry.Date = now;
            record.Entries.Add(entry);

            switch (entry.Kind)
            {
                case DossierKinds.Promotion when BsppGrades.All.Contains(entry.Title):
                    record.Grade = entry.Title;
                    record.GradeDate = entry.Date;
                    break;
                case DossierKinds.Affectation:
                    record.Affectation = entry.Title.ToUpperInvariant();
                    break;
                case DossierKinds.Mutation:
                    record.Status = entry.Title.Contains("départ", StringComparison.OrdinalIgnoreCase)
                        ? MilitaryStatuses.Parti : MilitaryStatuses.Mute;
                    break;
            }
            record.UpdatedAt = now;
            record.UpdatedBy = user.DisplayName;
            db.SaveDoc(RemiseTables.Military, record.Id, now, record);
            Journal(user, SigtaoJournal.Rubrique, record.FullName, $"{entry.Kind} : {entry.Title}");
            await NotifyAsync();
            return new(true, null, record.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ===== Comptes et habilitations =====

    public async Task<OdeResult> SaveAccountAsync(ArgosUserDto user, UserAccount incoming)
    {
        if (!Rights(user).ManageAccounts)
            return new(false, "Habilitation SADMIN requise.");
        incoming.DiscordUserId = (incoming.DiscordUserId ?? string.Empty).Trim();
        // Identifiant Discord repris du dossier choisi s'il n'est pas saisi.
        if (incoming.DiscordUserId.Length == 0
            && db.GetDoc<MilitaryRecord>(RemiseTables.Military, incoming.DossierId ?? string.Empty) is { DiscordUserId.Length: > 0 } fromDossier)
            incoming.DiscordUserId = fromDossier.DiscordUserId;
        if (incoming.DiscordUserId.Length == 0 || !incoming.DiscordUserId.All(char.IsDigit))
            return new(false, "Identifiant Discord du joueur obligatoire (nombre, clic droit › Copier l'identifiant).");
        incoming.Habilitations = incoming.Habilitations.Where(h => Habilitations.All.Any(a => a.Code == h)).Distinct().ToList();

        await _gate.WaitAsync();
        try
        {
            var all = SigtaoAccess.Accounts(db);
            if (all.Any(a => a.Account.Id != incoming.Id && a.Account.DiscordUserId == incoming.DiscordUserId))
                return new(false, "Un compte existe déjà pour ce joueur Discord.");
            // Le compte reprend l'identité de son dossier militaire.
            if (db.GetDoc<MilitaryRecord>(RemiseTables.Military, incoming.DossierId ?? string.Empty) is { } dossier)
            {
                incoming.Grade = dossier.Grade;
                incoming.Nom = dossier.Nom;
                incoming.Prenom = dossier.Prenom;
                incoming.Nid = dossier.Nid;
                if (string.IsNullOrWhiteSpace(incoming.Email)) incoming.Email = dossier.Email;
                // Et le dossier reçoit l'identifiant Discord du compte s'il ne l'avait pas.
                if (dossier.DiscordUserId.Length == 0)
                {
                    dossier.DiscordUserId = incoming.DiscordUserId;
                    dossier.UpdatedAt = DateTimeOffset.UtcNow;
                    dossier.UpdatedBy = user.DisplayName;
                    db.SaveDoc(RemiseTables.Military, dossier.Id, dossier.UpdatedAt, dossier);
                }
            }

            var existing = all.FirstOrDefault(a => a.Account.Id == incoming.Id);
            var now = DateTimeOffset.UtcNow;
            // Garde-fou : on ne retire pas sa propre habilitation SADMIN (ni ne désactive son compte).
            if (existing?.Account.DiscordUserId == user.UserId && existing.Account.Habilitations.Contains(Habilitations.Sadmin)
                && (!incoming.Habilitations.Contains(Habilitations.Sadmin) || !incoming.Active)
                && !user.Levels.Contains(AccessLevels.Administration))
                return new(false, "Tu ne peux pas retirer ta propre habilitation SADMIN ni désactiver ton compte.");

            var stored = existing ?? new StoredAccount();
            var before = existing?.Account;
            incoming.Id = existing?.Account.Id ?? Guid.NewGuid().ToString("N");
            incoming.CreatedAt = existing?.Account.CreatedAt ?? now;
            incoming.LastLoginAt = existing?.Account.LastLoginAt;
            incoming.HasPassword = stored.PasswordHash.Length > 0;
            incoming.UpdatedAt = now;
            stored.Account = incoming;
            db.SaveDoc(RemiseTables.Accounts, incoming.Id, now, stored);

            var who = $"{BsppGrades.Short(incoming.Grade)} {incoming.Nom.ToUpperInvariant()} {incoming.Prenom}".Trim();
            if (before is null)
                Journal(user, SigtaoJournal.CompteCree, who, "Habilitations : " + Or(string.Join(", ", incoming.Habilitations)));
            else
            {
                var changes = new List<string>();
                var added = incoming.Habilitations.Except(before.Habilitations).ToList();
                var removed = before.Habilitations.Except(incoming.Habilitations).ToList();
                if (added.Count > 0) changes.Add("habilitations ajoutées : " + string.Join(", ", added));
                if (removed.Count > 0) changes.Add("habilitations retirées : " + string.Join(", ", removed));
                if (before.Active != incoming.Active) changes.Add(incoming.Active ? "compte activé" : "compte désactivé");
                if (before.Email != incoming.Email) changes.Add("adresse électronique");
                if (before.DossierId != incoming.DossierId) changes.Add("dossier associé");
                if (before.Grade != incoming.Grade || before.Nom != incoming.Nom || before.Prenom != incoming.Prenom || before.Nid != incoming.Nid)
                    changes.Add("identité");
                if (changes.Count > 0) Journal(user, SigtaoJournal.CompteModifie, who, string.Join(" ; ", changes));
            }
            await NotifyAsync();
            return new(true, null, incoming.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Réinitialise les accès : nouveau mot de passe provisoire, affiché une seule fois.</summary>
    public async Task<AccountPasswordResult> ResetAccessAsync(ArgosUserDto user, string accountId)
    {
        if (!Rights(user).ManageAccounts)
            return new AccountPasswordResult { Error = "Habilitation SADMIN requise." };
        await _gate.WaitAsync();
        try
        {
            var stored = SigtaoAccess.Accounts(db).FirstOrDefault(a => a.Account.Id == accountId);
            if (stored is null) return new AccountPasswordResult { Error = "Compte introuvable." };
            var password = NewPassword();
            SetPassword(stored, password);
            stored.Account.HasPassword = true;
            stored.Account.UpdatedAt = DateTimeOffset.UtcNow;
            db.SaveDoc(RemiseTables.Accounts, stored.Account.Id, stored.Account.UpdatedAt, stored);
            Journal(user, SigtaoJournal.Acces, $"{stored.Account.Nom.ToUpperInvariant()} {stored.Account.Prenom}", "Mot de passe provisoire généré.");
            await NotifyAsync();
            return new AccountPasswordResult { Ok = true, TemporaryPassword = password };
        }
        finally
        {
            _gate.Release();
        }
    }

    public static void SetPassword(StoredAccount stored, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        stored.PasswordSalt = Convert.ToBase64String(salt);
        stored.PasswordHash = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32));
    }

    public static bool CheckPassword(StoredAccount stored, string password)
    {
        if (stored.PasswordHash.Length == 0) return false;
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(stored.PasswordSalt), 100_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(stored.PasswordHash));
    }

    private static string NewPassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        return new string(Enumerable.Range(0, 10).Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)]).ToArray());
    }

    // ===== Comptes-rendus =====

    /// <summary>
    /// Enregistre un compte-rendu. Brouillon (<see cref="CompteRendu.IsDraft"/>) : enregistrement libre, sans numéro,
    /// modifiable ensuite par son rédacteur. Sinon : contrôles complets, numéro attribué et compte-rendu figé.
    /// </summary>
    public async Task<OdeResult> SaveReportAsync(ArgosUserDto user, CompteRendu cr)
    {
        if (cr.IsDraft)
        {
            if (string.IsNullOrWhiteSpace(cr.Objet) && string.IsNullOrWhiteSpace(cr.Body))
                return new(false, "Brouillon vide : saisis au moins l'objet ou le texte.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(cr.Objet) || string.IsNullOrWhiteSpace(cr.Body))
                return new(false, "Objet et texte du compte-rendu obligatoires.");
            if (string.IsNullOrWhiteSpace(cr.AuthorNom) || string.IsNullOrWhiteSpace(cr.DestNom))
                return new(false, "Rédacteur et destinataire obligatoires.");
        }
        cr.Attachments = cr.Attachments.Where(a => a.Data.Length > 0).ToList();
        if (cr.Attachments.Sum(a => a.Data.Length) > 8_000_000)
            return new(false, "Pièces jointes trop lourdes (6 Mo au total).");

        await _gate.WaitAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(cr.Id))
            {
                var existing = db.GetDoc<CompteRendu>(RemiseTables.CrReports, cr.Id);
                if (existing is null || existing.AuthorUserId != user.UserId)
                    return new(false, "Brouillon introuvable.");
                if (!existing.IsDraft)
                    return new(false, "Ce compte-rendu a déjà été transmis : il n'est plus modifiable.");
                cr.CreatedAt = existing.CreatedAt;
            }
            else
            {
                cr.Id = Guid.NewGuid().ToString("N");
                cr.CreatedAt = now;
            }
            cr.AuthorUserId = user.UserId;
            cr.UpdatedAt = now;
            if (cr.Date == default) cr.Date = now;

            if (cr.IsDraft)
            {
                cr.NumDoc = string.Empty;
                db.SaveDoc(RemiseTables.CrReports, cr.Id, now, cr);
                await NotifyAsync();
                return new(true, null, cr.Id);
            }

            cr.NumDoc = $"{now:yyyy}-{db.NextCounter("cr-" + now.ToString("yyyy")):0000}";
            cr.Date = now;
            if (string.IsNullOrWhiteSpace(cr.Synthese)) cr.Synthese = CrText.Synthese(cr);
            db.SaveDoc(RemiseTables.CrReports, cr.Id, now, cr);
            Journal(user, SigtaoJournal.CompteRendu, "N° " + cr.NumDoc, $"{cr.Objet} - destinataire {cr.DestGrade} {cr.DestNom}");
            await NotifyAsync();
            return new(true, null, cr.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Supprime un brouillon (rédacteur seulement ; un compte-rendu transmis ne se supprime pas).</summary>
    public async Task<OdeResult> DeleteDraftAsync(ArgosUserDto user, string id)
    {
        await _gate.WaitAsync();
        try
        {
            var existing = db.GetDoc<CompteRendu>(RemiseTables.CrReports, id);
            if (existing is null || existing.AuthorUserId != user.UserId || !existing.IsDraft)
                return new(false, "Brouillon introuvable.");
            db.DeleteDoc(RemiseTables.CrReports, id);
            await NotifyAsync();
            return new(true, null, id);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ===== Feuilles de garde =====

    public List<FeuilleGarde> Gardes(DateTime from, DateTime to)
    {
        var a = from.ToString("yyyy-MM-dd");
        var b = to.ToString("yyyy-MM-dd");
        return db.Docs<FeuilleGarde>(RemiseTables.Gardes)
            .Where(g => string.CompareOrdinal(g.Date, a) >= 0 && string.CompareOrdinal(g.Date, b) <= 0)
            .OrderBy(g => g.Date).ThenBy(g => g.Centre).ToList();
    }

    /// <summary>
    /// Enregistre (et éventuellement valide) une feuille de garde. Le nom des militaires est figé depuis leur dossier ;
    /// un militaire ne peut tenir qu'un poste par feuille ni être de garde dans deux centres le même jour.
    /// </summary>
    public async Task<OdeResult> SaveGardeAsync(ArgosUserDto user, FeuilleGarde feuille, bool valider)
    {
        if (!Rights(user).ManageGarde)
            return new(false, "Habilitation insuffisante : la garde est préparée par l'encadrement.");
        if (!DateTime.TryParseExact(feuille.Date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _))
            return new(false, "Date de garde invalide.");
        feuille.Centre = (feuille.Centre ?? string.Empty).Trim().ToUpperInvariant();
        if (feuille.Centre.Length == 0)
            return new(false, "Centre de secours obligatoire.");

        await _gate.WaitAsync();
        try
        {
            var military = db.Docs<MilitaryRecord>(RemiseTables.Military).ToDictionary(m => m.Id);
            string? error = null;
            void Fix(GardeAffectation a)
            {
                a.Poste = (a.Poste ?? string.Empty).Trim();
                a.MilitaryId = (a.MilitaryId ?? string.Empty).Trim();
                if (a.MilitaryId.Length == 0) { a.Nom = string.Empty; return; }
                if (!military.TryGetValue(a.MilitaryId, out var m)) { error ??= "Militaire introuvable dans les dossiers."; return; }
                a.Nom = $"{BsppGrades.Short(m.Grade)} {m.Nom.ToUpperInvariant()} {m.Prenom}".Trim();
            }
            feuille.Service ??= [];
            feuille.Engins ??= [];
            feuille.Reserve ??= [];
            foreach (var a in feuille.Service) Fix(a);
            foreach (var e in feuille.Engins) { e.Postes ??= []; foreach (var a in e.Postes) Fix(a); }
            feuille.Reserve = feuille.Reserve.Where(r => !string.IsNullOrWhiteSpace(r.MilitaryId)).ToList();
            foreach (var a in feuille.Reserve) { if (a.Poste.Length == 0) a.Poste = "Réserve"; Fix(a); }
            if (error is not null) return new(false, error);

            var doubles = feuille.Affectations().GroupBy(a => a.Affectation.MilitaryId).Where(g => g.Count() > 1)
                .Select(g => g.First().Affectation.Nom).ToList();
            if (doubles.Count > 0)
                return new(false, $"Affecté(s) à plusieurs postes : {string.Join(", ", doubles)}.");

            feuille.Id = FeuilleGarde.BuildId(feuille.Date, feuille.Centre);
            var ids = feuille.Affectations().Select(a => a.Affectation.MilitaryId).ToHashSet();
            var ailleurs = db.Docs<FeuilleGarde>(RemiseTables.Gardes)
                .Where(g => g.Date == feuille.Date && g.Id != feuille.Id)
                .SelectMany(g => g.Affectations().Where(a => ids.Contains(a.Affectation.MilitaryId)).Select(a => $"{a.Affectation.Nom} ({g.Centre})"))
                .ToList();
            if (ailleurs.Count > 0)
                return new(false, $"Déjà de garde dans un autre centre ce jour-là : {string.Join(", ", ailleurs)}.");

            var existing = db.GetDoc<FeuilleGarde>(RemiseTables.Gardes, feuille.Id);
            var now = DateTimeOffset.UtcNow;
            feuille.UpdatedAt = now;
            feuille.UpdatedBy = user.DisplayName;
            if (valider)
            {
                feuille.Statut = GardeStatuts.Validee;
                feuille.ValidatedAt = now;
                feuille.ValidatedBy = user.DisplayName;
            }
            else
            {
                feuille.Statut = existing?.Statut ?? GardeStatuts.Brouillon;
                feuille.ValidatedAt = existing?.ValidatedAt;
                feuille.ValidatedBy = existing?.ValidatedBy ?? string.Empty;
            }
            db.SaveDoc(RemiseTables.Gardes, feuille.Id, now, feuille);
            var nonArmes = feuille.Engins.Count(e => !e.Arme);
            Journal(user, SigtaoJournal.Garde, $"{feuille.Centre} {feuille.Date}",
                $"{(valider ? "validée" : "enregistrée")} - effectif {feuille.Effectif}{(nonArmes > 0 ? $", {nonArmes} engin(s) non armé(s)" : string.Empty)}");
            await NotifyAsync();
            return new(true, null, feuille.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public FeuilleGarde? Garde(string id) => db.GetDoc<FeuilleGarde>(RemiseTables.Gardes, id);

    // ===== Import de l'effectif =====

    /// <summary>Crée ou met à jour les dossiers depuis le tableau d'effectif Discord.</summary>
    public async Task<OdeResult> ImportRosterAsync(ArgosUserDto user, string text)
    {
        if (!Rights(user).ManageCompany)
            return new(false, "Habilitation insuffisante (gestion de la compagnie).");
        var parsed = RosterImport.Parse(text ?? string.Empty);
        if (parsed.Count == 0)
            return new(false, "Aucun militaire reconnu : colle le message d'effectif tel qu'il est sur Discord (avec les insignes de grade).");

        await _gate.WaitAsync();
        try
        {
            var all = db.Docs<MilitaryRecord>(RemiseTables.Military);
            var now = DateTimeOffset.UtcNow;
            int created = 0, updated = 0;
            foreach (var p in parsed)
            {
                var existing = all.FirstOrDefault(m => p.DiscordUserId.Length > 0 && m.DiscordUserId == p.DiscordUserId)
                               ?? all.FirstOrDefault(m => p.Nid.Length > 0 && m.Nid == p.Nid)
                               ?? all.FirstOrDefault(m => p.Matricule.Length > 0 && m.Matricule == p.Matricule)
                               ?? all.FirstOrDefault(m => p.Nom.Length > 0 && m.Nom.Equals(p.Nom, StringComparison.OrdinalIgnoreCase)
                                                          && m.Prenom.Equals(p.Prenom, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    p.Id = Guid.NewGuid().ToString("N");
                    p.CreatedAt = now;
                    p.UpdatedAt = now;
                    p.UpdatedBy = user.DisplayName;
                    db.SaveDoc(RemiseTables.Military, p.Id, now, p);
                    all.Add(p);
                    created++;
                    continue;
                }
                var before = existing.FullName + existing.Fonction + existing.Nid + existing.Matricule;
                if (existing.Grade != p.Grade)
                {
                    existing.Entries.Add(new DossierEntry
                    {
                        Kind = DossierKinds.Promotion, Title = p.Grade, Date = now, By = user.DisplayName, RecordedAt = now,
                        Details = "Mise à jour depuis l'effectif Discord."
                    });
                    existing.Grade = p.Grade;
                    existing.GradeDate = now;
                }
                existing.Echelon = p.Echelon;
                if (p.Prenom.Length > 0) existing.Prenom = p.Prenom;
                if (p.Nom.Length > 0) existing.Nom = p.Nom;
                if (p.Fonction.Length > 0) existing.Fonction = p.Fonction;
                if (p.Nid.Length > 0) existing.Nid = p.Nid;
                if (p.Matricule.Length > 0) existing.Matricule = p.Matricule;
                if (p.DiscordUserId.Length > 0) existing.DiscordUserId = p.DiscordUserId;
                if (existing.FullName + existing.Fonction + existing.Nid + existing.Matricule == before) continue;
                existing.UpdatedAt = now;
                existing.UpdatedBy = user.DisplayName;
                db.SaveDoc(RemiseTables.Military, existing.Id, now, existing);
                updated++;
            }
            Journal(user, SigtaoJournal.DossierModifie, "Effectif de la compagnie",
                $"Import Discord : {created} dossier(s) créé(s), {updated} mis à jour.");
            await NotifyAsync();
            return new(true, $"{created} dossier(s) créé(s), {updated} mis à jour.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private static int GradeRank(string grade)
    {
        var index = BsppGrades.Rank(grade);
        return index < 0 ? 0 : -index;                       // du plus haut grade au plus bas
    }

    // ===== Outils =====

    private static List<string> Changes(MilitaryRecord a, MilitaryRecord b)
    {
        var changes = new List<string>();
        void Check(string label, object? x, object? y)
        {
            if (!Equals(x?.ToString() ?? string.Empty, y?.ToString() ?? string.Empty))
                changes.Add($"{label} : {Or(x?.ToString())} → {Or(y?.ToString())}");
        }
        Check("grade", a.Grade, b.Grade);
        Check("nom", a.Nom, b.Nom);
        Check("prénom", a.Prenom, b.Prenom);
        Check("NID", a.Nid, b.Nid);
        Check("matricule", a.Matricule, b.Matricule);
        Check("unité", a.Unite, b.Unite);
        Check("affectation", a.Affectation, b.Affectation);
        Check("section", a.Section, b.Section);
        Check("fonction", a.Fonction, b.Fonction);
        Check("statut", a.Status, b.Status);
        Check("adresse électronique", a.Email, b.Email);
        Check("téléphone", a.Telephone, b.Telephone);
        Check("prise de grade", a.GradeDate?.ToString("dd/MM/yyyy"), b.GradeDate?.ToString("dd/MM/yyyy"));
        Check("arrivée", a.ArrivalDate?.ToString("dd/MM/yyyy"), b.ArrivalDate?.ToString("dd/MM/yyyy"));
        if (a.PhotoData != b.PhotoData) changes.Add("photo d'identité");
        if (a.DiscordUserId != b.DiscordUserId) changes.Add("joueur Discord associé");
        return changes;
    }

    private void Journal(ArgosUserDto user, string action, string target, string details)
        => db.AddJournal(new JournalEntry
        {
            At = DateTimeOffset.UtcNow, UserId = user.UserId, UserName = user.DisplayName,
            Action = action, Target = target, Details = details
        });

    private Task NotifyAsync() => hub.BroadcastAsync(new RealtimeMessage(RealtimeTypes.SigtaoChanged));

    private static string Or(string? v) => string.IsNullOrWhiteSpace(v) ? "—" : v;
}
