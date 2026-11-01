using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Argos.Views.Mobile;

/// <summary>
/// Propriétés d'une vue iPhone dédiée (racine des vues de Views/Mobile) :
/// IsNative : la page gère elle-même sa mise en page (pas de tiroir ni de défilement horizontal automatiques) ;
/// Accent   : couleur de l'application, reprise par la barre de navigation (bouton Retour).
/// </summary>
public static class MobilePage
{
    public static readonly AttachedProperty<bool> IsNativeProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsNative", typeof(MobilePage));

    public static readonly AttachedProperty<IBrush?> AccentProperty =
        AvaloniaProperty.RegisterAttached<Control, IBrush?>("Accent", typeof(MobilePage));

    public static bool GetIsNative(Control control) => control.GetValue(IsNativeProperty);
    public static void SetIsNative(Control control, bool value) => control.SetValue(IsNativeProperty, value);
    public static IBrush? GetAccent(Control control) => control.GetValue(AccentProperty);
    public static void SetAccent(Control control, IBrush? value) => control.SetValue(AccentProperty, value);
}
