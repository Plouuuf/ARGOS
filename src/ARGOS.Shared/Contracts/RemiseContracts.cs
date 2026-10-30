namespace Argos.Shared.Contracts;

// =====================================================================
// ADAGIO REMISE : parc d'engins, matériel, vérifications, maintenance, stocks.
// Le statut opérationnel (alerté, parti, sur les lieux…) reste tenu par ADAGIO ODE ;
// REMISE tient la disponibilité administrative (disponible, indisponible, en maintenance)
// et la répercute dans l'ODE : un engin indisponible ou en maintenance ne peut pas être engagé.
// =====================================================================

/// <summary>Disponibilité administrative d'un engin (ADAGIO REMISE).</summary>
public static class FleetAvailability
{
    public const string Disponible = "Disponible";
    public const string Indisponible = "Indisponible";
    public const string Maintenance = "En maintenance";

    public static readonly IReadOnlyList<string> All = [Disponible, Indisponible, Maintenance];
}

/// <summary>Niveau d'immobilisation d'une anomalie.</summary>
public static class Immobilisations
{
    public const string Aucune = "Aucune - engin opérationnel";
    public const string Partielle = "Partielle - engin dégradé";
    public const string Totale = "Totale - engin immobilisé";

    public static readonly IReadOnlyList<string> All = [Aucune, Partielle, Totale];
}

/// <summary>Résultat de la vérification d'un équipement.</summary>
public static class CheckResults
{
    public const string Present = "Présent";
    public const string Absent = "Absent";
    public const string Defectueux = "Défectueux";

    public static readonly IReadOnlyList<string> All = [Present, Absent, Defectueux];
}

/// <summary>Mouvements de stock du magasin.</summary>
public static class StockKinds
{
    public const string Entree = "Entrée";
    public const string Sortie = "Sortie";
    public const string Affectation = "Affectation à un engin";
    public const string Retrait = "Retrait d'un engin";
    public const string Ajustement = "Ajustement d'inventaire";

    public static readonly IReadOnlyList<string> All = [Entree, Sortie, Affectation, Retrait, Ajustement];

    public static bool NeedsEngin(string kind) => kind is Affectation or Retrait;
}

public static class MaintenanceStatuses
{
    public const string Ouverte = "Ouverte";
    public const string Cloturee = "Clôturée";
}

/// <summary>Engin du parc, tel que géré dans ADAGIO REMISE.</summary>
public sealed class FleetEngin
{
    /// <summary>Nom complet, clé partagée avec l'ODE : « VSAV 947 CRET ».</summary>
    public string Nom { get; set; } = string.Empty;

    public string Indicatif { get; set; } = string.Empty;          // « VSAV 947 »
    public string Type { get; set; } = string.Empty;               // « VSAV »
    public string TypeGdm { get; set; } = string.Empty;            // « SPVL » sur la feuille GDM (vide = Type)
    public string Libelle { get; set; } = string.Empty;            // « Secours et assistance aux victimes »
    public string Centre { get; set; } = string.Empty;             // CRET, MALF, MASS
    public string Code { get; set; } = string.Empty;               // « VSAV_2_CRET »
    public string Immatriculation { get; set; } = string.Empty;
    public int Ordre { get; set; }

    /// <summary>Photo choisie dans ADAGIO REMISE (JPEG/PNG en base 64) ; vide = photo du type.</summary>
    public string ImageData { get; set; } = string.Empty;

    public DateTimeOffset? NextRevision { get; set; }
    public string Availability { get; set; } = FleetAvailability.Disponible;
    public string AvailabilityReason { get; set; } = string.Empty;
    public DateTimeOffset AvailabilitySince { get; set; }
    public List<DotationLine> Dotation { get; set; } = [];
    public bool Active { get; set; } = true;

    /// <summary>Équipage armé par la dernière feuille de garde validée dans SIGTAO.</summary>
    public List<GardeAffectation> Equipage { get; set; } = [];
    /// <summary>Jour de garde (« yyyy-MM-dd ») qui a armé l'engin ; vide = jamais armé.</summary>
    public string EquipageGarde { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;

    /// <summary>« VSAV BSPP », comme sur la feuille GDM.</summary>
    public string TypeLabel => $"{(string.IsNullOrWhiteSpace(TypeGdm) ? Type : TypeGdm)} BSPP".Trim();

    /// <summary>Rang de l'engin dans son type et son centre (le « 2 » de VSAV_2_CRET).</summary>
    public string Rang
    {
        get
        {
            var parts = Code.Split('_');
            return parts.Length >= 3 && int.TryParse(parts[^2], out var n) ? n.ToString() : "1";
        }
    }

    /// <summary>« VSAV 947 / VSAV 947 » ou « VID 251 / BF-642-ZL », comme sur la feuille GDM.</summary>
    public string GdmTitle => $"{Indicatif} / {(string.IsNullOrWhiteSpace(Immatriculation) ? Indicatif : Immatriculation)}";

    public static string BuildNom(string indicatif, string centre) => $"{indicatif.Trim()} {centre.Trim()}".Trim().ToUpperInvariant();
}

/// <summary>Matériel embarqué obligatoire d'un engin.</summary>
public sealed class DotationLine
{
    public string ItemId { get; set; } = string.Empty;
    public int Required { get; set; }
    public int Present { get; set; }
}

/// <summary>Référence de matériel (magasin du centre de secours).</summary>
public sealed class MaterielItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Unit { get; set; } = "unité";

