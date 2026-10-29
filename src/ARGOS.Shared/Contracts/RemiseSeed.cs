namespace Argos.Shared.Contracts;

/// <summary>
/// Données de départ d'ADAGIO REMISE (premier démarrage du serveur) : parc de la feuille GDM,
/// références de matériel et dotations par type d'engin. Tout est ensuite modifiable dans REMISE.
/// </summary>
public static class RemiseSeed
{
    /// <summary>(id, nom, catégorie, unité, stock magasin, seuil d'alerte)</summary>
    private static readonly (string Id, string Name, string Category, string Unit, int Stock, int Threshold)[] ItemsData =
    [
        ("sac-ps", "Sac de premiers secours", "Secourisme", "sac", 3, 1),
        ("dsa", "Défibrillateur (DSA)", "Secourisme", "unité", 2, 1),
        ("tensiometre", "Tensiomètre", "Secourisme", "unité", 3, 1),
        ("oxymetre", "Oxymètre de pouls", "Secourisme", "unité", 3, 1),
        ("kit-hemorragie", "Kit hémorragie (garrot, pansement compressif)", "Secourisme", "kit", 10, 4),
        ("kit-brules", "Kit brûlés", "Secourisme", "kit", 6, 2),
        ("kit-accouchement", "Kit accouchement", "Secourisme", "kit", 4, 1),
        ("couverture-survie", "Couverture de survie", "Secourisme", "unité", 60, 20),
        ("gants", "Gants à usage unique", "Hygiène", "boîte", 40, 10),
        ("sac-infirmier", "Sac infirmier", "Secourisme", "sac", 1, 0),

        ("o2-5l", "Bouteille d'oxygène 5 L", "Oxygénothérapie", "bouteille", 8, 3),
        ("aspi-mucosites", "Aspirateur de mucosités", "Oxygénothérapie", "unité", 2, 1),
        ("respirateur", "Respirateur de transport", "Oxygénothérapie", "unité", 1, 0),
        ("pousse-seringue", "Pousse-seringue électrique", "Médical", "unité", 1, 0),

        ("collier-cervical", "Lot de colliers cervicaux", "Immobilisation", "lot", 4, 1),
        ("attelles", "Lot d'attelles", "Immobilisation", "lot", 4, 1),
        ("plan-dur", "Plan dur", "Immobilisation", "unité", 2, 1),
        ("matelas-immo", "Matelas immobilisateur à dépression", "Immobilisation", "unité", 2, 1),
        ("brancard", "Brancard", "Immobilisation", "unité", 1, 0),
        ("chaise-portoir", "Chaise portoir", "Immobilisation", "unité", 1, 0),

        ("tuyau-45", "Tuyau de 45 mm (20 m)", "Hydraulique", "tuyau", 20, 8),
        ("tuyau-70", "Tuyau de 70 mm (20 m)", "Hydraulique", "tuyau", 20, 8),
        ("lance-45", "Lance à débit variable 45", "Hydraulique", "unité", 4, 2),
        ("lance-70", "Lance à débit variable 70", "Hydraulique", "unité", 2, 1),
        ("extincteur", "Extincteur 6 kg", "Hydraulique", "unité", 6, 2),

        ("ari", "Appareil respiratoire isolant (ARI)", "Protection respiratoire", "unité", 6, 2),
        ("bouteille-ari", "Bouteille d'ARI 6,8 L", "Protection respiratoire", "bouteille", 30, 10),
        ("camera-thermique", "Caméra thermique", "Protection respiratoire", "unité", 1, 0),
        ("compresseur", "Compresseur haute pression", "Protection respiratoire", "unité", 0, 0),

        ("ecarteur", "Écarteur hydraulique", "Désincarcération", "unité", 0, 0),
        ("cisaille", "Cisaille hydraulique", "Désincarcération", "unité", 0, 0),
        ("verin", "Vérin hydraulique", "Désincarcération", "unité", 0, 0),
        ("groupe-hydraulique", "Groupe hydraulique", "Désincarcération", "unité", 0, 0),
        ("cales", "Lot de calage", "Désincarcération", "lot", 2, 1),

        ("echelle-coulisse", "Échelle à coulisse", "Sauvetage", "unité", 1, 0),
        ("lot-sauvetage", "Lot de sauvetage et protection contre les chutes", "Sauvetage", "lot", 2, 1),
        ("harnais", "Harnais", "Sauvetage", "unité", 6, 2),

        ("projecteur", "Projecteur portatif", "Éclairage", "unité", 6, 2),
        ("groupe-electrogene", "Groupe électrogène", "Éclairage", "unité", 1, 0),
        ("cones", "Cône de balisage", "Balisage", "unité", 40, 10),
        ("tronconneuse", "Tronçonneuse", "Opérations diverses", "unité", 1, 0),
        ("aspirateur-eau", "Aspirateur à eau", "Opérations diverses", "unité", 1, 0),
        ("bache", "Bâche", "Opérations diverses", "unité", 10, 4),
        ("lot-pma", "Lot poste médical avancé", "Médical", "lot", 0, 0),

        ("radio", "Poste radio portatif ANTARES", "Transmissions", "unité", 8, 3)
    ];

