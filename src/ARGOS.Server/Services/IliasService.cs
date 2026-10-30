using Argos.Shared.Contracts;
using static Argos.Server.Services.OdeDatabase;

namespace Argos.Server.Services;

/// <summary>
/// ILIAS : bibliothèque documentaire (fichiers dans data/ilias), formations, questionnaires et suivi.
/// Les bonnes réponses ne quittent le serveur que pour l'administration ; la note est calculée ici.
/// </summary>
public sealed class IliasService(OdeDatabase db, RealtimeHub hub)
{
    public const long MaxFileBytes = 15 * 1024 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static string FilesDir => Path.Combine(AppContext.BaseDirectory, "data", "ilias");

    public static bool CanAdmin(ArgosUserDto user) => AccessLevels.IsRhAdmin(user.Levels);

    public IliasSnapshot Snapshot(ArgosUserDto user)
    {
        EnsureSeed();
        var admin = CanAdmin(user);
        bool Visible(string audience, bool archived) => (admin || !archived) && IliasAudiences.Allows(audience, user.Levels);

        var quizzes = db.Docs<IliasQuiz>(RemiseTables.IliasQuizzes).Where(q => Visible(q.Audience, q.Archived)).ToList();
        if (!admin)
            foreach (var q in quizzes)
                foreach (var question in q.Questions)
                {
                    question.Correct = -1;            // les postes ne reçoivent pas les réponses
                    question.Explanation = string.Empty;
                }

        return new IliasSnapshot
        {
            Categories = db.Docs<IliasCategory>(RemiseTables.IliasCategories).Where(c => admin || !c.Archived).OrderBy(c => c.Order).ThenBy(c => c.Name).ToList(),
            Documents = db.Docs<IliasDocument>(RemiseTables.IliasDocuments).Where(d => Visible(d.Audience, d.Archived)).OrderByDescending(d => d.UpdatedAt).ToList(),
            Formations = db.Docs<IliasFormation>(RemiseTables.IliasFormations).Where(f => Visible(f.Audience, f.Archived)).OrderBy(f => f.Title).ToList(),
            Quizzes = quizzes.OrderBy(q => q.Title).ToList(),
            MyProgress = db.Docs<IliasProgress>(RemiseTables.IliasProgress).Where(p => p.UserId == user.UserId).ToList(),
            MyResults = db.Docs<IliasResult>(RemiseTables.IliasResults).Where(r => r.UserId == user.UserId).OrderByDescending(r => r.At).ToList(),
            RecentDocuments = db.GetDoc<RecentList>(RemiseTables.IliasProgress, "recent:" + user.UserId)?.Ids ?? [],
            CanAdmin = admin,
            ServerTime = DateTimeOffset.UtcNow
        };
    }

    private sealed class RecentList
    {
        public List<string> Ids { get; set; } = [];
    }

    // ===== Documents =====

    /// <summary>Fichier d'un document (contrôle du public), et mémorisation « récemment consulté ».</summary>
    public (IliasDocument? Doc, string? Path, string? Error) OpenFile(ArgosUserDto user, string id)
    {
        var doc = db.GetDoc<IliasDocument>(RemiseTables.IliasDocuments, id);
        if (doc is null || (doc.Archived && !CanAdmin(user)) || !IliasAudiences.Allows(doc.Audience, user.Levels))
            return (null, null, "Document introuvable ou non autorisé.");
        var path = Path.Combine(FilesDir, doc.Id);
        if (!File.Exists(path))
            return (doc, null, "Fichier absent du serveur.");
        var recent = db.GetDoc<RecentList>(RemiseTables.IliasProgress, "recent:" + user.UserId) ?? new RecentList();
        recent.Ids.Remove(doc.Id);
        recent.Ids.Insert(0, doc.Id);
        recent.Ids = recent.Ids.Take(10).ToList();
        db.SaveDoc(RemiseTables.IliasProgress, "recent:" + user.UserId, DateTimeOffset.UtcNow, recent);
        return (doc, path, null);
    }

