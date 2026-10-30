using System.Globalization;
using System.Text;

namespace Argos.Shared.Contracts;

/// <summary>Dossiers de la messagerie.</summary>
public static class MailFolders
{
    public const string Reception = "reception";
    public const string Brouillons = "brouillons";
    public const string Envoyes = "envoyes";
    public const string Corbeille = "corbeille";

    public static readonly string[] All = [Reception, Brouillons, Envoyes, Corbeille];

    public static string Label(string folder) => folder switch
    {
        Reception => "Boîte de réception",
        Brouillons => "Brouillons",
        Envoyes => "Éléments envoyés",
        Corbeille => "Éléments supprimés",
        _ => folder
    };
}

public static class MailImportance
{
    public const string Normale = "Normale";
    public const string Haute = "Haute";
    public const string Basse = "Basse";
    public static readonly string[] All = [Normale, Haute, Basse];
}

/// <summary>Adresses électroniques (domaine de la Brigade).</summary>
public static class MailAddresses
{
    public const string Domain = "pompiersparis.fr";

    public static string Normalize(string? address) => (address ?? string.Empty).Trim().Trim('<', '>', ';', ',').ToLowerInvariant();

    /// <summary>prenom.nom@pompiersparis.fr (sans accents ni espaces).</summary>
    public static string Build(string? prenom, string? nom)
    {
        var p = Slug(prenom);
        var n = Slug(nom);
        var local = p.Length > 0 && n.Length > 0 ? p + "." + n : p + n;
        return local.Length == 0 ? string.Empty : local + "@" + Domain;
    }

    public static string Slug(string? text)
    {
        var builder = new StringBuilder();
        foreach (var ch in (text ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) builder.Append(char.ToLowerInvariant(ch));
            else if (ch is '-' or ' ' or '\'' or '.' && builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }
        return builder.ToString().Trim('-');
    }

    public static bool IsValid(string address)
    {
        var a = Normalize(address);
        var at = a.IndexOf('@');
        return at > 0 && at == a.LastIndexOf('@') && at < a.Length - 3 && a.IndexOf('.', at) > at + 1 && !a.Contains(' ');
    }

    /// <summary>Découpe « a@x; Nom &lt;b@y&gt;, c@z » en adresses.</summary>
    public static List<string> Split(string? text)
    {
        var result = new List<string>();
        foreach (var raw in (text ?? string.Empty).Split([';', ',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = raw;
            var lt = part.LastIndexOf('<');
            if (lt >= 0) part = part[(lt + 1)..];
            var a = Normalize(part);
            if (a.Length > 0 && !result.Contains(a)) result.Add(a);
        }
        return result;
    }
}

public sealed class MailAttachment
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
}

/// <summary>Un exemplaire de message dans la boîte d'un utilisateur (chaque destinataire a le sien).</summary>
public sealed class MailItem
{
    public string Id { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;              // identifiant Discord du propriétaire
    public string Folder { get; set; } = MailFolders.Reception;
    public string PreviousFolder { get; set; } = string.Empty;       // pour « Restaurer » depuis la corbeille
    public string MessageId { get; set; } = string.Empty;            // commun à tous les exemplaires
    public string From { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public List<string> To { get; set; } = [];
    public List<string> Cc { get; set; } = [];
    public List<string> Bcc { get; set; } = [];                      // seulement dans l'exemplaire de l'expéditeur
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;                 // **gras**, *italique*, __souligné__, « - » puces
    public string Importance { get; set; } = MailImportance.Normale;
    public List<MailAttachment> Attachments { get; set; } = [];
    public bool Read { get; set; }
    public bool Flagged { get; set; }
    public string InReplyTo { get; set; } = string.Empty;
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Entrée du carnet d'adresses personnel.</summary>
public sealed class MailContact
{
    public string Id { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

/// <summary>Entrée de l'annuaire (dossiers SIGTAO reliés à un joueur, et utilisateurs ARGOS connus).</summary>
public sealed class MailDirectoryEntry
{
    public string Address { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Grade { get; set; } = string.Empty;
    public string Fonction { get; set; } = string.Empty;
    public string Unite { get; set; } = string.Empty;
    public override string ToString() => $"{Name} <{Address}>";
}

/// <summary>Adresse attribuée à un utilisateur (mémorisée à sa première connexion à la messagerie).</summary>
public sealed class MailProfile
{
    public string UserId { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}

public sealed class MailSnapshot
{
    public string Address { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public List<MailItem> Items { get; set; } = [];
    public List<MailContact> Contacts { get; set; } = [];
    public List<MailDirectoryEntry> Directory { get; set; } = [];
    public DateTimeOffset ServerTime { get; set; }
}

public sealed class MailNewAttachment
{
    public string Name { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public string Data { get; set; } = string.Empty;                 // base 64
}

/// <summary>Brouillon à enregistrer ou message à envoyer.</summary>
public sealed class MailDraftRequest
{
    public string? DraftId { get; set; }
    public string To { get; set; } = string.Empty;
    public string Cc { get; set; } = string.Empty;
    public string Bcc { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Importance { get; set; } = MailImportance.Normale;
    public string InReplyTo { get; set; } = string.Empty;
    public List<string> KeepAttachments { get; set; } = [];          // pièces jointes déjà sur le serveur
    public List<MailNewAttachment> NewAttachments { get; set; } = [];
}

public sealed class MailDraftResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public MailItem? Item { get; set; }
}

public sealed class MailFlagRequest
{
    public bool? Read { get; set; }
    public bool? Flagged { get; set; }
    public string? Folder { get; set; }                              // déplacement (corbeille, restauration)
}

public sealed class MailProfileRequest
{
    public string Signature { get; set; } = string.Empty;
}

/// <summary>Préparation des réponses et transferts.</summary>
public static class MailCompose
{
    public static string ReplySubject(string subject)
        => subject.StartsWith("RE:", StringComparison.OrdinalIgnoreCase) ? subject : "RE: " + subject;

    public static string ForwardSubject(string subject)
        => subject.StartsWith("TR:", StringComparison.OrdinalIgnoreCase) ? subject : "TR: " + subject;

    public static string Quote(MailItem item)
    {
        var header = new StringBuilder()
            .Append("\n\n________________________________\n")
            .Append("De : ").Append(string.IsNullOrEmpty(item.FromName) ? item.From : $"{item.FromName} <{item.From}>").Append('\n')
            .Append("Envoyé : ").Append(item.SentAt?.ToLocalTime().ToString("dddd d MMMM yyyy HH:mm", CultureInfo.GetCultureInfo("fr-FR")) ?? "").Append('\n')
            .Append("À : ").Append(string.Join("; ", item.To)).Append('\n');
        if (item.Cc.Count > 0) header.Append("Cc : ").Append(string.Join("; ", item.Cc)).Append('\n');
        header.Append("Objet : ").Append(item.Subject).Append("\n\n").Append(item.Body);
        return header.ToString();
    }

    /// <summary>Destinataires de « Répondre à tous » : l'expéditeur, puis les autres (sans soi-même).</summary>
    public static (List<string> To, List<string> Cc) ReplyAll(MailItem item, string me)
    {
        var self = MailAddresses.Normalize(me);
        var to = new List<string> { item.From };
        to.AddRange(item.To);
        var cleanTo = to.Select(MailAddresses.Normalize).Where(a => a.Length > 0 && a != self).Distinct().ToList();
        var cc = item.Cc.Select(MailAddresses.Normalize).Where(a => a.Length > 0 && a != self && !cleanTo.Contains(a)).Distinct().ToList();
        return (cleanTo, cc);
    }
}
