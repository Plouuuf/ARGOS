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
    /// <summary>Grades BSPP (affiche officielle), du plus bas au plus haut : (libellé, abréviation, code d'insigne).</summary>
    private static readonly (string Label, string Short, string Code)[] Table =
    [
        ("Sapeur de 2e classe", "SAP 2CL", "SAP"),
        ("Sapeur de 1re classe", "SAP 1CL", "1CL"),
        ("Sapeur de 1re classe +15", "1CL+15", "1CL15"),
        ("Caporal", "CPL", "CPL"),
        ("Caporal +15", "CPL+15", "CPL15"),
        ("Caporal-chef", "CCH", "CCH"),
        ("Caporal-chef +15", "CCH+15", "CCH15"),
        ("Caporal-chef CT1", "CCH CT1", "CCHCT1"),
        ("Caporal-chef CT1 +15", "CCH CT1+15", "CCHCT115"),
        ("Sergent", "SGT", "SGT"),
        ("Sergent UV2 / FS2", "SGT UV2", "SGTUV2"),
        ("Sergent-chef", "SCH", "SCH"),
        ("Adjudant", "ADJ", "ADJ"),
        ("Adjudant-chef", "ADC", "ADC"),
        ("Major", "MAJ", "MAJ"),
        ("Aspirant", "ASP", "ASP"),
        ("Sous-lieutenant", "SLT", "SLT"),
        ("Lieutenant", "LTN", "LTN"),
        ("Capitaine", "CNE", "CNE"),
        ("Commandant", "CDT", "CDT"),
        ("Lieutenant-colonel", "LCL", "LCL"),
        ("Colonel", "COL", "COL"),
        ("Général de brigade", "GBR", "GBR")
    ];

    public static readonly IReadOnlyList<string> All = Table.Select(t => t.Label).ToList();

    /// <summary>Abréviation usuelle : « SGT », « CCH+15 »…</summary>
    public static string Short(string grade)
        => Table.FirstOrDefault(t => t.Label == grade).Short ?? grade;

    /// <summary>Code de l'insigne (Assets/Grades/{code}.png, émojis Discord VDMS) ; null si grade inconnu.</summary>
    public static string? Code(string? grade)
        => Table.FirstOrDefault(t => t.Label == grade).Code;

    /// <summary>Grade d'après le code d'insigne Discord (« 1CL15 », « CCHCT1 », « SGTFS2 »…).</summary>
    public static string? FromCode(string code)
    {
        var c = code.ToUpperInvariant().Replace("_", string.Empty).Replace("+", string.Empty);
        c = c switch
        {
            "2CL" or "SAP2CL" => "SAP",
            "SAP1CL" => "1CL",
            "SGTFS2" => "SGTUV2",
            "CT1" => "CCHCT1",
            "CT115" => "CCHCT115",
            "GAL" or "GEN" => "GBR",
            _ => c
        };
        return Table.FirstOrDefault(t => t.Code == c).Label;
    }

    /// <summary>Rang (0 = sapeur de 2e classe) pour trier du plus haut au plus bas.</summary>
    public static int Rank(string grade) => All.ToList().IndexOf(grade);
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
    public string Echelon { get; set; } = string.Empty;             // « +15 » (insigne Discord 1CL15)
    public string Nom { get; set; } = string.Empty;
    public string Prenom { get; set; } = string.Empty;
    public string PhotoData { get; set; } = string.Empty;           // photo d'identité choisie (base 64), prioritaire
    public string AvatarUrl { get; set; } = string.Empty;           // avatar Discord (photo par défaut)
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

    public string FullName => $"{BsppGrades.Short(Grade)}{Echelon} {Nom.ToUpperInvariant()} {Prenom}".Replace("  ", " ").Trim();

    /// <summary>« Sapeur de 1re classe +15 »</summary>
    public string GradeLabel => string.IsNullOrEmpty(Echelon) ? Grade : $"{Grade} {Echelon}";
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

