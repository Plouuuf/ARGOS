namespace Argos.Shared.Contracts;

/// <summary>
/// Niveaux d'accès ARGOS, calculés par le serveur à partir des rôles Discord VDMS.
/// Un joueur peut en cumuler plusieurs. « sapeur » correspond au rôle obligatoire.
/// </summary>
public static class AccessLevels
{
    public const string Sapeur = "sapeur";
    public const string Operateur = "operateur";
    public const string Encadrement = "encadrement";
    public const string Administration = "administration";

    /// <summary>Rôle Discord « Accès RH/ADMIN » : gestion de la compagnie dans SIGTAO, tous les comptes-rendus.</summary>
    public const string RhAdmin = "rh-admin";

    /// <summary>Accès à une application (rôle Discord de l'application) : « app:adagio-ode ».</summary>
    public static string App(string appId) => "app:" + appId;

    /// <summary>
    /// L'utilisateur peut-il ouvrir l'application ? Sans aucun niveau « app: » (serveur pas encore configuré
    /// par application), toutes les applications restent accessibles.
    /// </summary>
    public static bool CanUse(IReadOnlyCollection<string>? levels, string appId)
        => levels is null || !levels.Any(l => l.StartsWith("app:", StringComparison.Ordinal)) || levels.Contains(App(appId));

    public static bool IsRhAdmin(IReadOnlyCollection<string>? levels)
        => levels is not null && (levels.Contains(RhAdmin) || levels.Contains(Administration));

    /// <summary>Du plus élevé au plus bas, pour afficher le niveau principal.</summary>
    public static readonly IReadOnlyList<string> ByRank = [Administration, RhAdmin, Encadrement, Operateur, Sapeur];

    public static string Label(string level) => level switch
    {
        Administration => "Administration",
        RhAdmin => "RH / Administration",
        Encadrement => "Encadrement",
        Operateur => "Opérateur CTA",
        Sapeur => "Sapeur",
        _ => level
    };

    public static string HighestLabel(IReadOnlyCollection<string>? levels)
    {
        if (levels is null || levels.Count == 0)
            return Label(Sapeur);
        foreach (var level in ByRank)
            if (levels.Contains(level))
                return Label(level);
        return Label(Sapeur);
    }
}

/// <summary>Joueur connecté, tel que le serveur le renvoie à l'application.</summary>
public sealed record ArgosUserDto(
    string UserId,
    string DisplayName,
    string? AvatarUrl,
    IReadOnlyList<string> Levels);

/// <summary>Réponse à POST /auth/discord/session.</summary>
public sealed record AuthSessionStartResponse(string SessionId, string AuthorizeUrl, DateTimeOffset ExpiresUtc);

/// <summary>Réponse à GET /auth/discord/status/{sessionId}. Status : pending, approved, denied, expired.</summary>
public sealed record AuthStatusResponse(string Status, string? Error, string? Token, ArgosUserDto? User);

/// <summary>Corps de POST /auth/discord/validate.</summary>
public sealed record TokenValidationRequest(string Token);

/// <summary>Réponse à POST /auth/discord/validate.</summary>
public sealed record TokenValidationResponse(bool Valid, bool Reauthenticate, string? Error, ArgosUserDto? User);
