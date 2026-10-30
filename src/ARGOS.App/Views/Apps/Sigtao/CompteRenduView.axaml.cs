using System.ComponentModel;
using System.Text.RegularExpressions;
using Argos.Shared.Contracts;
using Argos.ViewModels.Sigtao;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Argos.Views.Apps.Sigtao;

/// <summary>
/// E-COMPTE-RENDU : barre de mise en forme (gras, italique, souligné, liste), aperçu fidèle au modèle,
/// pièces jointes (ajout, enregistrement sur le poste).
/// </summary>
public partial class CompteRenduView : UserControl
{
    private static readonly Regex Tokens = new(@"(\*\*[^*\n]+\*\*|__[^_\n]+__|\*[^*\n]+\*)", RegexOptions.Compiled);
    private CompteRenduViewModel? _vm;

    public CompteRenduView()
    {
        InitializeComponent();
        BoldButton.Click += (_, _) => Wrap("**");
        ItalicButton.Click += (_, _) => Wrap("*");
        UnderlineButton.Click += (_, _) => Wrap("__");
        ListButton.Click += (_, _) => InsertBullet();
        AttachButton.Click += async (_, _) => await AttachAsync();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as CompteRenduViewModel;
            if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
            Render();
        };
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CompteRenduViewModel.Body) or nameof(CompteRenduViewModel.Report))
            Render();
    }

    /// <summary>Entoure la sélection (ou insère des marqueurs) : **gras**, *italique*, __souligné__.</summary>
    private void Wrap(string marker)
    {
        var text = BodyBox.Text ?? string.Empty;
        var start = Math.Min(BodyBox.SelectionStart, BodyBox.SelectionEnd);
        var end = Math.Max(BodyBox.SelectionStart, BodyBox.SelectionEnd);
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, 0, text.Length);
        BodyBox.Text = text[..start] + marker + text[start..end] + marker + text[end..];
        BodyBox.Focus();
        BodyBox.SelectionStart = start + marker.Length;
        BodyBox.SelectionEnd = end + marker.Length;
    }

    private void InsertBullet()
    {
        var text = BodyBox.Text ?? string.Empty;
        var caret = Math.Clamp(BodyBox.CaretIndex, 0, text.Length);
        var lineStart = text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
        if (caret == 0) lineStart = 0;
        BodyBox.Text = text[..lineStart] + "- " + text[lineStart..];
        BodyBox.Focus();
        BodyBox.CaretIndex = caret + 2;
    }

    /// <summary>Aperçu : convertit les marqueurs en mise en forme, « - » en puces.</summary>
    private void Render()
    {
        BodyPreview.Inlines?.Clear();
        var body = _vm?.Body ?? string.Empty;
        var inlines = BodyPreview.Inlines ??= new InlineCollection();
        var lines = body.Replace("\r", string.Empty).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("- ", StringComparison.Ordinal))
                line = "    •  " + line[2..];
            foreach (var part in Tokens.Split(line))
            {
                if (part.Length == 0) continue;
                if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4)
                    inlines.Add(new Run(part[2..^2]) { FontWeight = FontWeight.Bold });
                else if (part.StartsWith("__") && part.EndsWith("__") && part.Length > 4)
                    inlines.Add(new Run(part[2..^2]) { TextDecorations = TextDecorations.Underline });
                else if (part.StartsWith('*') && part.EndsWith('*') && part.Length > 2)
                    inlines.Add(new Run(part[1..^1]) { FontStyle = FontStyle.Italic });
                else
                    inlines.Add(new Run(part));
            }
            if (i < lines.Length - 1)
                inlines.Add(new LineBreak());
        }
    }

    private async Task AttachAsync()
    {
        if (_vm is null || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Pièces jointes", AllowMultiple = true });
        foreach (var file in files)
        {
            try
            {
                await using var stream = await file.OpenReadAsync();
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                _vm.AddAttachment(file.Name, ContentType(file.Name), memory.ToArray());
            }
            catch
            {
                // Fichier illisible : ignoré.
            }
        }
    }

    private async void OnSaveAttachment(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not CrAttachment attachment
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        try
        {
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = attachment.Name, Title = "Enregistrer la pièce jointe" });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            var bytes = Convert.FromBase64String(attachment.Data);
            await stream.WriteAsync(bytes);
        }
        catch
        {
            // Enregistrement annulé ou impossible.
        }
    }

    private static string ContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".pdf" => "application/pdf",
        ".txt" => "text/plain",
        ".doc" or ".docx" => "application/msword",
        _ => "application/octet-stream"
    };
}
