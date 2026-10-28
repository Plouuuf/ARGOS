using System.ComponentModel;
using Argos.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Argos.Views;

/// <summary>
/// Racine d'ARGOS sur toutes les plateformes : fond d'écran + écran courant
/// (verrouillage, connexion, bienvenue, bureau ou accueil mobile).
/// </summary>
public partial class ShellView : UserControl
{
    // En dessous de cette largeur (plus petit côté de l'écran), l'interface téléphone remplace le bureau.
    private const double CompactThreshold = 600;

    private readonly ShellViewModel _vm;
    private IInsetsManager? _insets;

    public ShellView()
    {
        InitializeComponent();
        _vm = new ShellViewModel { OpenUrl = OpenUrlAsync };
        DataContext = _vm;

        SizeChanged += (_, e) => UpdateLayoutMode(e.NewSize);
        _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        var topLevel = TopLevel.GetTopLevel(this);
        _insets = topLevel?.InsetsManager;
        if (_insets is not null)
        {
            // Téléphone / tablette : le fond passe sous la barre d'état, le contenu reste dans la zone sûre.
            _insets.DisplayEdgeToEdgePreference = true;
            _insets.SafeAreaChanged += OnSafeAreaChanged;
            ApplySafeArea(_insets.SafeAreaPadding);
        }

        Focus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_insets is not null)
            _insets.SafeAreaChanged -= OnSafeAreaChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnSafeAreaChanged(object? sender, SafeAreaChangedArgs e) => ApplySafeArea(e.SafeAreaPadding);

    private void ApplySafeArea(Thickness padding) => SafeArea.Margin = padding;

    private void UpdateLayoutMode(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0)
            return;
        _vm.IsPortrait = size.Height > size.Width;
        _vm.IsCompact = Math.Min(size.Width, size.Height) < CompactThreshold;
        if (_vm.Desktop is not null)
            _vm.Desktop.IsCompactLayout = _vm.IsCompact;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.Desktop) && _vm.Desktop is not null)
            _vm.Desktop.IsCompactLayout = _vm.IsCompact;

        // Les touches du clavier doivent arriver à l'écran de verrouillage / connexion.
        if (e.PropertyName == nameof(ShellViewModel.Screen) && !_vm.IsDesktopScreen)
            Focus();
    }

    private async Task<bool> OpenUrlAsync(Uri uri)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return false;
        return await topLevel.Launcher.LaunchUriAsync(uri);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (_vm.Screen)
        {
            case ShellScreen.Lock:
                _vm.ShowLogin();
                e.Handled = true;
                return;
            case ShellScreen.Login when e.Key == Key.Escape:
                _vm.BackToLockCommand.Execute(null);
                e.Handled = true;
                return;
            case ShellScreen.Login when e.Key == Key.Enter && _vm.SignInCommand.CanExecute(null):
                _vm.SignInCommand.Execute(null);
                e.Handled = true;
                return;
        }
        base.OnKeyDown(e);
    }
}
