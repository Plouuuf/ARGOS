using Argos.Shared.Contracts;
using static Argos.Server.Services.OdeDatabase;

namespace Argos.Server.Services;

/// <summary>
/// Profils (personnages RP) : chaque dossier SIGTAO actif relié à un compte Discord est un profil.
/// Avec plusieurs profils, le joueur choisit le sien à la connexion ; le choix est attaché au jeton du poste.
/// - Profil principal (dossier du compte SIGTAO, sinon le plus ancien) : identifiant = identifiant Discord,
///   donc toutes les données existantes (messagerie, comptes-rendus, fichiers…) restent les siennes.
/// - Autre profil : identifiant « discord~dossier », c'est-à-dire un compte distinct dans tout ARGOS.
/// Les rôles Discord (accès aux applications) sont communs à tous les profils du joueur.
/// </summary>
public sealed class ProfileService(OdeDatabase db, PersistentDiscordSessionStore sessions)
{
    private static bool IsActive(MilitaryRecord r) => r.Status is not (MilitaryStatuses.Parti or MilitaryStatuses.Archive);

    /// <summary>Dossiers actifs reliés au compte Discord, du plus ancien au plus récent.</summary>
    public static List<MilitaryRecord> Dossiers(OdeDatabase db, string discordId)
        => string.IsNullOrWhiteSpace(discordId)
            ? []
            : db.Docs<MilitaryRecord>(RemiseTables.Military)
                .Where(r => r.DiscordUserId == discordId && IsActive(r))
                .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id, StringComparer.Ordinal)
                .ToList();

    /// <summary>Dossier principal d'un joueur : celui de son compte SIGTAO, sinon le plus ancien.</summary>
    public static string? PrimaryId(OdeDatabase db, string discordId)
        => PrimaryId(db.Docs<MilitaryRecord>(RemiseTables.Military).Where(r => r.DiscordUserId == discordId),
            SigtaoAccess.AccountOf(db, discordId));

    /// <summary>
    /// Dossier principal : celui du compte SIGTAO s'il est en activité, sinon le dossier actif modifié le plus récemment
    /// (comme avant les profils). Un dossier « Parti » ou « Archivé » n'est jamais le dossier principal.
    /// </summary>
    private static string? PrimaryId(IEnumerable<MilitaryRecord> linked, UserAccount? account)
    {
        var active = linked.Where(IsActive).ToList();
        if (account is not null && active.FirstOrDefault(r => r.Id == account.DossierId) is { } fromAccount) return fromAccount.Id;
        return active.OrderByDescending(r => r.UpdatedAt).ThenBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault()?.Id;
    }

    /// <summary>Identifiant ARGOS du titulaire d'un dossier (messagerie, notifications).</summary>
    public static Dictionary<string, string> UserIdsByDossier(OdeDatabase db)
    {
        var records = db.Docs<MilitaryRecord>(RemiseTables.Military).Where(r => !string.IsNullOrWhiteSpace(r.DiscordUserId)).ToList();
        var accounts = SigtaoAccess.Accounts(db).Select(a => a.Account).ToList();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in records.GroupBy(r => r.DiscordUserId))
        {
            var primary = PrimaryId(group, accounts.FirstOrDefault(a => a.DiscordUserId == group.Key));
            foreach (var r in group)
                map[r.Id] = r.Id == primary ? r.DiscordUserId : ArgosUserDto.ProfileUserId(r.DiscordUserId, r.Id);
        }
        return map;
    }

    /// <summary>Dossier du profil connecté (profil choisi, sinon dossier principal).</summary>
    public static MilitaryRecord? DossierOf(OdeDatabase db, ArgosUserDto user)
    {
        var discord = user.DiscordId;
        if (user.ProfileId is { Length: > 0 } id
            && db.GetDoc<MilitaryRecord>(RemiseTables.Military, id) is { } chosen && chosen.DiscordUserId == discord)
            return chosen;
        var primary = PrimaryId(db, discord);
        return primary is null ? null : db.GetDoc<MilitaryRecord>(RemiseTables.Military, primary);
    }

    /// <summary>Utilisateur tel que vu par ARGOS pour ce jeton : avec le profil choisi à la connexion.</summary>
    public ArgosUserDto Apply(string appToken, ArgosUserDto user)
    {
        var discord = user.DiscordId;
        var plain = user with { UserId = discord, ProfileId = null };
        var chosen = sessions.GetProfile(appToken);
        if (string.IsNullOrWhiteSpace(chosen)) return plain;

        var dossiers = Dossiers(db, discord);
        if (dossiers.Count < 2 || dossiers.FirstOrDefault(d => d.Id == chosen) is not { } dossier) return plain;
        var primary = PrimaryId(db, discord);
        return plain with
        {
            UserId = chosen == primary ? discord : ArgosUserDto.ProfileUserId(discord, chosen),
            ProfileId = chosen,
            // Plusieurs personnages : les applications affichent le nom du personnage, pas le pseudo Discord.
            DisplayName = string.IsNullOrWhiteSpace(dossier.Nom) ? plain.DisplayName : dossier.FullName
        };
    }

    public ProfilesResponse List(string appToken, ArgosUserDto user)
    {
        var discord = user.DiscordId;
        var dossiers = Dossiers(db, discord);
        var primary = PrimaryId(db, discord);
        var selected = sessions.GetProfile(appToken);
        if (selected is not null && dossiers.All(d => d.Id != selected)) selected = null;
        return new ProfilesResponse
        {
            Profiles = dossiers
                .OrderByDescending(d => d.Id == primary)
                .Select(d => new ArgosProfile
                {
                    Id = d.Id,
                    Name = string.IsNullOrWhiteSpace(d.Nom) ? d.GradeLabel : d.FullName,
                    Grade = d.GradeLabel,
                    Affectation = string.Join(" · ", new[] { d.Unite, d.Affectation }.Where(s => !string.IsNullOrWhiteSpace(s))),
                    Fonction = d.Fonction,
                    PhotoData = d.PhotoData,
                    Primary = d.Id == primary
                }).ToList(),
            SelectedId = selected ?? primary,
            MustChoose = dossiers.Count > 1
        };
    }

    /// <summary>Choix du profil à la connexion. Retourne l'utilisateur mis à jour, ou null si ce profil n'est pas le sien.</summary>
    public ArgosUserDto? Select(string appToken, ArgosUserDto user, string profileId)
    {
        var dossiers = Dossiers(db, user.DiscordId);
        if (dossiers.All(d => d.Id != profileId)) return null;
        if (!sessions.SetProfile(appToken, profileId)) return null;
        return Apply(appToken, user);
    }
}
