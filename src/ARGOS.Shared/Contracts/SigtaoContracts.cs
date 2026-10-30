namespace Argos.Shared.Contracts;

// =====================================================================
// SIGTAO : gestion administrative du personnel (dossiers militaires), comptes-rendus,
// comptes utilisateurs et habilitations, annuaire, historique administratif.
// =====================================================================

/// <summary>Habilitations attribuées dans SIGTAO.</summary>
public static class Habilitations
{
    public const string Soa = "SOA";             // salle opérationnelle : ADAGIO ODE
    public const string Sremise = "SREMISE";     // gestion du parc : ADAGIO REMISE
    public const string Smco = "SMCO";           // validation des mains courantes
    public const string Srh = "SRH";             // gestion de la compagnie (dossiers militaires)
    public const string Sadmin = "SADMIN";       // comptes, habilitations, tous les comptes-rendus

    public static readonly IReadOnlyList<(string Code, string Label)> All =
    [
        (Soa, "SOA - Salle opérationnelle (ADAGIO ODE)"),
        (Sremise, "SREMISE - Gestion de la remise (ADAGIO REMISE)"),
        (Smco, "SMCO - Mains courantes"),
        (Srh, "SRH - Gestion de la compagnie"),
        (Sadmin, "SADMIN - Administration SIGTAO")
    ];

    public static string Label(string code) => All.FirstOrDefault(h => h.Code == code).Label ?? code;
}

public static class MilitaryStatuses
{
    public const string Actif = "En activité";
    public const string Mute = "Muté";
    public const string Parti = "Parti";
    public const string Archive = "Archivé";

    public static readonly IReadOnlyList<string> All = [Actif, Mute, Parti, Archive];
}

/// <summary>Rubriques du dossier militaire (formations, carrière, entretiens…).</summary>
public static class DossierKinds
{
    public const string Formation = "Formation / qualification";
    public const string Diplome = "Diplôme";
    public const string Promotion = "Promotion";
    public const string Affectation = "Changement d'affectation";
    public const string Recompense = "Récompense / félicitation";
    public const string Sanction = "Sanction";
    public const string EntretienIntegration = "Entretien d'intégration";
    public const string EntretienSemestriel = "Entretien semestriel";
    public const string Mutation = "Mutation / départ";

    public static readonly IReadOnlyList<string> All =
        [Formation, Diplome, Promotion, Affectation, Recompense, Sanction, EntretienIntegration, EntretienSemestriel, Mutation];
}

public static class BsppGrades
{
    public static readonly IReadOnlyList<string> All =
    [
        "Sapeur de 2e classe", "Sapeur de 1re classe", "Caporal", "Caporal-chef", "Sergent", "Sergent-chef",
        "Adjudant", "Adjudant-chef", "Major", "Aspirant", "Sous-lieutenant", "Lieutenant", "Capitaine",
        "Commandant", "Lieutenant-colonel", "Colonel", "Général"
    ];

    /// <summary>Abréviation usuelle : « SGT », « CCH »…</summary>
    public static string Short(string grade) => grade switch
    {
        "Sapeur de 2e classe" => "SAP 2CL",
        "Sapeur de 1re classe" => "SAP 1CL",
        "Caporal" => "CPL",
        "Caporal-chef" => "CCH",
        "Sergent" => "SGT",
        "Sergent-chef" => "SCH",
        "Adjudant" => "ADJ",
        "Adjudant-chef" => "ADC",
        "Major" => "MAJ",
        "Aspirant" => "ASP",
        "Sous-lieutenant" => "SLT",
        "Lieutenant" => "LTN",
        "Capitaine" => "CNE",
        "Commandant" => "CDT",
        "Lieutenant-colonel" => "LCL",
        "Colonel" => "COL",
        "Général" => "GAL",
        _ => grade
    };
}

public sealed class DossierEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Kind { get; set; } = DossierKinds.Formation;
    public DateTimeOffset Date { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string By { get; set; } = string.Empty;
    public DateTimeOffset RecordedAt { get; set; }
}

