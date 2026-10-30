namespace Argos.Shared.Contracts;

// =====================================================================
// SIGTAO : feuille de garde du jour et planning de garde.
// La feuille validée arme les engins dans ADAGIO REMISE (équipage affiché sur l'engin) ;
// un engin déclaré « non armé » devient indisponible et ne peut plus être engagé dans l'ODE.
// =====================================================================

public static class GardeStatuts
{
    public const string Brouillon = "En préparation";
    public const string Validee = "Validée";
}

/// <summary>
/// Postes de garde, d'après la feuille du chef de garde VDMS (Google Sheets « FEUILLE DE GARDE DU … ») :
/// piquets complémentaires et postes de chaque engin selon son type.
/// </summary>
public static class GardePostes
{
    public const string ChefDeGarde = "Chef de garde incendie";

    /// <summary>Piquets complémentaires (personnels de service de la journée).</summary>
    public static readonly IReadOnlyList<string> Service =
    [
        "Caporal de jour", "Sergent de jour", ChefDeGarde, "Garde remise", "Garde cuisine", "Sous-officier remise",
        "Stationnaire", "CSO"
    ];

    public static IReadOnlyList<string> For(string? type)
    {
        var t = (type ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
        return t switch
        {
            "VSAV" or "VSAV2" or "VPS" => ["C/A", "COND", "EQP", "EQP2"],
            "PSE" or "FPT" or "FPTL" or "PS" or "FPTSR" => ["C/A", "COND", "1CE", "1SE", "2CE", "2SE"],
            "FA" => ["C/A / CGI", "COND", "1CE", "1SE", "2CE", "2SE"],
            "AR" => ["MED", "INF", "AMB"],
            "VLI" => ["INF", "AMB"],
            "CMO" => ["DSM", "COND", "EQP"],
            "VLROGC" or "VLROGCSPVL" or "SPVL" => ["OGC", "COND"],
            "VLRDSM" => ["DSM", "COND"],
            "VLROPCM" => ["OPCM", "COND"],
            "CSP" or "CSL" => ["TSU", "COND"],
            "CESD" => ["C/A", "COND"],
            "VIMP" => ["C/A / IMP 3", "COND", "EQ / IMP 2 (1)", "EQ / IMP 2 (2)", "EQ / IMP 2 (3)"],
            "VEII" => ["M.BCOM", "COND"],
            _ => ["C/A", "COND"]
        };
    }

    /// <summary>Abréviation pour le planning.</summary>
    public static string Short(string poste) => poste switch
    {
        "Caporal de jour" => "CPLJ",
        "Sergent de jour" => "SGTJ",
        ChefDeGarde => "CGI",
        "Garde remise" => "REM",
        "Garde cuisine" => "CUIS",
        "Sous-officier remise" => "SOREM",
        "Stationnaire" => "STA",
        "C/A / CGI" => "CA/CGI",
        "C/A" => "CA",
        "Renfort" => "RENF",
        // Anciennes feuilles
        "Chef d'agrès" => "CA",
        "Conducteur" => "COND",
        "Équipier" => "EQP",
        _ => poste.Length <= 6 ? poste : poste[..6]
    };
}

/// <summary>
/// Regroupement des engins sur la feuille : onglet « Feuille CDG GI » (SANITAIRE, INCENDIE, DIVERS, COMMANDEMENT)
/// puis onglet « Feuille CDG SPE » (DIVISION SANTÉ, GROUPE SIA-SIS, GROUPE RSMU, COMMUNICATION).
/// </summary>
public static class GardeCategories
{
    public const string Sanitaire = "SANITAIRE";
    public const string Incendie = "INCENDIE";
    public const string Divers = "DIVERS";
    public const string Commandement = "COMMANDEMENT";
    public const string Sante = "SPÉCIALITÉS · DIVISION SANTÉ";
    public const string SiaSis = "SPÉCIALITÉS · GROUPE SIA-SIS";
    public const string Rsmu = "SPÉCIALITÉS · GROUPE RSMU";
    public const string Communication = "SPÉCIALITÉS · COMMUNICATION";

    public static readonly IReadOnlyList<string> All = [Sanitaire, Incendie, Divers, Commandement, Sante, SiaSis, Rsmu, Communication];

    /// <summary>Engins de santé qui figurent sur la feuille GI (rubrique DIVERS) et non sur la feuille SPE.</summary>
    private static readonly string[] DiversGi = ["AR 64", "VLI 05", "CMO 1"];

    public static string Of(string? type, string? engin = null)
    {
        var t = (type ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
        var nom = (engin ?? string.Empty).Trim().ToUpperInvariant();
        if (DiversGi.Any(d => nom.StartsWith(d + " ", StringComparison.Ordinal) || nom == d)) return Divers;
        return t switch
        {
            "VSAV" or "VSAV2" or "VPS" => Sanitaire,
            "PSE" or "FPT" or "FPTL" or "PS" or "FPTSR" or "FA" or "CA" or "CD" or "EPA" or "EPAN" or "EPSA" or "BEA" => Incendie,
            "VLROGC" or "VLROGCSPVL" or "SPVL" => Commandement,
            "AR" or "VLI" or "VLRDSM" or "VLROPCM" => Sante,
            "CSP" or "CSL" or "CESD" => SiaSis,
            "VIMP" or "GRIMP" => Rsmu,
            "VEII" => Communication,
            _ when t.StartsWith("VLR", StringComparison.Ordinal) => Commandement,
            _ => Divers
        };
    }
}

/// <summary>Rubriques des consignes du jour (la rubrique « Autres » correspond aux observations).</summary>
public static class GardeConsignes
{
    public static readonly IReadOnlyList<string> All = ["Sport", "TIG", "Sergent de jour", "Chef de garde", "Manœuvre"];
}

/// <summary>Consigne du jour : rubrique + texte libre.</summary>
public sealed class GardeConsigne
{
    public string Titre { get; set; } = string.Empty;
    public string Texte { get; set; } = string.Empty;
}

/// <summary>Un poste tenu par un militaire (vide = poste non pourvu).</summary>
public sealed class GardeAffectation
{
    public string Poste { get; set; } = string.Empty;
    public string MilitaryId { get; set; } = string.Empty;
    /// <summary>« SGT DUPONT Jean », figé à l'enregistrement pour l'impression et l'historique.</summary>
    public string Nom { get; set; } = string.Empty;

