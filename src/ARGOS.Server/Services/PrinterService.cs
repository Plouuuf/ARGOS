using Argos.Shared.Contracts;
using static Argos.Server.Services.OdeDatabase;

namespace Argos.Server.Services;

/// <summary>
/// Imprimante virtuelle du centre : file partagée par tous les postes. Chaque document reçu est conservé
/// (data/print) et rangé dans « Mes documents › Impressions » de son auteur ; l'impression est simulée
/// (en attente → en cours → imprimé) et chaque étape est diffusée aux postes.
/// </summary>
public sealed class PrinterService(OdeDatabase db, RealtimeHub hub, FilesService files, ILogger<PrinterService> logger)
{
    public static string Dir { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "print");
    public static TimeSpan Step { get; set; } = TimeSpan.FromSeconds(3);

    public static bool CanManageAll(ArgosUserDto user) => AccessLevels.IsRhAdmin(user.Levels) || user.Levels.Contains(AccessLevels.Encadrement);

    public PrinterSnapshot Snapshot(ArgosUserDto user) => new()
    {
        Jobs = db.Docs<PrintJob>(RemiseTables.PrintJobs).OrderByDescending(j => j.CreatedAt).Take(300).ToList(),
        CanManageAll = CanManageAll(user)
    };

    public string? ImagePath(string id)
    {
        var path = Path.Combine(Dir, id + ".png");
        return db.GetDoc<PrintJob>(RemiseTables.PrintJobs, id) is { HasImage: true } && File.Exists(path) ? path : null;
    }

    /// <summary>Reçoit un document (image PNG de la feuille) et lance l'impression simulée.</summary>
    public async Task<PrintJob> SubmitAsync(ArgosUserDto user, PrintJob job, byte[]? png)
    {
        job.Id = Guid.NewGuid().ToString("N");
        job.OwnerId = user.UserId;
        job.OwnerName = user.DisplayName;
        job.Status = PrintStatuses.EnAttente;
        job.CreatedAt = DateTimeOffset.UtcNow;
        if (png is { Length: > 0 })
        {
            Directory.CreateDirectory(Dir);
            await File.WriteAllBytesAsync(Path.Combine(Dir, job.Id + ".png"), png);
            job.HasImage = true;
            job.Size = png.Length;
            var name = string.Concat($"{job.Title} - {job.CreatedAt.ToLocalTime():yyyy-MM-dd HH-mm}.png".Split(Path.GetInvalidFileNameChars()));
            job.FileId = await files.SaveCopyAsync(user, "Impressions", name, png) ?? string.Empty;
        }
        Save(job);
        await NotifyAsync();
        _ = SimulateAsync(job.Id);
        return job;
    }

    /// <summary>Réimprime un document archivé (nouvelle tâche, même feuille).</summary>
    public async Task<OdeResult> ReprintAsync(ArgosUserDto user, string id)
    {
        var original = db.GetDoc<PrintJob>(RemiseTables.PrintJobs, id);
        if (original is null) return new(false, "Document introuvable.");
        var path = ImagePath(id);
        var copy = new PrintJob
        {
            Kind = original.Kind, Title = original.Title, InterventionId = original.InterventionId,
            InterventionNumber = original.InterventionNumber, OrderNumber = original.OrderNumber, Reprint = true
        };
        var job = await SubmitAsync(user, copy, path is null ? null : await File.ReadAllBytesAsync(path));
        return new(true, null, job.Id);
    }

    /// <summary>Supprime un document de la file : son auteur, ou l'encadrement.</summary>
    public async Task<OdeResult> DeleteAsync(ArgosUserDto user, string id)
    {
        var job = db.GetDoc<PrintJob>(RemiseTables.PrintJobs, id);
        if (job is null) return new(false, "Document introuvable.");
        if (job.OwnerId != user.UserId && !CanManageAll(user)) return new(false, "Seul l'auteur (ou l'encadrement) peut supprimer ce document.");
        db.DeleteDoc(RemiseTables.PrintJobs, id);
        var path = Path.Combine(Dir, id + ".png");
        if (File.Exists(path)) File.Delete(path);
        await NotifyAsync();
        return new(true);
    }

    private async Task SimulateAsync(string id)
    {
        try
        {
            await Task.Delay(Step);
            if (Update(id, PrintStatuses.EnCours) is null) return;
            await NotifyAsync();
            await Task.Delay(Step);
            if (Update(id, PrintStatuses.Imprime) is not { } done) return;
            await NotifyAsync();
            await hub.SendToUserAsync(done.OwnerId, new RealtimeMessage(RealtimeTypes.Notify, Title: "Imprimante du centre",
                Text: $"« {done.Title} » imprimé.", AppId: ArgosApps.Imprimante));
        }
        catch (Exception ex)
        {
            logger.LogWarning("Impression simulée interrompue : {Message}", ex.Message);
        }
    }

    private PrintJob? Update(string id, string status)
    {
        var job = db.GetDoc<PrintJob>(RemiseTables.PrintJobs, id);
        if (job is null) return null;          // supprimé entre-temps
        job.Status = status;
        if (status == PrintStatuses.Imprime) job.PrintedAt = DateTimeOffset.UtcNow;
        Save(job);
        return job;
    }

    private void Save(PrintJob job) => db.SaveDoc(RemiseTables.PrintJobs, job.Id, job.CreatedAt, job);

    private Task NotifyAsync() => hub.BroadcastAsync(new RealtimeMessage(RealtimeTypes.PrinterChanged));
}
