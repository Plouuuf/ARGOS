namespace Argos.Shared.Contracts;

// =====================================================================
// ILIAS : documents de formation et réglementaires, formations, questionnaires, suivi pédagogique.
// Administration documentaire : rôle Discord « Accès RH/ADMIN ».
// =====================================================================

/// <summary>Public d'un document ou d'une formation (droits d'accès).</summary>
public static class IliasAudiences
{
    public const string Tous = "Tous";
    public const string Ode = "Opérateurs ADAGIO ODE";
    public const string Remise = "Gestion ADAGIO REMISE";
    public const string RhAdmin = "RH / ADMIN";

    public static readonly IReadOnlyList<string> All = [Tous, Ode, Remise, RhAdmin];

    public static bool Allows(string? audience, IReadOnlyCollection<string> levels) => audience switch
    {
        Ode => AccessLevels.CanUse(levels, ArgosApps.AdagioOde) || AccessLevels.IsRhAdmin(levels),
        Remise => AccessLevels.CanUse(levels, ArgosApps.AdagioRemise) || AccessLevels.IsRhAdmin(levels),
        RhAdmin => AccessLevels.IsRhAdmin(levels),
        _ => true
    };
}

public sealed class IliasCategory
{
    public string Id { get; set; } = string.Empty;
    public string ParentId { get; set; } = string.Empty;          // vide = catégorie principale
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool Archived { get; set; }
}

public sealed class IliasDocument
{
    public string Id { get; set; } = string.Empty;
    public string CategoryId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Keywords { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/pdf";
    public long Size { get; set; }
    public int Version { get; set; } = 1;
    public bool Downloadable { get; set; } = true;
    public string Audience { get; set; } = IliasAudiences.Tous;
    public bool Archived { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

public sealed class IliasModule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;           // texte du module
    public string DocumentId { get; set; } = string.Empty;        // support PDF (facultatif)
}

public sealed class IliasFormation
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Objectives { get; set; } = string.Empty;
    public string Prerequisites { get; set; } = string.Empty;
    public string Audience { get; set; } = IliasAudiences.Tous;
    public List<IliasModule> Modules { get; set; } = [];
    public string QuizId { get; set; } = string.Empty;            // évaluation finale (facultative)
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class IliasQuestion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Text { get; set; } = string.Empty;
    public List<string> Options { get; set; } = [];
    public int Correct { get; set; } = -1;                          // jamais envoyé aux postes (sauf administration)
    public string Explanation { get; set; } = string.Empty;
}

public sealed class IliasQuiz
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int TimeLimitMinutes { get; set; }                       // 0 = pas de chronomètre
    public int PassPercent { get; set; } = 70;
    public bool ShowCorrections { get; set; } = true;
    public string Audience { get; set; } = IliasAudiences.Tous;
    public List<IliasQuestion> Questions { get; set; } = [];
    public bool Archived { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Progression d'un militaire dans une formation.</summary>
public sealed class IliasProgress
{
    public string Id { get; set; } = string.Empty;                  // userId:formationId
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string FormationId { get; set; } = string.Empty;
    public List<string> CompletedModules { get; set; } = [];
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class IliasCorrection
{
    public string Question { get; set; } = string.Empty;
    public string Given { get; set; } = string.Empty;
    public string Expected { get; set; } = string.Empty;
    public bool Right { get; set; }
    public string Explanation { get; set; } = string.Empty;
}

public sealed class IliasResult
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string QuizId { get; set; } = string.Empty;
    public string QuizTitle { get; set; } = string.Empty;
    public int Score { get; set; }
    public int Total { get; set; }
    public int Percent => Total == 0 ? 0 : (int)Math.Round(100.0 * Score / Total);
    public bool Passed { get; set; }
    public int DurationSeconds { get; set; }
    public DateTimeOffset At { get; set; }
    public List<IliasCorrection> Corrections { get; set; } = [];
}

public sealed class IliasSnapshot
{
    public List<IliasCategory> Categories { get; set; } = [];
    public List<IliasDocument> Documents { get; set; } = [];
    public List<IliasFormation> Formations { get; set; } = [];
    public List<IliasQuiz> Quizzes { get; set; } = [];
    public List<IliasProgress> MyProgress { get; set; } = [];
    public List<IliasResult> MyResults { get; set; } = [];
    public List<string> RecentDocuments { get; set; } = [];
    public bool CanAdmin { get; set; }
    public DateTimeOffset ServerTime { get; set; }
}

// ===== Requêtes =====

public sealed class IliasDocumentRequest
{
    public IliasDocument Document { get; set; } = new();
    public string? FileBase64 { get; set; }                         // nouveau fichier (création ou remplacement)
}

public sealed class IliasModuleRequest
{
    public string ModuleId { get; set; } = string.Empty;
    public bool Done { get; set; } = true;
}

public sealed class IliasSubmitRequest
{
    public List<int> Answers { get; set; } = [];
    public int DurationSeconds { get; set; }
}
