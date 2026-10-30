using Avalonia;
using Avalonia.Controls;

namespace Argos.Views.Apps.Accessoires;

public partial class CorbeilleView : UserControl
{
    public CorbeilleView() => InitializeComponent();

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