    private static readonly (string Id, int Qty)[] Secours =
    [
        ("sac-ps", 1), ("dsa", 1), ("o2-5l", 2), ("aspi-mucosites", 1), ("tensiometre", 1), ("oxymetre", 1),
        ("collier-cervical", 1), ("attelles", 1), ("plan-dur", 1), ("matelas-immo", 1), ("brancard", 1),
        ("chaise-portoir", 1), ("kit-hemorragie", 2), ("kit-brules", 1), ("kit-accouchement", 1),
        ("couverture-survie", 10), ("gants", 4), ("radio", 2)
    ];

    /// <summary>Dotation obligatoire par type d'engin.</summary>
    private static readonly Dictionary<string, (string Id, int Qty)[]> Dotations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["VSAV"] = Secours,
        ["PSE"] = [.. Secours, ("tuyau-45", 4), ("lance-45", 1), ("extincteur", 1), ("ari", 2), ("bouteille-ari", 2)],
        ["FPTL"] = [("tuyau-70", 8), ("tuyau-45", 8), ("lance-45", 2), ("lance-70", 1), ("ari", 4), ("bouteille-ari", 8),
                    ("camera-thermique", 1), ("echelle-coulisse", 1), ("extincteur", 2), ("lot-sauvetage", 1),
                    ("projecteur", 2), ("cones", 6), ("radio", 4)],
        ["EPA"] = [("lot-sauvetage", 1), ("harnais", 3), ("ari", 2), ("bouteille-ari", 4), ("projecteur", 2), ("cones", 4), ("radio", 2)],
        ["EPAN"] = [("lot-sauvetage", 1), ("harnais", 3), ("ari", 2), ("bouteille-ari", 4), ("projecteur", 2), ("cones", 4), ("radio", 2)],
        ["CD"] = [("ecarteur", 1), ("cisaille", 1), ("verin", 1), ("groupe-hydraulique", 1), ("cales", 1),
                  ("projecteur", 2), ("cones", 8), ("extincteur", 1), ("radio", 2)],
        ["CA"] = [("groupe-electrogene", 1), ("projecteur", 4), ("cones", 10), ("bache", 2), ("radio", 2)],
        ["VID"] = [("tronconneuse", 1), ("aspirateur-eau", 1), ("bache", 4), ("cones", 6), ("projecteur", 1), ("radio", 2)],
        ["FA"] = [("groupe-electrogene", 1), ("projecteur", 4), ("ari", 4), ("bouteille-ari", 8), ("tuyau-70", 6), ("radio", 2)],
        ["VLROGC"] = [("radio", 2), ("cones", 4), ("extincteur", 1)],
        ["VLR DSM"] = [("radio", 2), ("sac-ps", 1)],
        ["VLI"] = [("sac-infirmier", 1), ("dsa", 1), ("o2-5l", 1), ("oxymetre", 1), ("tensiometre", 1), ("radio", 1)],
        ["AR"] = [("sac-ps", 1), ("dsa", 1), ("o2-5l", 3), ("aspi-mucosites", 1), ("respirateur", 1), ("pousse-seringue", 1),
                  ("brancard", 1), ("plan-dur", 1), ("matelas-immo", 1), ("radio", 2)],
        ["CMO"] = [("lot-pma", 1), ("radio", 4), ("projecteur", 2)],
        ["CRAC"] = [("compresseur", 1), ("bouteille-ari", 20), ("ari", 4), ("radio", 2)]
    };

    public static List<MaterielItem> Items(DateTimeOffset now)
        => ItemsData.Select(i => new MaterielItem
        {
            Id = i.Id, Name = i.Name, Category = i.Category, Unit = i.Unit,
            Stock = i.Stock, AlertThreshold = i.Threshold, UpdatedAt = now
        }).ToList();

    public static List<DotationLine> DotationFor(string type)
        => Dotations.TryGetValue(type, out var lines)
            ? lines.Select(l => new DotationLine { ItemId = l.Id, Required = l.Qty, Present = l.Qty }).ToList()
            : [];

    public static List<FleetEngin> Engins(DateTimeOffset now)
    {
        var list = new List<FleetEngin>();
        var index = 0;
        foreach (var e in EnginsCatalog.All)
        {
            list.Add(new FleetEngin
            {
                Nom = e.Nom,
                Indicatif = e.Indicatif,
                Type = e.Type,
                TypeGdm = e.Type == "VLROGC" ? "SPVL" : string.Empty,
                Libelle = e.Libelle,
                Centre = e.Centre,
                Code = e.Code,
                Immatriculation = e.Immatriculation ?? string.Empty,
                Ordre = index * 10,
                NextRevision = now.Date.AddDays(20 + index * 11),
                Availability = FleetAvailability.Disponible,
                AvailabilitySince = now,
                Dotation = DotationFor(e.Type),
                CreatedAt = now,
                UpdatedAt = now,
                UpdatedBy = "ARGOS"
            });
            index++;
        }
        return list;
    }

    /// <summary>Photo par défaut selon le type (Assets/Engins/*.jpg de l'application), ou null.</summary>
    public static string? PhotoKey(string type) => type.ToUpperInvariant() switch
    {
        "VSAV" => "vsav",
        "PSE" or "PS" => "pse",
        "CD" => "cd",
        "CA" => "vsav",
        "VID" => "vid",
        "FA" => "fa",
        "FPT" or "FPTL" => "fptl",
        "EPA" or "EPAN" or "EPS" => "epa",
        "CRAC" => "crac",
        "AR" or "VLI" or "CMO" => "medical",
        _ => null
    };
}
