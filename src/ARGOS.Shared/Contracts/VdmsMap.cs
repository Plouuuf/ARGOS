using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Argos.Shared.Contracts;

/// <summary>Point GTA V en mètres (mêmes coordonnées que FiveM : GetEntityCoords).</summary>
public readonly record struct MapPoint(double X, double Y);

/// <summary>Quartier / commune du plan VDMS (un ou plusieurs polygones).</summary>
public sealed record MapDistrict(string Name, IReadOnlyList<IReadOnlyList<MapPoint>> Polygons)
{
    public bool Contains(MapPoint p) => Polygons.Any(poly => VdmsMap.InPolygon(poly, p));
}

/// <summary>Voie du plan VDMS, ex. « Avenue Bosquet » à L'Échat.</summary>
public sealed record MapStreet(string Name, string District, IReadOnlyList<IReadOnlyList<MapPoint>> Segments)
{
    /// <summary>Libellé de la liste de choix de la prise d'appel : « AVENUE BOSQUET - L'ÉCHAT ».</summary>
    public string Display => string.IsNullOrEmpty(District) ? Name.ToUpperInvariant() : $"{Name} - {District}".ToUpperInvariant();

    /// <summary>Milieu approximatif de la voie (premier tronçon).</summary>
    public MapPoint? Center
    {
        get
        {
            var seg = Segments.FirstOrDefault(s => s.Count > 0);
            return seg is null ? null : seg[seg.Count / 2];
        }
    }

    public override string ToString() => Display;
}

/// <summary>Résultat d'une localisation à partir d'une position GTA : voie la plus proche et quartier.</summary>
public sealed record MapLocation(MapStreet? Street, string District, double DistanceMeters);

/// <summary>
/// Plan des voies et quartiers du serveur VDMS (Data/voies.json, export de l'éditeur cartographique VDMS).
/// Sert à la saisie d'adresse de la prise d'appel et à retrouver une adresse depuis une position FiveM.
/// </summary>
public static class VdmsMap
{
    private sealed record Data(IReadOnlyList<MapDistrict> Districts, IReadOnlyList<MapStreet> Streets);

    private static readonly Lazy<Data> LazyData = new(Load);
    private static readonly CompareInfo Compare = CultureInfo.GetCultureInfo("fr-FR").CompareInfo;

    public static IReadOnlyList<MapDistrict> Districts => LazyData.Value.Districts;

    public static IReadOnlyList<MapStreet> Streets => LazyData.Value.Streets;

    /// <summary>Retrouve une voie à partir de ce qui a été saisi (« av bosquet », « AVENUE BOSQUET - L'ÉCHAT »…).</summary>
    public static MapStreet? FindStreet(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var key = Normalize(text);
        var exact = Streets.FirstOrDefault(s => Normalize(s.Display) == key || Normalize(s.Name) == key);
        if (exact is not null) return exact;
        // Saisie « nom - quartier » avec un quartier mal orthographié : on se contente du nom.
        var dash = text.LastIndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0)
        {
            var name = Normalize(text[..dash]);
            var byName = Streets.Where(s => Normalize(s.Name) == name).ToList();
            if (byName.Count == 1) return byName[0];
        }
        return null;
    }

    /// <summary>Suggestions pour une saisie partielle : tous les mots doivent apparaître (sans accents ni majuscules).</summary>
    public static IEnumerable<MapStreet> Search(string? text)
    {
        var words = Normalize(text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Streets.Where(s =>
        {
            var hay = Normalize(s.Display);
            return words.All(w => hay.Contains(w, StringComparison.Ordinal));
        });
    }

    public static MapDistrict? DistrictAt(MapPoint p) => Districts.FirstOrDefault(d => d.Contains(p));

    /// <summary>Voie la plus proche d'une position GTA (pour SIMCALL / eFiBi / position d'un engin).</summary>
    public static MapLocation Locate(MapPoint p)
    {
        MapStreet? best = null;
        var bestDistance = double.MaxValue;
        foreach (var street in Streets)
        foreach (var seg in street.Segments)
        for (var i = 1; i < seg.Count; i++)
        {
            var d = DistanceToSegment(p, seg[i - 1], seg[i]);
            if (d < bestDistance) { bestDistance = d; best = street; }
        }
        var district = DistrictAt(p)?.Name ?? best?.District ?? string.Empty;
        return new MapLocation(best, district, best is null ? 0 : bestDistance);
    }

    public static string Normalize(string text)
    {
        var decomposed = text.Replace('’', '\'').Replace('-', ' ').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) || c == '\'' ? char.ToLowerInvariant(c) : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    internal static bool InPolygon(IReadOnlyList<MapPoint> poly, MapPoint p)
    {
        var inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var (a, b) = (poly[i], poly[j]);
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    private static double DistanceToSegment(MapPoint p, MapPoint a, MapPoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        var len = dx * dx + dy * dy;
        var t = len == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len, 0, 1);
        double ex = a.X + t * dx - p.X, ey = a.Y + t * dy - p.Y;
        return Math.Sqrt(ex * ex + ey * ey);
    }

    private static Data Load()
    {
        using var stream = typeof(VdmsMap).Assembly.GetManifestResourceStream("ARGOS.voies.json")
            ?? throw new InvalidOperationException("Plan VDMS (voies.json) absent.");
        using var doc = JsonDocument.Parse(stream);
        static IReadOnlyList<MapPoint> Points(JsonElement e) =>
            e.EnumerateArray().Select(p => new MapPoint(p[0].GetDouble(), p[1].GetDouble())).ToList();

        var districts = doc.RootElement.GetProperty("districts").EnumerateArray()
            .Select(d => new MapDistrict(
                d.GetProperty("name").GetString() ?? string.Empty,
                d.GetProperty("polygons").EnumerateArray().Select(Points).ToList()))
            .ToList();

        var streets = new List<MapStreet>();
        foreach (var s in doc.RootElement.GetProperty("streets").EnumerateArray())
        {
            var name = s.GetProperty("name").GetString() ?? string.Empty;
            var district = s.GetProperty("district").GetString() ?? string.Empty;
            var segments = s.GetProperty("segments").EnumerateArray().Select(Points).ToList();
            if (string.IsNullOrEmpty(district))
            {
                // Voie tracée sans quartier : on prend le quartier qui contient son milieu.
                var mid = segments.FirstOrDefault(x => x.Count > 0) is { } seg ? seg[seg.Count / 2] : (MapPoint?)null;
                district = mid is { } m ? districts.FirstOrDefault(d => d.Contains(m))?.Name ?? string.Empty : string.Empty;
            }
            streets.Add(new MapStreet(name, district, segments));
        }
        streets.Sort((a, b) => Compare.Compare(a.Display, b.Display, CompareOptions.IgnoreCase));
        return new Data(districts, streets);
    }
}
