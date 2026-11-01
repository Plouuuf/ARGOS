using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Argos.Views.Mobile.Mco;

/// <summary>MCO en version iPhone : onglets Accueil, Interventions (ADAGIO ODE), Mains courantes.</summary>
public partial class McoMobileView : UserControl
{
    public McoMobileView() => InitializeComponent();

    private void OnShowHome(object? sender, RoutedEventArgs e) => Show(0);
    private void OnShowInterventions(object? sender, RoutedEventArgs e) => Show(1);
    private void OnShowReports(object? sender, RoutedEventArgs e) => Show(2);

    /// <summary>Le ViewModel n'a pas de navigation : les onglets sont gérés ici.</summary>
    private void Show(int page)
    {
        PageHome.IsVisible = page == 0;
        PageInterventions.IsVisible = page == 1;
        PageReports.IsVisible = page == 2;
        BtnHome.Classes.Set("selected", page == 0);
        BtnInterventions.Classes.Set("selected", page == 1);
        BtnReports.Classes.Set("selected", page == 2);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as System.IDisposable)?.Dispose();
    }
}
