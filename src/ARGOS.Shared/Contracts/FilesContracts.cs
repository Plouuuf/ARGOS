namespace Argos.Shared.Contracts;

/// <summary>Fichier ou dossier de « Mes documents » (espace personnel de chaque utilisateur, sur le serveur).</summary>
public sealed class FileEntry
{
    public string Id { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string ParentId { get; set; } = string.Empty;            // vide = racine « Mes documents »
    public string Name { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public long Size { get; set; }
    public string ContentType { get; set; } = "application/octet-stream";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>Dans la corbeille (seul l'élément supprimé y apparaît ; son contenu le suit).</summary>
    public bool Deleted { get; set; }
    public bool DeletedWithParent { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class FilesSnapshot
{
    public List<FileEntry> Entries { get; set; } = [];
    public long Used { get; set; }
    public long Quota { get; set; }
}

public sealed class FileFolderRequest
{
    public string ParentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class FileUploadRequest
{
    public string ParentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public string Data { get; set; } = string.Empty;                // base 64
    /// <summary>Remplace le contenu de ce fichier (Bloc-notes : « Enregistrer »).</summary>
    public string? ReplaceId { get; set; }
}

public sealed class FileRenameRequest
{
    public string Name { get; set; } = string.Empty;
}

public sealed class FileMoveRequest
{
    public string ParentId { get; set; } = string.Empty;
}

public static class FileKinds
{
    private static readonly string[] TextExtensions = [".txt", ".md", ".log", ".csv", ".json", ".xml", ".ini", ".cfg", ".lua"];

    public static bool IsText(string name) => TextExtensions.Contains(Path.GetExtension(name).ToLowerInvariant());

    public static string Label(FileEntry e) => e.IsFolder ? "Dossier de fichiers" : Path.GetExtension(e.Name).ToLowerInvariant() switch
    {
        ".txt" => "Document texte",
        ".pdf" => "Document PDF",
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" => "Image",
        ".doc" or ".docx" => "Document Word",
        ".xls" or ".xlsx" or ".csv" => "Feuille de calcul",
        ".zip" => "Dossier compressé",
        "" => "Fichier",
        var ext => "Fichier " + ext.TrimStart('.').ToUpperInvariant()
    };

    public static string ContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".txt" or ".log" or ".md" or ".ini" or ".cfg" or ".lua" => "text/plain",
        ".csv" => "text/csv",
        ".json" => "application/json",
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".zip" => "application/zip",
        _ => "application/octet-stream"
    };

    /// <summary>Nom valide pour Windows (sans \ / : * ? " &lt; &gt; |), 120 caractères au plus.</summary>
    public static string? CleanName(string? name)
    {
        var clean = new string((name ?? string.Empty).Where(c => !"\\/:*?\"<>|".Contains(c) && !char.IsControl(c)).ToArray()).Trim().TrimEnd('.');
        if (clean.Length > 120) clean = clean[..120];
        return clean.Length == 0 ? null : clean;
    }
}
