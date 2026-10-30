using System.Globalization;
using Argos.Services;
using Argos.Shared.Contracts;

namespace Argos.ViewModels.Sigtao;

public sealed record GardePrintLine(string Poste, string Nom);

public sealed record GardePrintEngin(string Titre, string Etat, bool Arme, List<GardePrintLine> Lignes);

public sealed record GardePrintGroup(string Title, List<GardePrintEngin> Engins);

/// <summary>Aperçu avant impression de la feuille de garde, envoyée à l'imprimante virtuelle du centre.</summary>
public sealed class GardePrintViewModel : ViewModelBase
{
    private readonly DesktopViewModel _desktop;
    private readonly string _key;
    private readonly FeuilleGarde _feuille;
    private string _status = "Aperçu de la feuille de garde.";
    private bool _busy;

    public GardePrintViewModel(DesktopViewModel desktop, string key, FeuilleGarde feuille)
    {
        _desktop = desktop;
        _key = key;
        _feuille = feuille;
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        var day = DateTime.TryParseExact(feuille.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTime.Today;
        var text = day.ToString("dddd d MMMM yyyy", fr);
        DateText = char.ToUpper(text[0], fr) + text[1..];
        DateShort = day.ToString("dd/MM/yyyy");
        Centre = feuille.CentresLabel;
        Horaires = feuille.Horaires;
        Service = feuille.Service.Select(a => new GardePrintLine(a.Poste, a.Pourvu ? a.Nom : "—")).ToList();
        Engins = feuille.Engins.Select(e => new GardePrintEngin(
            e.Engin, e.Arme ? $"Armé {e.Postes.Count(p => p.Pourvu)}/{e.Postes.Count}" : "NON ARMÉ", e.Arme,
            e.Postes.Select(p => new GardePrintLine(p.Poste, e.Arme && p.Pourvu ? p.Nom : "—")).ToList())).ToList();
        var types = feuille.Engins.ToDictionary(e => e.Engin, e => e.Type, StringComparer.OrdinalIgnoreCase);
        Groups = GardeCategories.All
            .Select(c => new GardePrintGroup(c, Engins.Where(e => GardeCategories.Of(types.GetValueOrDefault(e.Titre)) == c).ToList()))
            .Where(g => g.Engins.Count > 0).ToList();
        Consignes = GardeConsignes.All
            .Select(t => new GardePrintLine(t.ToUpperInvariant() + " :", feuille.Consignes.FirstOrDefault(c => c.Titre == t)?.Texte is { Length: > 0 } x ? x : "—"))
            .ToList();
        Reserve = feuille.Reserve.Where(r => r.Pourvu).Select(r => new GardePrintLine(r.Poste, r.Nom)).ToList();
        Observations = string.IsNullOrWhiteSpace(feuille.Observations) ? "—" : feuille.Observations.Trim();
        Effectif = $"Effectif de garde : {feuille.Effectif}";
        Visa = feuille.IsValidated
            ? $"Validée par {feuille.ValidatedBy} le {feuille.ValidatedAt?.ToLocalTime():dd/MM/yyyy à HH:mm}"
            : "Feuille non validée (en préparation)";
        PrintedAt = $"Édité le {DateTime.Now:dd/MM/yyyy à HH:mm} - ARGOS / SIGTAO";
        PrintCommand = new RelayCommand(() => _ = PrintAsync(), () => !_busy);
        CloseCommand = new RelayCommand(() => _desktop.CloseWindow(_key));
    }

    public string DateText { get; }
    public string DateShort { get; }
    public string Centre { get; }
    public string Horaires { get; }
    public List<GardePrintLine> Service { get; }
    public List<GardePrintEngin> Engins { get; }
    public List<GardePrintGroup> Groups { get; }
    public List<GardePrintLine> Consignes { get; }
    public List<GardePrintLine> Reserve { get; }
    public bool HasReserve => Reserve.Count > 0;
    public string Observations { get; }
    public string Effectif { get; }
    public string Visa { get; }
    public string PrintedAt { get; }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public RelayCommand PrintCommand { get; }
    public RelayCommand CloseCommand { get; }

    /// <summary>Fourni par la vue : image PNG de la feuille.</summary>
    public Func<byte[]?>? CaptureSheet { get; set; }

    private async Task PrintAsync()
    {
        var session = ArgosSession.Current;
        if (session is null || _busy) return;
        _busy = true;
        PrintCommand.RaiseCanExecuteChanged();
        try
        {
            string? image = null;
            try
            {
                if (CaptureSheet?.Invoke() is { Length: > 0 } png) image = Convert.ToBase64String(png);
            }
            catch
            {
                // Capture impossible : le document part sans image.
            }
            Status = "Impression en cours…";
            var result = await session.Api.SendAsync(HttpMethod.Post, "api/printer/submit", new PrintDocumentRequest
            {
                Kind = "Feuille de garde",
                Title = $"Feuille de garde du {DateShort}",
                ImagePng = image,
                GardeId = _feuille.Id
            });
            if (!result.Ok)
            {
                Status = result.Error ?? "Impression impossible.";
                return;
            }
            var discord = result.Id switch
            {
                PrintDiscord.Sent => " Publiée sur le salon Discord des gardes.",
                PrintDiscord.Failed => " Échec de la publication sur Discord.",
                _ => string.Empty
            };
            Status = $"Envoyé à l'imprimante du CS à {DateTime.Now:HH:mm:ss} (copie dans Mes documents › Impressions).{discord}";
            _desktop.ShowNotification("Imprimante", $"Feuille de garde du {DateShort} imprimée.{discord}");
            _ = session.Printer.RefreshAsync();
        }
        finally
        {
            _busy = false;
            PrintCommand.RaiseCanExecuteChanged();
        }
    }
}
