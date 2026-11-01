using Argos.ViewModels.Accessoires;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Argos.Views.Mobile.Accessoires;

/// <summary>
/// Paramètres en version iPhone (app Réglages) : liste des rubriques, puis la rubrique choisie (NavigateCommand du modèle).
/// Ouvert sur une rubrique précise (ParametresViewModel.RequestedPage), la page s'affiche directement.
/// </summary>
public partial class ParametresMobileView : UserControl
{
    private bool _detail;

    public ParametresMobileView()
    {
        InitializeComponent();
        NavGroup.AddHandler(Button.ClickEvent, (_, _) => ShowDetail(true), RoutingStrategies.Bubble, handledEventsToo: true);
        AccountRow.Click += (_, _) => ShowDetail(true);
        BackToRoot.Click += (_, _) => ShowDetail(false);
        DataContextChanged += (_, _) =>
            ShowDetail(DataContext is ParametresViewModel vm && vm.Page.Id != vm.Pages[0].Id);
    }

    private void ShowDetail(bool detail)
    {
        _detail = detail;
        RootPage.IsVisible = !_detail;
        DetailPage.IsVisible = _detail;
        if (_detail) DetailPage.Offset = default;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
