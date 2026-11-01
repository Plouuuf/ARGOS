using Avalonia.Controls;

namespace Argos.Views.Mobile.Ode;

/// <summary>ADAGIO ODE en version iPhone : onglets Accueil, Appels, Suivi, Archives, Parc.</summary>
public partial class OdeMobileView : UserControl
{
    public OdeMobileView() => InitializeComponent();

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as System.IDisposable)?.Dispose();
    }
}
