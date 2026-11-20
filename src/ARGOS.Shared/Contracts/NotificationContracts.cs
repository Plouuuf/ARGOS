namespace Argos.Shared.Contracts;

// =====================================================================
// Notifications Discord : message privé du bot ARGOS quand l'application est fermée.
// Préférences rangées par compte Discord (tous les profils du joueur partagent les mêmes réglages).
// =====================================================================

/// <summary>Préférences de notification Discord d'un joueur (GET/POST /api/me/notifications).</summary>
public sealed class NotificationPreferences
{
    /// <summary>Interrupteur général : faux = aucun message privé.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Nouveau mail reçu.</summary>
    public bool Mail { get; set; } = true;
    /// <summary>Feuille de garde validée sur laquelle on figure.</summary>
    public bool Garde { get; set; } = true;
    /// <summary>Compte-rendu SIGTAO adressé au joueur.</summary>
    public bool CompteRendu { get; set; } = true;

    public bool Allows(NotificationKind kind) => kind switch
    {
        NotificationKind.Test => true,
        _ when !Enabled => false,
        NotificationKind.Mail => Mail,
        NotificationKind.Garde => Garde,
        NotificationKind.CompteRendu => CompteRendu,
        _ => false
    };
}

/// <summary>Nature d'une notification (choix de la case à cocher et de la couleur du message).</summary>
public enum NotificationKind
{
    Mail,
    Garde,
    CompteRendu,
    /// <summary>Message de test demandé depuis les Paramètres : toujours envoyé.</summary>
    Test
}
