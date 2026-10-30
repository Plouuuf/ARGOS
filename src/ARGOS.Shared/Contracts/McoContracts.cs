namespace Argos.Shared.Contracts;

// =====================================================================
// MCO : main courante opérationnelle, compte-rendu d'intervention d'un engin.
// Rattachée à une intervention ADAGIO ODE (numéro commun « 210-593 »), dont elle reprend
// la nature, l'adresse et les horaires de statut. Brouillon → validée (figée) → archivée.
// =====================================================================

public static class McoStatuses
{
    public const string Brouillon = "Brouillon";
    public const string Validee = "Validée";
    public const string Archivee = "Archivée";

    public static readonly IReadOnlyList<string> All = [Brouillon, Validee, Archivee];
}

/// <summary>Fonctions d'un personnel dans l'équipage.</summary>
public static class McoFonctions
{
    public static readonly IReadOnlyList<string> All =
        ["Chef d'agrès", "Conducteur", "Chef d'équipe", "Équipier", "Stagiaire", "Infirmier", "Médecin", "Officier"];

    public static readonly IReadOnlyList<string> Grades =
        ["SAP 2CL", "SAP 1CL", "1CL+15", "CPL", "CPL+15", "CCH", "CCH+15", "CCH CT1", "CCH CT1+15", "SGT", "SGT UV2", "SCH",
         "ADJ", "ADC", "MAJ", "ASP", "SLT", "LTN", "CNE", "CDT", "LCL", "COL", "ISP", "MED"];
}

public sealed class McoPerson
{
    public string Grade { get; set; } = string.Empty;
    public string Nom { get; set; } = string.Empty;
    public string Prenom { get; set; } = string.Empty;
    public string Fonction { get; set; } = string.Empty;

