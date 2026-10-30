using Argos.ViewModels.Ilias;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Argos.Views.Apps.Ilias;

/// <summary>ILIAS : la vue fournit l'accès aux fichiers (ouverture des PDF, envoi de documents).</summary>
public partial class IliasAppView : UserControl
{
    public IliasAppView()
    {
        InitializeComponent();
        FileButton.Click += async (_, _) => await PickFileAsync();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is IliasAppViewModel vm && TopLevel.GetTopLevel(this) is { } top)
        {
            vm.Storage = top.StorageProvider;
            vm.Launcher = top.Launcher;
            IliasWindows.DocumentOpener = vm.OpenDocumentAsync;
        }
    }

    private async Task PickFileAsync()
    {
        if (DataContext is not IliasAppViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Document ILIAS",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }, FilePickerFileTypes.All]
        });
        if (files.Count == 0) return;
        await using var stream = await files[0].OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        var name = files[0].Name;
        vm.SetPendingFile(name, name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "application/pdf" : "application/octet-stream", memory.ToArray());
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
