using System.Globalization;
using Argos.Shared.Contracts;

namespace Argos.ViewModels.Ode;

/// <summary>Mises en forme communes d'ADAGIO (dates à la française, heure locale du poste).</summary>
public static class OdeFormat
{
    public static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>« vendredi 7 juin 2024 13:59:20 »</summary>
    public static string LongDateTime(DateTimeOffset value)
        => value.ToLocalTime().ToString("dddd d MMMM yyyy HH:mm:ss", French);

    /// <summary>« vendredi 07 juin 2024 »</summary>
    public static string StatusDate(DateTime value) => value.ToString("dddd dd MMMM yyyy", French);

    /// <summary>« 19/12/2025 à 22:30 », comme sur l'ODE.</summary>
    public static string OdeDateTime(DateTimeOffset value)
        => value.ToLocalTime().ToString("dd/MM/yyyy 'à' HH:mm", French);

    /// <summary>« 29/10 14:05 »</summary>
    public static string Short(DateTimeOffset value) => value.ToLocalTime().ToString("dd/MM HH:mm", French);

    /// <summary>« 14:05:32 »</summary>
    public static string Time(DateTimeOffset value) => value.ToLocalTime().ToString("HH:mm:ss", French);

    public static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
    }

    public static string Nature(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "Non renseignée";
        return Natures.Find(code)?.Display ?? code;
    }

    public static string OrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
}