    public string Display => string.Join(" ", new[] { Grade, Nom.ToUpperInvariant(), Prenom }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>Événement horodaté de la chronologie (heure « HH:mm »).</summary>
public sealed class McoEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Time { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

public sealed class McoHistoryEntry
{
    public DateTimeOffset At { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime LocalAt => At.ToLocalTime().DateTime;

    public string By { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

/// <summary>Compte-rendu d'intervention (MCO).</summary>
public sealed class McoReport
{
    public string Id { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;              // « MCO-2026-0012 »
    public string Status { get; set; } = McoStatuses.Brouillon;

    // ===== Identification =====
    public string InterventionId { get; set; } = string.Empty;      // intervention ADAGIO ODE associée
    public string InterventionNumber { get; set; } = string.Empty;  // « 210-593 »
    public DateTimeOffset Date { get; set; }
    public string NatureCode { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Locality { get; set; } = string.Empty;            // commune / quartier
    public string Building { get; set; } = string.Empty;            // bâtiment, étage
    public string Access { get; set; } = string.Empty;              // accès, codes
    public string LocationDetails { get; set; } = string.Empty;

    // ===== Horodatage (« HH:mm ») =====
    public string TimeDepart { get; set; } = string.Empty;
    public string TimeArrivee { get; set; } = string.Empty;
    public string TimeDebutOperations { get; set; } = string.Empty;
    public string TimeFinOperations { get; set; } = string.Empty;
    public string TimeDisponible { get; set; } = string.Empty;
    public string TimeRetour { get; set; } = string.Empty;
    public List<McoEvent> Events { get; set; } = [];

    // ===== Engin et personnel =====
    public string Engin { get; set; } = string.Empty;
    public McoPerson ChefAgres { get; set; } = new() { Fonction = "Chef d'agrès" };
    public List<McoPerson> Crew { get; set; } = [];
    public List<string> OtherEngins { get; set; } = [];
    public int Effectif => (string.IsNullOrWhiteSpace(ChefAgres.Nom) ? 0 : 1) + Crew.Count;

    // ===== Déroulement =====
    public string SituationArrivee { get; set; } = string.Empty;
    public string Risques { get; set; } = string.Empty;
    public string Reconnaissances { get; set; } = string.Empty;
    public string Actions { get; set; } = string.Empty;
    public string Moyens { get; set; } = string.Empty;
    public string Difficultes { get; set; } = string.Empty;
    public string Renforts { get; set; } = string.Empty;
    public string SituationFinale { get; set; } = string.Empty;

    // ===== Bilan =====
    public int VictimCount { get; set; }
    public string BilanGeneral { get; set; } = string.Empty;
    public string ActionsSecours { get; set; } = string.Empty;
    public string Transports { get; set; } = string.Empty;
    public string Destinations { get; set; } = string.Empty;
    public string ObservationsChef { get; set; } = string.Empty;
    public List<string> EfibiIds { get; set; } = [];

    // ===== Synthèse et validation =====
    public string Synthese { get; set; } = string.Empty;
    public string AuthorUserId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset? ValidatedAt { get; set; }
    public string ValidatedBy { get; set; } = string.Empty;
    public DateTimeOffset? ArchivedAt { get; set; }
    public List<McoHistoryEntry> History { get; set; } = [];

    public bool IsEditable => Status == McoStatuses.Brouillon;
}

public sealed class McoSnapshot
{
    public List<McoReport> Reports { get; set; } = [];
    public DateTimeOffset ServerTime { get; set; }
}

/// <summary>
/// Pré-remplissage d'une MCO depuis l'intervention ADAGIO ODE : nature, adresse, autres engins
/// et horaires tirés des changements de statut de l'engin (« VSAV 947 CRET : Alerté → Parti. »).
/// </summary>
public static class McoPrefill
{
    public static void FromIntervention(McoReport report, Intervention intervention, string? engin)
    {
        report.InterventionId = intervention.Id;
        report.InterventionNumber = intervention.Number;
        report.Date = intervention.CreatedAt;
        report.NatureCode = intervention.NatureCode;
        report.Address = intervention.Address;
        report.Locality = intervention.Locality;
        report.Building = intervention.Building;
        report.Access = intervention.Access;
        if (string.IsNullOrWhiteSpace(report.LocationDetails))
            report.LocationDetails = intervention.CallerInfo;

        var nom = string.IsNullOrWhiteSpace(engin)
            ? intervention.Engins.OrderByDescending(e => e.FirstDeparture).FirstOrDefault()?.Nom ?? string.Empty
            : engin.Trim();
        report.Engin = nom;
        report.OtherEngins = intervention.Engins.Select(e => e.Nom)
            .Where(n => !string.Equals(n, nom, StringComparison.OrdinalIgnoreCase)).ToList();

        string? TimeOf(string status) => intervention.Events
            .Where(e => e.Kind == "statut" && e.Text.StartsWith(nom + " :", StringComparison.OrdinalIgnoreCase)
                        && e.Text.TrimEnd('.').EndsWith("→ " + status, StringComparison.Ordinal))
            .OrderBy(e => e.At)
            .Select(e => e.At.ToLocalTime().ToString("HH:mm"))
            .FirstOrDefault();

        report.TimeDepart = Keep(report.TimeDepart, TimeOf(EnginStatuses.Parti));
        report.TimeArrivee = Keep(report.TimeArrivee, TimeOf(EnginStatuses.SurLesLieux));
        report.TimeDisponible = Keep(report.TimeDisponible, TimeOf(EnginStatuses.DisponibleIntervention));
        report.TimeRetour = Keep(report.TimeRetour, TimeOf(EnginStatuses.Rentre));

        // Renforts demandés : messages « renfort » de l'intervention.
        if (string.IsNullOrWhiteSpace(report.Renforts))
            report.Renforts = string.Join("\n", intervention.Events.Where(e => e.Kind == "renfort")
                .Select(e => $"{e.At.ToLocalTime():HH:mm} {e.Text}"));
    }

    private static string Keep(string current, string? found)
        => string.IsNullOrWhiteSpace(current) ? found ?? string.Empty : current;

    /// <summary>Synthèse générée à partir des rubriques renseignées (modifiable ensuite).</summary>
    public static string Synthese(McoReport r)
    {
        var lines = new List<string>();
        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                lines.Add($"{label} : {value.Trim()}");
        }

        var nature = Natures.Find(r.NatureCode)?.Display ?? r.NatureCode;
        lines.Add($"Intervention {r.InterventionNumber} du {r.Date.ToLocalTime():dd/MM/yyyy} - {nature}");
        lines.Add($"Adresse : {string.Join(" - ", new[] { r.Address, r.Locality, r.Building }.Where(s => !string.IsNullOrWhiteSpace(s)))}");
        lines.Add($"Engin : {r.Engin} - Chef d'agrès : {r.ChefAgres.Display} - Effectif : {r.Effectif}");
        if (r.Crew.Count > 0)
            lines.Add("Équipage : " + string.Join(", ", r.Crew.Select(c => $"{c.Display} ({c.Fonction})")));
        if (r.OtherEngins.Count > 0)
            lines.Add("Autres engins : " + string.Join(", ", r.OtherEngins));

        var times = new[]
        {
            ("Départ", r.TimeDepart), ("Sur les lieux", r.TimeArrivee), ("Début opérations", r.TimeDebutOperations),
            ("Fin opérations", r.TimeFinOperations), ("Disponible", r.TimeDisponible), ("Retour CS", r.TimeRetour)
        }.Where(t => !string.IsNullOrWhiteSpace(t.Item2)).Select(t => $"{t.Item1} {t.Item2}");
        var timeLine = string.Join(" · ", times);
        if (timeLine.Length > 0) lines.Add("Horaires : " + timeLine);
        foreach (var e in r.Events.Where(e => !string.IsNullOrWhiteSpace(e.Text)).OrderBy(e => e.Time, StringComparer.Ordinal))
            lines.Add($"  {e.Time} {e.Text.Trim()}");

        lines.Add(string.Empty);
        Add("Situation à l'arrivée", r.SituationArrivee);
        Add("Risques", r.Risques);
        Add("Reconnaissances", r.Reconnaissances);
        Add("Actions entreprises", r.Actions);
        Add("Moyens utilisés", r.Moyens);
        Add("Difficultés", r.Difficultes);
        Add("Renforts", r.Renforts);
        Add("Situation finale", r.SituationFinale);

        lines.Add(string.Empty);
        lines.Add($"Bilan : {r.VictimCount} victime(s).");
        Add("Bilan général", r.BilanGeneral);
        Add("Actions de secours", r.ActionsSecours);
        Add("Transports", r.Transports);
        Add("Destinations", r.Destinations);
        Add("Observations du chef d'agrès", r.ObservationsChef);
        if (r.EfibiIds.Count > 0)
            lines.Add("Fiches eFiBi : " + string.Join(", ", r.EfibiIds));
        return string.Join("\n", lines).Trim();
    }

    /// <summary>Éléments manquants pour valider la MCO (liste vide = validable).</summary>
    public static List<string> Missing(McoReport r)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(r.InterventionNumber)) missing.Add("numéro d'intervention");
        if (string.IsNullOrWhiteSpace(r.NatureCode)) missing.Add("nature");
        if (string.IsNullOrWhiteSpace(r.Address)) missing.Add("adresse");
        if (string.IsNullOrWhiteSpace(r.Engin)) missing.Add("engin");
        if (string.IsNullOrWhiteSpace(r.ChefAgres.Nom)) missing.Add("chef d'agrès");
        if (string.IsNullOrWhiteSpace(r.TimeDepart)) missing.Add("heure de départ");
        if (string.IsNullOrWhiteSpace(r.TimeArrivee)) missing.Add("heure d'arrivée");
        if (string.IsNullOrWhiteSpace(r.Synthese)) missing.Add("synthèse");
        return missing;
    }
}
