namespace Argos.Shared.Contracts;

/// <summary>Engin d'un centre de secours, base d'ADAGIO REMISE.</summary>
public sealed record EnginDefinition(string Centre, string Indicatif, string Type, string Libelle);

/// <summary>
/// Parc d'engins fourni pour le RP VDMS (28/10/2026).
/// Sert de liste initiale ; le serveur pourra ensuite la gérer en base de données.
/// </summary>
public static class EnginsCatalog
{
    public const string Creteil = "CRET";
    public const string MaisonsAlfort = "MALF";
    public const string Mass = "MASS";

    public static readonly IReadOnlyList<EnginDefinition> All =
    [
        new(Creteil, "PS 212", "PS", "Premier secours évacuation"),
        new(Creteil, "VSAV 494", "VSAV", "Secours et assistance aux victimes"),
        new(Creteil, "VSAV 497", "VSAV", "Secours et assistance aux victimes"),
        new(Creteil, "CD 05", "CD", "Camion de désincarcération"),
        new(Creteil, "CA 49", "CA", "Camion d'appui"),
        new(Creteil, "VID 251", "VID", "Véhicule d'intervention diverse"),
        new(Creteil, "FA 32", "FA", "Fourgon d'appui"),
        new(Creteil, "VLROGC 604", "VLROGC", "Officier de garde compagnie"),

        new(MaisonsAlfort, "FPTL 202", "FPTL", "Fourgon pompe-tonne léger"),
        new(MaisonsAlfort, "EPAN 24", "EPAN", "Échelle pivotante à nacelle"),
        new(MaisonsAlfort, "EPA 84", "EPA", "Échelle pivotante automatique"),

        new(Mass, "AR 64", "AR", "Ambulance de réanimation"),
        new(Mass, "VLI 05", "VLI", "Véhicule léger infirmier"),
        new(Mass, "VLR DSM 274", "VLR DSM", "Directeur des secours médicaux"),
        new(Mass, "CRAC 12", "CRAC", "Ravitaillement en air comprimé"),
        new(Mass, "CMO 01", "CMO", "PC médical")
    ];
}
