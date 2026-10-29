using Avalonia;
using Avalonia.Controls;

namespace Argos.Views.Apps.Ode;

public partial class InterventionView : UserControl
{
    public InterventionView()
    {
        InitializeComponent();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
