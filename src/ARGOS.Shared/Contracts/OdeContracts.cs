namespace Argos.Shared.Contracts;

// =====================================================================
// ADAGIO ODE : appels, interventions, ordres de départ, statuts d'engins.
// Les mêmes objets circulent entre le serveur (base SQLite) et les postes (JSON).
// =====================================================================

/// <summary>Procédures de départ (touches F1 à F9 de la prise d'appel).</summary>
public static class Procedures
{
    public sealed record ProcedureDefinition(string Key, string Code, string Label, string Color, string TextColor, bool Departure);

    // Departure = la touche engage des moyens (création d'un ODE). Sinon l'appel est clos sans départ.
    public static readonly IReadOnlyList<ProcedureDefinition> All =
    [
        new("F1", "BLANCHE", "BLANCHE", "#FFFFFF", "#000000", true),
        new("F2", "ORANGE", "ORANGE", "#F0862F", "#000000", true),
        new("F3", "ROUGE", "ROUGE", "#E8302A", "#000000", true),
        new("F4", "EXERCICE", "EXERCICE", "#F7C99B", "#000000", true),
        new("F5", "INU", "INU", "#2C6FD1", "#000000", false),
        new("F6", "VERTE", "VERTE", "#B4F7A1", "#000000", true),
        new("F7", "HMB", "HMB", "#8B4FD8", "#000000", true),
        new("F8", "RETARD", "RETARD", "#F08BCB", "#000000", true),
        new("F9", "ADRESSE_INEXISTANTE", "ADRESSE INEXISTANTE", "#606060", "#000000", false)
    ];

    public static ProcedureDefinition? Find(string? keyOrCode)
        => All.FirstOrDefault(p => string.Equals(p.Key, keyOrCode, StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(p.Code, keyOrCode, StringComparison.OrdinalIgnoreCase));

    /// <summary>« Mission BSPP - Procédure BLANCHE », en-tête de l'ODE.</summary>
    public static string MissionTitle(string? code)
        => "Mission BSPP - Procédure " + (Find(code)?.Label ?? "BLANCHE");
}

public static class CallStatuses
{
    public const string EnCours = "En cours";            // décroché, saisie en cours
    public const string Enregistre = "Enregistré";       // sauvegardé sans départ
    public const string Transmis = "Transmis";           // transmis pour traitement
    public const string Intervention = "Intervention";   // rattaché à une intervention (ODE)
    public const string SansSuite = "Sans suite";        // classé sans suite

    public static readonly IReadOnlyList<string> All = [EnCours, Enregistre, Transmis, Intervention, SansSuite];
}

public static class CallOrigins
{
    public static readonly IReadOnlyList<string> All =
        ["Téléphone urbain", "Téléphone portable", "18", "112", "Transfert 15", "Transfert 17", "Autre"];
}

public static class EnginStatuses
{
    public const string Disponible = "Disponible";
    public const string Alerte = "Alerté";
    public const string Parti = "Parti";
    public const string SurLesLieux = "Sur les lieux";
    public const string Transport = "Transport";
    public const string DisponibleIntervention = "Disponible sur intervention";
    public const string Rentre = "Rentré";
    public const string Indisponible = "Indisponible";
    public const string Maintenance = "En maintenance";

    /// <summary>Statuts proposés pour un engin engagé, dans l'ordre du déroulé.</summary>
    public static readonly IReadOnlyList<string> Operationnels =
        [Alerte, Parti, SurLesLieux, Transport, DisponibleIntervention, Rentre];

    /// <summary>L'engin peut-il recevoir un nouvel ordre de départ ?</summary>
    public static bool IsAvailable(string status) => status is Disponible or DisponibleIntervention or Rentre;

    /// <summary>L'engin est-il engagé sur une intervention ?</summary>
    public static bool IsEngaged(string status) => status is Alerte or Parti or SurLesLieux or Transport;
}

/// <summary>Événement horodaté (main courante d'un appel ou d'une intervention).</summary>
public sealed class OdeEvent
{
    public DateTimeOffset At { get; set; }

    /// <summary>Heure locale du poste (affichage).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime LocalAt => At.ToLocalTime().DateTime;

