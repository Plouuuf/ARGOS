using System.Collections.ObjectModel;
using System.Globalization;
using Argos.Services;
using Argos.Shared.Contracts;
using Argos.Views.Apps.Mail;
using Avalonia.Platform.Storage;

namespace Argos.ViewModels.Mail;

public sealed record FolderRow(string Id, string Label, int Unread, bool Selected, RelayCommand SelectCommand)
{
    public bool HasUnread => Unread > 0;
}

public sealed record MessageRow(string Id, string Correspondent, string Subject, string Preview, string Date, bool Unread,
    bool Flagged, bool High, bool HasAttachments, bool Selected, RelayCommand SelectCommand)
{
    public bool IsDraft { get; init; }
}

public sealed record AttachmentRow(string Id, string Name, string Size, RelayCommand OpenCommand, RelayCommand SaveCommand);

public sealed record ContactRow(string Id, string Name, string Address, string Details, bool Personal, RelayCommand WriteCommand, RelayCommand EditCommand);

/// <summary>Messagerie : dossiers, liste des messages, volet de lecture, contacts et signature.</summary>
public sealed class MailAppViewModel : ViewModelBase, IDisposable
{
    public static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private readonly DesktopViewModel _desktop;
    private string _folder = MailFolders.Reception;
    private string? _selectedId;
    private string _search = string.Empty;
    private string? _message;
    private bool _messageIsError;
    private bool _showContacts;

    public MailAppViewModel(DesktopViewModel desktop)
    {
        _desktop = desktop;
        NewCommand = new RelayCommand(() => Compose(new MailComposeInit { Body = SignatureBlock() }));
        ReplyCommand = new RelayCommand(() => Reply(false), () => Selected is { Folder: not MailFolders.Brouillons });
        ReplyAllCommand = new RelayCommand(() => Reply(true), () => Selected is { Folder: not MailFolders.Brouillons });
        ForwardCommand = new RelayCommand(Forward, () => Selected is { Folder: not MailFolders.Brouillons });
        DeleteCommand = new RelayCommand(() => _ = DeleteAsync(), () => Selected is not null);
        RestoreCommand = new RelayCommand(() => _ = UpdateAsync(new MailFlagRequest { Folder = MailFolders.Reception }), () => Selected?.Folder == MailFolders.Corbeille);
        ToggleReadCommand = new RelayCommand(() => _ = UpdateAsync(new MailFlagRequest { Read = !(Selected?.Read ?? true) }), () => Selected is not null);
        ToggleFlagCommand = new RelayCommand(() => _ = UpdateAsync(new MailFlagRequest { Flagged = !(Selected?.Flagged ?? false) }), () => Selected is not null);
        EditDraftCommand = new RelayCommand(EditDraft, () => Selected?.Folder == MailFolders.Brouillons);
        EmptyTrashCommand = new RelayCommand(() => _ = RunAsync("api/mail/trash/empty", new { }, "Éléments supprimés vidés."));
        RefreshCommand = new RelayCommand(() => _ = ArgosSession.Current?.Mail.RefreshAsync());
        ContactsCommand = new RelayCommand(() => ShowContacts = true);
        NewContactCommand = new RelayCommand(() => EditContact(null));
        SaveContactCommand = new RelayCommand(() => _ = SaveContactAsync());
        DeleteContactCommand = new RelayCommand(() => _ = DeleteContactAsync(), () => EditedContact.Id.Length > 0);
        SaveSignatureCommand = new RelayCommand(() => _ = RunAsync("api/mail/signature", new MailProfileRequest { Signature = Signature }, "Signature enregistrée."));

        if (ArgosSession.Current is { } session)
        {
            session.Mail.Changed += Build;
            if (!session.Mail.IsLoaded) _ = session.Mail.RefreshAsync();
        }
        Build();
    }

    public IStorageProvider? Storage { get; set; }
    public ILauncher? Launcher { get; set; }

    private MailSnapshot S => ArgosSession.Current?.Mail.Snapshot ?? new MailSnapshot();

    public string Address => S.Address;
    public string Name => S.Name;