    public bool Pourvu => !string.IsNullOrWhiteSpace(MilitaryId);
}

/// <summary>Un engin de la garde avec son équipage.</summary>
public sealed class GardeEngin
{
    /// <summary>Nom complet de l'engin dans REMISE : « VSAV 947 CRET ».</summary>
    public string Engin { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    /// <summary>Faux : engin non armé (manque de personnel) → indisponible dans REMISE et l'ODE.</summary>
    public bool Arme { get; set; } = true;
    public List<GardeAffectation> Postes { get; set; } = [];
}

/// <summary>Feuille de garde d'un centre pour une journée (relève à 7 h 30).</summary>
public sealed class FeuilleGarde
{
    /// <summary>« 2026-10-30-CRET ».</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Jour de garde au format « yyyy-MM-dd ».</summary>
    public string Date { get; set; } = string.Empty;
    public string Centre { get; set; } = EnginsCatalog.Creteil;
    public string Horaires { get; set; } = "07h30 → 07h30 (J+1)";
    public List<GardeAffectation> Service { get; set; } = [];
    public List<GardeEngin> Engins { get; set; } = [];
    /// <summary>Personnels de garde sans poste attribué (renfort, piquet).</summary>
    public List<GardeAffectation> Reserve { get; set; } = [];
    /// <summary>Consignes : rubrique « Autres ».</summary>
    public string Observations { get; set; } = string.Empty;
    /// <summary>Consignes du jour : sport, TIG, sergent de jour, chef de garde, manœuvre.</summary>
    public List<GardeConsigne> Consignes { get; set; } = [];
    public string Statut { get; set; } = GardeStatuts.Brouillon;
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
    public string ValidatedBy { get; set; } = string.Empty;
    public DateTimeOffset? ValidatedAt { get; set; }

    public bool IsValidated => Statut == GardeStatuts.Validee;

    public static string BuildId(string date, string centre) => $"{date}-{centre.Trim().ToUpperInvariant()}";

    /// <summary>FiveM : une seule feuille par jour pour tous les centres (tous les engins ensemble).</summary>
    public const string TousCentres = "TOUS";

    /// <summary>« CRET · MALF · MASS » d'après les engins de la feuille (ou le centre seul).</summary>
    public string CentresLabel
    {
        get
        {
            if (Centre != TousCentres) return Centre;
            var centres = Engins.Select(e => e.Engin.Trim().Split(' ').LastOrDefault() ?? string.Empty).Where(c => c.Length > 0).Distinct().ToList();
            return centres.Count == 0 ? "tous les centres" : string.Join(" · ", centres);
        }
    }

    /// <summary>Toutes les affectations pourvues de la feuille (service, engins, réserve).</summary>
    public IEnumerable<(GardeAffectation Affectation, string? Engin)> Affectations()
    {
        foreach (var s in Service.Where(a => a.Pourvu)) yield return (s, null);
        foreach (var e in Engins)
            foreach (var p in e.Postes.Where(a => a.Pourvu)) yield return (p, e.Engin);
        foreach (var r in Reserve.Where(a => a.Pourvu)) yield return (r, null);
    }

    public int Effectif => Affectations().Select(a => a.Affectation.MilitaryId).Distinct().Count();
}

/// <summary>Un militaire s'inscrit lui-même sur la garde d'un centre (le sien ou un autre), ou s'en retire.</summary>
public sealed class SelfGardeRequest
{
    public string Date { get; set; } = string.Empty;
    public string Centre { get; set; } = string.Empty;
    /// <summary>Engin visé ; vide = personnels de service ou renfort.</summary>
    public string? Engin { get; set; }
    public string? Type { get; set; }
    /// <summary>Poste visé ; « Renfort » = réserve.</summary>
    public string Poste { get; set; } = "Renfort";
    /// <summary>Vrai : se retirer de cette feuille.</summary>
    public bool Retirer { get; set; }
}

public sealed class SaveGardeRequest
{
    public FeuilleGarde Feuille { get; set; } = new();
    /// <summary>Valider la feuille et armer les engins dans ADAGIO REMISE.</summary>
    public bool Valider { get; set; }
}

/// <summary>Impression d'un document quelconque sur l'imprimante virtuelle du centre.</summary>
public sealed class PrintDocumentRequest
{
    public string Kind { get; set; } = "Document";
    public string Title { get; set; } = string.Empty;
    public string? ImagePng { get; set; }
    /// <summary>Feuille de garde imprimée : publiée aussi sur le salon Discord des gardes (si configuré).</summary>
    public string? GardeId { get; set; }
}
