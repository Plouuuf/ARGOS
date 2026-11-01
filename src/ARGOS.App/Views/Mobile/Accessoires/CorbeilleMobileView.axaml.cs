using Avalonia;
using Avalonia.Controls;

namespace Argos.Views.Mobile.Accessoires;

/// <summary>Corbeille en version iPhone.</summary>
public partial class CorbeilleMobileView : UserControl
{
    public CorbeilleMobileView() => InitializeComponent();

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
