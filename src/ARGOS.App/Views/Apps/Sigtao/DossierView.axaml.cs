using Argos.ViewModels.Sigtao;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace Argos.Views.Apps.Sigtao;

/// <summary>Dossier militaire : choix de la photo d'identité par le sélecteur de fichiers.</summary>
public partial class DossierView : UserControl
{
    public DossierView()
    {
        InitializeComponent();
        PhotoButton.Click += async (_, _) => await PickPhotoAsync();
    }

    private async Task PickPhotoAsync()
    {
        if (DataContext is not DossierViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Photo d'identité", AllowMultiple = false, FileTypeFilter = [FilePickerFileTypes.ImageAll]
        });
        if (files.Count == 0) return;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var bitmap = Bitmap.DecodeToWidth(stream, 360);
            using var output = new MemoryStream();
            bitmap.Save(output);
            vm.SetPhoto(Convert.ToBase64String(output.ToArray()));
        }
        catch
        {
            // Image illisible : photo inchangée.
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
