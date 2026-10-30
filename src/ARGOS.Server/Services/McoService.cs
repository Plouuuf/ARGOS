using System.Text.Json;
using Argos.Shared.Contracts;
using static Argos.Server.Services.OdeDatabase;

namespace Argos.Server.Services;

/// <summary>
/// Main courante opérationnelle : comptes-rendus d'intervention. Brouillon modifiable, MCO validée figée,
/// puis archivée. Chaque enregistrement est tracé dans l'historique de la MCO et dans le journal commun.
/// </summary>
public sealed class McoService(OdeDatabase db, RealtimeHub hub)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public McoSnapshot Snapshot() => new()
    {
        Reports = db.Docs<McoReport>(RemiseTables.Mco, 1000),
        ServerTime = DateTimeOffset.UtcNow
    };

    public McoReport? Get(string id) => db.GetDoc<McoReport>(RemiseTables.Mco, id);

    /// <summary>Crée (Id vide) ou enregistre un brouillon.</summary>
    public async Task<OdeResult> SaveAsync(ArgosUserDto user, McoReport incoming)
    {
        await _gate.WaitAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;
            Normalize(incoming);
            var existing = string.IsNullOrWhiteSpace(incoming.Id) ? null : Get(incoming.Id);
            if (!string.IsNullOrWhiteSpace(incoming.Id) && existing is null)
                return new(false, "MCO introuvable.");

            if (existing is null)
            {
                incoming.Id = Guid.NewGuid().ToString("N");
                incoming.Number = $"MCO-{now:yyyy}-{db.NextCounter("mco-" + now.ToString("yyyy")):0000}";
                incoming.Status = McoStatuses.Brouillon;
                incoming.AuthorUserId = user.UserId;
                incoming.AuthorName = user.DisplayName;
                incoming.CreatedAt = now;
                incoming.UpdatedAt = now;
                incoming.UpdatedBy = user.DisplayName;
                incoming.ValidatedAt = null;
                incoming.ValidatedBy = string.Empty;
                incoming.ArchivedAt = null;
                incoming.History = [History(user, "Création", $"Brouillon créé{(incoming.InterventionNumber.Length > 0 ? " pour l'intervention " + incoming.InterventionNumber : string.Empty)}.")];
                db.SaveDoc(RemiseTables.Mco, incoming.Id, now, incoming);
                Journal(user, "MCO créée", incoming.Number, $"Intervention {Or(incoming.InterventionNumber)} - {Or(incoming.Engin)}");
                await NotifyAsync();
                return new(true, null, incoming.Id);
            }

            if (!existing.IsEditable)
                return new(false, $"Cette MCO est {existing.Status.ToLowerInvariant()} : elle ne peut plus être modifiée.");

            var changes = Changes(existing, incoming);
            // Champs gérés par le serveur : jamais repris du poste.
            incoming.Number = existing.Number;
            incoming.Status = existing.Status;
            incoming.AuthorUserId = existing.AuthorUserId;
            incoming.AuthorName = existing.AuthorName;
            incoming.CreatedAt = existing.CreatedAt;
            incoming.ValidatedAt = null;
            incoming.ValidatedBy = string.Empty;
            incoming.ArchivedAt = null;
            incoming.History = existing.History;
            if (changes.Count == 0)
                return new(true, null, existing.Id);

            incoming.UpdatedAt = now;
            incoming.UpdatedBy = user.DisplayName;
            incoming.History.Add(History(user, "Modification", string.Join(", ", changes)));
            db.SaveDoc(RemiseTables.Mco, incoming.Id, now, incoming);
            Journal(user, "MCO modifiée", incoming.Number, string.Join(", ", changes));
            await NotifyAsync();
            return new(true, null, incoming.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OdeResult> ValidateAsync(ArgosUserDto user, string id)
    {
        await _gate.WaitAsync();
        try
        {
            var report = Get(id);
            if (report is null) return new(false, "MCO introuvable.");
            if (!report.IsEditable) return new(false, "Cette MCO est déjà validée.");
            var missing = McoPrefill.Missing(report);
            if (missing.Count > 0)
                return new(false, "Impossible de valider, à compléter : " + string.Join(", ", missing) + ".");
            var now = DateTimeOffset.UtcNow;
            report.Status = McoStatuses.Validee;
            report.ValidatedAt = now;
            report.ValidatedBy = user.DisplayName;
            report.UpdatedAt = now;
            report.UpdatedBy = user.DisplayName;
            report.History.Add(History(user, "Validation", "MCO validée définitivement."));
            db.SaveDoc(RemiseTables.Mco, report.Id, now, report);
            Journal(user, "MCO validée", report.Number, $"Intervention {report.InterventionNumber} - {report.Engin}");
            await NotifyAsync();
            return new(true, null, report.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OdeResult> ArchiveAsync(ArgosUserDto user, string id)
    {
        await _gate.WaitAsync();
        try
        {
            var report = Get(id);
            if (report is null) return new(false, "MCO introuvable.");
            if (report.Status != McoStatuses.Validee) return new(false, "Seule une MCO validée peut être archivée.");
            var now = DateTimeOffset.UtcNow;
            report.Status = McoStatuses.Archivee;
            report.ArchivedAt = now;
            report.UpdatedAt = now;
            report.UpdatedBy = user.DisplayName;
            report.History.Add(History(user, "Archivage", "MCO archivée."));
            db.SaveDoc(RemiseTables.Mco, report.Id, now, report);
            Journal(user, "MCO archivée", report.Number, report.InterventionNumber);
            await NotifyAsync();
            return new(true, null, report.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ===== Outils =====

    private static void Normalize(McoReport r)
    {
        r.ChefAgres ??= new McoPerson();
        r.ChefAgres.Fonction = "Chef d'agrès";
        r.Crew = (r.Crew ?? []).Where(c => !string.IsNullOrWhiteSpace(c.Nom) || !string.IsNullOrWhiteSpace(c.Prenom)).ToList();
        r.OtherEngins = (r.OtherEngins ?? []).Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).Distinct().ToList();
        r.EfibiIds = (r.EfibiIds ?? []).Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).Distinct().ToList();
        r.Events = (r.Events ?? []).Where(e => !string.IsNullOrWhiteSpace(e.Text)).OrderBy(e => e.Time).ToList();
        r.VictimCount = Math.Max(0, r.VictimCount);
    }

    /// <summary>Rubriques modifiées (comparaison JSON champ par champ).</summary>
    private static List<string> Changes(McoReport a, McoReport b)
    {
        var labels = new Dictionary<string, string>
        {
            ["interventionNumber"] = "intervention", ["date"] = "date", ["natureCode"] = "nature", ["address"] = "adresse",
            ["locality"] = "commune", ["building"] = "bâtiment", ["access"] = "accès", ["locationDetails"] = "précisions",
            ["timeDepart"] = "départ", ["timeArrivee"] = "arrivée", ["timeDebutOperations"] = "début opérations",
            ["timeFinOperations"] = "fin opérations", ["timeDisponible"] = "disponibilité", ["timeRetour"] = "retour",
            ["events"] = "chronologie", ["engin"] = "engin", ["chefAgres"] = "chef d'agrès", ["crew"] = "équipage",
            ["otherEngins"] = "autres engins", ["situationArrivee"] = "situation à l'arrivée", ["risques"] = "risques",
            ["reconnaissances"] = "reconnaissances", ["actions"] = "actions", ["moyens"] = "moyens",
            ["difficultes"] = "difficultés", ["renforts"] = "renforts", ["situationFinale"] = "situation finale",
            ["victimCount"] = "victimes", ["bilanGeneral"] = "bilan", ["actionsSecours"] = "actions de secours",
            ["transports"] = "transports", ["destinations"] = "destinations", ["observationsChef"] = "observations",
            ["efibiIds"] = "fiches eFiBi", ["synthese"] = "synthèse", ["interventionId"] = "ODE associé"
        };
        var ja = JsonSerializer.SerializeToElement(a, Json);
        var jb = JsonSerializer.SerializeToElement(b, Json);
        return labels.Where(l => ja.TryGetProperty(l.Key, out var x) && jb.TryGetProperty(l.Key, out var y)
                                 && x.GetRawText() != y.GetRawText())
            .Select(l => l.Value).ToList();
    }

    private static McoHistoryEntry History(ArgosUserDto user, string action, string details)
        => new() { At = DateTimeOffset.UtcNow, By = user.DisplayName, Action = action, Details = details };

    private void Journal(ArgosUserDto user, string action, string target, string details)
        => db.AddJournal(new JournalEntry
        {
            At = DateTimeOffset.UtcNow, UserId = user.UserId, UserName = user.DisplayName,
            Action = action, Target = target, Details = details
        });

    private Task NotifyAsync() => hub.BroadcastAsync(new RealtimeMessage(RealtimeTypes.McoChanged));

    private static string Or(string? v) => string.IsNullOrWhiteSpace(v) ? "—" : v;
}