    /// <summary>Quantité disponible au magasin (hors engins).</summary>
    public int Stock { get; set; }

    /// <summary>Seuil d'alerte : en dessous, le stock est insuffisant.</summary>
    public int AlertThreshold { get; set; }

    public bool Active { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Vérification d'un engin (inventaire de la dotation obligatoire).</summary>
public sealed class Verification
{
    public string Id { get; set; } = string.Empty;
    public string Engin { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; }
    public string By { get; set; } = string.Empty;                  // militaire responsable
    public string ByUserId { get; set; } = string.Empty;
    public List<VerificationLine> Lines { get; set; } = [];
    public string Observations { get; set; } = string.Empty;

    public int Missing => Lines.Count(l => l.Result == CheckResults.Absent);
    public int Defective => Lines.Count(l => l.Result == CheckResults.Defectueux);
    public bool Conforme => Missing == 0 && Defective == 0 && Lines.All(l => l.Counted >= l.Required);
}

public sealed class VerificationLine
{
    public string ItemId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Required { get; set; }
    public int Counted { get; set; }
    public string Result { get; set; } = CheckResults.Present;
    public string Observation { get; set; } = string.Empty;
}

/// <summary>Anomalie signalée sur un engin et suivi de sa maintenance.</summary>
public sealed class MaintenanceRecord
{
    public string Id { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;              // « MNT-0012 »
    public string Engin { get; set; } = string.Empty;
    public DateTimeOffset ReportedAt { get; set; }
    public string ReportedBy { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Immobilisation { get; set; } = Immobilisations.Aucune;
    public string Source { get; set; } = "Signalement";             // ou « Vérification »
    public string Status { get; set; } = MaintenanceStatuses.Ouverte;
    public List<RepairEntry> Repairs { get; set; } = [];
    public DateTimeOffset? ClosedAt { get; set; }
    public string ClosedBy { get; set; } = string.Empty;
    public bool IsOpen => Status == MaintenanceStatuses.Ouverte;
}

public sealed class RepairEntry
{
    public DateTimeOffset At { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime LocalAt => At.ToLocalTime().DateTime;

    public string By { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

/// <summary>Mouvement de stock (non modifiable).</summary>
public sealed class StockMovement
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; }
    public string By { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string Engin { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public int StockAfter { get; set; }
}

/// <summary>État complet d'ADAGIO REMISE (GET /api/remise/state).</summary>
public sealed class RemiseSnapshot
{
    public List<FleetEngin> Engins { get; set; } = [];
    public List<MaterielItem> Items { get; set; } = [];
    public List<Verification> Verifications { get; set; } = [];
    public List<MaintenanceRecord> Maintenances { get; set; } = [];
    public List<StockMovement> Movements { get; set; } = [];

    /// <summary>L'utilisateur peut-il gérer le parc (création, modification, stocks, remise en service) ?</summary>
    public bool CanManage { get; set; }

    public DateTimeOffset ServerTime { get; set; }
}

// ===== Requêtes =====

/// <summary>Création (OriginalNom vide) ou modification d'un engin.</summary>
public sealed class SaveEnginRequest
{
    public string? OriginalNom { get; set; }
    public FleetEngin Engin { get; set; } = new();
}

public sealed class VerificationRequest
{
    public string Engin { get; set; } = string.Empty;
    public List<VerificationLine> Lines { get; set; } = [];
    public string Observations { get; set; } = string.Empty;
}

public sealed class AnomalyRequest
{
    public string Engin { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Immobilisation { get; set; } = Immobilisations.Aucune;
}

public sealed class RepairRequest
{
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset? NextRevision { get; set; }
}

public sealed class CloseMaintenanceRequest
{
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset? NextRevision { get; set; }
    public bool ReturnToService { get; set; } = true;
}

public sealed class StockRequest
{
    public string ItemId { get; set; } = string.Empty;
    public string Kind { get; set; } = StockKinds.Entree;
    public int Quantity { get; set; }
    public string Engin { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
}

/// <summary>Actions du journal propres à ADAGIO REMISE (page Historique et traçabilité).</summary>
public static class RemiseJournal
{
    public const string EnginCree = "Engin créé";
    public const string EnginModifie = "Engin modifié";
    public const string Disponibilite = "Changement de disponibilité";
    public const string VerificationFaite = "Vérification d'engin";
    public const string Anomalie = "Anomalie signalée";
    public const string Reparation = "Réparation";
    public const string MaintenanceCloturee = "Maintenance clôturée";
    public const string Mouvement = "Mouvement de stock";
    public const string Materiel = "Référence de matériel";

    /// <summary>Actions de l'ODE qui changent le statut d'un engin.</summary>
    public static readonly IReadOnlyList<string> OdeActions =
        ["Changement de statut", "Création d'ODE", "Départ complémentaire", "Clôture d'intervention"];

    public static readonly IReadOnlyList<string> Actions =
        [EnginCree, EnginModifie, Disponibilite, VerificationFaite, Anomalie, Reparation, MaintenanceCloturee, Mouvement, Materiel];

    public static bool IsRemise(string action) => Actions.Contains(action) || OdeActions.Contains(action);
}
