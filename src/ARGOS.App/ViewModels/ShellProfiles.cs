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
        }
    }

    /// <summary>Écran de connexion habituel (photo, bouton) : masqué pendant le choix du profil.</summary>
    public bool ShowSignIn => !IsChoosingProfile;

    public RelayCommand ChooseProfileCommand { get; }
    public RelayCommand CancelProfileCommand { get; }

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
