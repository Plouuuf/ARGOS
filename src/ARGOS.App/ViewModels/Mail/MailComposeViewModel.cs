using System.Collections.ObjectModel;
using Argos.Services;
using Argos.Shared.Contracts;
using Avalonia.Threading;

namespace Argos.ViewModels.Mail;

/// <summary>Contenu initial d'une fenêtre de rédaction (nouveau, réponse, transfert, brouillon).</summary>
public sealed class MailComposeInit
{
    public string? DraftId { get; set; }
    public string To { get; set; } = string.Empty;
    public string Cc { get; set; } = string.Empty;
    public string Bcc { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Importance { get; set; } = MailImportance.Normale;
    public string InReplyTo { get; set; } = string.Empty;
    public List<MailAttachment> Attachments { get; set; } = [];
}

public sealed class ComposeAttachment(string name, long size, string? existingId, MailNewAttachment? upload, Action<ComposeAttachment> remove)
{
    public string Name { get; } = name;
    public string Size { get; } = MailAppViewModel.Size(size);
    public long Bytes { get; } = size;
    public string? ExistingId { get; set; } = existingId;
    public MailNewAttachment? Upload { get; set; } = upload;
    public RelayCommand RemoveCommand => new(() => remove(this));
}

/// <summary>
/// Rédaction d'un message : À / Cc / Cci avec l'annuaire, mise en forme, importance, pièces jointes.
/// Le brouillon est enregistré automatiquement (toutes les 20 s si modifié, et à la fermeture).
/// </summary>
public sealed class MailComposeViewModel : ViewModelBase, IDisposable
{
    public const long MaxBytes = 20 * 1024 * 1024;
    private readonly DesktopViewModel _desktop;
    private readonly string _key;
    private readonly DispatcherTimer? _timer;
    private string? _draftId;
    private string _to, _cc, _bcc, _subject, _body, _importance;
    private readonly string _inReplyTo;
    private bool _dirty, _busy, _sent, _showBcc;
    private string _status = string.Empty;
    private bool _statusIsError;
    private MailDirectoryEntry? _pick;

    public MailComposeViewModel(DesktopViewModel desktop, string key, MailComposeInit init, bool autoSave = true)
    {
        _desktop = desktop;
        _key = key;
        _draftId = init.DraftId;
        _to = init.To;
        _cc = init.Cc;
        _bcc = init.Bcc;
        _subject = init.Subject;
        _body = init.Body;
        _importance = MailImportance.All.Contains(init.Importance) ? init.Importance : MailImportance.Normale;
        _inReplyTo = init.InReplyTo;
        _showBcc = init.Bcc.Length > 0;
        foreach (var a in init.Attachments)
            Attachments.Add(new ComposeAttachment(a.Name, a.Size, a.Id, null, Remove));

        var s = ArgosSession.Current?.Mail.Snapshot ?? new MailSnapshot();
        From = string.IsNullOrEmpty(s.Name) ? s.Address : $"{s.Name} <{s.Address}>";
        Suggestions = s.Contacts.Select(c => new MailDirectoryEntry { Name = c.Name, Address = c.Address })
            .Concat(s.Directory).GroupBy(e => e.Address).Select(g => g.First())
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        SendCommand = new RelayCommand(() => _ = SendAsync(), () => !_busy);
        SaveCommand = new RelayCommand(() => _ = SaveDraftAsync(true), () => !_busy);
        DiscardCommand = new RelayCommand(() => _ = DiscardAsync());
        AddToCommand = new RelayCommand(() => AddPick("to"));
        AddCcCommand = new RelayCommand(() => AddPick("cc"));
        AddBccCommand = new RelayCommand(() => { ShowBcc = true; AddPick("bcc"); });
        ToggleBccCommand = new RelayCommand(() => ShowBcc = !ShowBcc);
        Attachments.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(HasAttachments)); OnPropertyChanged(nameof(AttachmentsLabel)); };

