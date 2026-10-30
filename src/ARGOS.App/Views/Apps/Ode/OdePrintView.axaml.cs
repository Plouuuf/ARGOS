using Argos.ViewModels.Ode;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace Argos.Views.Apps.Ode;

/// <summary>Imprimante ADAGIO : la feuille affichée est photographiée pour l'export Discord.</summary>
public partial class OdePrintView : UserControl
{
    public OdePrintView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is OdePrintViewModel vm)
                vm.CaptureSheet = CaptureSheet;
        };
    }

    /// <summary>Image PNG de la feuille ODE, en double résolution pour rester lisible sur Discord.</summary>
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
