using Argos.ViewModels.Mail;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Argos.Views.Apps.Mail;

public static class MailConverters
{
    public static readonly IValueConverter Bold = new FuncValueConverter<bool, FontWeight>(b => b ? FontWeight.SemiBold : FontWeight.Normal);
    public static readonly IValueConverter Accent = new FuncValueConverter<bool, IBrush>(b => b ? new SolidColorBrush(Color.Parse("#0078D4")) : new SolidColorBrush(Color.Parse("#201F1E")));
    public static readonly IValueConverter Initials = new FuncValueConverter<string?, string>(MailAppViewModel.Initials);
    public static readonly IValueConverter EditLabel = new FuncValueConverter<bool, string>(personal => personal ? "Modifier" : "Ajouter aux contacts");
}
