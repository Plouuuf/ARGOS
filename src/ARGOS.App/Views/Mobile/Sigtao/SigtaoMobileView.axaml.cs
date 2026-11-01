using System.ComponentModel;
using System.Windows.Input;
using Argos.ViewModels.Sigtao;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Argos.Views.Mobile.Sigtao;

/// <summary>Personnel de garde un jour donné (planning en liste sur iPhone).</summary>
public sealed record PlanningAgendaEntry(string Grade, string Name, string Poste, string Tip, IBrush Background);

/// <summary>Un jour du planning en liste : en-tête, effectif, personnels et leurs postes.</summary>
public sealed record PlanningAgendaDay(string Header, string Top, string Bottom, IBrush DayBackground, string Effectif, string EffectifTip,
    IBrush EffectifForeground, List<PlanningAgendaEntry> Entries, string Compact, ICommand OpenCommand)
{
    public bool HasEntries => Entries.Count > 0;
}

/// <summary>
/// SIGTAO en version iPhone : onglets Accueil, Comptes-rendus, Garde, Planning, Annuaire
/// (gestion de la compagnie et historique depuis l'accueil). Le planning, tableau militaire × jour sur le bureau,
/// est présenté ici jour par jour à partir des mêmes données du modèle (PlanningDays, PlanningRows, PlanningTotals).
/// </summary>
public partial class SigtaoMobileView : UserControl
{
    private SigtaoAppViewModel? _vm;

    public SigtaoMobileView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as SigtaoAppViewModel;
            if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
            BuildAgenda();
        };
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        // PlanningTitle est notifié en dernier par la construction du planning.
        if (e.PropertyName is nameof(SigtaoAppViewModel.PlanningTitle) or nameof(SigtaoAppViewModel.IsPlanningMonth))
            BuildAgenda();
    }

    private void BuildAgenda()
    {
        var days = new List<PlanningAgendaDay>();
        if (_vm is { } vm)
        {
            for (var i = 0; i < vm.PlanningDays.Count; i++)
            {
                var day = vm.PlanningDays[i];
                var entries = new List<PlanningAgendaEntry>();
                foreach (var row in vm.PlanningRows)
                {
                    if (i >= row.Cells.Count) continue;
                    var cell = row.Cells[i];
                    if (string.IsNullOrEmpty(cell.Text)) continue;
                    entries.Add(new PlanningAgendaEntry(row.Grade, row.Name, cell.Text, cell.Tip, cell.Background));
                }
                var total = i < vm.PlanningTotals.Count ? vm.PlanningTotals[i] : null;
                var effectif = total?.Text ?? "—";
                var tip = total is null || effectif == "—" ? "Aucune feuille de garde" : total.Tip;
                var foreground = total?.Foreground ?? Brushes.Gray;
                var header = $"{day.Top} {day.Bottom}".ToUpperInvariant()
                             + (effectif == "—" ? string.Empty : $" · EFFECTIF {effectif}");
                var compact = entries.Count == 0
                    ? "Personne de garde"
                    : string.Join(" · ", entries.Select(e => $"{e.Grade} {e.Name} ({e.Poste})".Trim()));
                days.Add(new PlanningAgendaDay(header, day.Top, day.Bottom, day.Background, effectif, tip, foreground, entries, compact,
                    day.OpenCommand));
            }
        }
        PlanningWeekList.ItemsSource = days;
        PlanningMonthList.ItemsSource = days;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        (DataContext as IDisposable)?.Dispose();
    }
}
