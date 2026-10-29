using Argos.Shared.Contracts;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Argos.ViewModels.Remise;

/// <summary>Couleurs des statuts d'engins (feuille GDM et listes).</summary>
public static class RemiseColors
{
    public static readonly IBrush Disponible = new SolidColorBrush(Color.Parse("#2E7D32"));
    public static readonly IBrush Engage = new SolidColorBrush(Color.Parse("#C62828"));
    public static readonly IBrush SurInter = new SolidColorBrush(Color.Parse("#E07B00"));
    public static readonly IBrush Indisponible = new SolidColorBrush(Color.Parse("#5F5F5F"));
    public static readonly IBrush Maintenance = new SolidColorBrush(Color.Parse("#B26A00"));
    public static readonly IBrush Alerte = new SolidColorBrush(Color.Parse("#C62828"));
    public static readonly IBrush Normal = new SolidColorBrush(Color.Parse("#1F3A52"));

    public static IBrush ForStatus(string status) => status switch
    {
        EnginStatuses.Disponible or EnginStatuses.Rentre => Disponible,
        EnginStatuses.DisponibleIntervention => SurInter,
        EnginStatuses.Indisponible => Indisponible,
        EnginStatuses.Maintenance => Maintenance,
        _ => Engage
    };
}

public static class RemiseText
{
    /// <summary>« Conforme » ou « 1 absent, 1 incomplet ».</summary>
    public static string Result(Verification v)
    {
        if (v.Conforme) return "Conforme";
        var parts = new List<string>();
        if (v.Missing > 0) parts.Add($"{v.Missing} absent(s)");
        if (v.Defective > 0) parts.Add($"{v.Defective} défectueux");
        var incomplete = v.Lines.Count(l => l.Result == CheckResults.Present && l.Counted < l.Required);
        if (incomplete > 0) parts.Add($"{incomplete} incomplet(s)");
        return string.Join(", ", parts);
    }
}

/// <summary>Vignette d'engin, comme sur la feuille GDM (photo, indicatif / immatriculation, type, rang, centre, statut).</summary>
public sealed record EnginTile(
    string Nom,
    string GdmTitle,
    string TypeLabel,
    string Rang,
    string Centre,
    Bitmap? Photo,
    string StatusLine,
    string Status,
    IBrush StatusBrush,
    string Revision,
    bool RevisionDue,
    string Affectation,
    RelayCommand OpenCommand)
{
    public bool HasPhoto => Photo is not null;
    public bool HasAffectation => !string.IsNullOrEmpty(Affectation);
}

public sealed record RevisionRow(string Engin, string Date, string Delay, bool Overdue, RelayCommand OpenCommand)
{
    public IBrush DelayBrush => Overdue ? RemiseColors.Alerte : RemiseColors.Normal;
}

public sealed record AnomalyRow(string Number, string Engin, string Date, string Immobilisation, string Description,
    string Status, string By, bool IsOpen, RelayCommand OpenCommand);

public sealed record StockAlertRow(string Name, string Category, string Stock, string Threshold);

public sealed record ItemRow(string Id, string Name, string Category, string Unit, int Stock, int Affected, int Required,
    int Missing, int Threshold, bool LowStock, bool Active, RelayCommand EditCommand)
{
    public bool HasMissing => Missing > 0;
}

public sealed record VerificationRow(string Date, string Engin, string By, string Result, bool Conforme, string Details);

public sealed record MovementRow(string Date, string By, string Item, string Kind, string Quantity, string Engin,
    string StockAfter, string Comment);

public sealed record DotationRow(string ItemId, string Name, string Category, int Required, int Present)
{
    public int Missing => Math.Max(0, Required - Present);
    public bool HasMissing => Missing > 0;
}

/// <summary>Ligne de vérification en cours de saisie.</summary>
public sealed class VerificationLineViewModel(string itemId, string name, int required) : ViewModelBase
{
    private string _result = CheckResults.Present;
    private string _counted = required.ToString();
    private string _observation = string.Empty;

    public string ItemId { get; } = itemId;
    public string Name { get; } = name;
    public int Required { get; } = required;
    public IReadOnlyList<string> Results => CheckResults.All;

    public string Result
    {
        get => _result;
        set
        {
            if (!SetProperty(ref _result, value ?? CheckResults.Present)) return;
            if (_result == CheckResults.Absent) Counted = "0";
            else if (Counted == "0") Counted = Required.ToString();
        }
    }

    public string Counted { get => _counted; set => SetProperty(ref _counted, value); }
    public string Observation { get => _observation; set => SetProperty(ref _observation, value); }

    public VerificationLine ToLine() => new()
    {
        ItemId = ItemId,
        Name = Name,
        Required = Required,
        Counted = int.TryParse(Counted, out var n) ? Math.Max(0, n) : Required,
        Result = Result,
        Observation = Observation
    };
}

/// <summary>Choix d'une référence de matériel dans une liste déroulante.</summary>
public sealed record ItemChoice(string Id, string Name, string Unit)
{
    public override string ToString() => Name;
}
