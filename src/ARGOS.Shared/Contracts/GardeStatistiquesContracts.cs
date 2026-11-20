namespace Argos.Shared.Contracts;

// =====================================================================
// SIGTAO : statistiques de présence aux gardes, d'après les feuilles de garde VALIDÉES.
// Un militaire compte une garde par feuille validée où il apparaît (même s'il tient
// plusieurs postes ou arme plusieurs engins ce jour-là).
// =====================================================================

/// <summary>Périodes proposées sur la page « Statistiques » (en jours ; 0 = depuis toujours).</summary>
public static class GardeStatistiquesPeriodes
{
    public const int Defaut = 30;
    public const int DepuisToujours = 0;
    public static readonly IReadOnlyList<int> All = [7, 30, 90, DepuisToujours];

    /// <summary>Période inconnue ou négative : période par défaut (30 jours).</summary>
    public static int Normalize(int jours) => All.Contains(jours) ? jours : Defaut;

    public static string Label(int jours) => jours switch
    {
        DepuisToujours => "Depuis toujours",
        _ => $"{jours} derniers jours"
    };
}

/// <summary>Présence d'un militaire (dossier en activité) sur la période.</summary>
public sealed class StatistiqueMilitaire
{
    /// <summary>Identifiant du dossier militaire.</summary>
    public string MilitaryId { get; set; } = string.Empty;
    public string Grade { get; set; } = string.Empty;
    public string Echelon { get; set; } = string.Empty;
    public string Nom { get; set; } = string.Empty;
    public string Prenom { get; set; } = string.Empty;
    /// <summary>Rang au classement (ex æquo = même rang) ; 0 = aucune garde sur la période.</summary>
    public int Rang { get; set; }
    /// <summary>Nombre de gardes validées sur la période.</summary>
    public int Gardes { get; set; }
    /// <summary>Dernière garde validée effectuée (« yyyy-MM-dd », vide = aucune), toutes périodes confondues.</summary>
    public string DerniereGarde { get; set; } = string.Empty;
    /// <summary>Poste le plus souvent tenu sur la période : « C/A VSAV », « Chef de garde incendie »… (vide = aucun).</summary>
    public string PosteFrequent { get; set; } = string.Empty;
    /// <summary>Nombre de fois où ce poste a été tenu sur la période.</summary>
    public int PosteFrequentFois { get; set; }
}

/// <summary>Statistiques de présence aux gardes sur une période (GET /api/sigtao/statistiques?jours=30).</summary>
public sealed class SigtaoStatistiques
{
    /// <summary>Durée de la période en jours (0 = depuis toujours).</summary>
    public int Jours { get; set; } = GardeStatistiquesPeriodes.Defaut;
    /// <summary>Premier jour pris en compte (« yyyy-MM-dd ») ; vide = depuis toujours.</summary>
    public string Du { get; set; } = string.Empty;
    /// <summary>Dernier jour pris en compte (« yyyy-MM-dd ») : aujourd'hui.</summary>
    public string Au { get; set; } = string.Empty;
    /// <summary>Feuilles de garde validées sur la période.</summary>
    public int GardesValidees { get; set; }
    /// <summary>Militaires différents présents au moins une fois sur la période.</summary>
    public int MilitairesPresents { get; set; }
    /// <summary>Moyenne de militaires présents par feuille validée (arrondie au dixième).</summary>
    public double MoyennePresents { get; set; }
    /// <summary>Dossiers en activité, du plus présent au moins présent ; ceux à 0 garde en dernier.</summary>
    public List<StatistiqueMilitaire> Classement { get; set; } = [];
}

