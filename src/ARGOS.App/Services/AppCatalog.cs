using Argos.Shared.Contracts;

namespace Argos.Services;

/// <summary>Application du bureau ARGOS.</summary>
public sealed record AppDefinition(string Id, string Title, string Subtitle, string IconPath,
    bool Accessory = false, double Width = 980, double Height = 640);

/// <summary>Applications du bureau, dans l'ordre des icônes (la Corbeille en premier, comme sur Windows).</summary>
public static class AppCatalog
{
    public static readonly IReadOnlyList<AppDefinition> All =
    [
        new(ArgosApps.Corbeille, "Corbeille", "Éléments supprimés de Mes documents", "Icons/corbeille.png", true, 760, 480),
        new(ArgosApps.AdagioOde, "ADAGIO ODE", "Ordres de départ", "Icons/adagio-ode.png"),
        new(ArgosApps.AdagioRemise, "ADAGIO REMISE", "Engins du centre de secours", "Icons/adagio-remise.png"),
        new(ArgosApps.Efibi, "eFIBI", "Fiches bilans", "Icons/efibi.png"),
        new(ArgosApps.Ilias, "Ilias", "Documents de formation", "Icons/ilias.png"),
        new(ArgosApps.Mail, "Mail", "Messagerie", "Icons/mail.png"),
        new(ArgosApps.Mco, "MCO", "Main courante opérationnelle", "Icons/mco.png"),
        new(ArgosApps.Sigtao, "SIGTAO", "Planning et personnel", "Icons/sigtao.png"),
        new(ArgosApps.Explorateur, "Explorateur de fichiers", "Mes documents", "Icons/explorateur.png", true, 940, 600),
        new(ArgosApps.BlocNotes, "Bloc-notes", "Notes et textes", "Icons/bloc-notes.png", true, 760, 560),
        new(ArgosApps.Calculatrice, "Calculatrice", "Calculatrice standard", "Icons/calculatrice.png", true, 340, 540),
        new(ArgosApps.Imprimante, "Imprimante", "Imprimante du centre de secours", "Icons/imprimante.png", true, 1000, 640)
    ];

    public static AppDefinition? Find(string id) => All.FirstOrDefault(a => a.Id == id);
}
