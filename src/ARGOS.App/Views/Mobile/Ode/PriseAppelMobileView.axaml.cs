using Argos.Shared.Contracts;
using Argos.ViewModels.Ode;
using Avalonia;
using Avalonia.Controls;

namespace Argos.Views.Mobile.Ode;

/// <summary>
/// Prise d'appel en version iPhone : les touches F1 à F9 deviennent des lignes de procédure,
/// les listes déroulantes du bureau (plan VDMS, natures) s'ouvrent dans un panneau de choix plein écran.
/// </summary>
public partial class PriseAppelMobileView : UserControl
{
    private IReadOnlyList<string> _pickerSource = [];
    private Action<string>? _pick;

    public PriseAppelMobileView()
    {
        InitializeComponent();

        StreetPick.Click += (_, _) =>
        {
            if (DataContext is PriseAppelViewModel vm)
                OpenPicker("Voie, site, commune", vm.StreetSuggestions.ToList(), s => vm.Street = s);
        };
        MotifPick.Click += (_, _) =>
        {
            if (DataContext is PriseAppelViewModel vm)
                OpenPicker("Nature présumée", vm.MotifSuggestions.ToList(), s => vm.Motif = s);
        };
        FinalNaturePick.Click += (_, _) =>
        {
            if (DataContext is PriseAppelViewModel vm)
                OpenPicker("Nature définitive", vm.MotifSuggestions.ToList(), s => vm.FinalNature = s);
        };
        ClearFinalNature.Click += (_, _) =>
        {
            if (DataContext is PriseAppelViewModel vm)
                vm.FinalNature = string.Empty;
        };

        PickerCancel.Click += (_, _) => ClosePicker();
        PickerSearch.TextChanged += (_, _) => RefreshPicker();
        PickerList.SelectionChanged += (_, _) =>
        {
            if (PickerList.SelectedItem is not string chosen) return;
            var pick = _pick;
            ClosePicker();
            pick?.Invoke(chosen);
        };
    }

    private void OpenPicker(string title, IReadOnlyList<string> source, Action<string> pick)
    {
        PickerTitle.Text = title;
        _pickerSource = source;
        _pick = pick;
        PickerSearch.Text = string.Empty;
        RefreshPicker();
        PickerOverlay.IsVisible = true;
        PickerSearch.Focus();
    }

    /// <summary>Tous les mots saisis, sans tenir compte des accents (« bosq echat » → AVENUE BOSQUET - L'ÉCHAT).</summary>
    private void RefreshPicker()
    {
        var words = VdmsMap.Normalize(PickerSearch.Text ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        PickerList.ItemsSource = _pickerSource
            .Where(item =>
            {
                var hay = VdmsMap.Normalize(item);
                return words.All(w => hay.Contains(w, StringComparison.Ordinal));
            })
            .Take(300)
            .ToList();
    }

    private void ClosePicker()
    {
        _pick = null;
        PickerOverlay.IsVisible = false;
        PickerList.ItemsSource = null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