        if (autoSave)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _timer.Tick += (_, _) => { if (_dirty && !_busy && !_sent) _ = SaveDraftAsync(false); };
            _timer.Start();
        }
    }

    public string From { get; }
    public IReadOnlyList<MailDirectoryEntry> Suggestions { get; }
    public IReadOnlyList<string> Importances => MailImportance.All;

    public string To { get => _to; set => Edit(ref _to, value); }
    public string Cc { get => _cc; set => Edit(ref _cc, value); }
    public string Bcc { get => _bcc; set => Edit(ref _bcc, value); }
    public string Subject { get => _subject; set { Edit(ref _subject, value); OnPropertyChanged(nameof(WindowSubject)); } }
    public string Body { get => _body; set => Edit(ref _body, value); }
    public string Importance { get => _importance; set => Edit(ref _importance, value); }
    public string WindowSubject => string.IsNullOrWhiteSpace(Subject) ? "Nouveau message" : Subject;
    public bool ShowBcc { get => _showBcc; set => SetProperty(ref _showBcc, value); }

    /// <summary>Destinataire choisi dans l'annuaire (zone de recherche), à ajouter en À / Cc / Cci.</summary>
    public MailDirectoryEntry? Pick { get => _pick; set => SetProperty(ref _pick, value); }

    public ObservableCollection<ComposeAttachment> Attachments { get; } = [];
    public bool HasAttachments => Attachments.Count > 0;
    public string AttachmentsLabel => $"{Attachments.Count} pièce(s) jointe(s) · {MailAppViewModel.Size(Attachments.Sum(a => a.Bytes))}";

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool StatusIsError { get => _statusIsError; private set => SetProperty(ref _statusIsError, value); }
    public bool IsDirty => _dirty;
    public string? DraftId => _draftId;

    public RelayCommand SendCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand DiscardCommand { get; }
    public RelayCommand AddToCommand { get; }
    public RelayCommand AddCcCommand { get; }
    public RelayCommand AddBccCommand { get; }
    public RelayCommand ToggleBccCommand { get; }

    private void Edit(ref string field, string? value)
    {
        if (SetProperty(ref field, value ?? string.Empty)) _dirty = true;
    }

    private void AddPick(string target)
    {
        if (Pick is not { } entry) return;
        string Append(string current) => MailAddresses.Split(current).Contains(entry.Address)
            ? current
            : (string.IsNullOrWhiteSpace(current) ? string.Empty : current.TrimEnd().TrimEnd(';') + "; ") + entry.Address;
        switch (target)
        {
            case "cc": Cc = Append(Cc); break;
            case "bcc": Bcc = Append(Bcc); break;
            default: To = Append(To); break;
        }
        Pick = null;
    }

    public void AddAttachment(string name, string contentType, byte[] data)
    {
        if (data.Length > 10 * 1024 * 1024)
        {
            SetStatus($"« {name} » dépasse 10 Mo.", true);
            return;
        }
        if (Attachments.Sum(a => a.Bytes) + data.Length > MaxBytes)
        {
            SetStatus("Les pièces jointes dépassent 20 Mo au total.", true);
            return;
        }
        Attachments.Add(new ComposeAttachment(name, data.Length, null,
            new MailNewAttachment { Name = name, ContentType = contentType, Data = Convert.ToBase64String(data) }, Remove));
        _dirty = true;
    }

    private void Remove(ComposeAttachment a)
    {
        Attachments.Remove(a);
        _dirty = true;
    }

    public MailDraftRequest Request() => new()
    {
        DraftId = _draftId, To = To, Cc = Cc, Bcc = Bcc, Subject = Subject, Body = Body, Importance = Importance, InReplyTo = _inReplyTo,
        KeepAttachments = Attachments.Where(a => a.ExistingId is not null).Select(a => a.ExistingId!).ToList(),
        NewAttachments = Attachments.Where(a => a.Upload is not null).Select(a => a.Upload!).ToList()
    };

    private async Task<MailDraftResult> PostAsync(string path)
    {
        var session = ArgosSession.Current;
        if (session is null) return new MailDraftResult { Error = "Session fermée." };
        try
        {
            return await session.Api.PostAsync<MailDraftResult>(path, Request());
        }
        catch (Exception ex)
        {
            return new MailDraftResult { Error = ex is HttpRequestException or TaskCanceledException ? "Serveur ARGOS injoignable." : ex.Message };
        }
    }

    private async Task SaveDraftAsync(bool manual)
    {
        if (_busy || _sent) return;
        SetBusy(true);
        var result = await PostAsync("api/mail/drafts");
        SetBusy(false);
        if (!result.Ok || result.Item is null)
        {
            SetStatus(result.Error ?? "Brouillon non enregistré.", true);
            return;
        }
        Adopt(result.Item);
        SetStatus($"Brouillon enregistré à {DateTime.Now:HH:mm}", false);
        if (manual) _ = ArgosSession.Current?.Mail.RefreshAsync();
    }

    /// <summary>Le serveur a enregistré le brouillon : les pièces jointes envoyées deviennent des pièces conservées.</summary>
    private void Adopt(MailItem item)
    {
        _draftId = item.Id;
        _dirty = false;
        var uploaded = Attachments.Where(a => a.Upload is not null).ToList();
        var known = Attachments.Where(a => a.ExistingId is not null).Select(a => a.ExistingId).ToHashSet();
        var fresh = item.Attachments.Where(a => !known.Contains(a.Id)).ToList();
        for (var i = 0; i < uploaded.Count && i < fresh.Count; i++)
        {
            uploaded[i].ExistingId = fresh[i].Id;
            uploaded[i].Upload = null;
        }
    }

    private async Task SendAsync()
    {
        if (MailAddresses.Split(To).Count + MailAddresses.Split(Cc).Count + MailAddresses.Split(Bcc).Count == 0)
        {
            SetStatus("Indiquez au moins un destinataire.", true);
            return;
        }
        SetBusy(true);
        SetStatus("Envoi en cours…", false);
        var result = await PostAsync("api/mail/send");
        SetBusy(false);
        if (!result.Ok)
        {
            SetStatus(result.Error ?? "Envoi refusé.", true);
            return;
        }
        _sent = true;
        _ = ArgosSession.Current?.Mail.RefreshAsync();
        _desktop.CloseWindow(_key);
    }

    private async Task DiscardAsync()
    {
        _sent = true;                                   // plus d'enregistrement automatique
        if (_draftId is not null && ArgosSession.Current is { } session)
        {
            await session.Api.SendAsync(HttpMethod.Post, $"api/mail/items/{_draftId}/delete");
            _ = session.Mail.RefreshAsync();
        }
        _desktop.CloseWindow(_key);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        SendCommand.RaiseCanExecuteChanged();
        SaveCommand.RaiseCanExecuteChanged();
    }

    private void SetStatus(string text, bool error)
    {
        StatusIsError = error;
        Status = text;
    }

    public void Dispose()
    {
        _timer?.Stop();
        // Fermeture de la fenêtre sans envoyer : le travail est gardé dans les brouillons.
        if (_dirty && !_sent && (Body.Trim().Length > 0 || Subject.Length > 0 || To.Length > 0 || Attachments.Count > 0))
            _ = SaveDraftAsync(true);
    }
}
