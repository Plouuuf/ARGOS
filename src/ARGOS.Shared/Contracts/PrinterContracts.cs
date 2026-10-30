namespace Argos.Shared.Contracts;

public static class PrintStatuses
{
    public const string EnAttente = "En attente";
    public const string EnCours = "Impression en cours";
    public const string Imprime = "Imprimé";
}

/// <summary>Document envoyé à l'imprimante virtuelle (ODE, et à terme tout autre document).</summary>
public sealed class PrintJob
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = "ODE";
    public string Title { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string Status { get; set; } = PrintStatuses.EnAttente;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PrintedAt { get; set; }
    public bool HasImage { get; set; }
    public long Size { get; set; }
    public string InterventionId { get; set; } = string.Empty;
    public string InterventionNumber { get; set; } = string.Empty;
    public int OrderNumber { get; set; }
    public bool Reprint { get; set; }
    /// <summary>Copie enregistrée dans « Mes documents › Impressions » de l'auteur.</summary>
    public string FileId { get; set; } = string.Empty;
}

public sealed class PrinterSnapshot
{
    public List<PrintJob> Jobs { get; set; } = [];
    public bool CanManageAll { get; set; }
}
