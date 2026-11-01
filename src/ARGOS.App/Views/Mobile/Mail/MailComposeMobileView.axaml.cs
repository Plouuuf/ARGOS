using Argos.ViewModels.Mail;
using Argos.Views.Apps.Mail;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Argos.Views.Mobile.Mail;

/// <summary>Rédaction sur iPhone : barre de mise en forme, aperçu, pièces jointes depuis le sélecteur de fichiers.</summary>
public partial class MailComposeMobileView : UserControl
{
    public MailComposeMobileView()
    {
        InitializeComponent();
        BoldButton.Click += (_, _) => Wrap("**");
        ItalicButton.Click += (_, _) => Wrap("*");
        UnderlineButton.Click += (_, _) => Wrap("__");
        ListButton.Click += (_, _) => InsertBullet();
        LinkButton.Click += (_, _) => InsertLink();
        AttachButton.Click += async (_, _) => await AttachAsync();
        PreviewToggle.IsCheckedChanged += (_, _) => MailText.Render(Preview, BodyBox.Text);
    }

    private void Wrap(string marker)
    {
        if (PreviewToggle.IsChecked == true) return;
        var text = BodyBox.Text ?? string.Empty;
        var start = Math.Clamp(Math.Min(BodyBox.SelectionStart, BodyBox.SelectionEnd), 0, text.Length);
        var end = Math.Clamp(Math.Max(BodyBox.SelectionStart, BodyBox.SelectionEnd), 0, text.Length);
        BodyBox.Text = text[..start] + marker + text[start..end] + marker + text[end..];
        BodyBox.Focus();
        BodyBox.SelectionStart = start + marker.Length;
        BodyBox.SelectionEnd = end + marker.Length;
    }

    /// <summary>Insère [texte sélectionné](https://) et place le curseur sur l'adresse à compléter.</summary>
    private void InsertLink()
    {
        if (PreviewToggle.IsChecked == true) return;
        var text = BodyBox.Text ?? string.Empty;
        var start = Math.Clamp(Math.Min(BodyBox.SelectionStart, BodyBox.SelectionEnd), 0, text.Length);
        var end = Math.Clamp(Math.Max(BodyBox.SelectionStart, BodyBox.SelectionEnd), 0, text.Length);
        var label = end > start ? text[start..end] : "texte du lien";
        var inserted = $"[{label}](https://)";
        BodyBox.Text = text[..start] + inserted + text[end..];
        BodyBox.Focus();
        var urlStart = start + label.Length + 3;
        BodyBox.SelectionStart = urlStart;
        BodyBox.SelectionEnd = urlStart + "https://".Length;
    }

    private void InsertBullet()
    {
        if (PreviewToggle.IsChecked == true) return;
        var text = BodyBox.Text ?? string.Empty;
        var caret = Math.Clamp(BodyBox.CaretIndex, 0, text.Length);
        var lineStart = caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
        BodyBox.Text = text[..lineStart] + "- " + text[lineStart..];
        BodyBox.Focus();
        BodyBox.CaretIndex = caret + 2;
    }

    private async Task AttachAsync()
    {
        if (DataContext is not MailComposeViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Joindre des fichiers", AllowMultiple = true });
        foreach (var file in files)
        {
            try
            {
                await using var stream = await file.OpenReadAsync();
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                vm.AddAttachment(file.Name, ContentType(file.Name), memory.ToArray());
            }
            catch
            {
                // Fichier illisible : ignoré.
            }
        }
    }

    private static string ContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".heic" => "image/heic",
        ".txt" => "text/plain",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "application/octet-stream"
    };

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
