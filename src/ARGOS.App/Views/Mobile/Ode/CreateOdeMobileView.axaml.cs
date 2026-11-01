using Argos.Shared.Contracts;
using Argos.ViewModels.Ode;
using Avalonia.Controls;

namespace Argos.Views.Mobile.Ode;

/// <summary>Création de l'ordre de départ en version iPhone : la nature se choisit dans un panneau plein écran.</summary>
public partial class CreateOdeMobileView : UserControl
{
    public CreateOdeMobileView()
    {
        InitializeComponent();

        NaturePick.Click += (_, _) =>
        {
            PickerSearch.Text = string.Empty;
            RefreshPicker();
            PickerOverlay.IsVisible = true;
            PickerSearch.Focus();
        };
        PickerCancel.Click += (_, _) => ClosePicker();
        PickerSearch.TextChanged += (_, _) => RefreshPicker();
        PickerList.SelectionChanged += (_, _) =>
        {
            if (PickerList.SelectedItem is not string chosen) return;
            ClosePicker();
            if (DataContext is CreateOdeViewModel vm)
                vm.Nature = chosen;
        };
    }

    /// <summary>Natures de la catégorie choisie : tous les mots saisis, sans tenir compte des accents.</summary>
    private void RefreshPicker()
    {
        if (DataContext is not CreateOdeViewModel vm) return;
        var words = VdmsMap.Normalize(PickerSearch.Text ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        PickerList.ItemsSource = vm.NatureSuggestions
            .Where(item =>
            {
                var hay = VdmsMap.Normalize(item);
                return words.All(w => hay.Contains(w, StringComparison.Ordinal));
            })
            .ToList();
    }

    private void ClosePicker()
    {
        PickerOverlay.IsVisible = false;
        PickerList.ItemsSource = null;
    }
}
