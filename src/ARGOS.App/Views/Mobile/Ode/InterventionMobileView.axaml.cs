using Avalonia;
using Avalonia.Controls;

namespace Argos.Views.Mobile.Ode;

/// <summary>Suivi d'intervention ADAGIO ODE en version iPhone.</summary>
public partial class InterventionMobileView : UserControl
{
    public InterventionMobileView() => InitializeComponent();

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
