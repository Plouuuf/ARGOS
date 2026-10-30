using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Argos.Views.Apps.Mail;

/// <summary>
/// Corps de message mis en forme : **gras**, *italique*, __souligné__, « - » en puces,
/// lignes citées (après le séparateur de réponse) en gris. Propriété attachée MailText.Body.
/// </summary>
public static class MailText
{
    private static readonly Regex Tokens = new(@"(\*\*[^*\n]+\*\*|__[^_\n]+__|\*[^*\n]+\*)", RegexOptions.Compiled);
    private static readonly IBrush Quoted = new SolidColorBrush(Color.Parse("#605E5C"));

    public static readonly AttachedProperty<string?> BodyProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, string?>("Body", typeof(MailText));

    static MailText()
    {
        BodyProperty.Changed.AddClassHandler<TextBlock>((block, e) => Render(block, e.NewValue as string));
    }

    public static string? GetBody(TextBlock block) => block.GetValue(BodyProperty);
    public static void SetBody(TextBlock block, string? value) => block.SetValue(BodyProperty, value);

    public static void Render(TextBlock block, string? body)
    {
        var inlines = block.Inlines ??= new InlineCollection();
        inlines.Clear();
        var lines = (body ?? string.Empty).Replace("\r", string.Empty).Split('\n');
        var quoted = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("________________", StringComparison.Ordinal)) quoted = true;
            if (line.StartsWith("- ", StringComparison.Ordinal)) line = "    •  " + line[2..];
            foreach (var part in Tokens.Split(line))
            {
                if (part.Length == 0) continue;
                Run run;
                if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4)
                    run = new Run(part[2..^2]) { FontWeight = FontWeight.Bold };
                else if (part.StartsWith("__") && part.EndsWith("__") && part.Length > 4)
                    run = new Run(part[2..^2]) { TextDecorations = TextDecorations.Underline };
                else if (part.StartsWith('*') && part.EndsWith('*') && part.Length > 2)
                    run = new Run(part[1..^1]) { FontStyle = FontStyle.Italic };
                else
                    run = new Run(part);
                if (quoted) run.Foreground = Quoted;
                inlines.Add(run);
            }
            if (i < lines.Length - 1) inlines.Add(new LineBreak());
        }
    }
}
