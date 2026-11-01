using Argos.ViewModels.Accessoires;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Argos.Views.Mobile.Accessoires;

/// <summary>Explorateur en version iPhone (app Fichiers) : import depuis l'appareil et téléchargement, comme la version bureau.</summary>
public partial class ExplorateurMobileView : UserControl
{
    private ExplorateurViewModel? _vm;

    public ExplorateurMobileView()
    {
        InitializeComponent();
        ImportButton.Click += async (_, _) => await ImportAsync();
        ImportTab.Click += async (_, _) => await ImportAsync();
        DownloadButton.Click += async (_, _) => await DownloadAsync();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as ExplorateurViewModel;
            if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
        };
    }

    private ExplorateurViewModel? Vm => DataContext as ExplorateurViewModel;

    // Renommage : le champ prend le focus et son texte est sélectionné.
    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ExplorateurViewModel.IsRenaming) || _vm?.IsRenaming != true) return;
        Dispatcher.UIThread.Post(() =>
        {
            RenameBox.BringIntoView();
            RenameBox.Focus();
            RenameBox.SelectAll();
        }, DispatcherPriority.Background);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Vm is { } vm && TopLevel.GetTopLevel(this) is { } top)
        {
            vm.Storage = top.StorageProvider;
            vm.Launcher = top.Launcher;
        }
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
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        (DataContext as IDisposable)?.Dispose();
    }
}
