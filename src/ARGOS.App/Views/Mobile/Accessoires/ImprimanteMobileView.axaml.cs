using Argos.ViewModels.Accessoires;
using Avalonia;
using Avalonia.Controls;

namespace Argos.Views.Mobile.Accessoires;

/// <summary>Imprimante en version iPhone : liste des documents, puis le document choisi en plein écran.</summary>
public partial class ImprimanteMobileView : UserControl
{
    public ImprimanteMobileView()
    {
        InitializeComponent();
        BackToList.Click += (_, _) => (DataContext as ImprimanteViewModel)?.Select(null);
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