/// <summary>Dossier militaire (gestion de la compagnie).</summary>
public sealed class MilitaryRecord
{
    public string Id { get; set; } = string.Empty;
    public string Nid { get; set; } = string.Empty;                 // numéro d'identification défense
    public string Matricule { get; set; } = string.Empty;
    public string Grade { get; set; } = BsppGrades.All[0];
    public DateTimeOffset? GradeDate { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string Prenom { get; set; } = string.Empty;
    public string PhotoData { get; set; } = string.Empty;           // photo d'identité (base 64)
    public string Unite { get; set; } = "17e Cie";
    public string Affectation { get; set; } = EnginsCatalog.Creteil; // centre de secours
    public string Section { get; set; } = string.Empty;
    public string Fonction { get; set; } = string.Empty;
    public DateTimeOffset? ArrivalDate { get; set; }
    public string Status { get; set; } = MilitaryStatuses.Actif;
    public string Email { get; set; } = string.Empty;
    public string Telephone { get; set; } = string.Empty;
    public string DiscordUserId { get; set; } = string.Empty;       // joueur VDMS correspondant
    public List<DossierEntry> Entries { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;

    public string FullName => $"{BsppGrades.Short(Grade)} {Nom.ToUpperInvariant()} {Prenom}".Trim();
}

/// <summary>Compte utilisateur ARGOS (le mot de passe n'est jamais renvoyé par le serveur).</summary>
public sealed class UserAccount
{
    public string Id { get; set; } = string.Empty;
    public string DiscordUserId { get; set; } = string.Empty;
    public string DossierId { get; set; } = string.Empty;
    public string Grade { get; set; } = string.Empty;
    public string Nom { get; set; } = string.Empty;
    public string Prenom { get; set; } = string.Empty;
    public string Nid { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Habilitations { get; set; } = [];
    public bool Active { get; set; } = true;
    public bool HasPassword { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}

public sealed class CrAttachment
{
    public string Name { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public string Data { get; set; } = string.Empty;                // base 64
    public long Size { get; set; }
}

public static class CrMentions
{
    public static readonly IReadOnlyList<string> All = ["POUR INFORMATION", "POUR DÉCISION", "POUR ATTRIBUTION", "POUR AVIS"];
}

/// <summary>E-COMPTE-RENDU (modèle BSPP, 17e Cie).</summary>
public sealed class CompteRendu
{
    public string Id { get; set; } = string.Empty;
    public string NumDoc { get; set; } = string.Empty;              // « 2026-0042 »
    public DateTimeOffset Date { get; set; }
    public string Lieu { get; set; } = "Créteil";
    public string Unite { get; set; } = "17e Cie";

    // Rédacteur
    public string AuthorUserId { get; set; } = string.Empty;
    public string AuthorGrade { get; set; } = string.Empty;
    public string AuthorNom { get; set; } = string.Empty;
    public string AuthorPrenom { get; set; } = string.Empty;
    public string AuthorNid { get; set; } = string.Empty;
    public string AuthorTempsService { get; set; } = string.Empty;  // « 4 ans 3 mois »

    // Destinataire
    public string DestGrade { get; set; } = string.Empty;
    public string DestPrenom { get; set; } = string.Empty;
    public string DestNom { get; set; } = string.Empty;
    public string Mention { get; set; } = CrMentions.All[0];

    public string Objet { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;

    /// <summary>Corps du compte-rendu, mise en forme simple : **gras**, *italique*, __souligné__, « - » pour les listes.</summary>
    public string Body { get; set; } = string.Empty;

    public string Synthese { get; set; } = string.Empty;
    public List<CrAttachment> Attachments { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Droits SIGTAO de l'utilisateur connecté (calculés par le serveur).</summary>
public sealed class SigtaoRights
{
    public bool ManageCompany { get; set; }
    public bool ManageAccounts { get; set; }
    public bool ReadAllReports { get; set; }
    public List<string> Habilitations { get; set; } = [];
}

public sealed class SigtaoSnapshot
{
    public List<MilitaryRecord> Military { get; set; } = [];
    public List<UserAccount> Accounts { get; set; } = [];
    public List<CompteRendu> Reports { get; set; } = [];
    public SigtaoRights Rights { get; set; } = new();
    public string? MyDossierId { get; set; }
    public string? MyAccountId { get; set; }
    public DateTimeOffset ServerTime { get; set; }
}

// ===== Requêtes =====

public sealed class AccountPasswordResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }

    /// <summary>Mot de passe provisoire, affiché une seule fois à l'administrateur.</summary>
    public string? TemporaryPassword { get; set; }
}

/// <summary>Actions du journal propres à SIGTAO (historique administratif).</summary>
public static class SigtaoJournal
{
    public const string DossierCree = "Dossier créé";
    public const string DossierModifie = "Dossier modifié";
    public const string Rubrique = "Dossier : ajout";
    public const string CompteCree = "Compte créé";
    public const string CompteModifie = "Compte modifié";
    public const string Acces = "Accès réinitialisés";
    public const string CompteRendu = "Compte-rendu";

    public static readonly IReadOnlyList<string> Actions = [DossierCree, DossierModifie, Rubrique, CompteCree, CompteModifie, Acces, CompteRendu];
}

/// <summary>Synthèse automatique d'un compte-rendu : objet + premières phrases du corps.</summary>
public static class CrText
{
    public static string Plain(string body)
        => body.Replace("**", string.Empty).Replace("__", string.Empty).Replace("*", string.Empty);

    public static string Synthese(CompteRendu cr)
    {
        var plain = Plain(cr.Body).Replace("\r", string.Empty);
        var sentences = plain.Split(['.', '!', '?', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 3).Take(3).ToList();
        var who = $"{BsppGrades.Short(cr.AuthorGrade)} {cr.AuthorNom.ToUpperInvariant()} {cr.AuthorPrenom}".Trim();
        var head = $"Compte-rendu du {cr.Date.ToLocalTime():dd/MM/yyyy} par {who} - objet : {cr.Objet}.";
        return sentences.Count == 0 ? head : head + " " + string.Join(". ", sentences) + ".";
    }

    /// <summary>Temps de service depuis la date d'arrivée : « 4 ans 3 mois ».</summary>
    public static string ServiceTime(DateTimeOffset? arrival, DateTimeOffset now)
    {
        if (arrival is null) return string.Empty;
        var months = (now.Year - arrival.Value.Year) * 12 + now.Month - arrival.Value.Month - (now.Day < arrival.Value.Day ? 1 : 0);
        months = Math.Max(0, months);
        var years = months / 12;
        months %= 12;
        return (years > 0 ? $"{years} an{(years > 1 ? "s" : string.Empty)} " : string.Empty) + $"{months} mois";
    }
}
