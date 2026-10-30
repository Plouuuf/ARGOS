using System.Text.Json;

namespace Argos.Shared.Contracts;

/// <summary>Lecture du contenu (payload JSON) d'une fiche eFiBi enregistrée par le script FiveM.</summary>
public static class EfibiPayload
{
    /// <summary>
    /// Lit le formulaire enregistré par le script eFiBi : sections { title, controls[label, value, type], lists, choices }.
    /// Les cases non cochées sont omises ; un champ vide s'affiche « Non renseigné ».
    /// </summary>
    public static void Parse(string json, EfibiRecord record)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("metadata", out var meta) && meta.ValueKind == JsonValueKind.Object)
                foreach (var p in meta.EnumerateObject())
                    record.Metadata[p.Name] = Text(p.Value);

            if (!root.TryGetProperty("sections", out var sections) || sections.ValueKind != JsonValueKind.Object)
                return;
            foreach (var s in sections.EnumerateObject().OrderBy(s => Order(s.Name)))
            {
                var section = new EfibiSection
                {
                    Key = s.Name,
                    Title = s.Value.TryGetProperty("title", out var title) ? Text(title) : s.Name
                };
                if (s.Value.TryGetProperty("controls", out var controls) && controls.ValueKind == JsonValueKind.Array)
                {
                    var unnamed = 0;
                    foreach (var c in controls.EnumerateArray().OrderBy(c => c.TryGetProperty("index", out var i) && i.TryGetInt32(out var n) ? n : 0))
                    {
                        var label = c.TryGetProperty("label", out var l) ? Text(l).Trim().TrimEnd(':').Trim() : string.Empty;
                        var type = c.TryGetProperty("type", out var t) ? Text(t) : "text";
                        var value = c.TryGetProperty("value", out var v) ? v : default;
                        if (label.Length == 0)
                        {
                            // Champ sans libellé : identifiant technique du formulaire, ou « Champ n » (comme CORTEX).
                            label = c.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString() ?? "" : "";
                            if (label.Length == 0) label = $"Champ {++unnamed}";
                        }
                        if (type is "checkbox" or "radio")
                        {
                            // Case cochée = « Oui » ; une case décochée n'apporte rien et n'est pas affichée.
                            var on = c.TryGetProperty("checked", out var ch)
                                ? ch.ValueKind == JsonValueKind.True
                                : value.ValueKind == JsonValueKind.True || (value.ValueKind == JsonValueKind.String && value.GetString() is "true" or "on" or "1");
                            if (on) section.Fields.Add(new EfibiField { Label = label, Value = "Oui" });
                            continue;
                        }
                        var text = value.ValueKind == JsonValueKind.Undefined ? string.Empty : Text(value).Trim();
                        if (text.Length == 0 && IsTechnical(label)) continue;
                        section.Fields.Add(new EfibiField { Label = label, Value = text.Length == 0 ? EfibiText.NonRenseigne : text });
                    }
                }
                if (s.Value.TryGetProperty("lists", out var lists) && lists.ValueKind == JsonValueKind.Object)
                    foreach (var l in lists.EnumerateObject())
                    {
                        // Comme CORTEX : seules les listes renseignées sont affichées.
                        var items = l.Value.ValueKind == JsonValueKind.Array
                            ? l.Value.EnumerateArray().Select(Item).Where(x => x.Length > 0).ToList()
                            : [];
                        if (items.Count > 0) section.Lists.Add(new EfibiList { Name = EfibiText.ListName(l.Name), Items = items });
                    }
                if (s.Value.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
                {
                    var items = choices.EnumerateArray().Select(Item).Where(x => x.Length > 0).ToList();
                    if (items.Count > 0) section.Lists.Add(new EfibiList { Name = "Choix / sélections", Items = items });
                }
                record.Sections.Add(section);
            }
        }
        catch (JsonException)
        {
            record.Error = "Contenu de la fiche illisible.";
        }
    }

    private static bool IsTechnical(string label)
        => label.StartsWith("reg", StringComparison.OrdinalIgnoreCase)
           || label.StartsWith("startedAt", StringComparison.OrdinalIgnoreCase)
           || label.StartsWith("transmittedAt", StringComparison.OrdinalIgnoreCase);

    /// <summary>Valeur d'un champ du formulaire, cherchée par libellé dans toutes les rubriques (chef d'agrès, observations…).</summary>
    public static string ExtractControl(string? json, params string[] labels)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        static string Norm(string v) => v.Trim().ToUpperInvariant().Replace("É", "E").Replace("È", "E").Replace("Ê", "E")
            .Replace("À", "A").Replace("’", "'");
        var wanted = labels.Select(Norm).ToHashSet();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("sections", out var sections) || sections.ValueKind != JsonValueKind.Object) return string.Empty;
            foreach (var s in sections.EnumerateObject())
            {
                if (!s.Value.TryGetProperty("controls", out var controls) || controls.ValueKind != JsonValueKind.Array) continue;
                foreach (var c in controls.EnumerateArray())
                    if (c.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String && wanted.Contains(Norm(l.GetString() ?? ""))
                        && c.TryGetProperty("value", out var v) && Text(v).Trim() is { Length: > 0 } found)
                        return found;
            }
        }
        catch (JsonException)
        {
            // Contenu illisible : pas de valeur.
        }
        return string.Empty;
    }

    private static int Order(string key)
    {
        var index = EfibiText.SectionOrder.ToList().IndexOf(key);
        return index < 0 ? 100 : index;
    }

    private static string Item(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => string.Join(" - ", e.EnumerateObject().Select(p => Text(p.Value)).Where(s => s.Length > 0)),
        _ => Text(e)
    };

    private static string Text(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? string.Empty,
        JsonValueKind.Number => e.ToString(),
        JsonValueKind.True => "Oui",
        JsonValueKind.False => "Non",
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => e.ToString()
    };

}
