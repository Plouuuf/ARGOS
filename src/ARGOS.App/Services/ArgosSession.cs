using Argos.Shared.Contracts;

namespace Argos.Services;

/// <summary>Session ouverte sur ce poste : utilisateur, accès au serveur et données partagées.</summary>
public sealed class ArgosSession
{
    public ArgosSession(ArgosUserDto user, string token)
    {
        User = user;
        Token = token;
        Api = new ApiClient(token);
        Ode = new OdeStore(Api);
        Remise = new RemiseStore(Api);
        Mco = new McoStore(Api);
    }

    /// <summary>Session courante (null sur l'écran de verrouillage et de connexion).</summary>
    public static ArgosSession? Current { get; set; }

    public ArgosUserDto User { get; set; }
    public string Token { get; }
    public ApiClient Api { get; }
    public OdeStore Ode { get; }
    public RemiseStore Remise { get; }
    public McoStore Mco { get; }

    /// <summary>Nom d'opérateur affiché dans ADAGIO (en majuscules).</summary>
    public string OperatorName => User.DisplayName.Trim().ToUpperInvariant();

    public bool HasLevel(string level) => User.Levels.Contains(level);
}
