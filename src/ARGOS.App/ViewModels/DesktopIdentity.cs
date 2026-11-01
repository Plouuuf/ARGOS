using Argos.Services;
using Argos.Shared.Contracts;

namespace Argos.ViewModels;

/// <summary>
/// Identité de session : formulaire « Prénom / NOM » à la première connexion,
/// encart en bas à droite de l'accueil (prénom NOM, adresse BSPP).
/// </summary>
public sealed partial class DesktopViewModel
{
    private string _identityPrenom = string.Empty;
    private string _identityNom = string.Empty;
    private string _sessionName = string.Empty;
    private string _sessionEmail = string.Empty;
    private bool _needsIdentity;
    private string? _identityError;
    private bool _identityBusy;

    /// <summary>Vrai tant que l'utilisateur n'a pas saisi son prénom et son NOM.</summary>
    public bool NeedsIdentity { get => _needsIdentity; private set => SetProperty(ref _needsIdentity, value); }

    /// <summary>Prénom : initiale de chaque partie en majuscule, au fil de la saisie.</summary>
    public string IdentityPrenom
    {
        get => _identityPrenom;
        set
        {
            var formatted = UserIdentity.FormatPrenom(value);
            // Les espaces/tirets en fin de saisie sont conservés pour permettre « Jean-… ».
            if (value is { Length: > 0 } && value.EndsWith(' ') ) formatted += " ";
            if (value is { Length: > 0 } && value.EndsWith('-')) formatted = formatted.TrimEnd('-') + "-";
            SetProperty(ref _identityPrenom, formatted);
            SaveIdentityCommand?.RaiseCanExecuteChanged();
        }
    }

    /// <summary>NOM : tout en majuscules, au fil de la saisie.</summary>
    public string IdentityNom
    {
        get => _identityNom;
        set
        {
            var formatted = (value ?? string.Empty).ToUpper(System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
            SetProperty(ref _identityNom, formatted);
            SaveIdentityCommand?.RaiseCanExecuteChanged();
        }
    }

    public string? IdentityError { get => _identityError; private set { if (SetProperty(ref _identityError, value)) OnPropertyChanged(nameof(HasIdentityError)); } }
    public bool HasIdentityError => !string.IsNullOrWhiteSpace(IdentityError);

    public string SessionName { get => _sessionName; private set { if (SetProperty(ref _sessionName, value)) OnPropertyChanged(nameof(HasSessionInfo)); } }
    public string SessionEmail { get => _sessionEmail; private set => SetProperty(ref _sessionEmail, value); }
    public bool HasSessionInfo => SessionName.Length > 0;

    public RelayCommand? SaveIdentityCommand { get; private set; }

    private void InitIdentity()
    {
        SaveIdentityCommand = new RelayCommand(() => _ = SaveIdentityAsync(),
            () => !_identityBusy && UserIdentity.FormatPrenom(_identityPrenom).Length > 0 && UserIdentity.FormatNom(_identityNom).Length > 0);
        SessionName = _displayName;
        _ = LoadIdentityAsync();
    }

    private async Task LoadIdentityAsync()
    {
        var session = ArgosSession.Current;
        if (session is null) return;
        try
        {
            var identity = await session.Api.GetAsync<UserIdentity>("api/me/identity");
            ApplyIdentity(identity);
            NeedsIdentity = !identity.Complete;
        }
        catch
        {
            // Serveur ancien ou injoignable : pas de formulaire, l'encart garde le pseudo Discord.
        }
    }

    private void ApplyIdentity(UserIdentity identity)
    {
        if (identity.Complete) SessionName = identity.FullName;
        SessionEmail = identity.Email;
    }

    private async Task SaveIdentityAsync()
    {
        var session = ArgosSession.Current;
        if (session is null || _identityBusy) return;
        _identityBusy = true;
        SaveIdentityCommand?.RaiseCanExecuteChanged();
        try
        {
            var result = await session.Api.SendAsync(HttpMethod.Post, "api/me/identity",
                new UserIdentity { Prenom = _identityPrenom, Nom = _identityNom });
            if (!result.Ok)
            {
                IdentityError = result.Error ?? "Enregistrement impossible.";
                return;
            }
            IdentityError = null;
            var identity = await session.Api.GetAsync<UserIdentity>("api/me/identity");
            ApplyIdentity(identity);
            NeedsIdentity = !identity.Complete;
            _ = session.Mail.RefreshAsync();
            _ = session.Sigtao.RefreshAsync();
        }
        catch (Exception ex)
        {
            IdentityError = "Serveur injoignable : " + ex.Message;
        }
        finally
        {
            _identityBusy = false;
            SaveIdentityCommand?.RaiseCanExecuteChanged();
        }
    }
}
