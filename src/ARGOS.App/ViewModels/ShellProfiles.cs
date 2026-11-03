using System.Collections.ObjectModel;
using Argos.Shared.Contracts;
using Avalonia.Media.Imaging;

namespace Argos.ViewModels;

/// <summary>Un profil (personnage) proposé sur l'écran de connexion.</summary>
public sealed class ProfileChoice
{
    public required ArgosProfile Profile { get; init; }
    public bool IsLast { get; init; }

    public string Name => Profile.Name;
    public string Details => Profile.Details;
    public string Badge => IsLast ? "Dernier utilisé" : Profile.Primary ? "Principal" : string.Empty;
    public bool HasBadge => Badge.Length > 0;
    public Bitmap? Photo { get; init; }
    public bool HasPhoto => Photo is not null;

    public string Initials
    {
        get
        {
            var parts = Profile.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // « SGT DUPONT Jean » → « DJ » (le grade est ignoré).
            var words = parts.Length >= 3 ? parts[1..] : parts;
            return string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
        }
    }

    public static ProfileChoice From(ArgosProfile profile, bool isLast)
    {
        Bitmap? photo = null;
        if (!string.IsNullOrWhiteSpace(profile.PhotoData))
        {
            try
            {
                var data = profile.PhotoData;
                var comma = data.IndexOf(',');
                if (data.StartsWith("data:", StringComparison.Ordinal) && comma > 0) data = data[(comma + 1)..];
                using var stream = new MemoryStream(Convert.FromBase64String(data));
                photo = Bitmap.DecodeToWidth(stream, 112);
            }
            catch
            {
                photo = null;
            }
        }
        return new ProfileChoice { Profile = profile, IsLast = isLast, Photo = photo };
    }
}

/// <summary>Un profil dans la liste de l'écran de connexion (en bas à gauche).</summary>
public sealed class LoginProfileItem(ProfileChoice choice) : ViewModelBase
{
    private bool _isSelected;
    public ProfileChoice Choice { get; } = choice;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

/// <summary>
/// Choix du profil à la connexion : un compte Discord relié à plusieurs dossiers SIGTAO (personnages)
/// choisit celui qu'il prend. Chaque profil est un compte distinct (messagerie, comptes-rendus, gardes…).
/// </summary>
public sealed partial class ShellViewModel
{
    private bool _isChoosingProfile;

    public ObservableCollection<ProfileChoice> Profiles { get; } = [];

    public bool IsChoosingProfile
    {
        get => _isChoosingProfile;
        private set
        {
            if (!SetProperty(ref _isChoosingProfile, value)) return;
            OnPropertyChanged(nameof(ShowSignIn));
            OnPropertyChanged(nameof(ShowLoginProfiles));
        }
    }

    /// <summary>Écran de connexion habituel (photo, bouton) : masqué pendant le choix du profil.</summary>
    public bool ShowSignIn => !IsChoosingProfile;

    public RelayCommand ChooseProfileCommand { get; }
    public RelayCommand CancelProfileCommand { get; }
    public RelayCommand PickLoginProfileCommand { get; }

    // ===== Écran de connexion : profils du compte enregistré, comme les comptes de Windows 10 (en bas à gauche) =====

    private ProfileChoice? _loginProfile;

    /// <summary>Profils du compte Discord enregistré sur l'appareil (affichés seulement s'il y en a plusieurs).</summary>
    public ObservableCollection<LoginProfileItem> LoginProfiles { get; } = [];
    public bool HasLoginProfiles => LoginProfiles.Count > 1;
    public bool ShowLoginProfiles => HasLoginProfiles && !IsChoosingProfile;

    /// <summary>Profil sélectionné sur l'écran de connexion : ouvert directement par « Se connecter ».</summary>
    public ProfileChoice? LoginProfile
    {
        get => _loginProfile;
        private set
        {
            if (!SetProperty(ref _loginProfile, value)) return;
            foreach (var item in LoginProfiles) item.IsSelected = ReferenceEquals(item.Choice, value);
        }
    }

