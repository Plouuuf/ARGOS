using Argos.ViewModels.Mail;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Argos.Views.Mobile.Mail;

/// <summary>
/// Messagerie en version iPhone : boîtes aux lettres → liste des messages → lecture en plein écran (retour par CloseReading),
/// contacts et signature. La page affichée (dossiers, messages, contacts) est un état de la vue ; la lecture suit HasSelection.
/// </summary>
public partial class MailMobileView : UserControl
{
    private enum Page { Folders, Messages, Contacts }

    private Page _page = Page.Messages;      // comme sur iPhone : ouverture sur la boîte de réception
    private MailAppViewModel? _vm;

    public MailMobileView()
    {
        InitializeComponent();
        FoldersList.AddHandler(Button.ClickEvent, (_, _) => Show(Page.Messages), RoutingStrategies.Bubble, handledEventsToo: true);
        ContactsRow.Click += (_, _) => Show(Page.Contacts);
        BackToFolders.Click += (_, _) => Show(Page.Folders);
        BackFromContacts.Click += (_, _) => Show(Page.Folders);
        BackToList.Click += (_, _) => _vm?.CloseReading();
        // « Modifier » / « Ajouter aux contacts » : la fiche du contact est plus bas dans la page.
        ContactsPage.AddHandler(Button.ClickEvent, (_, e) =>
        {
            if (e.Source is Button { Tag: "edit" })
                Dispatcher.UIThread.Post(() => ContactForm.BringIntoView(), DispatcherPriority.Background);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as MailAppViewModel;
            if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
            UpdatePages();
        };
    }

    private void Show(Page page)
    {
        _page = page;
        UpdatePages();
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MailAppViewModel.HasSelection) or nameof(MailAppViewModel.Selected) or null or "")
            UpdatePages();
    }

    private bool _wasReading;

    private void UpdatePages()
    {
        var reading = _vm?.HasSelection == true;
        FoldersPage.IsVisible = !reading && _page == Page.Folders;
        MessagesPage.IsVisible = !reading && _page == Page.Messages;
        ContactsPage.IsVisible = !reading && _page == Page.Contacts;
        ReadingPage.IsVisible = reading;
        ListToolbar.IsVisible = !reading;
        ReadingToolbar.IsVisible = reading;
        if (reading && !_wasReading) ReadingPage.Offset = default;
        _wasReading = reading;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is MailAppViewModel vm && TopLevel.GetTopLevel(this) is { } top)
        {
            vm.Storage = top.StorageProvider;
            vm.Launcher = top.Launcher;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
