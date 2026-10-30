using Argos.Shared.Contracts;
using static Argos.Server.Services.OdeDatabase;

namespace Argos.Server.Services;

/// <summary>
/// « Mes documents » : espace de fichiers personnel (Explorateur, Bloc-notes, Corbeille).
/// Métadonnées dans user_files, contenus dans data/files. Chacun ne voit que ses propres fichiers.
/// </summary>
public sealed class FilesService(OdeDatabase db, RealtimeHub hub)
{
    public const long QuotaBytes = 200L * 1024 * 1024;
    public const long MaxFileBytes = 25L * 1024 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);
    internal static string FilesDir { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "files");

    private List<FileEntry> All(string userId) => db.Docs<FileEntry>(RemiseTables.Files).Where(e => e.OwnerId == userId).ToList();

    private FileEntry? Own(ArgosUserDto user, string? id)
        => string.IsNullOrEmpty(id) ? null : db.GetDoc<FileEntry>(RemiseTables.Files, id) is { } e && e.OwnerId == user.UserId ? e : null;

    public FilesSnapshot Snapshot(ArgosUserDto user)
    {
        var entries = All(user.UserId);
        return new FilesSnapshot { Entries = entries, Used = entries.Sum(e => e.Size), Quota = QuotaBytes };
    }

    public (FileEntry? Entry, string? Path) Open(ArgosUserDto user, string id)
    {
        var entry = Own(user, id);
        if (entry is null || entry.IsFolder) return (null, null);
        var path = Path.Combine(FilesDir, entry.Id);
        return File.Exists(path) ? (entry, path) : (entry, null);
    }

    // ===== Création =====

    public async Task<OdeResult> CreateFolderAsync(ArgosUserDto user, FileFolderRequest request)
        => await Locked(user, () =>
        {
            if (FileKinds.CleanName(request.Name) is not { } name) return new OdeResult(false, "Nom de dossier invalide.");
            if (!ValidParent(user, request.ParentId)) return new OdeResult(false, "Dossier de destination introuvable.");
            var now = DateTimeOffset.UtcNow;
            var entry = new FileEntry
            {
                Id = NewId(), OwnerId = user.UserId, ParentId = request.ParentId, IsFolder = true,
                Name = FreeName(user.UserId, request.ParentId, name, null), CreatedAt = now, UpdatedAt = now
            };
            db.SaveDoc(RemiseTables.Files, entry.Id, now, entry);
            return new OdeResult(true, null, entry.Id);
        });

    public async Task<OdeResult> UploadAsync(ArgosUserDto user, FileUploadRequest request)
        => await Locked(user, () =>
        {
            byte[] data;
            try { data = Convert.FromBase64String(request.Data ?? string.Empty); }
            catch (FormatException) { return new OdeResult(false, "Fichier illisible."); }
            if (data.Length > MaxFileBytes) return new OdeResult(false, "Fichier trop lourd (25 Mo maximum).");

            var now = DateTimeOffset.UtcNow;
            var existing = Own(user, request.ReplaceId);
            if (existing is { IsFolder: true }) return new OdeResult(false, "Impossible d'écrire dans un dossier.");
            var used = All(user.UserId).Sum(e => e.Size) - (existing?.Size ?? 0);
            if (used + data.Length > QuotaBytes) return new OdeResult(false, "Espace « Mes documents » plein (200 Mo).");

            FileEntry entry;
            if (existing is not null)
            {
                entry = existing;
                if (FileKinds.CleanName(request.Name) is { } rename && rename != entry.Name)
                    entry.Name = FreeName(user.UserId, entry.ParentId, rename, entry.Id);
            }
            else
            {
                if (FileKinds.CleanName(request.Name) is not { } name) return new OdeResult(false, "Nom de fichier invalide.");
                if (!ValidParent(user, request.ParentId)) return new OdeResult(false, "Dossier de destination introuvable.");
                entry = new FileEntry
                {
                    Id = NewId(), OwnerId = user.UserId, ParentId = request.ParentId,
                    Name = FreeName(user.UserId, request.ParentId, name, null), CreatedAt = now
                };
            }
            Directory.CreateDirectory(FilesDir);
            File.WriteAllBytes(Path.Combine(FilesDir, entry.Id), data);
            entry.Size = data.Length;
            entry.ContentType = string.IsNullOrWhiteSpace(request.ContentType) ? FileKinds.ContentType(entry.Name) : request.ContentType;
            entry.UpdatedAt = now;
            db.SaveDoc(RemiseTables.Files, entry.Id, now, entry);
            return new OdeResult(true, null, entry.Id);
        });

    /// <summary>Range une copie dans un dossier de premier niveau (« Impressions »…), créé au besoin. Renvoie l'identifiant du fichier.</summary>
    public async Task<string?> SaveCopyAsync(ArgosUserDto user, string folderName, string fileName, byte[] data)
    {
        var folder = All(user.UserId).FirstOrDefault(e => e.IsFolder && !e.Deleted && e.ParentId.Length == 0
                                                          && string.Equals(e.Name, folderName, StringComparison.OrdinalIgnoreCase));
        var parent = folder?.Id;
        if (parent is null)
        {
            var created = await CreateFolderAsync(user, new FileFolderRequest { Name = folderName });
            if (!created.Ok) return null;
            parent = created.Id;
        }
        var result = await UploadAsync(user, new FileUploadRequest
        {
            ParentId = parent!, Name = fileName, ContentType = FileKinds.ContentType(fileName), Data = Convert.ToBase64String(data)
        });
        return result.Ok ? result.Id : null;
    }

    // ===== Organisation =====

    public async Task<OdeResult> RenameAsync(ArgosUserDto user, string id, FileRenameRequest request)
        => await Locked(user, () =>
        {
            var entry = Own(user, id);
            if (entry is null || entry.Deleted) return new OdeResult(false, "Élément introuvable.");
            if (FileKinds.CleanName(request.Name) is not { } name) return new OdeResult(false, "Nom invalide.");
            entry.Name = FreeName(user.UserId, entry.ParentId, name, entry.Id);
            entry.UpdatedAt = DateTimeOffset.UtcNow;
            db.SaveDoc(RemiseTables.Files, entry.Id, entry.UpdatedAt, entry);
            return new OdeResult(true, null, entry.Id);
        });

    public async Task<OdeResult> MoveAsync(ArgosUserDto user, string id, FileMoveRequest request)
        => await Locked(user, () =>
        {
            var entry = Own(user, id);
            if (entry is null || entry.Deleted) return new OdeResult(false, "Élément introuvable.");
            if (!ValidParent(user, request.ParentId)) return new OdeResult(false, "Dossier de destination introuvable.");
            // Un dossier ne peut pas aller dans lui-même ni dans l'un de ses sous-dossiers.
            for (var p = request.ParentId; !string.IsNullOrEmpty(p); p = Own(user, p)?.ParentId)
                if (p == entry.Id) return new OdeResult(false, "Un dossier ne peut pas être déplacé dans lui-même.");
            entry.ParentId = request.ParentId;
            entry.Name = FreeName(user.UserId, request.ParentId, entry.Name, entry.Id);
            entry.UpdatedAt = DateTimeOffset.UtcNow;
            db.SaveDoc(RemiseTables.Files, entry.Id, entry.UpdatedAt, entry);
            return new OdeResult(true, null, entry.Id);
        });

    // ===== Corbeille =====

    public async Task<OdeResult> DeleteAsync(ArgosUserDto user, string id)
        => await Locked(user, () =>
        {
            var entry = Own(user, id);
            if (entry is null || entry.Deleted) return new OdeResult(false, "Élément introuvable.");
            var now = DateTimeOffset.UtcNow;
            entry.Deleted = true;
            entry.DeletedWithParent = false;
            entry.DeletedAt = now;
            db.SaveDoc(RemiseTables.Files, entry.Id, now, entry);
            foreach (var child in Descendants(user.UserId, entry.Id).Where(c => !c.Deleted))
            {
                child.Deleted = true;
                child.DeletedWithParent = true;
                child.DeletedAt = now;
                db.SaveDoc(RemiseTables.Files, child.Id, now, child);
            }
            return new OdeResult(true, null, entry.Id);
        });

    public async Task<OdeResult> RestoreAsync(ArgosUserDto user, string id)
        => await Locked(user, () =>
        {
            var entry = Own(user, id);
            if (entry is not { Deleted: true, DeletedWithParent: false }) return new OdeResult(false, "Élément introuvable dans la corbeille.");
            // Si le dossier d'origine n'existe plus (ou est à la corbeille), retour à la racine.
            var parent = Own(user, entry.ParentId);
            if (entry.ParentId.Length > 0 && (parent is null || parent.Deleted)) entry.ParentId = string.Empty;
            entry.Name = FreeName(user.UserId, entry.ParentId, entry.Name, entry.Id);
            entry.Deleted = false;
            entry.DeletedAt = null;
            db.SaveDoc(RemiseTables.Files, entry.Id, DateTimeOffset.UtcNow, entry);
            foreach (var child in Descendants(user.UserId, entry.Id).Where(c => c.DeletedWithParent))
            {
                child.Deleted = false;
                child.DeletedWithParent = false;
                child.DeletedAt = null;
                db.SaveDoc(RemiseTables.Files, child.Id, DateTimeOffset.UtcNow, child);
            }
            return new OdeResult(true, null, entry.Id);
        });

    public async Task<OdeResult> PurgeAsync(ArgosUserDto user, string id)
        => await Locked(user, () =>
        {
            var entry = Own(user, id);
            if (entry is not { Deleted: true }) return new OdeResult(false, "Seuls les éléments de la corbeille peuvent être supprimés définitivement.");
            Remove(user.UserId, entry);
            return new OdeResult(true);
        });

    public async Task<OdeResult> EmptyTrashAsync(ArgosUserDto user)
        => await Locked(user, () =>
        {
            foreach (var entry in All(user.UserId).Where(e => e.Deleted && !e.DeletedWithParent))
                Remove(user.UserId, entry);
            return new OdeResult(true);
        });

    private void Remove(string userId, FileEntry entry)
    {
        foreach (var e in Descendants(userId, entry.Id).Append(entry))
        {
            db.DeleteDoc(RemiseTables.Files, e.Id);
            var path = Path.Combine(FilesDir, e.Id);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ===== Outils =====

    private List<FileEntry> Descendants(string userId, string folderId)
    {
        var all = All(userId);
        var result = new List<FileEntry>();
        var queue = new Queue<string>([folderId]);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            foreach (var child in all.Where(e => e.ParentId == id))
            {
                result.Add(child);
                if (child.IsFolder) queue.Enqueue(child.Id);
            }
        }
        return result;
    }

    private bool ValidParent(ArgosUserDto user, string? parentId)
        => string.IsNullOrEmpty(parentId) || Own(user, parentId) is { IsFolder: true, Deleted: false };

    /// <summary>« Nom », sinon « Nom (2) », « Nom (3) »… comme l'Explorateur Windows.</summary>
    private string FreeName(string userId, string parentId, string name, string? self)
    {
        var taken = All(userId).Where(e => e.ParentId == parentId && !e.Deleted && e.Id != self)
            .Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name)) return name;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var i = 2; ; i++)
        {
            var candidate = $"{stem} ({i}){ext}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    private async Task<OdeResult> Locked(ArgosUserDto user, Func<OdeResult> action)
    {
        OdeResult result;
        await _gate.WaitAsync();
        try { result = action(); }
        finally { _gate.Release(); }
        if (result.Ok) await hub.SendToUserAsync(user.UserId, new RealtimeMessage(RealtimeTypes.FilesChanged));
        return result;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}
