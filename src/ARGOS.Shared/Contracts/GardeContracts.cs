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

/// <summary>Postes de garde : personnels de service et postes de chaque engin selon son type.</summary>
public static class GardePostes
{
    public const string ChefDeGarde = "Chef de garde";

    public static readonly IReadOnlyList<string> Service =
        [ChefDeGarde, "Adjoint au chef de garde", "Stationnaire (standard)", "Planton"];

    public static IReadOnlyList<string> For(string? type)
    {
        var t = (type ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
        return t switch
        {
            "VSAV" or "VSAV2" or "VPS" => ["Chef d'agrès", "Conducteur", "Équipier"],
            "PSE" or "FPT" or "FPTL" or "PS" or "FPTSR" => ["Chef d'agrès", "Conducteur", "Chef BAT", "Équipier BAT", "Chef BAL", "Équipier BAL"],
            "AR" => ["Médecin", "Infirmier", "Conducteur"],
            "VLI" => ["Infirmier", "Conducteur"],
            "CD" or "CA" or "FA" or "CRAC" => ["Chef d'agrès", "Conducteur", "Équipier"],
            _ when t.StartsWith("VL", StringComparison.Ordinal) => ["Officier", "Conducteur"],
            _ => ["Chef d'agrès", "Conducteur"]
        };
    }

    /// <summary>Abréviation pour le planning : « Chef d'agrès » → « CA ».</summary>
    public static string Short(string poste) => poste switch
    {
        ChefDeGarde => "CDG",
        "Adjoint au chef de garde" => "ADJ",
        "Stationnaire (standard)" => "STA",
        "Planton" => "PLT",
        "Chef d'agrès" => "CA",
        "Conducteur" => "COND",
        "Équipier" => "EQ",
        "Chef BAT" => "CBAT",
        "Équipier BAT" => "EBAT",
        "Chef BAL" => "CBAL",
        "Équipier BAL" => "EBAL",
        "Officier" => "OFF",
        "Médecin" => "MED",
        "Infirmier" => "INF",
        _ => poste.Length <= 5 ? poste : poste[..5]
    };
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
    public string Observations { get; set; } = string.Empty;
    public string Statut { get; set; } = GardeStatuts.Brouillon;
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
    public string ValidatedBy { get; set; } = string.Empty;
    public DateTimeOffset? ValidatedAt { get; set; }

    public bool IsValidated => Statut == GardeStatuts.Validee;

    public static string BuildId(string date, string centre) => $"{date}-{centre.Trim().ToUpperInvariant()}";

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
}
