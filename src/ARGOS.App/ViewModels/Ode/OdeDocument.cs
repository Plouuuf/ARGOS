using Argos.Shared.Contracts;

namespace Argos.ViewModels.Ode;

/// <summary>Contenu de la feuille d'ordre de départ (même mise en page que la feuille ODE fournie).</summary>
public sealed class OdeDocument
{
    public string MissionTitle { get; init; } = string.Empty;    // Mission BSPP - Procédure BLANCHE
    public string DepartTitle { get; init; } = string.Empty;     // PREMIERS DEPARTS - INCENDIE
    public string MainEngin { get; init; } = string.Empty;       // EPAN 24 MALF (EPAN_1_MALF)
    public string Locality { get; init; } = string.Empty;        // L'ECHAT
    public string ValidatedAt { get; init; } = string.Empty;     // 19/12/2025 à 22:30
    public string Cstc { get; init; } = string.Empty;
    public string Number { get; init; } = string.Empty;          // 210-593
    public string Nature { get; init; } = string.Empty;          // 117 - Feu …
    public string Address { get; init; } = string.Empty;
    public string CallerPhone { get; init; } = string.Empty;
    public string CallerInfo { get; init; } = string.Empty;
    public string Consignes { get; init; } = string.Empty;
    public string Complementary { get; init; } = string.Empty;   // FPTL 202 MALF (FPTL_1_MALF)
    public string Footer { get; init; } = string.Empty;          // ODE n°1 · opérateur

    public static string EnginLabel(string nom)
        => Argos.Services.ArgosSession.Current?.Ode.FindEngin(nom)?.NomOde ?? EnginsCatalog.Find(nom)?.NomOde ?? nom;

    public static OdeDocument From(Intervention intervention, DepartureOrder order)
    {
        var engins = order.Engins;
        var isFirst = order.Number == 1;
        var address = string.Join(" ", new[] { intervention.Address, string.IsNullOrWhiteSpace(intervention.Building) ? null : $"({intervention.Building})" }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        return new OdeDocument
        {
            MissionTitle = Procedures.MissionTitle(order.Procedure),
            DepartTitle = isFirst ? Natures.DepartTitle(intervention.NatureCode) : "DEPART COMPLEMENTAIRE",
            MainEngin = engins.Count > 0 ? EnginLabel(engins[0]) : string.Empty,
            Locality = (intervention.Locality ?? string.Empty).ToUpperInvariant(),
            ValidatedAt = OdeFormat.OdeDateTime(order.ValidatedAt),
            Cstc = intervention.Cstc,
            Number = intervention.Number,
            Nature = OdeFormat.Nature(intervention.NatureCode),
            Address = address,
            CallerPhone = intervention.CallerPhone,
            CallerInfo = intervention.CallerInfo,
            Consignes = string.IsNullOrWhiteSpace(order.Consignes) ? intervention.Consignes : order.Consignes,
            Complementary = string.Join("\n", engins.Skip(1).Select(EnginLabel)),
            Footer = $"ODE n°{order.Number} - validé par {order.OperatorName} - ADAGIO"
        };
    }
}