    public string Author { get; set; } = string.Empty;
    public string Kind { get; set; } = "info";
    public string Text { get; set; } = string.Empty;
}

/// <summary>Fiche d'appel (prise d'appel).</summary>
public sealed class CallRecord
{
    public string Id { get; set; } = string.Empty;               // identifiant unique : 20261029-0004
    public string Status { get; set; } = CallStatuses.EnCours;
    public DateTimeOffset ReceivedAt { get; set; }               // groupe horaire du décroché
    public DateTimeOffset? EndedAt { get; set; }                 // raccroché (durée de l'appel)
    public string OperatorUserId { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public string Poste { get; set; } = "ADAGIO";
    public string? SimcallCallId { get; set; }

    // Informations téléphoniques
    public string Origin { get; set; } = "Téléphone urbain";
    public string CallerPhone { get; set; } = string.Empty;      // numéro du requérant
    public string ImplicatedPhone { get; set; } = string.Empty;  // numéro de l'impliqué
    public string CallerName { get; set; } = string.Empty;

    // Localisation de l'événement
    public string LocationMode { get; set; } = "NORMAL";
    public string LocationNetwork { get; set; } = "ROUTIER/SITES";
    public string StreetNumber { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;           // voie - site - commune
    public bool LocationConfirmed { get; set; }

    // Adresse de l'alerte
    public string Cstc { get; set; } = string.Empty;
    public string AlertAddress { get; set; } = string.Empty;
    public string Locality { get; set; } = string.Empty;         // commune / quartier, ex. L'ECHAT
    public string Building { get; set; } = string.Empty;         // bâtiment, étage, appartement
    public string Access { get; set; } = string.Empty;           // codes, accès
    public string Observations { get; set; } = string.Empty;

    // Motif de l'appel
    public string NatureCode { get; set; } = string.Empty;       // nature présumée puis définitive
    public string Reason { get; set; } = string.Empty;           // grande zone de texte (circonstances)
    public int VictimCount { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string Risks { get; set; } = string.Empty;

    // Traitement
    public string Procedure { get; set; } = string.Empty;        // F1…F9
    public string ClosingReason { get; set; } = string.Empty;    // motif du classement sans suite
    public string? InterventionId { get; set; }

    public List<OdeEvent> Events { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; }

    public string AddressLine
    {
        get
        {
            var street = string.Join(" ", new[] { StreetNumber, Street }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return string.IsNullOrWhiteSpace(AlertAddress) ? street : AlertAddress;
        }
    }
}

/// <summary>Engin engagé sur une intervention.</summary>
public sealed class EngagedEngin
{
    public string Nom { get; set; } = string.Empty;              // « VSAV 947 CRET »
    public bool FirstDeparture { get; set; }                     // premier départ ou complémentaire
    public string Status { get; set; } = EnginStatuses.Alerte;
    public DateTimeOffset AlertedAt { get; set; }
    public DateTimeOffset StatusAt { get; set; }
    public int OrderNumber { get; set; } = 1;
}

/// <summary>Ordre de départ (une intervention peut en compter plusieurs : renforts).</summary>
public sealed class DepartureOrder
{
    public int Number { get; set; } = 1;
    public DateTimeOffset ValidatedAt { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public string Procedure { get; set; } = "BLANCHE";
    public List<string> Engins { get; set; } = [];
    public string Consignes { get; set; } = string.Empty;
    public int PrintCount { get; set; }
}

/// <summary>Intervention : référence commune entre ODE, MCO et eFiBi (numéro « 210-593 »).</summary>
public sealed class Intervention
{
    public string Id { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;

    public string NatureCode { get; set; } = string.Empty;
    public string Procedure { get; set; } = "BLANCHE";
    public string Secteur { get; set; } = string.Empty;
    public string Cstc { get; set; } = "CRET";
    public string Address { get; set; } = string.Empty;
    public string Locality { get; set; } = string.Empty;
    public string Building { get; set; } = string.Empty;
    public string Access { get; set; } = string.Empty;
    public string CallerPhone { get; set; } = string.Empty;
    public string CallerInfo { get; set; } = string.Empty;       // informations requérants
    public string Consignes { get; set; } = string.Empty;        // consignes chef d'agrès

    public List<string> CallIds { get; set; } = [];
    public List<EngagedEngin> Engins { get; set; } = [];
    public List<DepartureOrder> Orders { get; set; } = [];
    public List<OdeEvent> Events { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>État courant d'un engin (synchronisé avec ADAGIO REMISE).</summary>
public sealed class EnginState
{
    public string Nom { get; set; } = string.Empty;

    // Fiche du parc (ADAGIO REMISE), recopiée par le serveur à chaque lecture de l'état.
    public string Type { get; set; } = string.Empty;
    public string Libelle { get; set; } = string.Empty;
    public string Centre { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Availability { get; set; } = FleetAvailability.Disponible;

    /// <summary>« EPAN 24 MALF (EPAN_1_MALF) », forme de l'ordre de départ.</summary>
    public string NomOde => string.IsNullOrWhiteSpace(Code) ? Nom : $"{Nom} ({Code})";

    public string Status { get; set; } = EnginStatuses.Disponible;
    public string? InterventionNumber { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>Ligne du journal d'activité (non modifiable).</summary>
public sealed class JournalEntry
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime LocalAt => At.ToLocalTime().DateTime;

    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

/// <summary>Tout ce dont ADAGIO ODE a besoin à l'ouverture (et après chaque changement).</summary>
public sealed class OdeSnapshot
{
    public List<CallRecord> Calls { get; set; } = [];
    public List<Intervention> Interventions { get; set; } = [];
    public List<EnginState> Engins { get; set; } = [];
    public DateTimeOffset ServerTime { get; set; }
}

// ===== Requêtes =====

/// <summary>Création d'un ODE, depuis un appel ou manuellement.</summary>
public sealed class CreateOdeRequest
{
    public string? CallId { get; set; }
    public string NatureCode { get; set; } = string.Empty;
    public string Procedure { get; set; } = "BLANCHE";
    public string Secteur { get; set; } = string.Empty;
    public string Cstc { get; set; } = "CRET";
    public string Address { get; set; } = string.Empty;
    public string Locality { get; set; } = string.Empty;
    public string Building { get; set; } = string.Empty;
    public string Access { get; set; } = string.Empty;
    public string CallerPhone { get; set; } = string.Empty;
    public string CallerInfo { get; set; } = string.Empty;
    public string Consignes { get; set; } = string.Empty;
    public string FirstEngin { get; set; } = string.Empty;
    public List<string> ComplementaryEngins { get; set; } = [];

    /// <summary>Rattacher à une intervention existante (renfort) au lieu d'en créer une.</summary>
    public string? ExistingInterventionId { get; set; }
}

public sealed record EnginStatusRequest(string Nom, string Status);
public sealed record TextRequest(string Text);

/// <summary>Impression d'un ODE : image PNG de la feuille (base 64), publiée sur Discord si le webhook est configuré.</summary>
public sealed class PrintRequest
{
    public string? ImagePng { get; set; }
}

/// <summary>Valeurs de <see cref="OdeResult.Id"/> renvoyées par l'impression, pour le webhook Discord.</summary>
public static class PrintDiscord
{
    public const string Sent = "discord:envoye";
    public const string Disabled = "discord:inactif";
    public const string Failed = "discord:echec";
}
public sealed record SansSuiteRequest(string Procedure, string Reason);
public sealed record LinkCallRequest(string InterventionId);

/// <summary>Résultat d'une action ODE : ok, ou message d'erreur lisible.</summary>
public sealed record OdeResult(bool Ok, string? Error = null, string? Id = null);

// ===== Intégration SIMCALL =====

/// <summary>SIMCALL → ARGOS : un opérateur vient de décrocher un appel au 18.</summary>
public sealed record SimcallCallAnswered(
    string SimcallCallId,
    string DiscordUserId,
    string OperatorName,
    string? CallerNumber,
    DateTimeOffset AnsweredAt);

/// <summary>SIMCALL → ARGOS : l'appel est terminé (raccroché).</summary>
public sealed record SimcallCallEnded(string SimcallCallId, DateTimeOffset EndedAt);
