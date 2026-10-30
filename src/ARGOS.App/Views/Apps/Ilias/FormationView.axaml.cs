using Avalonia;
using Avalonia.Controls;

namespace Argos.Views.Apps.Ilias;

public partial class FormationView : UserControl
{
    public FormationView() => InitializeComponent();

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