    /// <summary>Charge les profils du compte enregistré (session déjà valide) pour l'écran de connexion.</summary>
    private async Task LoadLoginProfilesAsync()
    {
        if (!HasKnownAccount || IsBusy) return;
        ProfilesResponse response;
        try
        {
            if (!_sessionReady || string.IsNullOrWhiteSpace(_auth.SessionToken))
                _sessionReady = await _auth.TryResumeAsync();
            if (!_sessionReady) return;
            response = await _auth.GetProfilesAsync();
        }
        catch
        {
            return;
        }

        var keep = LoginProfile?.Profile.Id ?? response.SelectedId;
        LoginProfiles.Clear();
        if (response.Profiles.Count > 1)
            foreach (var p in response.Profiles)
                LoginProfiles.Add(new LoginProfileItem(ProfileChoice.From(p, p.Id == response.SelectedId)));
        OnPropertyChanged(nameof(HasLoginProfiles));
        OnPropertyChanged(nameof(ShowLoginProfiles));

        var selected = LoginProfiles.FirstOrDefault(i => i.Choice.Profile.Id == keep) ?? LoginProfiles.FirstOrDefault();
        _loginProfile = null;
        if (selected is not null) ShowLoginProfile(selected.Choice);
        else LoginProfile = null;
    }

    private void ShowLoginProfile(ProfileChoice choice)
    {
        LoginProfile = choice;
        DisplayName = choice.Name;
        Subtitle = string.IsNullOrWhiteSpace(choice.Details) ? "BSPP" : choice.Details;
        if (choice.Photo is not null) Avatar = choice.Photo;
    }

    private void PickLoginProfile(object? parameter)
    {
        if (IsBusy || parameter is not LoginProfileItem item) return;
        ErrorText = null;
        ShowLoginProfile(item.Choice);
    }

    /// <summary>Après la connexion Discord : choix du profil s'il y en a plusieurs, puis bureau.</summary>
    private async Task ContinueWithProfileAsync(CancellationToken ct)
    {
        ProfilesResponse profiles;
        try
        {
            StatusText = "Recherche de tes profils…";
            profiles = await _auth.GetProfilesAsync(ct);
        }
        catch (HttpRequestException)
        {
            profiles = new ProfilesResponse();
        }

        if (!profiles.MustChoose || profiles.Profiles.Count < 2)
        {
            await EnterDesktopAsync();
            return;
        }

        // Profil déjà choisi sur l'écran de connexion : ouvert directement.
        if (LoginProfile is { } picked && profiles.Profiles.Any(p => p.Id == picked.Profile.Id))
        {
            StatusText = $"Ouverture du profil {picked.Name}…";
            if (await _auth.SelectProfileAsync(picked.Profile.Id, ct))
            {
                await EnterDesktopAsync();
                return;
            }
        }

        Profiles.Clear();
        foreach (var p in profiles.Profiles)
            Profiles.Add(ProfileChoice.From(p, p.Id == profiles.SelectedId));
        IsChoosingProfile = true;
    }

    private async Task ChooseProfileAsync(ProfileChoice? choice)
    {
        if (choice is null || IsBusy) return;
        ErrorText = null;
        IsBusy = true;
        StatusText = $"Ouverture du profil {choice.Name}…";
        try
        {
            if (!await _auth.SelectProfileAsync(choice.Profile.Id))
            {
                ErrorText = "Ce profil n'est plus disponible. Choisis-en un autre.";
                return;
            }
            IsChoosingProfile = false;
            Profiles.Clear();
            await EnterDesktopAsync();
        }
        catch (HttpRequestException)
        {
            ErrorText = "Le serveur ARGOS est injoignable. Vérifie ta connexion internet.";
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            StatusText = null;
            IsBusy = false;
        }
    }

    /// <summary>Retour à l'écran de connexion sans ouvrir de session.</summary>
    private void CancelProfile()
    {
        IsChoosingProfile = false;
        Profiles.Clear();
        _sessionReady = false;
    }
}
