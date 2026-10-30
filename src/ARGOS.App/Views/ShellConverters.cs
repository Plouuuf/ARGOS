using Avalonia.Data.Converters;

namespace Argos.Views;

public static class ShellConverters
{
    /// <summary>Calque visible (1) ou masqué (0), avec transition de fondu.</summary>
    public static readonly IValueConverter Opacity = new FuncValueConverter<bool, double>(visible => visible ? 1 : 0);
}
