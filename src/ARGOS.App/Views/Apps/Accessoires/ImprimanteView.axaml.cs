using Argos.ViewModels.Accessoires;
using Avalonia;
using Avalonia.Controls;

namespace Argos.Views.Apps.Accessoires;

public partial class ImprimanteView : UserControl
{
    public ImprimanteView()
    {
        InitializeComponent();
        SaveButton.Click += async (_, _) =>
        {
            if (DataContext is ImprimanteViewModel vm) await vm.SaveAsync();
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is ImprimanteViewModel vm && TopLevel.GetTopLevel(this) is { } top) vm.Storage = top.StorageProvider;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
