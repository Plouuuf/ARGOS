using Argos.Shared.Contracts;

namespace Argos.Services;

/// <summary>Application du bureau ARGOS.</summary>
public sealed record AppDefinition(string Id, string Title, string Subtitle, string IconPath);

/// <summary>Les 7 applications du bureau, dans l'ordre des icônes.</summary>
public static class AppCatalog
{
    public static readonly IReadOnlyList<AppDefinition> All =
    [
        new(ArgosApps.AdagioOde, "ADAGIO ODE", "Ordres de départ", "Icons/adagio-ode.png"),
        new(ArgosApps.AdagioRemise, "ADAGIO REMISE", "Engins du centre de secours", "Icons/adagio-remise.png"),
        new(ArgosApps.Efibi, "eFIBI", "Fiches bilans", "Icons/efibi.png"),
        new(ArgosApps.Ilias, "Ilias", "Documents de formation", "Icons/ilias.png"),
        new(ArgosApps.Mail, "Mail", "Messagerie", "Icons/mail.png"),
        new(ArgosApps.Mco, "MCO", "Main courante opérationnelle", "Icons/mco.png"),
        new(ArgosApps.Sigtao, "SIGTAO", "Planning et personnel", "Icons/sigtao.png")
    ];

    public static AppDefinition? Find(string id) => All.FirstOrDefault(a => a.Id == id);
}
