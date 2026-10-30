namespace Argos.Shared.Contracts;

// =====================================================================
// eFiBi : consultation en LECTURE SEULE des fiches bilans de la base FiveM (vdms_efibi_records).
// Aucune création, modification, suppression ni transmission depuis ARGOS.
// =====================================================================

public sealed class EfibiSummary
{
    public long Id { get; set; }
    public string ShareCode { get; set; } = string.Empty;
    public string InterventionNumber { get; set; } = string.Empty;
    public string VictimName { get; set; } = string.Empty;
    public string TransmissionStatus { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;          // citizenid du rédacteur (FiveM)
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? TransmittedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Critères de recherche (registre et recherche avancée), combinables.</summary>
public sealed class EfibiQuery
{
    public string? Text { get; set; }                 // n° d'intervention, victime, identifiant ou code de partage
    public string? Intervention { get; set; }
    public string? Victim { get; set; }
    public long? Id { get; set; }
    public string? ShareCode { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public bool OldestFirst { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class EfibiPage
{
    public List<EfibiSummary> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public List<string> Statuses { get; set; } = [];
    public bool Configured { get; set; }
    public string? Error { get; set; }

    public int PageCount => PageSize <= 0 ? 1 : Math.Max(1, (Total + PageSize - 1) / PageSize);
}

public sealed class EfibiField
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class EfibiList
{
    public string Name { get; set; } = string.Empty;
    public List<string> Items { get; set; } = [];
}

public sealed class EfibiSection
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<EfibiField> Fields { get; set; } = [];
    public List<EfibiList> Lists { get; set; } = [];
}

/// <summary>Fiche complète, telle qu'enregistrée (rubriques du formulaire eFiBi).</summary>
public sealed class EfibiRecord
{
    public EfibiSummary Summary { get; set; } = new();
    public List<EfibiSection> Sections { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = [];
    public string? Error { get; set; }
}

public static class EfibiText
{
    public const string NonRenseigne = "Non renseigné";

    /// <summary>Ordre d'affichage des rubriques connues ; les autres suivent.</summary>
    public static readonly IReadOnlyList<string> SectionOrder =
        ["identityForm", "circonstancielForm", "circulatoryForm", "neuroForm", "lesionnelForm", "accouchementForm",
         "regulationForm", "transmissionForm"];

    public static string ListName(string key) => key switch
    {
        "traitementList" => "Traitements en cours",
        "maladieList" => "Antécédents médicaux",
        "allergieList" => "Allergies",
        "hospitalisationList" => "Hospitalisations",
        "lesionList" => "Lésions",
        "regTimeline" => "Chronologie de la régulation",
        _ => key
    };
}
