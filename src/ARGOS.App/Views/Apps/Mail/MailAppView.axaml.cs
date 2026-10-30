using Argos.ViewModels.Mail;
using Avalonia;
using Avalonia.Controls;

namespace Argos.Views.Apps.Mail;

public partial class MailAppView : UserControl
{
    public MailAppView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is MailAppViewModel vm && TopLevel.GetTopLevel(this) is { } top)
        {
            vm.Storage = top.StorageProvider;
            vm.Launcher = top.Launcher;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
