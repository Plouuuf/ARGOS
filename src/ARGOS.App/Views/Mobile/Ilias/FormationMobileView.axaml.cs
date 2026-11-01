using Avalonia;
using Avalonia.Controls;

namespace Argos.Views.Mobile.Ilias;

/// <summary>Formation ILIAS en version iPhone : progression, objectifs, prérequis, modules, évaluation finale.</summary>
public partial class FormationMobileView : UserControl
{
    public FormationMobileView() => InitializeComponent();

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
