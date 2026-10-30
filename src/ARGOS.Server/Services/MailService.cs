using Argos.Shared.Contracts;
using static Argos.Server.Services.OdeDatabase;

namespace Argos.Server.Services;

/// <summary>
/// Messagerie interne : chaque destinataire reçoit son propre exemplaire (mail_items), les pièces jointes
/// sont stockées une fois (data/mail) et ne sont servies qu'aux utilisateurs dont un message les référence.
/// Personne (pas même l'administration) ne lit la boîte d'un autre.
/// </summary>
public sealed class MailService(OdeDatabase db, RealtimeHub hub)
{
    public const long MaxAttachmentBytes = 10 * 1024 * 1024;
    public const long MaxMessageBytes = 20 * 1024 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);
    internal static string FilesDir { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "mail");

    // ===== Adresses et annuaire =====

    /// <summary>Adresse de l'utilisateur : celle de son dossier SIGTAO, sinon une adresse attribuée une fois pour toutes.</summary>
    public MailProfile Profile(ArgosUserDto user)
    {
        var existing = db.GetDoc<MailProfile>(RemiseTables.MailProfiles, user.UserId);
        var record = db.Docs<MilitaryRecord>(RemiseTables.Military).FirstOrDefault(r => r.DiscordUserId == user.UserId);
        var address = record is null ? string.Empty : RecordAddress(record);
        var name = record is null || string.IsNullOrWhiteSpace(record.Nom) ? user.DisplayName : record.FullName;
        if (address.Length == 0)
            address = existing?.Address is { Length: > 0 } a ? a : FreeAddress(MailAddresses.Build(null, user.DisplayName), user.UserId);

        var profile = existing ?? new MailProfile { UserId = user.UserId };
        if (profile.Address != address || profile.Name != name || existing is null)
        {
            profile.Address = address;
            profile.Name = name;
            db.SaveDoc(RemiseTables.MailProfiles, user.UserId, DateTimeOffset.UtcNow, profile);
        }
        return profile;
    }

    private static string RecordAddress(MilitaryRecord r)
        => MailAddresses.IsValid(r.Email) ? MailAddresses.Normalize(r.Email) : MailAddresses.Build(r.Prenom, r.Nom);

    private string FreeAddress(string wanted, string userId)
    {
        if (wanted.Length == 0) wanted = "agent@" + MailAddresses.Domain;
        var taken = Resolver().Where(kv => kv.Value != userId).Select(kv => kv.Key).ToHashSet();
        if (!taken.Contains(wanted)) return wanted;
        var at = wanted.IndexOf('@');
        for (var i = 2; ; i++)
        {
            var candidate = wanted[..at] + i + wanted[at..];
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    /// <summary>Adresse → identifiant Discord (dossiers reliés à un joueur d'abord, puis adresses attribuées).</summary>
    public Dictionary<string, string> Resolver()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in db.Docs<MilitaryRecord>(RemiseTables.Military))
            if (!string.IsNullOrWhiteSpace(r.DiscordUserId) && RecordAddress(r) is { Length: > 0 } a)
                map.TryAdd(a, r.DiscordUserId);
        foreach (var p in db.Docs<MailProfile>(RemiseTables.MailProfiles))
            if (p.Address.Length > 0)
                map.TryAdd(p.Address, p.UserId);
        return map;
    }

    public List<MailDirectoryEntry> AddressBook()
    {
        var entries = new Dictionary<string, MailDirectoryEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in db.Docs<MilitaryRecord>(RemiseTables.Military))
        {
            if (string.IsNullOrWhiteSpace(r.DiscordUserId) || r.Status is MilitaryStatuses.Parti or MilitaryStatuses.Archive) continue;
            var address = RecordAddress(r);
            if (address.Length == 0) continue;
            entries.TryAdd(address, new MailDirectoryEntry
            {
                Address = address, Name = string.IsNullOrWhiteSpace(r.Nom) ? address : r.FullName,
                Grade = r.GradeLabel, Fonction = r.Fonction, Unite = r.Unite
            });
        }
        foreach (var p in db.Docs<MailProfile>(RemiseTables.MailProfiles))
            if (p.Address.Length > 0)
                entries.TryAdd(p.Address, new MailDirectoryEntry { Address = p.Address, Name = p.Name });
        return entries.Values.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    // ===== Lecture =====

    public MailSnapshot Snapshot(ArgosUserDto user)
    {
        var profile = Profile(user);
        return new MailSnapshot
        {
            Address = profile.Address,
            Name = profile.Name,
            Signature = profile.Signature,
            Items = Items(user.UserId).OrderByDescending(i => i.SentAt ?? i.UpdatedAt).ToList(),
            Contacts = db.Docs<MailContact>(RemiseTables.MailContacts).Where(c => c.OwnerId == user.UserId)
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            Directory = AddressBook(),
            ServerTime = DateTimeOffset.UtcNow
        };
    }

    private List<MailItem> Items(string userId) => db.Docs<MailItem>(RemiseTables.MailItems).Where(i => i.OwnerId == userId).ToList();

    private MailItem? Own(ArgosUserDto user, string id)
        => db.GetDoc<MailItem>(RemiseTables.MailItems, id) is { } item && item.OwnerId == user.UserId ? item : null;

    /// <summary>Fichier d'une pièce jointe, si l'utilisateur possède un message qui la contient.</summary>
    public (MailAttachment? Attachment, string? Path) OpenAttachment(ArgosUserDto user, string attachmentId)
    {
        var attachment = Items(user.UserId).SelectMany(i => i.Attachments).FirstOrDefault(a => a.Id == attachmentId);
        if (attachment is null) return (null, null);
        var path = Path.Combine(FilesDir, attachment.Id);
        return File.Exists(path) ? (attachment, path) : (null, null);
    }

    // ===== Brouillons et envoi =====

    public async Task<MailDraftResult> SaveDraftAsync(ArgosUserDto user, MailDraftRequest request)
    {
        await _gate.WaitAsync();
        try
        {
            var profile = Profile(user);
            var draft = request.DraftId is { Length: > 0 } id ? Own(user, id) : null;
            if (draft is not null && draft.Folder != MailFolders.Brouillons) draft = null;
            draft ??= new MailItem { Id = NewId(), OwnerId = user.UserId, Folder = MailFolders.Brouillons, Read = true };

            var (attachments, error) = Attachments(user, draft, request);
            if (error is not null) return new MailDraftResult { Error = error };

            Fill(draft, profile, request, attachments);
            draft.UpdatedAt = DateTimeOffset.UtcNow;
            db.SaveDoc(RemiseTables.MailItems, draft.Id, draft.UpdatedAt, draft);
            await Signal(user.UserId);
            return new MailDraftResult { Ok = true, Item = draft };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MailDraftResult> SendAsync(ArgosUserDto user, MailDraftRequest request)
    {
        List<(string UserId, MailItem Copy)> deliveries;
        MailItem sent;
        await _gate.WaitAsync();
        try
        {
            var profile = Profile(user);
            var to = MailAddresses.Split(request.To);
            var cc = MailAddresses.Split(request.Cc);
            var bcc = MailAddresses.Split(request.Bcc);
            var all = to.Concat(cc).Concat(bcc).Distinct().ToList();
            if (all.Count == 0)
                return new MailDraftResult { Error = "Indiquez au moins un destinataire." };
            if (all.Count > 100)
                return new MailDraftResult { Error = "100 destinataires au maximum." };
            var invalid = all.Where(a => !MailAddresses.IsValid(a)).ToList();
            if (invalid.Count > 0)
                return new MailDraftResult { Error = "Adresse invalide : " + string.Join(", ", invalid) };
            var resolver = Resolver();
            var unknown = all.Where(a => !resolver.ContainsKey(a)).ToList();
            if (unknown.Count > 0)
                return new MailDraftResult { Error = "Destinataire introuvable dans l'annuaire : " + string.Join(", ", unknown) };

            var draft = request.DraftId is { Length: > 0 } id ? Own(user, id) : null;
            if (draft is not null && draft.Folder != MailFolders.Brouillons) draft = null;
            var (attachments, error) = Attachments(user, draft, request);
            if (error is not null) return new MailDraftResult { Error = error };

            var now = DateTimeOffset.UtcNow;
            sent = draft ?? new MailItem { Id = NewId(), OwnerId = user.UserId };
            Fill(sent, profile, request, attachments);
            sent.To = to;
            sent.Cc = cc;
            sent.Bcc = bcc;
            sent.Folder = MailFolders.Envoyes;
            sent.Read = true;
            sent.MessageId = NewId();
            sent.SentAt = now;
            sent.UpdatedAt = now;
            db.SaveDoc(RemiseTables.MailItems, sent.Id, now, sent);

            deliveries = [];
            foreach (var recipient in all.Select(a => resolver[a]).Distinct())
            {
                var copy = new MailItem
                {
                    Id = NewId(), OwnerId = recipient, Folder = MailFolders.Reception, MessageId = sent.MessageId,
                    From = sent.From, FromName = sent.FromName, To = to, Cc = cc, Bcc = [],
                    Subject = sent.Subject, Body = sent.Body, Importance = sent.Importance,
                    Attachments = sent.Attachments.Select(a => new MailAttachment { Id = a.Id, Name = a.Name, ContentType = a.ContentType, Size = a.Size }).ToList(),
                    InReplyTo = sent.InReplyTo, SentAt = now, UpdatedAt = now
                };
                db.SaveDoc(RemiseTables.MailItems, copy.Id, now, copy);
                deliveries.Add((recipient, copy));
            }

            db.AddJournal(new JournalEntry
            {
                At = now, UserId = user.UserId, UserName = user.DisplayName, Action = "mail-envoi",
                Target = sent.MessageId, Details = $"{deliveries.Count} destinataire(s), {sent.Attachments.Count} pièce(s) jointe(s)"
            });
        }
        finally
        {
            _gate.Release();
        }

        await Signal(user.UserId);
        foreach (var (recipient, copy) in deliveries)
        {
            await hub.SendToUserAsync(recipient, new RealtimeMessage(RealtimeTypes.MailChanged));
            await hub.SendToUserAsync(recipient, new RealtimeMessage(RealtimeTypes.Notify,
                Title: "Nouveau message" + (copy.Importance == MailImportance.Haute ? " (importance haute)" : string.Empty),
                Text: $"{copy.FromName} : {(string.IsNullOrWhiteSpace(copy.Subject) ? "(sans objet)" : copy.Subject)}",
                AppId: ArgosApps.Mail));
        }
        return new MailDraftResult { Ok = true, Item = sent };
    }

    private static void Fill(MailItem item, MailProfile profile, MailDraftRequest request, List<MailAttachment> attachments)
    {
        item.From = profile.Address;
        item.FromName = profile.Name;
        item.To = MailAddresses.Split(request.To);
        item.Cc = MailAddresses.Split(request.Cc);
        item.Bcc = MailAddresses.Split(request.Bcc);
        item.Subject = (request.Subject ?? string.Empty).Trim();
        if (item.Subject.Length > 250) item.Subject = item.Subject[..250];
        item.Body = request.Body ?? string.Empty;
        if (item.Body.Length > 200_000) item.Body = item.Body[..200_000];
        item.Importance = MailImportance.All.Contains(request.Importance) ? request.Importance : MailImportance.Normale;
        item.InReplyTo = request.InReplyTo ?? string.Empty;
        item.Attachments = attachments;
    }

    /// <summary>Pièces jointes conservées (déjà accessibles à l'utilisateur) et nouvelles (écrites sur disque).</summary>
    private (List<MailAttachment> List, string? Error) Attachments(ArgosUserDto user, MailItem? draft, MailDraftRequest request)
    {
        var accessible = (draft?.Attachments ?? []).Concat(Items(user.UserId).SelectMany(i => i.Attachments))
            .GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First());
        var list = new List<MailAttachment>();
        foreach (var id in request.KeepAttachments.Distinct())
            if (accessible.TryGetValue(id, out var a))
                list.Add(new MailAttachment { Id = a.Id, Name = a.Name, ContentType = a.ContentType, Size = a.Size });

        var decoded = new List<(MailAttachment Meta, byte[] Data)>();
        foreach (var n in request.NewAttachments)
        {
            byte[] data;
            try { data = Convert.FromBase64String(n.Data); }
            catch (FormatException) { return ([], $"Pièce jointe illisible : {n.Name}"); }
            if (data.Length > MaxAttachmentBytes)
                return ([], $"« {n.Name} » dépasse 10 Mo.");
            var name = Path.GetFileName(n.Name);
            decoded.Add((new MailAttachment
            {
                Id = NewId(), Name = string.IsNullOrWhiteSpace(name) ? "piece-jointe" : name,
                ContentType = string.IsNullOrWhiteSpace(n.ContentType) ? "application/octet-stream" : n.ContentType, Size = data.Length
            }, data));
        }
        if (list.Sum(a => a.Size) + decoded.Sum(d => d.Meta.Size) > MaxMessageBytes)
            return ([], "Les pièces jointes dépassent 20 Mo au total.");

        System.IO.Directory.CreateDirectory(FilesDir);
        foreach (var (meta, data) in decoded)
        {
            File.WriteAllBytes(Path.Combine(FilesDir, meta.Id), data);
            list.Add(meta);
        }
        return (list, null);
    }

    // ===== Classement =====

    public async Task<OdeResult> UpdateAsync(ArgosUserDto user, string id, MailFlagRequest request)
    {
        var item = Own(user, id);
        if (item is null) return new OdeResult(false, "Message introuvable.");
        if (request.Read is { } read) item.Read = read;
        if (request.Flagged is { } flagged) item.Flagged = flagged;
        if (request.Folder is { Length: > 0 } folder && folder != item.Folder)
        {
            if (!MailFolders.All.Contains(folder)) return new OdeResult(false, "Dossier inconnu.");
            if (folder == MailFolders.Corbeille)
            {
                item.PreviousFolder = item.Folder;
                item.Folder = folder;
            }
            else
            {
                // Restauration : un message ne revient que dans son dossier d'origine.
                item.Folder = item.Folder == MailFolders.Corbeille && item.PreviousFolder.Length > 0 ? item.PreviousFolder : item.Folder;
                item.PreviousFolder = string.Empty;
            }
        }
        db.SaveDoc(RemiseTables.MailItems, item.Id, DateTimeOffset.UtcNow, item);
        await Signal(user.UserId);
        return new OdeResult(true, null);
    }

    /// <summary>Suppression définitive (depuis la corbeille, ou d'un brouillon).</summary>
    public async Task<OdeResult> DeleteAsync(ArgosUserDto user, string id)
    {
        var item = Own(user, id);
        if (item is null) return new OdeResult(false, "Message introuvable.");
        if (item.Folder is not (MailFolders.Corbeille or MailFolders.Brouillons))
            return new OdeResult(false, "Placez d'abord le message dans les éléments supprimés.");
        await RemoveAsync([item]);
        await Signal(user.UserId);
        return new OdeResult(true, null);
    }

    public async Task<OdeResult> EmptyTrashAsync(ArgosUserDto user)
    {
        await RemoveAsync(Items(user.UserId).Where(i => i.Folder == MailFolders.Corbeille).ToList());
        await Signal(user.UserId);
        return new OdeResult(true, null);
    }

    private async Task RemoveAsync(List<MailItem> items)
    {
        await _gate.WaitAsync();
        try
        {
            foreach (var item in items) db.DeleteDoc(RemiseTables.MailItems, item.Id);
            var orphans = items.SelectMany(i => i.Attachments).Select(a => a.Id).ToHashSet();
            if (orphans.Count == 0) return;
            foreach (var remaining in db.Docs<MailItem>(RemiseTables.MailItems))
                foreach (var a in remaining.Attachments) orphans.Remove(a.Id);
            foreach (var orphan in orphans)
            {
                var path = Path.Combine(FilesDir, orphan);
                if (File.Exists(path)) File.Delete(path);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // ===== Carnet d'adresses et signature =====

    public async Task<OdeResult> SaveContactAsync(ArgosUserDto user, MailContact contact)
    {
        var address = MailAddresses.Normalize(contact.Address);
        if (!MailAddresses.IsValid(address)) return new OdeResult(false, "Adresse électronique invalide.");
        if (string.IsNullOrWhiteSpace(contact.Name)) return new OdeResult(false, "Indiquez un nom.");
        var existing = contact.Id.Length > 0 ? db.GetDoc<MailContact>(RemiseTables.MailContacts, contact.Id) : null;
        if (existing is not null && existing.OwnerId != user.UserId) return new OdeResult(false, "Contact introuvable.");
        contact.Id = existing?.Id ?? NewId();
        contact.OwnerId = user.UserId;
        contact.Address = address;
        contact.Name = contact.Name.Trim();
        db.SaveDoc(RemiseTables.MailContacts, contact.Id, DateTimeOffset.UtcNow, contact);
        await Signal(user.UserId);
        return new OdeResult(true, null);
    }

    public async Task<OdeResult> DeleteContactAsync(ArgosUserDto user, string id)
    {
        var existing = db.GetDoc<MailContact>(RemiseTables.MailContacts, id);
        if (existing is null || existing.OwnerId != user.UserId) return new OdeResult(false, "Contact introuvable.");
        db.DeleteDoc(RemiseTables.MailContacts, id);
        await Signal(user.UserId);
        return new OdeResult(true, null);
    }

    public async Task<OdeResult> SaveSignatureAsync(ArgosUserDto user, MailProfileRequest request)
    {
        var profile = Profile(user);
        profile.Signature = (request.Signature ?? string.Empty).Trim();
        if (profile.Signature.Length > 2000) profile.Signature = profile.Signature[..2000];
        db.SaveDoc(RemiseTables.MailProfiles, user.UserId, DateTimeOffset.UtcNow, profile);
        await Signal(user.UserId);
        return new OdeResult(true, null);
    }

    private Task Signal(string userId) => hub.SendToUserAsync(userId, new RealtimeMessage(RealtimeTypes.MailChanged));

    private static string NewId() => Guid.NewGuid().ToString("N");
}
