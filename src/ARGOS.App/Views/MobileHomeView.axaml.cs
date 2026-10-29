using System.ComponentModel;
using Argos.ViewModels;
using Avalonia.Controls;

namespace Argos.Views;

/// <summary>Interface téléphone : écran d'accueil avec les applications, chacune ouverte en plein écran.</summary>
public partial class MobileHomeView : UserControl
{
    private DesktopViewModel? _vm;

    public MobileHomeView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = DataContext as DesktopViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        UpdateBody();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesktopViewModel.MobileApp))
            UpdateBody();
    }

    private void UpdateBody()
        => MobileBody.Content = _vm?.MobileApp is { } page ? page.CreateContent() : null;
}
