using Avalonia;
using Avalonia.Controls;

namespace Argos.Views.Apps.Remise;

public partial class RemiseAppView : UserControl
{
    public RemiseAppView()
    {
        InitializeComponent();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
