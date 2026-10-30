using Argos.Services;
using Argos.ViewModels;
using Argos.ViewModels.Mail;

namespace Argos.Views.Apps.Mail;

/// <summary>Fenêtres de la messagerie.</summary>
public static class MailWindows
{
    private static Avalonia.Media.Imaging.Bitmap Icon => ImageLoader.Asset("Icons/mail.png");

    public static WindowSpec Compose(DesktopViewModel desktop, MailComposeInit init)
    {
        var key = "mail-compose:" + (init.DraftId ?? Guid.NewGuid().ToString("N")[..8]);
        return new WindowSpec(key, string.IsNullOrWhiteSpace(init.Subject) ? "Nouveau message" : init.Subject, Icon,
            () => new MailComposeView { DataContext = new MailComposeViewModel(desktop, key, init) }, 900, 720);
    }
}