/// <summary>Calcul des statistiques de présence (serveur, et repli local de l'application).</summary>
public static class GardeStatistiquesCalcul
{
    /// <param name="military">Dossiers militaires (seuls ceux en activité figurent au classement).</param>
    /// <param name="feuilles">Feuilles de garde (les feuilles non validées sont ignorées).</param>
    /// <param name="jours">7, 30, 90 ou 0 (depuis toujours).</param>
    /// <param name="today">Jour de référence (inclus) ; les gardes à venir ne comptent pas.</param>
    public static SigtaoStatistiques Compute(IEnumerable<MilitaryRecord> military, IEnumerable<FeuilleGarde> feuilles, int jours, DateTime today)
    {
        jours = GardeStatistiquesPeriodes.Normalize(jours);
        var au = today.Date.ToString("yyyy-MM-dd");
        var du = jours == GardeStatistiquesPeriodes.DepuisToujours ? string.Empty : today.Date.AddDays(1 - jours).ToString("yyyy-MM-dd");

        var validees = feuilles.Where(f => f.IsValidated && !string.IsNullOrWhiteSpace(f.Date)).ToList();
        var periode = validees
            .Where(f => string.CompareOrdinal(f.Date, au) <= 0 && (du.Length == 0 || string.CompareOrdinal(f.Date, du) >= 0))
            .ToList();

        var gardes = new Dictionary<string, int>();
        var postes = new Dictionary<string, Dictionary<string, int>>();
        var totalPresents = 0;
        foreach (var feuille in periode)
        {
            var affectations = feuille.Affectations().ToList();
            var presents = affectations.Select(a => a.Affectation.MilitaryId).Distinct().ToList();
            totalPresents += presents.Count;
            foreach (var id in presents)
                gardes[id] = gardes.GetValueOrDefault(id) + 1;
            foreach (var (affectation, engin) in affectations)
            {
                if (!postes.TryGetValue(affectation.MilitaryId, out var parPoste))
                    postes[affectation.MilitaryId] = parPoste = [];
                var poste = PosteLabel(affectation.Poste, engin);
                parPoste[poste] = parPoste.GetValueOrDefault(poste) + 1;
            }
        }

        // Dernière garde : la plus récente entre le dossier et les feuilles validées passées.
        var derniere = validees.Where(f => string.CompareOrdinal(f.Date, au) <= 0)
            .SelectMany(f => f.Affectations().Select(a => (a.Affectation.MilitaryId, f.Date)))
            .GroupBy(x => x.MilitaryId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Date).Max(StringComparer.Ordinal) ?? string.Empty);

        var classement = military.Where(m => m.Status == MilitaryStatuses.Actif)
            .Select(m =>
            {
                // Une garde validée à l'avance (date future) n'est pas encore « effectuée ».
                var date = string.CompareOrdinal(m.DerniereGarde ?? string.Empty, au) <= 0 ? m.DerniereGarde ?? string.Empty : string.Empty;
                if (derniere.TryGetValue(m.Id, out var d) && string.CompareOrdinal(d, date) > 0) date = d;
                var frequent = postes.TryGetValue(m.Id, out var parPoste)
                    ? parPoste.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).First()
                    : default;
                return new StatistiqueMilitaire
                {
                    MilitaryId = m.Id, Grade = m.Grade, Echelon = m.Echelon, Nom = m.Nom, Prenom = m.Prenom,
                    Gardes = gardes.GetValueOrDefault(m.Id), DerniereGarde = date,
                    PosteFrequent = frequent.Key ?? string.Empty, PosteFrequentFois = frequent.Value
                };
            })
            .OrderByDescending(s => s.Gardes)
            .ThenByDescending(s => s.DerniereGarde, StringComparer.Ordinal)
            .ThenByDescending(s => BsppGrades.Rank(s.Grade))
            .ThenBy(s => s.Nom, StringComparer.CurrentCulture)
            .ThenBy(s => s.Prenom, StringComparer.CurrentCulture)
            .ToList();

        // Classement « olympique » : 1, 2, 2, 4… ; pas de rang pour les militaires sans garde.
        for (var i = 0; i < classement.Count; i++)
        {
            var s = classement[i];
            s.Rang = s.Gardes == 0 ? 0 : i > 0 && classement[i - 1].Gardes == s.Gardes ? classement[i - 1].Rang : i + 1;
        }

        return new SigtaoStatistiques
        {
            Jours = jours,
            Du = du,
            Au = au,
            GardesValidees = periode.Count,
            MilitairesPresents = gardes.Count,
            MoyennePresents = periode.Count == 0 ? 0 : Math.Round((double)totalPresents / periode.Count, 1),
            Classement = classement
        };
    }

    /// <summary>« C/A VSAV » pour un poste d'engin (type = premier mot du nom de l'engin), sinon le poste seul.</summary>
    private static string PosteLabel(string poste, string? engin)
    {
        poste = string.IsNullOrWhiteSpace(poste) ? "Réserve" : poste.Trim();
        var type = (engin ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return type is null ? poste : $"{poste} {type}";
    }
}
