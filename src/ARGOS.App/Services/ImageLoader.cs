using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Argos.Services;

/// <summary>Chargement des images intégrées (icônes, fonds) et des avatars Discord.</summary>
public static class ImageLoader
{
    private static readonly ConcurrentDictionary<string, Bitmap> Assets = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Bitmap> Avatars = new(StringComparer.Ordinal);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Image intégrée, par chemin relatif à Assets (ex. « Icons/mail.png »).</summary>
    public static Bitmap Asset(string relativePath)
        => Assets.GetOrAdd(relativePath, path =>
        {
            using var stream = AssetLoader.Open(new Uri($"avares://ARGOS.App/Assets/{path}"));
            return new Bitmap(stream);
        });

    private static readonly ConcurrentDictionary<int, Bitmap> Photos = new();

    /// <summary>Photo d'un engin : celle choisie dans ADAGIO REMISE, sinon celle de son type ; null si aucune.</summary>
    public static Bitmap? EnginPhoto(Argos.Shared.Contracts.FleetEngin engin)
    {
        if (!string.IsNullOrWhiteSpace(engin.ImageData))
        {
            try
            {
                return Photos.GetOrAdd(engin.ImageData.GetHashCode(StringComparison.Ordinal), _ =>
                {
                    using var stream = new MemoryStream(Convert.FromBase64String(engin.ImageData));
                    return new Bitmap(stream);
                });
            }
            catch
            {
                // Image illisible : on retombe sur la photo du type.
            }
        }
        var key = Argos.Shared.Contracts.RemiseSeed.PhotoKey(engin.Type);
        return key is null ? null : Asset($"Engins/{key}.jpg");
    }

    /// <summary>Insigne de grade BSPP (Assets/Grades), ou null.</summary>
    public static Bitmap? Grade(string? grade)
        => Argos.Shared.Contracts.BsppGrades.Code(grade) is { } code ? Asset($"Grades/{code}.png") : null;

    /// <summary>Avatar Discord ; null si l'adresse est vide ou l'image injoignable.</summary>
    public static async Task<Bitmap?> AvatarAsync(string? url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        if (Avatars.TryGetValue(url, out var cached))
            return cached;

        try
        {
            var bytes = await Http.GetByteArrayAsync(url, ct);
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            Avatars[url] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
