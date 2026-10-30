namespace Argos.Shared.Contracts;

/// <summary>Engin d'un centre de secours, base d'ADAGIO REMISE et des ordres de départ.</summary>
/// <param name="Centre">Centre de secours (CRET, MALF, MASS).</param>
/// <param name="Indicatif">Indicatif radio, ex. « VSAV 947 ».</param>
/// <param name="Type">Type d'engin, ex. « VSAV ».</param>
/// <param name="Libelle">Désignation complète.</param>
/// <param name="Code">Code ADAGIO, ex. « VSAV_1_CRET » (type, rang dans le centre, centre).</param>
/// <param name="Immatriculation">Plaque, quand l'engin en a une propre.</param>
public sealed record EnginDefinition(
    string Centre,
    string Indicatif,
    string Type,
    string Libelle,
    string Code,
    string? Immatriculation = null)
{
    /// <summary>Nom complet affiché partout : « VSAV 947 CRET ».</summary>
    public string Nom => $"{Indicatif} {Centre}";

    /// <summary>Forme de l'ordre de départ : « EPAN 24 MALF (EPAN_1_MALF) ».</summary>
    public string NomOde => $"{Nom} ({Code})";
}

/// <summary>
/// Parc d'engins du RP VDMS (liste MCO du 29/10/2026, complétée par la feuille GDM).
/// Sert de liste initiale ; ADAGIO REMISE pourra ensuite la gérer en base de données.
/// </summary>
public static class EnginsCatalog
{
    public const string Creteil = "CRET";
    public const string MaisonsAlfort = "MALF";
    public const string Mass = "MASS";
    /// <summary>Engins de spécialité sans centre précisé sur la feuille CDG SPE (centre modifiable dans REMISE).</summary>
    public const string Specialites = "SPE";

    public static readonly IReadOnlyList<EnginDefinition> All =
    [
        new(Creteil, "PSE 212", "PSE", "Premier secours évacuation", "PSE_1_CRET"),
        new(Creteil, "VSAV 947", "VSAV", "Secours et assistance aux victimes", "VSAV_1_CRET"),
        new(Creteil, "VSAV 949", "VSAV", "Secours et assistance aux victimes", "VSAV_2_CRET"),
        new(Creteil, "CD 05", "CD", "Camion de désincarcération", "CD_1_CRET"),
        new(Creteil, "CA 49", "CA", "Camion d'appui", "CA_1_CRET"),
        new(Creteil, "VID 251", "VID", "Véhicule d'intervention diverse", "VID_1_CRET", "BF-642-ZL"),
        new(Creteil, "FA 32", "FA", "Fourgon d'appui", "FA_1_CRET"),
        new(Creteil, "VLROGC 604", "VLROGC", "Officier de garde compagnie", "SPVL_1_CRET", "LR-924-HR"),

        new(MaisonsAlfort, "FPTL 202", "FPTL", "Fourgon pompe-tonne léger", "FPTL_1_MALF", "GH-024-QK"),
        new(MaisonsAlfort, "EPAN 24", "EPAN", "Échelle pivotante à nacelle", "EPAN_1_MALF"),
        new(MaisonsAlfort, "EPA 84", "EPA", "Échelle pivotante automatique", "EPA_1_MALF"),

        new(Mass, "AR 64", "AR", "Ambulance de réanimation", "AR_1_MASS"),
        new(Mass, "VLI 05", "VLI", "Véhicule léger infirmier", "VLI_1_MASS"),
        new(Mass, "VLR DSM 274", "VLR DSM", "Directeur des secours médicaux", "VLRDSM_1_MASS"),
        new(Mass, "CRAC 12", "CRAC", "Ravitaillement en air comprimé", "CRAC_1_MASS"),
        new(Mass, "CMO 1", "CMO", "PC médical", "CMO_1_MASS"),

        // Feuille CDG SPE (spécialités) - ajoutés aux parcs existants une seule fois (voir SpecialitesAjout).
        new(Mass, "AR 63", "AR", "Ambulance de réanimation", "AR_2_MASS"),
        new(Mass, "AR 54", "AR", "Ambulance de réanimation", "AR_3_MASS"),
        new(Mass, "VLI 02", "VLI", "Véhicule léger infirmier", "VLI_2_MASS"),
        new(Mass, "VLR OPCM 875", "VLR OPCM", "Officier PC médical", "SPVL_2_MASS"),
        new(Specialites, "CSP 3", "CSP", "Groupe SIA-SIS", "CSP_1_SPE"),
        new(Specialites, "CSL 24", "CSL", "Groupe SIA-SIS", "CSL_1_SPE"),
        new(Specialites, "CESD 1", "CESD", "Groupe SIA-SIS", "CESD_1_SPE"),
        new(Specialites, "VIMP 1", "VIMP", "Groupe RSMU - GRIMP", "VIMP_1_SPE"),
        new(Specialites, "VEII 1", "VEII", "Communication", "VEII_1_SPE")
    ];

    /// <summary>Indicatifs ajoutés avec la feuille CDG SPE, à créer une fois dans les parcs déjà en service.</summary>
    public static readonly IReadOnlyList<string> SpecialitesAjout =
        ["AR 63", "AR 54", "VLI 02", "VLR OPCM 875", "CSP 3", "CSL 24", "CESD 1", "VIMP 1", "VEII 1"];

    /// <summary>Retrouve un engin par son nom complet (« VSAV 947 CRET ») ou son indicatif.</summary>
    public static EnginDefinition? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var n = name.Trim();
        return All.FirstOrDefault(e => string.Equals(e.Nom, n, StringComparison.OrdinalIgnoreCase))
               ?? All.FirstOrDefault(e => string.Equals(e.Indicatif, n, StringComparison.OrdinalIgnoreCase));
    }
}
