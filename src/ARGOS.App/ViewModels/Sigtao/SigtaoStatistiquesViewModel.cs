using System.Collections.ObjectModel;
using Argos.Services;
using Argos.Shared.Contracts;
using Argos.ViewModels.Ode;
using Argos.Views.Apps.Sigtao;
using Avalonia.Media.Imaging;

namespace Argos.ViewModels.Sigtao;

/// <summary>Une ligne du classement de présence aux gardes.</summary>
public sealed record StatistiqueRow(string MilitaryId, string Rang, string Grade, string GradeShort, string Name, int Gardes, int Max,
    string DerniereGarde, string Poste, RelayCommand OpenCommand)
{
    public Bitmap? GradeIcon => ImageLoader.Grade(Grade);
    /// <summary>Aucune garde sur la période (militaire à relancer).</summary>
    public bool IsAbsent => Gardes == 0;
    public bool IsPodium => Rang is "1" or "2" or "3";
    public string GardesText => Gardes switch { 0 => "0 garde", 1 => "1 garde", _ => $"{Gardes} gardes" };
    /// <summary>iPhone : « 12 gardes · C/A VSAV ».</summary>
    public string Resume => Poste == "—" ? GardesText : $"{GardesText} · {Poste}";
    public string DerniereGardeLong => DerniereGarde == "—" ? "Aucune garde validée" : "Dernière garde : " + DerniereGarde;
}

/// <summary>Statistiques : présence aux gardes (feuilles validées) sur 7, 30 ou 90 jours, ou depuis toujours.</summary>
public sealed partial class SigtaoAppViewModel
{
    private int _statsJours = GardeStatistiquesPeriodes.Defaut;
    private SigtaoStatistiques? _stats;
    private bool _statsLoading;
    private bool _statsLocal;
    private int _statsRequest;

    public ObservableCollection<StatistiqueRow> StatsRows { get; } = [];

    /// <summary>Paramètre : « 7 », « 30 », « 90 » ou « 0 » (depuis toujours).</summary>
    public RelayCommand StatsPeriodCommand { get; private set; } = null!;

    public int StatsJours => _statsJours;
    public bool IsStats7 => _statsJours == 7;
    public bool IsStats30 => _statsJours == 30;
    public bool IsStats90 => _statsJours == 90;
    public bool IsStatsAll => _statsJours == GardeStatistiquesPeriodes.DepuisToujours;

    public bool StatsLoading { get => _statsLoading; private set => SetProperty(ref _statsLoading, value); }
    public bool HasStats => _stats is not null;
    public bool HasStatsRows => StatsRows.Count > 0;

    public string StatsGardes => _stats?.GardesValidees.ToString() ?? "—";
    public string StatsPresents => _stats?.MilitairesPresents.ToString() ?? "—";
    public string StatsMoyenne => _stats is null ? "—" : _stats.MoyennePresents.ToString("0.#", OdeFormat.French);
    public string StatsAbsents => _stats is null ? "—" : _stats.Classement.Count(c => c.Gardes == 0).ToString();

    /// <summary>« Du 22/10/2026 au 20/11/2026 · feuilles de garde validées ».</summary>
    public string StatsPeriodLabel
    {
        get
        {
            if (_stats is null) return StatsLoading ? "Calcul en cours…" : "Statistiques non chargées.";
            var au = DateText(_stats.Au);
            var texte = _stats.Du.Length == 0 ? $"Depuis toujours, jusqu'au {au}" : $"Du {DateText(_stats.Du)} au {au}";
            return texte + " · feuilles de garde validées" + (_statsLocal ? " (calcul local sur les feuilles chargées, serveur indisponible)" : string.Empty);
        }
    }

    private void InitStatistiques()
    {
        StatsPeriodCommand = new RelayCommand(p =>
        {
            if (p is string s && int.TryParse(s, out var jours)) SetStatsPeriod(jours);
        });
    }