/// <summary>Import de l'effectif depuis le message Discord de la compagnie.</summary>
public sealed class RosterImportRequest
{
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Lecture du tableau d'effectif Discord, une ligne par militaire :
/// « &lt;:CNE:…&gt; CNE FAUBOURG (Laurent) - *Commandant d'Unité* - NM : 0000109387 »
/// « &lt;:1CL15:…&gt; &lt;@560037927695024139&gt; (Diego) - NID : 0000109823 ».
/// </summary>
public static class RosterImport
{
    private static readonly System.Text.RegularExpressions.Regex GradeRx = new(@"<:([A-Za-z0-9]+):\d+>");
    private static readonly System.Text.RegularExpressions.Regex MentionRx = new(@"<@!?(\d+)>");
    private static readonly System.Text.RegularExpressions.Regex PrenomRx = new(@"\(\s*([^)\-]+?)\s*(?:\)|-)");
    private static readonly System.Text.RegularExpressions.Regex FonctionRx = new(@"\*([^*]+)\*");
    private static readonly System.Text.RegularExpressions.Regex NidRx = new(@"\bNID\s*:\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private static readonly System.Text.RegularExpressions.Regex NmRx = new(@"\bNM\s*:\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Code d'insigne Discord → grade : « 1CL15 » → « Sapeur de 1re classe +15 ».</summary>
    public static (string Grade, string Echelon)? Grade(string code)
        => BsppGrades.FromCode(code) is { } grade ? (grade, string.Empty) : null;

    public static List<MilitaryRecord> Parse(string text)
    {
        var list = new List<MilitaryRecord>();
        foreach (var raw in text.Replace("\r", string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            var g = GradeRx.Match(line);
            if (!g.Success || Grade(g.Groups[1].Value) is not { } grade)
                continue;
            var record = new MilitaryRecord
            {
                Grade = grade.Grade,
                Unite = "17e Cie",
                Affectation = EnginsCatalog.Creteil,
                Status = MilitaryStatuses.Actif
            };
            var mention = MentionRx.Match(line);
            if (mention.Success) record.DiscordUserId = mention.Groups[1].Value;
            var prenom = PrenomRx.Match(line);
            if (prenom.Success) record.Prenom = prenom.Groups[1].Value.Trim();
            var fonction = FonctionRx.Match(line);
            if (fonction.Success) record.Fonction = fonction.Groups[1].Value.Trim();
            if (NidRx.Match(line) is { Success: true } nid) record.Nid = nid.Groups[1].Value;
            if (NmRx.Match(line) is { Success: true } nm) record.Matricule = nm.Groups[1].Value;

            // Nom écrit en clair (officiers) : « CNE FAUBOURG (Laurent) ».
            if (!mention.Success)
            {
                var start = g.Index + g.Length;
                var end = prenom.Success ? prenom.Index : line.Length;
                var words = line[start..end].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => !string.Equals(w, g.Groups[1].Value, StringComparison.OrdinalIgnoreCase)
                                && Grade(w) is null && w != "-")
                    .ToList();
                record.Nom = string.Join(" ", words).ToUpperInvariant();
            }

            // CS d'affectation cité dans la fonction (« Chef de Centre Adjoint CS CRET »).
            foreach (var cs in new[] { EnginsCatalog.Creteil, EnginsCatalog.MaisonsAlfort, EnginsCatalog.Mass })
                if (record.Fonction.Contains("CS " + cs, StringComparison.OrdinalIgnoreCase))
                    record.Affectation = cs;

            if (record.Prenom.Length > 0 || record.Nom.Length > 0 || record.DiscordUserId.Length > 0)
                list.Add(record);
        }
        return list;
    }

    /// <summary>Nom de famille tiré d'un pseudo Discord VDMS : « [1CL+15] SANTOS D. » → « SANTOS ».</summary>
    public static string NameFromNickname(string nickname)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(nickname, @"^\s*[\[\(][^\]\)]*[\]\)]\s*", string.Empty).Trim();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !(w.Length <= 2 && w.EndsWith('.')))       // initiale du prénom
            .ToList();
        return words.Count == 0 ? string.Empty : string.Join(" ", words).ToUpperInvariant();
    }
}
