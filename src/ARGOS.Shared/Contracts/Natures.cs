using System.Text.Json;

namespace Argos.Shared.Contracts;

/// <summary>Nature d'intervention (code BSPP), ex. 117 « Feu ou explosion en chantier souterrain ».</summary>
public sealed record NatureDefinition(string Code, string Label, string CategoryCode, string CategoryLabel)
{
    /// <summary>« 117 - Feu ou explosion en chantier souterrain », comme sur l'ODE.</summary>
    public string Display => $"{Code} - {Label}";

    public override string ToString() => Display;
}

public sealed record NatureCategory(string Code, string Label, IReadOnlyList<NatureDefinition> Items);

/// <summary>Liste des natures d'intervention (Data/natures.json, fournie pour le RP VDMS).</summary>
public static class Natures
{
    private static readonly Lazy<IReadOnlyList<NatureCategory>> LazyCategories = new(Load);

    public static IReadOnlyList<NatureCategory> Categories => LazyCategories.Value;

    public static IReadOnlyList<NatureDefinition> All => Categories.SelectMany(c => c.Items).ToList();

    public static NatureDefinition? Find(string? code)
        => string.IsNullOrWhiteSpace(code)
            ? null
            : All.FirstOrDefault(n => string.Equals(n.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Titre de la feuille de départ selon la catégorie : « PREMIERS DEPARTS - INCENDIE ».</summary>
    public static string DepartTitle(string? natureCode)
    {
        var category = Find(natureCode)?.CategoryCode;
        var label = category switch
        {
            "1" => "INCENDIE",
            "2" => "ACCIDENT",
            "3" => "SECOURS A VICTIME",
            "4" => "PROTECTION DES PERSONNES",
            "5" => "ANIMAUX",
            "6" => "EAU GAZ ELECTRICITE",
            "7" => "PROTECTION DES BIENS",
            "8" => "NRBC POLLUTION",
            "9" => "RECONNAISSANCE",
            "A" => "AUTRES",
            _ => "OPERATIONS DIVERSES"
        };
        return "PREMIERS DEPARTS - " + label;
    }

    private static IReadOnlyList<NatureCategory> Load()
    {
        using var stream = typeof(Natures).Assembly.GetManifestResourceStream("ARGOS.natures.json")
                           ?? throw new InvalidOperationException("Liste des natures introuvable.");
        using var doc = JsonDocument.Parse(stream);
        var result = new List<NatureCategory>();
        foreach (var cat in doc.RootElement.GetProperty("categories").EnumerateArray())
        {
            var code = cat.GetProperty("code").GetString() ?? string.Empty;
            var label = cat.GetProperty("label").GetString() ?? string.Empty;
            var items = cat.GetProperty("items").EnumerateArray()
                .Select(i => new NatureDefinition(
                    i.GetProperty("code").GetString() ?? string.Empty,
                    i.GetProperty("label").GetString() ?? string.Empty,
                    code,
                    label))
                .ToList();
            result.Add(new NatureCategory(code, label, items));
        }
        return result;
    }
}
