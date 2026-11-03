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

    /// <summary>Développeur d'ARGOS (identifiant Discord dans la configuration du serveur) : accès à tout.</summary>
    public const string Developpeur = "developpeur";

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
    public static readonly IReadOnlyList<string> ByRank = [Developpeur, Administration, RhAdmin, Encadrement, Operateur, Sapeur];

    public static string Label(string level) => level switch
    {
        Developpeur => "Développeur",
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
/// <remarks>
/// Profils : un même compte Discord peut avoir plusieurs personnages (dossiers SIGTAO).
/// Le profil principal garde l'identifiant Discord ; un autre profil a l'identifiant « discord~dossier »,
/// si bien que sa messagerie, ses comptes-rendus, ses fichiers… sont ceux d'un compte distinct.
/// </remarks>
public sealed record ArgosUserDto(
    string UserId,
    string DisplayName,
    string? AvatarUrl,
    IReadOnlyList<string> Levels,
    string? ProfileId = null)
{
    public const char ProfileSeparator = '~';

    /// <summary>Identifiant Discord du joueur, quel que soit le profil.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DiscordId => DiscordOf(UserId);

    public static string DiscordOf(string userId)
    {
        var i = userId.IndexOf(ProfileSeparator);
        return i < 0 ? userId : userId[..i];
    }

    public static string ProfileUserId(string discordId, string dossierId) => $"{discordId}{ProfileSeparator}{dossierId}";
}

/// <summary>Un profil (personnage) proposé à la connexion : un dossier SIGTAO relié au compte Discord.</summary>
public sealed class ArgosProfile
{
    public string Id { get; set; } = string.Empty;
    /// <summary>« SGT DUPONT Jean »</summary>
    public string Name { get; set; } = string.Empty;
    public string Grade { get; set; } = string.Empty;
    public string Affectation { get; set; } = string.Empty;
    public string Fonction { get; set; } = string.Empty;
    public string PhotoData { get; set; } = string.Empty;
    public bool Primary { get; set; }

    public string Details => string.Join(" · ", new[] { Grade, Affectation, Fonction }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>Réponse à GET /api/me/profiles.</summary>
public sealed class ProfilesResponse
{
    public List<ArgosProfile> Profiles { get; set; } = [];
    /// <summary>Dernier profil choisi sur ce poste.</summary>
    public string? SelectedId { get; set; }
    /// <summary>Plusieurs profils : le joueur choisit à la connexion.</summary>
    public bool MustChoose { get; set; }
}

/// <summary>Corps de POST /api/me/profile.</summary>
public sealed class SelectProfileRequest
{
    public string Id { get; set; } = string.Empty;
}

/// <summary>Réponse à POST /auth/discord/session.</summary>
public sealed record AuthSessionStartResponse(string SessionId, string AuthorizeUrl, DateTimeOffset ExpiresUtc);

/// <summary>Réponse à GET /auth/discord/status/{sessionId}. Status : pending, approved, denied, expired.</summary>
public sealed record AuthStatusResponse(string Status, string? Error, string? Token, ArgosUserDto? User);

/// <summary>Corps de POST /auth/discord/validate.</summary>
public sealed record TokenValidationRequest(string Token);

/// <summary>Réponse à POST /auth/discord/validate.</summary>
public sealed record TokenValidationResponse(bool Valid, bool Reauthenticate, string? Error, ArgosUserDto? User);

/// <summary>Identité saisie à la première connexion : « Prénom » et « NOM », adresse BSPP qui en découle.</summary>
public sealed class UserIdentity
{
    public string Prenom { get; set; } = string.Empty;
    public string Nom { get; set; } = string.Empty;
    /// <summary>Adresse de messagerie BSPP (calculée par le serveur).</summary>
    public string Email { get; set; } = string.Empty;
    /// <summary>Vrai quand prénom et nom sont renseignés.</summary>
    public bool Complete => Prenom.Length > 0 && Nom.Length > 0;
    public string FullName => $"{Prenom} {Nom}".Trim();

    /// <summary>« jean-pierre » → « Jean-Pierre » (initiale de chaque partie en majuscule).</summary>
    public static string FormatPrenom(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return text;
        var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        var chars = text.ToLower(fr).ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (i == 0 || chars[i - 1] is ' ' or '-' or '\'')
                chars[i] = char.ToUpper(chars[i], fr);
        return new string(chars);
    }

    /// <summary>« dupont » → « DUPONT ».</summary>
    public static string FormatNom(string? value)
        => (value ?? string.Empty).Trim().ToUpper(System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
}
