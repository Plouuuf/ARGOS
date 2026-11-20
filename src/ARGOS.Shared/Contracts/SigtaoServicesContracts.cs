namespace Argos.Shared.Contracts;

// =====================================================================
// SIGTAO : services de la compagnie (remise, instruction, casernement, SOA).
// Liste fixe des services et de leurs rôles ; un militaire peut appartenir à plusieurs services.
// =====================================================================

/// <summary>Un service de la compagnie et les rôles qu'on peut y tenir.</summary>
/// <remarks>Chaque membre, quel que soit le service, a une colonne « Observations » en texte libre.</remarks>
public sealed record SigtaoServiceDefinition(string Id, string Label, IReadOnlyList<string> Roles);

/// <summary>Services de la compagnie (liste fixe).</summary>
public static class SigtaoServices
{
    public const string Remise = "remise";
    public const string Instruction = "instruction";
    public const string Casernement = "casernement";
    public const string Soa = "soa";

    public static readonly IReadOnlyList<SigtaoServiceDefinition> All =
    [
        new(Remise, "Service REMISE", ["Sous-officier de remise", "Caporal de remise", "Garde-remise"]),
        new(Instruction, "Service Instruction", ["Sous-officier d'instruction", "Caporal d'instruction", "Instructeur"]),
        new(Casernement, "Service Casernement", ["Sous-officier de casernement", "Caporal de casernement", "Effectif"]),
        new(Soa, "Sous-officier d'active (SOA)", ["SOA CIE", "Secrétaire SOA"])
    ];

    public static SigtaoServiceDefinition? Find(string? id) => All.FirstOrDefault(s => s.Id == id);
}

/// <summary>Un militaire affecté à un service, avec son rôle.</summary>
public sealed class ServiceMember
{
    /// <summary>Identifiant du dossier militaire.</summary>
    public string MilitaryId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    /// <summary>Texte libre (tous les services).</summary>
    public string Observations { get; set; } = string.Empty;
    public DateTimeOffset AddedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Membres d'un service (un document par service, identifiant = <see cref="SigtaoServiceDefinition.Id"/>).</summary>
public sealed class ServiceAffectations
{
    public string Id { get; set; } = string.Empty;
    public List<ServiceMember> Members { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>Ajoute un militaire à un service, change son rôle ou ses observations, ou le retire.</summary>
public sealed class ServiceMemberRequest
{
    public string ServiceId { get; set; } = string.Empty;
    public string MilitaryId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Observations { get; set; } = string.Empty;
    /// <summary>Vrai : retirer le militaire du service.</summary>
    public bool Retirer { get; set; }
}
