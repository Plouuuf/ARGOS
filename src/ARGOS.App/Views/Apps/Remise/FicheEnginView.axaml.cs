using Argos.ViewModels.Remise;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace Argos.Views.Apps.Remise;

/// <summary>Fiche engin : le choix de la photo passe par le sélecteur de fichiers du système.</summary>
public partial class FicheEnginView : UserControl
{
    public FicheEnginView()
    {
        InitializeComponent();
        PhotoButton.Click += async (_, _) => await PickPhotoAsync();
    }

    private async Task PickPhotoAsync()
    {
        if (DataContext is not FicheEnginViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Photo de l'engin",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll]
        });
        if (files.Count == 0) return;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            // Réduite à 480 px de large pour rester légère dans la base.
            using var bitmap = Bitmap.DecodeToWidth(stream, 480);
            using var output = new MemoryStream();
            bitmap.Save(output);
            vm.SetImage(Convert.ToBase64String(output.ToArray()));
        }
        catch
        {
            // Fichier illisible : on garde la photo actuelle.
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
