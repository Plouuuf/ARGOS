namespace Argos.Shared.Contracts;

/// <summary>
/// Message échangé sur la connexion temps réel (/ws), en JSON.
/// Premier message du client : { "type": "auth", "token": "…" }.
/// Le serveur répond « welcome » (ou « denied »), puis pousse ses notifications.
/// </summary>
public sealed record RealtimeMessage(
    string Type,
    string? Token = null,
    string? Title = null,
    string? Text = null,
    string? AppId = null);

public static class RealtimeTypes
{
    public const string Auth = "auth";
    public const string Welcome = "welcome";
    public const string Denied = "denied";
    public const string Ping = "ping";
    public const string Pong = "pong";

    /// <summary>Notification à afficher (coin de l'écran sur ordinateur, bandeau sur téléphone).</summary>
    public const string Notify = "notify";

    /// <summary>Les données d'ADAGIO ODE ont changé : les postes rechargent l'état (Text = raison).</summary>
    public const string OdeChanged = "ode-changed";

    /// <summary>Ouvrir la prise d'appel de l'appel indiqué (Text = identifiant), au décroché d'un 18.</summary>
    public const string OpenCall = "open-call";
}
