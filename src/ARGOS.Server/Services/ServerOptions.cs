namespace Argos.Server.Services;

public sealed class ArgosServerOptions
{
    /// <summary>Secret du serveur : chiffre les sessions Discord enregistrées sur disque.</summary>
    public string AccessKey { get; set; } = string.Empty;
}

public sealed class DiscordOAuthOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Adresse publique du serveur ARGOS ; le retour Discord arrive sur {PublicBaseUrl}/auth/discord/callback.</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>Serveur Discord VDMS.</summary>
    public string GuildId { get; set; } = string.Empty;

    /// <summary>Rôle obligatoire pour ouvrir une session ARGOS (whitelist BSPP). Donne le niveau « sapeur ».</summary>
    public string RequiredRoleId { get; set; } = string.Empty;

    /// <summary>
    /// Rôles Discord donnant un niveau supplémentaire : clé = niveau (operateur, encadrement, administration),
    /// valeur = ID du rôle, ou plusieurs ID séparés par des virgules. Valeur vide = niveau attribué à personne.
    /// </summary>
    public Dictionary<string, string> LevelRoles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Rôle(s) Discord donnant accès à chaque application : clé = identifiant (adagio-ode, mco…), valeur = ID de rôle(s).</summary>
    public Dictionary<string, string> AppRoles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Rôle(s) Discord « Accès RH/ADMIN ».</summary>
    public string RhAdminRoleIds { get; set; } = string.Empty;
}
