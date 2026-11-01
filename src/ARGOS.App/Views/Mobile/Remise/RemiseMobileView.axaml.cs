using System.ComponentModel;
using Argos.ViewModels.Remise;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Argos.Views.Mobile.Remise;

/// <summary>
/// ADAGIO REMISE en version iPhone : onglets Accueil, Parc, Vérifier, Maintenance, Stocks
/// (Inventaire et Historique dans le groupe PLUS de l'accueil).
/// Le détail d'une maintenance et l'édition d'une référence s'affichent en haut de leur page : on y remonte.
/// </summary>
public partial class RemiseMobileView : UserControl
{
    private RemiseAppViewModel? _vm;
    private string? _selectedId;

    public RemiseMobileView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as RemiseAppViewModel);
    }

    private void Attach(RemiseAppViewModel? vm)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelChanged;
        _vm = vm;
        _selectedId = vm?.SelectedMaintenance?.Id;
        if (_vm is not null)
            _vm.PropertyChanged += OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm is null) return;
        if (e.PropertyName == nameof(RemiseAppViewModel.SelectedMaintenance))
        {
            var id = _vm.SelectedMaintenance?.Id;
            if (id is not null && id != _selectedId)
                Dispatcher.UIThread.Post(() => MaintenanceScroll.ScrollToHome());
            _selectedId = id;
        }
        else if (e.PropertyName == nameof(RemiseAppViewModel.EditIsNew))
        {
            // Signalé à chaque ouverture de l'édition (nouvelle référence ou « Modifier »).
            Dispatcher.UIThread.Post(() => InventaireScroll.ScrollToHome());
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Attach(null);
        (DataContext as IDisposable)?.Dispose();
    }
}
