using Argos.ViewModels.Mail;
using Avalonia;
using Avalonia.Controls;

namespace Argos.Views.Apps.Mail;

public partial class MailAppView : UserControl
{
    // En dessous de cette largeur (téléphone), la liste et la lecture s'affichent l'une après l'autre.
    private const double SinglePaneWidth = 700;
    private MailAppViewModel? _vm;

    public MailAppView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdatePanes();
        BackToList.Click += (_, _) => _vm?.CloseReading();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as MailAppViewModel;
            if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
            UpdatePanes();
        };
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MailAppViewModel.HasSelection) or nameof(MailAppViewModel.Selected) or null or "")
            UpdatePanes();
    }

    private void UpdatePanes()
    {
        var single = Bounds.Width > 0 && Bounds.Width < SinglePaneWidth;
        var reading = _vm?.HasSelection == true;
        MailboxGrid.ColumnDefinitions[0].Width = !single ? new GridLength(360) : reading ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        MailboxGrid.ColumnDefinitions[1].Width = single && !reading ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        BackToList.IsVisible = single && reading;
        ReadingPane.IsVisible = !single || reading;
        ReadingScroll.Margin = single && reading ? new Thickness(0, 40, 0, 0) : default;
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
