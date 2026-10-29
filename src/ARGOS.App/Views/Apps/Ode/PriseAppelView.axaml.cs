using Argos.Shared.Contracts;
using Argos.ViewModels.Ode;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Argos.Views.Apps.Ode;

/// <summary>Prise d'appel : touches F1 à F9 au clavier, flèches des listes déroulantes.</summary>
public partial class PriseAppelView : UserControl
{
    public PriseAppelView()
    {
        InitializeComponent();
        StreetArrow.Click += (_, _) => OpenSuggestions(StreetBox);
        MotifArrow.Click += (_, _) => OpenSuggestions(MotifBox);

        // Voies : tous les mots saisis, sans tenir compte des accents (« bosq echat » → AVENUE BOSQUET - L'ÉCHAT).
        StreetBox.FilterMode = AutoCompleteFilterMode.Custom;
        StreetBox.ItemFilter = (search, item) =>
        {
            var hay = VdmsMap.Normalize(item?.ToString() ?? string.Empty);
            return VdmsMap.Normalize(search ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .All(w => hay.Contains(w, StringComparison.Ordinal));
        };

        // Les touches F sont interceptées avant les champs de saisie.
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
    }

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (DataContext is PriseAppelViewModel vm && e.Key is >= Key.F1 and <= Key.F9 && vm.HandleKey(e.Key.ToString()))
            e.Handled = true;
    }

    private static void OpenSuggestions(AutoCompleteBox box)
    {
        box.Focus();
        box.IsDropDownOpen = !box.IsDropDownOpen;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