    private void SetStatsPeriod(int jours)
    {
        jours = GardeStatistiquesPeriodes.Normalize(jours);
        if (_statsJours == jours) return;
        _statsJours = jours;
        foreach (var n in new[] { nameof(StatsJours), nameof(IsStats7), nameof(IsStats30), nameof(IsStats90), nameof(IsStatsAll) })
            OnPropertyChanged(n);
        _ = LoadStatistiquesAsync();
    }

    /// <summary>Calcul serveur (toutes les feuilles validées) ; repli sur les feuilles de l'instantané si le serveur ne répond pas.</summary>
    private async Task LoadStatistiquesAsync()
    {
        var session = ArgosSession.Current;
        if (session is null) return;
        var request = ++_statsRequest;
        var jours = _statsJours;
        StatsLoading = true;
        OnPropertyChanged(nameof(StatsPeriodLabel));
        try
        {
            var stats = await session.Api.GetAsync<SigtaoStatistiques>($"api/sigtao/statistiques?jours={jours}");
            if (request == _statsRequest) SetStatistiques(stats, local: false);
        }
        catch (Exception ex)
        {
            if (request != _statsRequest) return;
            SetStatistiques(GardeStatistiquesCalcul.Compute(Snapshot.Military, Snapshot.Gardes, jours, DateTime.Today), local: true);
            ShowMessage("Statistiques du serveur indisponibles : " + ex.Message, true);
        }
        finally
        {
            if (request == _statsRequest) StatsLoading = false;
        }
    }

    /// <summary>Affiche des statistiques déjà calculées (réponse du serveur ; utilisé aussi par les captures d'écran).</summary>
    public void ApplyStatistiques(SigtaoStatistiques stats)
    {
        _statsRequest++;                     // une réponse plus ancienne ne remplacera pas celles-ci
        StatsLoading = false;
        if (GardeStatistiquesPeriodes.Normalize(stats.Jours) != _statsJours)
        {
            _statsJours = GardeStatistiquesPeriodes.Normalize(stats.Jours);
            foreach (var n in new[] { nameof(StatsJours), nameof(IsStats7), nameof(IsStats30), nameof(IsStats90), nameof(IsStatsAll) })
                OnPropertyChanged(n);
        }
        SetStatistiques(stats, local: false);
    }

    private void SetStatistiques(SigtaoStatistiques stats, bool local)
    {
        _stats = stats;
        _statsLocal = local;
        var max = Math.Max(1, stats.Classement.Select(c => c.Gardes).DefaultIfEmpty().Max());
        Fill(StatsRows, stats.Classement.Select(c =>
        {
            var id = c.MilitaryId;
            var name = $"{c.Nom.ToUpperInvariant()} {c.Prenom}".Trim();
            return new StatistiqueRow(c.MilitaryId, c.Rang > 0 ? c.Rang.ToString() : "—", c.Grade, (BsppGrades.Short(c.Grade) + c.Echelon).Trim(),
                name.Length == 0 ? "(nom à la 1re connexion)" : name, c.Gardes, max, DateText(c.DerniereGarde),
                c.PosteFrequent.Length == 0 ? "—" : $"{c.PosteFrequent} ({c.PosteFrequentFois}×)",
                new RelayCommand(() => _desktop.OpenWindow(SigtaoWindows.Dossier(_desktop, id))));
        }));
        foreach (var n in new[] { nameof(HasStats), nameof(HasStatsRows), nameof(StatsGardes), nameof(StatsPresents), nameof(StatsMoyenne),
                                  nameof(StatsAbsents), nameof(StatsPeriodLabel) })
            OnPropertyChanged(n);
    }

    /// <summary>« 2026-11-20 » → « 20/11/2026 » ; vide → « — ».</summary>
    private static string DateText(string? date)
        => DateTime.TryParseExact(date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d)
            ? d.ToString("dd/MM/yyyy", OdeFormat.French)
            : "—";
}
