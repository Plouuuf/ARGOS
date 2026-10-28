using Argos.ViewModels;
using Avalonia.Controls;

namespace Argos.Views.Apps;

/// <summary>
/// Crée le contenu d'une application (dans une fenêtre du bureau ou en plein écran sur téléphone).
/// Chaque application aura sa propre vue au fil des étapes ; en attendant, une page provisoire.
/// </summary>
public static class AppContentFactory
{
    public static Control Create(AppTileViewModel app) => app.Id switch
    {
        _ => new PlaceholderAppView { DataContext = app }
    };
}
