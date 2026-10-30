using Avalonia;
using Avalonia.Controls;

namespace Argos.Views.Apps.Sigtao;

public partial class SigtaoAppView : UserControl
{
    public SigtaoAppView()
    {
        InitializeComponent();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
