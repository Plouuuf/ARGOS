using Avalonia;
using Avalonia.Controls;

namespace Argos.Views.Apps.Ode;

public partial class OdeAppView : UserControl
{
    public OdeAppView()
    {
        InitializeComponent();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
