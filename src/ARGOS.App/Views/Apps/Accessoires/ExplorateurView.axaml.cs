using Argos.ViewModels.Accessoires;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace Argos.Views.Apps.Accessoires;

public partial class ExplorateurView : UserControl
{
    public ExplorateurView()
    {
        InitializeComponent();
        ImportButton.Click += async (_, _) => await ImportAsync();
        DownloadButton.Click += async (_, _) => await DownloadAsync();
    }

    private ExplorateurViewModel? Vm => DataContext as ExplorateurViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Vm is { } vm && TopLevel.GetTopLevel(this) is { } top)
        {
            vm.Storage = top.StorageProvider;
            vm.Launcher = top.Launcher;
        }
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ExplorerItem item) Vm?.Open(item.Id);
    }

    private async Task ImportAsync()
    {
        if (Vm is not { } vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Importer dans Mes documents", AllowMultiple = true });
        foreach (var file in files)
        {
            try
            {
                await using var stream = await file.OpenReadAsync();
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                await vm.ImportAsync(file.Name, memory.ToArray());
            }
            catch (Exception ex)
            {
                vm.Show($"« {file.Name} » illisible : {ex.Message}", true);
            }
        }
    }

    private async Task DownloadAsync()
    {
        if (Vm is not { } vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        if (vm.Selected is not { IsFolder: false } entry)
        {
            vm.Show("Sélectionne un fichier à télécharger.", false);
            return;
        }
        var target = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Télécharger", SuggestedFileName = entry.Name });
        if (target is null) return;
        if (await vm.ExportAsync() is not { } file) return;
        await using var stream = await target.OpenWriteAsync();
        await stream.WriteAsync(file.Data);
        vm.Show($"« {file.Name} » téléchargé.", false);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
