using Argos.ViewModels.Sigtao;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace Argos.Views.Apps.Sigtao;

/// <summary>Aperçu de la feuille de garde ; la feuille affichée est photographiée pour l'imprimante.</summary>
public partial class GardePrintView : UserControl
{
    public GardePrintView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is GardePrintViewModel vm)
                vm.CaptureSheet = CaptureSheet;
        };
    }

    private byte[]? CaptureSheet()
    {
        var size = Sheet.Bounds.Size;
        if (size.Width < 10 || size.Height < 10)
            return null;
        const double scale = 2;
        using var bitmap = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)),
            new Vector(96 * scale, 96 * scale));
        bitmap.Render(Sheet);
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }
}