    public async Task<OdeResult> SaveDocumentAsync(ArgosUserDto user, IliasDocumentRequest request)
    {
        if (!CanAdmin(user)) return new(false, "Administration documentaire réservée au rôle Accès RH/ADMIN.");
        var d = request.Document;
        d.Title = (d.Title ?? string.Empty).Trim();
        if (d.Title.Length == 0 || string.IsNullOrWhiteSpace(d.CategoryId))
            return new(false, "Titre et catégorie obligatoires.");
        byte[]? bytes = null;
        if (!string.IsNullOrWhiteSpace(request.FileBase64))
        {
            try { bytes = Convert.FromBase64String(request.FileBase64); }
            catch (FormatException) { return new(false, "Fichier illisible."); }
            if (bytes.Length > MaxFileBytes) return new(false, "Fichier trop lourd (15 Mo maximum).");
        }

        await _gate.WaitAsync();
        try
        {
            var existing = string.IsNullOrWhiteSpace(d.Id) ? null : db.GetDoc<IliasDocument>(RemiseTables.IliasDocuments, d.Id);
            if (existing is null && bytes is null) return new(false, "Ajoute le fichier du document.");
            var now = DateTimeOffset.UtcNow;
            d.Id = existing?.Id ?? Guid.NewGuid().ToString("N");
            d.PublishedAt = existing?.PublishedAt ?? now;
            d.UpdatedAt = now;
            d.UpdatedBy = user.DisplayName;
            d.Version = existing is null ? 1 : existing.Version + (bytes is null ? 0 : 1);
            if (bytes is null && existing is not null)
            {
                d.FileName = existing.FileName;
                d.ContentType = existing.ContentType;
                d.Size = existing.Size;
            }
            else if (bytes is not null)
            {
                Directory.CreateDirectory(FilesDir);
                await File.WriteAllBytesAsync(Path.Combine(FilesDir, d.Id), bytes);
                d.Size = bytes.Length;
            }
            db.SaveDoc(RemiseTables.IliasDocuments, d.Id, now, d);
            Journal(user, existing is null ? "ILIAS : document ajouté" : bytes is null ? "ILIAS : document modifié" : "ILIAS : document remplacé",
                d.Title, $"v{d.Version}{(d.Archived ? " (archivé)" : string.Empty)}");
            await NotifyAsync();
            return new(true, null, d.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<OdeResult> SaveCategoryAsync(ArgosUserDto user, IliasCategory c)
        => SaveAsync(user, RemiseTables.IliasCategories, c, c.Name, x => x.Id, (x, id) => x.Id = id, "catégorie",
            () => string.IsNullOrWhiteSpace(c.Name) ? "Nom de la catégorie obligatoire." : null);

    public Task<OdeResult> SaveFormationAsync(ArgosUserDto user, IliasFormation f)
    {
        f.Modules = f.Modules.Where(m => !string.IsNullOrWhiteSpace(m.Title)).ToList();
        if (f.CreatedAt == default) f.CreatedAt = DateTimeOffset.UtcNow;
        f.UpdatedAt = DateTimeOffset.UtcNow;
        return SaveAsync(user, RemiseTables.IliasFormations, f, f.Title, x => x.Id, (x, id) => x.Id = id, "formation",
            () => string.IsNullOrWhiteSpace(f.Title) ? "Titre obligatoire." : f.Modules.Count == 0 ? "Ajoute au moins un module." : null);
    }

    public Task<OdeResult> SaveQuizAsync(ArgosUserDto user, IliasQuiz q)
    {
        q.Questions = q.Questions.Where(x => !string.IsNullOrWhiteSpace(x.Text)).ToList();
        foreach (var x in q.Questions)
            x.Options = x.Options.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()).ToList();
        q.PassPercent = Math.Clamp(q.PassPercent, 0, 100);
        q.TimeLimitMinutes = Math.Clamp(q.TimeLimitMinutes, 0, 180);
        q.UpdatedAt = DateTimeOffset.UtcNow;
        return SaveAsync(user, RemiseTables.IliasQuizzes, q, q.Title, x => x.Id, (x, id) => x.Id = id, "questionnaire", () =>
            string.IsNullOrWhiteSpace(q.Title) ? "Titre obligatoire."
            : q.Questions.Count == 0 ? "Ajoute au moins une question."
            : q.Questions.Any(x => x.Options.Count < 2 || x.Correct < 0 || x.Correct >= x.Options.Count)
                ? "Chaque question doit avoir au moins deux réponses et une bonne réponse." : null);
    }

    private async Task<OdeResult> SaveAsync<T>(ArgosUserDto user, string table, T item, string label, Func<T, string> getId,
        Action<T, string> setId, string kind, Func<string?> validate)
    {
        if (!CanAdmin(user)) return new(false, "Administration documentaire réservée au rôle Accès RH/ADMIN.");
        if (validate() is { } error) return new(false, error);
        await _gate.WaitAsync();
        try
        {
            var isNew = string.IsNullOrWhiteSpace(getId(item));
            if (isNew) setId(item, Guid.NewGuid().ToString("N"));
            db.SaveDoc(table, getId(item), DateTimeOffset.UtcNow, item!);
            Journal(user, $"ILIAS : {kind} {(isNew ? "créée" : "modifiée")}", label.Trim(), string.Empty);
            await NotifyAsync();
            return new(true, null, getId(item));
        }
        finally
        {
            _gate.Release();
        }
    }

    // ===== Formations et questionnaires =====

    public async Task<OdeResult> SetModuleAsync(ArgosUserDto user, string formationId, IliasModuleRequest request)
    {
        var formation = db.GetDoc<IliasFormation>(RemiseTables.IliasFormations, formationId);
        if (formation is null || !IliasAudiences.Allows(formation.Audience, user.Levels)) return new(false, "Formation introuvable.");
        await _gate.WaitAsync();
        try
        {
            var id = user.UserId + ":" + formationId;
            var now = DateTimeOffset.UtcNow;
            var progress = db.GetDoc<IliasProgress>(RemiseTables.IliasProgress, id)
                           ?? new IliasProgress { Id = id, UserId = user.UserId, FormationId = formationId, StartedAt = now };
            progress.UserName = user.DisplayName;
            progress.CompletedModules.Remove(request.ModuleId);
            if (request.Done && formation.Modules.Any(m => m.Id == request.ModuleId))
                progress.CompletedModules.Add(request.ModuleId);
            var modulesDone = formation.Modules.All(m => progress.CompletedModules.Contains(m.Id));
            var quizPassed = string.IsNullOrEmpty(formation.QuizId)
                             || db.Docs<IliasResult>(RemiseTables.IliasResults).Any(r => r.UserId == user.UserId && r.QuizId == formation.QuizId && r.Passed);
            progress.CompletedAt = modulesDone && quizPassed ? progress.CompletedAt ?? now : null;
            progress.UpdatedAt = now;
            db.SaveDoc(RemiseTables.IliasProgress, id, now, progress);
            return new(true, null, id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IliasResult?> SubmitAsync(ArgosUserDto user, string quizId, IliasSubmitRequest request)
    {
        var quiz = db.GetDoc<IliasQuiz>(RemiseTables.IliasQuizzes, quizId);
        if (quiz is null || quiz.Archived || !IliasAudiences.Allows(quiz.Audience, user.Levels)) return null;
        var result = new IliasResult
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = user.UserId,
            UserName = user.DisplayName,
            QuizId = quiz.Id,
            QuizTitle = quiz.Title,
            Total = quiz.Questions.Count,
            DurationSeconds = Math.Max(0, request.DurationSeconds),
            At = DateTimeOffset.UtcNow
        };
        for (var i = 0; i < quiz.Questions.Count; i++)
        {
            var q = quiz.Questions[i];
            var given = i < request.Answers.Count ? request.Answers[i] : -1;
            var right = given == q.Correct;
            if (right) result.Score++;
            result.Corrections.Add(new IliasCorrection
            {
                Question = q.Text,
                Given = given >= 0 && given < q.Options.Count ? q.Options[given] : "Sans réponse",
                Expected = quiz.ShowCorrections ? q.Options[q.Correct] : string.Empty,
                Right = right,
                Explanation = quiz.ShowCorrections ? q.Explanation : string.Empty
            });
        }
        // Chronomètre dépassé de plus d'une minute : réponses non retenues.
        if (quiz.TimeLimitMinutes > 0 && result.DurationSeconds > quiz.TimeLimitMinutes * 60 + 60)
            result.Score = 0;
        result.Passed = result.Percent >= quiz.PassPercent;
        if (!quiz.ShowCorrections)
            result.Corrections = result.Corrections.Select(c => new IliasCorrection { Question = c.Question, Given = c.Given, Right = c.Right }).ToList();

        await _gate.WaitAsync();
        try
        {
            db.SaveDoc(RemiseTables.IliasResults, result.Id, result.At, result);
            Journal(user, "ILIAS : évaluation", quiz.Title, $"{result.Score}/{result.Total} ({result.Percent} %) - {(result.Passed ? "réussie" : "échouée")}");
        }
        finally
        {
            _gate.Release();
        }
        // La formation liée peut devenir validée.
        foreach (var f in db.Docs<IliasFormation>(RemiseTables.IliasFormations).Where(f => f.QuizId == quiz.Id))
            if (db.GetDoc<IliasProgress>(RemiseTables.IliasProgress, user.UserId + ":" + f.Id) is { } p && p.CompletedModules.Count > 0)
                await SetModuleAsync(user, f.Id, new IliasModuleRequest { ModuleId = p.CompletedModules[0], Done = true });
        return result;
    }

    // ===== Contenu de départ =====

    private void EnsureSeed()
    {
        if (db.AnyDoc(RemiseTables.IliasCategories)) return;
        var now = DateTimeOffset.UtcNow;
        var categories = new (string Id, string Parent, string Name, string Description)[]
        {
            ("reglementaire", "", "Documents réglementaires", "Notes de service, règlements et instructions de la BSPP."),
            ("operationnel", "", "Opérationnel", "Doctrines et fiches réflexes."),
            ("secourisme", "operationnel", "Secours à victimes", "Référentiels SUAP, bilans, gestes."),
            ("incendie", "operationnel", "Incendie", "Techniques d'extinction, sauvetages, ventilation."),
            ("cta", "operationnel", "Salle opérationnelle (CTA)", "Prise d'appel, ADAGIO, procédures de départ."),
            ("formation", "", "Formation", "Supports des stages et formations de la compagnie.")
        };
        var order = 0;
        foreach (var c in categories)
            db.SaveDoc(RemiseTables.IliasCategories, c.Id, now, new IliasCategory
            {
                Id = c.Id, ParentId = c.Parent, Name = c.Name, Description = c.Description, Order = order++
            });

        var quiz = new IliasQuiz
        {
            Id = "quiz-prise-appel",
            Title = "Prise d'appel 18 - procédures ADAGIO",
            Description = "Évaluation de la formation « Prise d'appel au CTA ».",
            TimeLimitMinutes = 10,
            PassPercent = 75,
            Audience = IliasAudiences.Tous,
            UpdatedAt = now,
            Questions =
            [
                new() { Text = "Quelle touche classe un appel sans départ pour « INU » ?", Options = ["F1", "F5", "F9", "F3"], Correct = 1,
                        Explanation = "F5 INU clôt l'appel sans engager de moyens." },
                new() { Text = "Que faut-il faire avant de créer l'ordre de départ ?", Options = ["Confirmer la localisation", "Imprimer l'ODE", "Clôturer l'appel", "Ouvrir la MCO"], Correct = 0,
                        Explanation = "La localisation confirmée renseigne l'adresse et le quartier de l'ODE." },
                new() { Text = "Un engin « En maintenance » dans ADAGIO REMISE peut-il être engagé ?", Options = ["Oui", "Non"], Correct = 1,
                        Explanation = "Un engin indisponible ou en maintenance n'est pas proposé dans l'ODE." },
                new() { Text = "Quelle procédure correspond à la touche F3 ?", Options = ["Blanche", "Orange", "Rouge", "Verte"], Correct = 2,
                        Explanation = "F3 = procédure ROUGE." }
            ]
        };
        db.SaveDoc(RemiseTables.IliasQuizzes, quiz.Id, now, quiz);
        db.SaveDoc(RemiseTables.IliasFormations, "formation-cta", now, new IliasFormation
        {
            Id = "formation-cta",
            Title = "Prise d'appel au CTA",
            Description = "Recevoir un appel 18, le localiser, le qualifier et engager les moyens avec ADAGIO.",
            Objectives = "Être capable de traiter un appel 18 de bout en bout dans ADAGIO ODE.",
            Prerequisites = "Accès ADAGIO ODE.",
            QuizId = quiz.Id,
            CreatedAt = now,
            UpdatedAt = now,
            Modules =
            [
                new() { Id = "m1", Title = "La prise d'appel", Content = "Décrocher, se présenter, recueillir le numéro du requérant et l'origine de l'appel. Garder la maîtrise de l'échange." },
                new() { Id = "m2", Title = "Localiser l'événement", Content = "Saisir la voie dans « Voie - site - commune » (plan VDMS), le numéro, puis « Confirmer localisation »." },
                new() { Id = "m3", Title = "Qualifier et engager", Content = "Choisir la nature, la procédure (F1 à F9) et les engins disponibles, puis valider et imprimer l'ODE." }
            ]
        });
    }

    private void Journal(ArgosUserDto user, string action, string target, string details)
        => db.AddJournal(new JournalEntry
        {
            At = DateTimeOffset.UtcNow, UserId = user.UserId, UserName = user.DisplayName,
            Action = action, Target = target, Details = details
        });

    private Task NotifyAsync() => hub.BroadcastAsync(new RealtimeMessage(RealtimeTypes.IliasChanged));
}
