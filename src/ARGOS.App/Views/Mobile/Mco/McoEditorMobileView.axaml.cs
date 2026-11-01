using System.ComponentModel;
using Argos.ViewModels.Mco;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Argos.Views.Mobile.Mco;

/// <summary>
/// Rédaction d'une MCO en version iPhone : les 7 pages du rapport (contrôle segmenté)
/// suivent la propriété Tab du ViewModel, comme les onglets de la version bureau.
/// </summary>
public partial class McoEditorMobileView : UserControl
{
    private static readonly string[] PageNames =
        ["Identification", "Horodatage", "Engin et personnel", "Déroulement", "Bilan", "Synthèse", "Validation"];

    private McoEditorViewModel? _vm;

    public McoEditorMobileView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as McoEditorViewModel);
    }

    private void Attach(McoEditorViewModel? vm)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm = vm;
        if (_vm is not null) _vm.PropertyChanged += OnViewModelPropertyChanged;
        UpdatePage();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(McoEditorViewModel.Tab))
            UpdatePage();
    }

    private void OnSegment(object? sender, RoutedEventArgs e)
    {
        if (_vm is not null && sender is Button { Tag: string tag } && int.TryParse(tag, out var index))
            _vm.Tab = index;
    }

    private void UpdatePage()
    {
        var tab = Math.Clamp(_vm?.Tab ?? 0, 0, PageNames.Length - 1);
        var pages = new Control[] { Page0, Page1, Page2, Page3, Page4, Page5, Page6 };
        var segments = new Button[] { Seg0, Seg1, Seg2, Seg3, Seg4, Seg5, Seg6 };
        for (var i = 0; i < pages.Length; i++)
        {
            pages[i].IsVisible = i == tab;
            segments[i].Classes.Set("selected", i == tab);
        }
        PageLabel.Text = $"Page {tab + 1} / {PageNames.Length} · {PageNames[tab]}";
        segments[tab].BringIntoView();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Attach(null);
        (DataContext as IDisposable)?.Dispose();
    }
}