    public ObservableCollection<FolderRow> Folders { get; } = [];
    public ObservableCollection<MessageRow> Messages { get; } = [];
    public ObservableCollection<AttachmentRow> Attachments { get; } = [];
    public ObservableCollection<ContactRow> Contacts { get; } = [];
    public ObservableCollection<ContactRow> Directory { get; } = [];

    public string FolderTitle => MailFolders.Label(_folder);
    public bool IsTrash => _folder == MailFolders.Corbeille;
    public bool HasMessages => Messages.Count > 0;

    public bool ShowContacts
    {
        get => _showContacts;
        set
        {
            if (!SetProperty(ref _showContacts, value)) return;
            OnPropertyChanged(nameof(ShowMailbox));
            Build();
        }
    }
    public bool ShowMailbox => !ShowContacts;

    public string Search
    {
        get => _search;
        set { if (SetProperty(ref _search, value)) Build(); }
    }

    public string? Message { get => _message; private set { if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage)); } }
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public bool MessageIsError { get => _messageIsError; private set => SetProperty(ref _messageIsError, value); }

    // ===== Volet de lecture =====

    public MailItem? Selected => ArgosSession.Current?.Mail.Find(_selectedId);
    public bool HasSelection => Selected is not null;
    public bool NoSelection => Selected is null;
    public string ReadSubject => Selected is { } m ? (string.IsNullOrWhiteSpace(m.Subject) ? "(sans objet)" : m.Subject) : string.Empty;
    public string ReadFrom => Selected is { } m ? (string.IsNullOrEmpty(m.FromName) ? m.From : $"{m.FromName} <{m.From}>") : string.Empty;
    public string ReadInitials => Initials(Selected?.FromName ?? Selected?.From);
    public string ReadDate => Selected?.SentAt is { } at ? at.ToLocalTime().ToString("ddd dd/MM/yyyy HH:mm", Fr)
        : Selected is { } d ? "Brouillon enregistré le " + d.UpdatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Fr) : string.Empty;
    public string ReadTo => Selected is { } m ? Names(m.To) : string.Empty;
    public string ReadCc => Selected is { } m ? Names(m.Cc) : string.Empty;
    public string ReadBcc => Selected is { } m ? Names(m.Bcc) : string.Empty;
    public bool HasCc => Selected?.Cc.Count > 0;
    public bool HasBcc => Selected?.Bcc.Count > 0;
    public string ReadBody => Selected?.Body ?? string.Empty;
    public bool ReadHigh => Selected?.Importance == MailImportance.Haute;
    public bool HasAttachments => Attachments.Count > 0;
    public bool IsDraftSelected => Selected?.Folder == MailFolders.Brouillons;
    public bool IsTrashSelected => Selected?.Folder == MailFolders.Corbeille;
    public string FlagLabel => Selected?.Flagged == true ? "Retirer l'indicateur" : "Indicateur";
    public string ReadLabel => Selected?.Read == true ? "Marquer comme non lu" : "Marquer comme lu";

    public RelayCommand NewCommand { get; }
    public RelayCommand ReplyCommand { get; }
    public RelayCommand ReplyAllCommand { get; }
    public RelayCommand ForwardCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public RelayCommand ToggleReadCommand { get; }
    public RelayCommand ToggleFlagCommand { get; }
    public RelayCommand EditDraftCommand { get; }
    public RelayCommand EmptyTrashCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ContactsCommand { get; }

    // ===== Construction =====

    private void Build()
    {
        var store = ArgosSession.Current?.Mail;
        if (store?.LastError is { } error) ShowMessage(error, true);
        var s = S;

        Fill(Folders, MailFolders.All.Select(f => new FolderRow(f, MailFolders.Label(f),
            f == MailFolders.Brouillons ? s.Items.Count(i => i.Folder == f) : s.Items.Count(i => i.Folder == f && !i.Read),
            !ShowContacts && f == _folder,
            new RelayCommand(() => SelectFolder(f)))));

        var query = Search.Trim();
        var items = s.Items.Where(i => i.Folder == _folder);
        if (query.Length > 0)
            items = items.Where(i => Contains(i.Subject, query) || Contains(i.FromName, query) || Contains(i.From, query)
                                     || i.To.Any(t => Contains(t, query)) || Contains(i.Body, query));
        var list = items.OrderByDescending(i => i.SentAt ?? i.UpdatedAt).ToList();
        if (_selectedId is not null && list.All(i => i.Id != _selectedId)) _selectedId = null;
        Fill(Messages, list.Select(RowOf));

        Attachments.Clear();
        foreach (var a in Selected?.Attachments ?? [])
        {
            var att = a;
            Attachments.Add(new AttachmentRow(a.Id, a.Name, Size(a.Size),
                new RelayCommand(() => _ = OpenAttachmentAsync(att)), new RelayCommand(() => _ = SaveAttachmentAsync(att))));
        }

        Fill(Contacts, s.Contacts.Where(c => query.Length == 0 || Contains(c.Name, query) || Contains(c.Address, query))
            .Select(c => new ContactRow(c.Id, c.Name, c.Address, c.Notes, true,
                new RelayCommand(() => Write(c.Address)), new RelayCommand(() => EditContact(c.Id)))));
        Fill(Directory, s.Directory.Where(d => query.Length == 0 || Contains(d.Name, query) || Contains(d.Address, query) || Contains(d.Fonction, query))
            .Select(d => new ContactRow(string.Empty, d.Name, d.Address, string.Join(" · ", new[] { d.Fonction, d.Unite }.Where(x => !string.IsNullOrWhiteSpace(x))), false,
                new RelayCommand(() => Write(d.Address)), new RelayCommand(() => { EditContact(null); EditedContact.Name = d.Name; EditedContact.Address = d.Address; OnPropertyChanged(nameof(EditedContact)); }))));

        if (!_signatureEdited) _signature = s.Signature;
        foreach (var n in new[]
                 {
                     nameof(Address), nameof(Name), nameof(FolderTitle), nameof(IsTrash), nameof(HasMessages), nameof(Selected), nameof(HasSelection),
                     nameof(NoSelection), nameof(ReadSubject), nameof(ReadFrom), nameof(ReadInitials), nameof(ReadDate), nameof(ReadTo), nameof(ReadCc),
                     nameof(ReadBcc), nameof(HasCc), nameof(HasBcc), nameof(ReadBody), nameof(ReadHigh), nameof(HasAttachments), nameof(IsDraftSelected),
                     nameof(IsTrashSelected), nameof(FlagLabel), nameof(ReadLabel), nameof(Signature)
                 })
            OnPropertyChanged(n);
        foreach (var c in new[] { ReplyCommand, ReplyAllCommand, ForwardCommand, DeleteCommand, RestoreCommand, ToggleReadCommand, ToggleFlagCommand, EditDraftCommand, DeleteContactCommand })
            c.RaiseCanExecuteChanged();
    }

    private MessageRow RowOf(MailItem i)
    {
        var outgoing = i.Folder is MailFolders.Envoyes or MailFolders.Brouillons
                       || (i.Folder == MailFolders.Corbeille && i.PreviousFolder is MailFolders.Envoyes or MailFolders.Brouillons);
        var correspondent = outgoing ? (i.To.Count == 0 ? "(aucun destinataire)" : "À : " + Names(i.To)) : (string.IsNullOrEmpty(i.FromName) ? i.From : i.FromName);
        var preview = Preview(i.Body);
        var id = i.Id;
        return new MessageRow(i.Id, correspondent, string.IsNullOrWhiteSpace(i.Subject) ? "(sans objet)" : i.Subject, preview,
            ShortDate(i.SentAt ?? i.UpdatedAt), !i.Read, i.Flagged, i.Importance == MailImportance.Haute, i.Attachments.Count > 0,
            id == _selectedId, new RelayCommand(() => Select(id))) { IsDraft = i.Folder == MailFolders.Brouillons };
    }

    public void SelectFolder(string folder)
    {
        _folder = folder;
        _selectedId = null;
        ShowContacts = false;
        Build();
    }

    public void Select(string id)
    {
        _selectedId = id;
        Build();
        if (Selected is { Read: false })
            _ = UpdateAsync(new MailFlagRequest { Read = true });
    }

    // ===== Actions =====

    private void Compose(MailComposeInit init) => _desktop.OpenWindow(MailWindows.Compose(_desktop, init));

    private void Write(string address) => Compose(new MailComposeInit { To = address, Body = SignatureBlock() });

    private void Reply(bool all)
    {
        if (Selected is not { } m) return;
        var to = new List<string> { m.From };
        var cc = new List<string>();
        if (all) (to, cc) = MailCompose.ReplyAll(m, S.Address);
        else if (m.Folder == MailFolders.Envoyes) to = m.To.ToList();
        Compose(new MailComposeInit
        {
            To = string.Join("; ", to), Cc = string.Join("; ", cc), Subject = MailCompose.ReplySubject(m.Subject),
            Body = SignatureBlock() + MailCompose.Quote(m), InReplyTo = m.MessageId
        });
    }

    private void Forward()
    {
        if (Selected is not { } m) return;
        Compose(new MailComposeInit
        {
            Subject = MailCompose.ForwardSubject(m.Subject), Body = SignatureBlock() + MailCompose.Quote(m),
            Attachments = m.Attachments.ToList()
        });
    }

    private void EditDraft()
    {
        if (Selected is not { Folder: MailFolders.Brouillons } d) return;
        Compose(new MailComposeInit
        {
            DraftId = d.Id, To = string.Join("; ", d.To), Cc = string.Join("; ", d.Cc), Bcc = string.Join("; ", d.Bcc),
            Subject = d.Subject, Body = d.Body, Importance = d.Importance, InReplyTo = d.InReplyTo, Attachments = d.Attachments.ToList()
        });
    }

    private async Task DeleteAsync()
    {
        if (Selected is not { } m) return;
        if (m.Folder is MailFolders.Corbeille or MailFolders.Brouillons)
            await RunAsync($"api/mail/items/{m.Id}/delete", new { }, m.Folder == MailFolders.Brouillons ? "Brouillon supprimé." : "Message supprimé définitivement.");
        else
            await UpdateAsync(new MailFlagRequest { Folder = MailFolders.Corbeille });
    }

    private async Task UpdateAsync(MailFlagRequest request)
    {
        if (Selected is not { } m) return;
        // Mise à jour immédiate à l'écran, confirmée par le serveur.
        if (request.Read is { } r) m.Read = r;
        if (request.Flagged is { } f) m.Flagged = f;
        Build();
        await RunAsync($"api/mail/items/{m.Id}", request, null);
    }

    private async Task RunAsync(string path, object body, string? success)
    {
        var session = ArgosSession.Current;
        if (session is null) return;
        var result = await session.Api.SendAsync(HttpMethod.Post, path, body);
        if (!result.Ok) ShowMessage(result.Error ?? "Action refusée.", true);
        else if (success is not null) ShowMessage(success, false);
        await session.Mail.RefreshAsync();
    }

    // ===== Pièces jointes =====

    private async Task<string?> FetchAsync(MailAttachment a)
    {
        var session = ArgosSession.Current;
        if (session is null) return null;
        try
        {
            var bytes = await session.Api.GetBytesAsync($"api/mail/attachments/{a.Id}");
            var dir = Path.Combine(Path.GetTempPath(), "argos-mail", a.Id);
            System.IO.Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, string.Concat(a.Name.Split(Path.GetInvalidFileNameChars())));
            await File.WriteAllBytesAsync(path, bytes);
            return path;
        }
        catch (Exception ex)
        {
            ShowMessage("Pièce jointe indisponible : " + ex.Message, true);
            return null;
        }
    }

    private async Task OpenAttachmentAsync(MailAttachment a)
    {
        var path = await FetchAsync(a);
        if (path is null) return;
        if (Launcher is null || !await Launcher.LaunchFileInfoAsync(new FileInfo(path)))
            ShowMessage("Impossible d'ouvrir la pièce jointe sur ce poste : utilise « Enregistrer ».", true);
    }

    private async Task SaveAttachmentAsync(MailAttachment a)
    {
        if (Storage is null) return;
        var file = await Storage.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = a.Name, Title = "Enregistrer la pièce jointe" });
        if (file is null) return;
        var path = await FetchAsync(a);
        if (path is null) return;
        await using var target = await file.OpenWriteAsync();
        await using var source = File.OpenRead(path);
        await source.CopyToAsync(target);
        ShowMessage($"« {a.Name} » enregistré.", false);
    }

    // ===== Contacts et signature =====

    public MailContact EditedContact { get; private set; } = new();
    public RelayCommand NewContactCommand { get; }
    public RelayCommand SaveContactCommand { get; }
    public RelayCommand DeleteContactCommand { get; }
    public RelayCommand SaveSignatureCommand { get; }

    private string _signature = string.Empty;
    private bool _signatureEdited;
    public string Signature
    {
        get => _signature;
        set { _signatureEdited = true; SetProperty(ref _signature, value ?? string.Empty); }
    }

    private void EditContact(string? id)
    {
        var c = S.Contacts.FirstOrDefault(x => x.Id == id);
        EditedContact = c is null ? new MailContact() : new MailContact { Id = c.Id, Name = c.Name, Address = c.Address, Notes = c.Notes };
        ShowContacts = true;
        OnPropertyChanged(nameof(EditedContact));
        DeleteContactCommand.RaiseCanExecuteChanged();
    }

    private async Task SaveContactAsync()
    {
        await RunAsync("api/mail/contacts", EditedContact, "Contact enregistré.");
        if (MessageIsError) return;
        EditContact(null);
    }

    private async Task DeleteContactAsync()
    {
        if (EditedContact.Id.Length == 0) return;
        await RunAsync($"api/mail/contacts/{EditedContact.Id}/delete", new { }, "Contact supprimé.");
        EditContact(null);
    }

    private string SignatureBlock() => string.IsNullOrWhiteSpace(S.Signature) ? string.Empty : "\n\n" + S.Signature;

    // ===== Outils =====

    private string Names(IEnumerable<string> addresses)
    {
        var directory = S.Directory;
        var contacts = S.Contacts;
        return string.Join("; ", addresses.Select(a =>
            directory.FirstOrDefault(d => d.Address == a)?.Name ?? contacts.FirstOrDefault(c => c.Address == a)?.Name ?? a));
    }

    public static string Initials(string? name)
    {
        var parts = (name ?? string.Empty).Split([' ', '.', '@'], StringSplitOptions.RemoveEmptyEntries)
            .Where(p => char.IsLetter(p[0])).ToList();
        if (parts.Count >= 3) parts.RemoveAt(0);                     // grade en tête (« Sgt SANTOS Diego »)
        return string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }

    public static string Preview(string body)
    {
        var text = body.Replace("**", "").Replace("__", "").Replace("\r", "").Replace('\n', ' ').Replace("*", "");
        var cut = text.IndexOf("________________", StringComparison.Ordinal);
        if (cut >= 0) text = text[..cut];
        text = text.Trim();
        return text.Length > 110 ? text[..110] + "…" : text;
    }

    public static string ShortDate(DateTimeOffset at)
    {
        var local = at.ToLocalTime();
        var today = DateTimeOffset.Now.Date;
        if (local.Date == today) return local.ToString("HH:mm", Fr);
        if (local.Date > today.AddDays(-7)) return local.ToString("ddd HH:mm", Fr);
        return local.ToString("dd/MM/yyyy", Fr);
    }

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} o",
        < 1024 * 1024 => $"{bytes / 1024} Ko",
        _ => $"{bytes / 1024.0 / 1024.0:0.0} Mo"
    };

    private static bool Contains(string? text, string query) => text?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true;

    private void ShowMessage(string text, bool error)
    {
        MessageIsError = error;
        Message = text;
    }

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        var list = items.ToList();
        if (list.SequenceEqual(target)) return;
        target.Clear();
        foreach (var item in list) target.Add(item);
    }

    public void Dispose()
    {
        if (ArgosSession.Current is { } session) session.Mail.Changed -= Build;
    }
}
