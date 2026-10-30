using Avalonia;
using Avalonia.Controls;

namespace Argos.Views.Apps.Mco;

public partial class McoAppView : UserControl
{
    public McoAppView()
    {
        InitializeComponent();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
