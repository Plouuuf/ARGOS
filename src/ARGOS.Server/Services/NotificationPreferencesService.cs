using Argos.Shared.Contracts;
using static Argos.Server.Services.OdeDatabase;

namespace Argos.Server.Services;

/// <summary>
/// Préférences de notification Discord, une par compte Discord (table user_notifications) :
/// les différents profils (personnages) d'un même joueur partagent les mêmes réglages.
/// </summary>
public sealed class NotificationPreferencesService(OdeDatabase db)
{
    /// <summary>Préférences du compte Discord ; tout activé tant que rien n'a été enregistré.</summary>
    public NotificationPreferences Get(string discordId)
        => string.IsNullOrWhiteSpace(discordId)
            ? new NotificationPreferences()
            : db.GetDoc<NotificationPreferences>(RemiseTables.Notifications, discordId) ?? new NotificationPreferences();

    public OdeResult Save(string discordId, NotificationPreferences? preferences)
    {
        if (string.IsNullOrWhiteSpace(discordId))
            return new(false, "Compte Discord inconnu.");
        if (preferences is null)
            return new(false, "Préférences manquantes.");
        var stored = new NotificationPreferences
        {
            Enabled = preferences.Enabled,
            Mail = preferences.Mail,
            Garde = preferences.Garde,
            CompteRendu = preferences.CompteRendu
        };
        db.SaveDoc(RemiseTables.Notifications, discordId, DateTimeOffset.UtcNow, stored);
        return new(true);
    }
}
