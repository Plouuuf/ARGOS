using Argos.ViewModels.Mail;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Argos.Views.Apps.Mail;

/// <summary>Rédaction : barre de mise en forme, aperçu, pièces jointes depuis le sélecteur de fichiers.</summary>
public partial class MailComposeView : UserControl
{
    public MailComposeView()
    {
        InitializeComponent();
        BoldButton.Click += (_, _) => Wrap("**");
        ItalicButton.Click += (_, _) => Wrap("*");
        UnderlineButton.Click += (_, _) => Wrap("__");
        ListButton.Click += (_, _) => InsertBullet();
        AttachButton.Click += async (_, _) => await AttachAsync();
        PreviewToggle.IsCheckedChanged += (_, _) => MailText.Render(Preview, BodyBox.Text);
    }

    private void Wrap(string marker)
    {
        var text = BodyBox.Text ?? string.Empty;
        var start = Math.Clamp(Math.Min(BodyBox.SelectionStart, BodyBox.SelectionEnd), 0, text.Length);
        var end = Math.Clamp(Math.Max(BodyBox.SelectionStart, BodyBox.SelectionEnd), 0, text.Length);
        BodyBox.Text = text[..start] + marker + text[start..end] + marker + text[end..];
        BodyBox.Focus();
        BodyBox.SelectionStart = start + marker.Length;
        BodyBox.SelectionEnd = end + marker.Length;
    }

    private void InsertBullet()
    {
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
