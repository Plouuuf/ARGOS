using System.Collections.ObjectModel;
using System.Text.Json;
using Argos.Services;
using Argos.Shared.Contracts;
using Argos.ViewModels.Ode;
using Argos.Views.Apps.Ilias;
using Avalonia.Platform.Storage;

namespace Argos.ViewModels.Ilias;

public sealed record CategoryRow(string Id, string Name, string Description, int Count, int Depth, RelayCommand SelectCommand)
{
    public Avalonia.Thickness Indent => new(Depth * 16, 0, 0, 0);
}

public sealed record DocumentRow(string Id, string Title, string Description, string Category, string Published, string Updated,
    string Size, int Version, bool Downloadable, bool Archived, RelayCommand OpenCommand, RelayCommand DownloadCommand, RelayCommand EditCommand);

public sealed record FormationRow(string Id, string Title, string Description, int Modules, int Done, string State, double Progress,
    RelayCommand OpenCommand)
{
    public string ProgressLabel => $"{Done}/{Modules} modules";
}

public sealed record QuizRow(string Id, string Title, string Description, int Questions, string Timer, string LastResult,
    RelayCommand StartCommand);

public sealed record ResultRow(string Date, string Quiz, string Score, bool Passed);

public sealed record Choice(string Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Question en cours d'édition (administration).</summary>
public sealed class QuestionEditViewModel(IliasQuestion q, Action<QuestionEditViewModel> remove) : ViewModelBase
{
    public IliasQuestion Question { get; } = q;
    public string Text { get => Question.Text; set { Question.Text = value ?? string.Empty; OnPropertyChanged(); } }
    public string Options
    {
        get => string.Join("\n", Question.Options);
        set { Question.Options = (value ?? string.Empty).Split('\n').Select(o => o.Trim()).Where(o => o.Length > 0).ToList(); OnPropertyChanged(); }
    }
    public string Correct { get => Question.Correct < 0 ? string.Empty : (Question.Correct + 1).ToString(); set { Question.Correct = int.TryParse(value, out var n) ? n - 1 : -1; OnPropertyChanged(); } }
    public string Explanation { get => Question.Explanation; set { Question.Explanation = value ?? string.Empty; OnPropertyChanged(); } }
    private RelayCommand? _remove;
    public RelayCommand RemoveCommand => _remove ??= new RelayCommand(() => remove(this));
}

/// <summary>Module en cours d'édition (administration).</summary>
public sealed class ModuleEditViewModel(IliasModule m, IReadOnlyList<Choice> documents, Action<ModuleEditViewModel> remove) : ViewModelBase
{
    public IliasModule Module { get; } = m;
    public string Title { get => Module.Title; set { Module.Title = value ?? string.Empty; OnPropertyChanged(); } }
    public string Content { get => Module.Content; set { Module.Content = value ?? string.Empty; OnPropertyChanged(); } }
    public IReadOnlyList<Choice> Documents { get; } = documents;
    public Choice? Document
    {
        get => Documents.FirstOrDefault(d => d.Id == Module.DocumentId);
        set { Module.DocumentId = value?.Id ?? string.Empty; OnPropertyChanged(); }
    }
    private RelayCommand? _remove;
    public RelayCommand RemoveCommand => _remove ??= new RelayCommand(() => remove(this));
}

/// <summary>ILIAS : accueil, bibliothèque, formations, questionnaires, suivi pédagogique, administration.</summary>
public sealed class IliasAppViewModel : ViewModelBase, IDisposable
{
    public enum Page { Accueil, Bibliotheque, Formations, Questionnaires, Suivi, Administration }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly DesktopViewModel _desktop;
    private Page _page = Page.Accueil;
    private string? _message;
    private bool _messageIsError;
    private string _search = string.Empty;
    private string? _categoryId;

    public IliasAppViewModel(DesktopViewModel desktop)
    {
        _desktop = desktop;
        NavigateCommand = new RelayCommand(p => { if (p is string s && Enum.TryParse<Page>(s, out var page)) CurrentPage = page; });
        AllCategoriesCommand = new RelayCommand(() => SelectCategory(null));
        RefreshCommand = new RelayCommand(() => _ = ArgosSession.Current?.Ilias.RefreshAsync());

        NewCategoryCommand = new RelayCommand(() => EditCategory(null));
        SaveCategoryCommand = new RelayCommand(() => _ = SaveAsync("api/ilias/categories", EditedCategory, "Catégorie enregistrée."));
        NewDocumentCommand = new RelayCommand(() => EditDocument(null));
        SaveDocumentCommand = new RelayCommand(() => _ = SaveDocumentAsync());
        NewFormationCommand = new RelayCommand(() => EditFormation(null));
        SaveFormationCommand = new RelayCommand(() => _ = SaveFormationAsync());
        AddModuleCommand = new RelayCommand(() => Modules.Add(new ModuleEditViewModel(new IliasModule(), DocumentChoices, m => Modules.Remove(m))));
        NewQuizCommand = new RelayCommand(() => EditQuiz(null));
        SaveQuizCommand = new RelayCommand(() => _ = SaveQuizAsync());
        AddQuestionCommand = new RelayCommand(() => Questions.Add(new QuestionEditViewModel(new IliasQuestion(), q => Questions.Remove(q))));

        if (ArgosSession.Current is { } session)
        {
            session.Ilias.Changed += Build;
            if (!session.Ilias.IsLoaded) _ = session.Ilias.RefreshAsync();
        }
        EditCategory(null);
        EditDocument(null);
        EditFormation(null);
        EditQuiz(null);
        Build();
    }

    /// <summary>Accès aux fichiers et au lancement de documents (fournis par la vue).</summary>
    public IStorageProvider? Storage { get; set; }
    public ILauncher? Launcher { get; set; }

    private IliasSnapshot S => ArgosSession.Current?.Ilias.Snapshot ?? new IliasSnapshot();

    // ===== Navigation =====

    public Page CurrentPage
    {
        get => _page;
        set
        {
            if (!SetProperty(ref _page, value)) return;
            foreach (var p in Enum.GetNames<Page>()) OnPropertyChanged("Is" + p);
            Message = null;
        }
    }
    public bool IsAccueil => CurrentPage == Page.Accueil;
    public bool IsBibliotheque => CurrentPage == Page.Bibliotheque;
    public bool IsFormations => CurrentPage == Page.Formations;
    public bool IsQuestionnaires => CurrentPage == Page.Questionnaires;
    public bool IsSuivi => CurrentPage == Page.Suivi;
    public bool IsAdministration => CurrentPage == Page.Administration;
    public bool CanAdmin => S.CanAdmin;

    public RelayCommand NavigateCommand { get; }
    public RelayCommand AllCategoriesCommand { get; }
    public RelayCommand RefreshCommand { get; }

    public string? Message { get => _message; private set { if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage)); } }
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public bool MessageIsError { get => _messageIsError; private set => SetProperty(ref _messageIsError, value); }

    public string UserName => ArgosSession.Current?.User.DisplayName ?? string.Empty;
    public string UserAccess => AccessLevels.HighestLabel(ArgosSession.Current?.User.Levels);

    // ===== Accueil =====

    public ObservableCollection<CategoryRow> MainCategories { get; } = [];
    public ObservableCollection<DocumentRow> Latest { get; } = [];
    public ObservableCollection<DocumentRow> Recent { get; } = [];
    public ObservableCollection<FormationRow> FormationsHome { get; } = [];

    // ===== Bibliothèque =====

    public string Search { get => _search; set { if (SetProperty(ref _search, value)) BuildDocuments(); } }
    public ObservableCollection<CategoryRow> Categories { get; } = [];
    public ObservableCollection<DocumentRow> Documents { get; } = [];
    public string CategoryTitle => _categoryId is null ? "Tous les documents" : ArgosSession.Current?.Ilias.FindCategory(_categoryId)?.Name ?? "Catégorie";
    public string CategoryDescription => _categoryId is null ? "Toutes les catégories" : ArgosSession.Current?.Ilias.FindCategory(_categoryId)?.Description ?? string.Empty;

    // ===== Formations, questionnaires, suivi =====

    public ObservableCollection<FormationRow> Formations { get; } = [];
    public ObservableCollection<QuizRow> Quizzes { get; } = [];
    public ObservableCollection<ResultRow> Results { get; } = [];
    public int FormationsFollowed { get; private set; }
    public int FormationsValidated { get; private set; }
    public int QuizzesPassed { get; private set; }
    public int QuizzesFailed { get; private set; }
    public double GlobalProgress { get; private set; }
    public string GlobalProgressLabel => $"{GlobalProgress:P0}";
    public ObservableCollection<string> ValidatedFormations { get; } = [];

    // ===== Construction =====

    private void Build()
    {
        var s = S;
        var store = ArgosSession.Current?.Ilias;
        if (store?.LastError is { } error) ShowMessage(error, true);
        OnPropertyChanged(nameof(CanAdmin));

        Fill(MainCategories, s.Categories.Where(c => string.IsNullOrEmpty(c.ParentId) && !c.Archived).Select(c => CategoryRowOf(c, 0)));
        var cats = new List<CategoryRow>();
        void Walk(string parent, int depth)
        {
            foreach (var c in s.Categories.Where(c => c.ParentId == parent && !c.Archived))
            {
                cats.Add(CategoryRowOf(c, depth));
                Walk(c.Id, depth + 1);
            }
        }
        Walk(string.Empty, 0);
        Fill(Categories, cats);

        Fill(Latest, s.Documents.Where(d => !d.Archived).OrderByDescending(d => d.UpdatedAt).Take(6).Select(DocRowOf));
        Fill(Recent, s.RecentDocuments.Select(id => s.Documents.FirstOrDefault(d => d.Id == id)).Where(d => d is not null).Select(d => DocRowOf(d!)));
        BuildDocuments();

        var formations = s.Formations.Where(f => !f.Archived).Select(FormationRowOf).ToList();
        Fill(Formations, formations);
        Fill(FormationsHome, formations.Take(4));

        Fill(Quizzes, s.Quizzes.Where(q => !q.Archived).Select(q =>
        {
            var last = s.MyResults.FirstOrDefault(r => r.QuizId == q.Id);
            var id = q.Id;
            return new QuizRow(q.Id, q.Title, q.Description, q.Questions.Count,
                q.TimeLimitMinutes > 0 ? $"{q.TimeLimitMinutes} min" : "Sans limite",
                last is null ? "Jamais passé" : $"Dernier résultat : {last.Percent} % ({(last.Passed ? "réussi" : "échoué")})",
                new RelayCommand(() => _desktop.OpenWindow(IliasWindows.Quiz(_desktop, id))));
        }));
        Fill(Results, s.MyResults.Select(r => new ResultRow(r.At.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), r.QuizTitle,
            $"{r.Score}/{r.Total} · {r.Percent} %", r.Passed)));

        FormationsFollowed = s.MyProgress.Count(p => p.CompletedModules.Count > 0);
        FormationsValidated = s.MyProgress.Count(p => p.CompletedAt is not null);
        QuizzesPassed = s.MyResults.Where(r => r.Passed).Select(r => r.QuizId).Distinct().Count();
        QuizzesFailed = s.MyResults.Count(r => !r.Passed);
        var totalModules = s.Formations.Where(f => !f.Archived).Sum(f => f.Modules.Count);
        GlobalProgress = totalModules == 0 ? 0 : (double)s.MyProgress.Sum(p => p.CompletedModules.Count) / totalModules;
        Fill(ValidatedFormations, s.MyProgress.Where(p => p.CompletedAt is not null).Select(p =>
            $"{s.Formations.FirstOrDefault(f => f.Id == p.FormationId)?.Title ?? p.FormationId} - validée le {p.CompletedAt!.Value.ToLocalTime():dd/MM/yyyy}"));
        foreach (var n in new[] { nameof(FormationsFollowed), nameof(FormationsValidated), nameof(QuizzesPassed), nameof(QuizzesFailed),
                                  nameof(GlobalProgress), nameof(GlobalProgressLabel) })
            OnPropertyChanged(n);

        // Listes de l'administration
        Fill(CategoryChoices, s.Categories.Select(c => new Choice(c.Id, c.Name)));
        Fill(ParentChoices, new[] { new Choice(string.Empty, "(catégorie principale)") }.Concat(s.Categories.Select(c => new Choice(c.Id, c.Name))));
        Fill(DocumentChoices, new[] { new Choice(string.Empty, "(aucun support)") }.Concat(s.Documents.Select(d => new Choice(d.Id, d.Title))));
        Fill(QuizChoices, new[] { new Choice(string.Empty, "(pas d'évaluation)") }.Concat(s.Quizzes.Select(q => new Choice(q.Id, q.Title))));
        Fill(CategoryEditChoices, new[] { new Choice(string.Empty, "Nouvelle catégorie…") }.Concat(s.Categories.Select(c => new Choice(c.Id, c.Name))));
        Fill(DocumentEditChoices, new[] { new Choice(string.Empty, "Nouveau document…") }.Concat(s.Documents.Select(d => new Choice(d.Id, d.Title))));
        Fill(FormationEditChoices, new[] { new Choice(string.Empty, "Nouvelle formation…") }.Concat(s.Formations.Select(f => new Choice(f.Id, f.Title))));
        Fill(QuizEditChoices, new[] { new Choice(string.Empty, "Nouveau questionnaire…") }.Concat(s.Quizzes.Select(q => new Choice(q.Id, q.Title))));
    }

    private void BuildDocuments()
    {
        var s = S;
        var q = VdmsMap.Normalize(Search);
        var inCategory = _categoryId is null ? null : Descendants(_categoryId);
        Fill(Documents, s.Documents
            .Where(d => !d.Archived || CanAdmin)
            .Where(d => inCategory is null || inCategory.Contains(d.CategoryId))
            .Where(d => q.Length == 0 || VdmsMap.Normalize($"{d.Title} {d.Description} {d.Keywords}").Contains(q))
            .OrderBy(d => d.Title)
            .Select(DocRowOf));
        OnPropertyChanged(nameof(CategoryTitle));
        OnPropertyChanged(nameof(CategoryDescription));
    }

    private HashSet<string> Descendants(string id)
    {
        var set = new HashSet<string> { id };
        bool added;
        do
        {
            added = false;
            foreach (var c in S.Categories.Where(c => set.Contains(c.ParentId) && set.Add(c.Id)))
                added = true;
        } while (added);
        return set;
    }

    private void SelectCategory(string? id)
    {
        _categoryId = id;
        BuildDocuments();
        CurrentPage = Page.Bibliotheque;
    }

    private CategoryRow CategoryRowOf(IliasCategory c, int depth)
    {
        var id = c.Id;
        var count = S.Documents.Count(d => Descendants(id).Contains(d.CategoryId) && !d.Archived);
        return new CategoryRow(c.Id, c.Name, c.Description, count, depth, new RelayCommand(() => SelectCategory(id)));
    }

    private DocumentRow DocRowOf(IliasDocument d)
    {
        var id = d.Id;
        return new DocumentRow(d.Id, d.Title, d.Description, ArgosSession.Current?.Ilias.FindCategory(d.CategoryId)?.Name ?? "—",
            d.PublishedAt.ToLocalTime().ToString("dd/MM/yyyy"), d.UpdatedAt.ToLocalTime().ToString("dd/MM/yyyy"),
            d.Size >= 1024 * 1024 ? $"{d.Size / 1024d / 1024d:0.0} Mo" : $"{Math.Max(1, d.Size / 1024)} Ko", d.Version, d.Downloadable, d.Archived,
            new RelayCommand(() => _ = OpenDocumentAsync(id)),
            new RelayCommand(() => _ = DownloadAsync(id), () => d.Downloadable || CanAdmin),
            new RelayCommand(() => { EditDocument(id); CurrentPage = Page.Administration; }));
    }

    private FormationRow FormationRowOf(IliasFormation f)
    {
        var p = S.MyProgress.FirstOrDefault(x => x.FormationId == f.Id);
        var done = p?.CompletedModules.Count(m => f.Modules.Any(x => x.Id == m)) ?? 0;
        var state = p?.CompletedAt is not null ? "Validée" : done > 0 ? "En cours" : "Non commencée";
        var id = f.Id;
        return new FormationRow(f.Id, f.Title, f.Description, f.Modules.Count, done, state,
            f.Modules.Count == 0 ? 0 : (double)done / f.Modules.Count,
            new RelayCommand(() => _desktop.OpenWindow(IliasWindows.Formation(_desktop, id))));
    }

    // ===== Documents : ouvrir, télécharger =====

    public async Task OpenDocumentAsync(string id)
    {
        var path = await FetchAsync(id);
        if (path is null) return;
        if (Launcher is null || !await Launcher.LaunchFileInfoAsync(new FileInfo(path)))
            ShowMessage("Impossible d'ouvrir le document sur ce poste. Utilise « Télécharger ».", true);
    }

    private async Task DownloadAsync(string id)
    {
        var doc = ArgosSession.Current?.Ilias.FindDocument(id);
        if (doc is null || Storage is null) return;
        var file = await Storage.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = doc.FileName, Title = "Télécharger le document" });
        if (file is null) return;
        var path = await FetchAsync(id);
        if (path is null) return;
        await using var target = await file.OpenWriteAsync();
        await using var source = File.OpenRead(path);
        await source.CopyToAsync(target);
        ShowMessage($"« {doc.Title} » téléchargé.", false);
    }

    private async Task<string?> FetchAsync(string id)
    {
        var session = ArgosSession.Current;
        var doc = session?.Ilias.FindDocument(id);
        if (session is null || doc is null) return null;
        try
        {
            var bytes = await session.Api.GetBytesAsync($"api/ilias/documents/{id}/file");
            var dir = Path.Combine(Path.GetTempPath(), "argos-ilias");
            Directory.CreateDirectory(dir);
            var name = string.Concat((string.IsNullOrWhiteSpace(doc.FileName) ? doc.Id + ".pdf" : doc.FileName).Split(Path.GetInvalidFileNameChars()));
            var path = Path.Combine(dir, $"v{doc.Version}-{name}");
            await File.WriteAllBytesAsync(path, bytes);
            _ = session.Ilias.RefreshAsync();               // « récemment consultés »
            return path;
        }
        catch (Exception ex)
        {
            ShowMessage("Document indisponible : " + ex.Message, true);
            return null;
        }
    }

    // ===== Administration =====

    public ObservableCollection<Choice> CategoryChoices { get; } = [];
    public ObservableCollection<Choice> ParentChoices { get; } = [];
    public ObservableCollection<Choice> DocumentChoices { get; } = [];
    public ObservableCollection<Choice> QuizChoices { get; } = [];
    public ObservableCollection<Choice> CategoryEditChoices { get; } = [];
    public ObservableCollection<Choice> DocumentEditChoices { get; } = [];
    public ObservableCollection<Choice> FormationEditChoices { get; } = [];
    public ObservableCollection<Choice> QuizEditChoices { get; } = [];
    public IReadOnlyList<string> Audiences => IliasAudiences.All;

    public RelayCommand NewCategoryCommand { get; }
    public RelayCommand SaveCategoryCommand { get; }
    public RelayCommand NewDocumentCommand { get; }
    public RelayCommand SaveDocumentCommand { get; }
    public RelayCommand NewFormationCommand { get; }
    public RelayCommand SaveFormationCommand { get; }
    public RelayCommand AddModuleCommand { get; }
    public RelayCommand NewQuizCommand { get; }
    public RelayCommand SaveQuizCommand { get; }
    public RelayCommand AddQuestionCommand { get; }

    // Catégorie
    public IliasCategory EditedCategory { get; private set; } = new();
    public Choice? CategoryPick { get => CategoryEditChoices.FirstOrDefault(c => c.Id == EditedCategory.Id); set => EditCategory(value?.Id); }
    public Choice? CategoryParent
    {
        get => ParentChoices.FirstOrDefault(c => c.Id == EditedCategory.ParentId);
        set { EditedCategory.ParentId = value?.Id ?? string.Empty; OnPropertyChanged(); }
    }

    private void EditCategory(string? id)
    {
        EditedCategory = Clone(ArgosSession.Current?.Ilias.FindCategory(id)) ?? new IliasCategory { Order = S.Categories.Count };
        foreach (var n in new[] { nameof(EditedCategory), nameof(CategoryPick), nameof(CategoryParent) }) OnPropertyChanged(n);
    }

    // Document
    public IliasDocument EditedDocument { get; private set; } = new();
    public Choice? DocumentPick { get => DocumentEditChoices.FirstOrDefault(c => c.Id == EditedDocument.Id); set => EditDocument(value?.Id); }
    public Choice? DocumentCategory
    {
        get => CategoryChoices.FirstOrDefault(c => c.Id == EditedDocument.CategoryId);
        set { EditedDocument.CategoryId = value?.Id ?? string.Empty; OnPropertyChanged(); }
    }
    private byte[]? _pendingFile;
    private string _pendingFileLabel = "Aucun fichier choisi";
    public string PendingFileLabel { get => _pendingFileLabel; private set => SetProperty(ref _pendingFileLabel, value); }

    public void SetPendingFile(string name, string contentType, byte[] data)
    {
        if (data.Length > 15 * 1024 * 1024)
        {
            ShowMessage("Fichier trop lourd (15 Mo maximum).", true);
            return;
        }
        _pendingFile = data;
        EditedDocument.FileName = name;
        EditedDocument.ContentType = contentType;
        PendingFileLabel = $"{name} ({data.Length / 1024} Ko)";
        if (string.IsNullOrWhiteSpace(EditedDocument.Title))
        {
            EditedDocument.Title = Path.GetFileNameWithoutExtension(name);
            OnPropertyChanged(nameof(EditedDocument));
        }
    }

    private void EditDocument(string? id)
    {
        EditedDocument = Clone(ArgosSession.Current?.Ilias.FindDocument(id)) ?? new IliasDocument { CategoryId = _categoryId ?? string.Empty };
        _pendingFile = null;
        PendingFileLabel = id is null ? "Aucun fichier choisi" : $"Fichier actuel : {EditedDocument.FileName} (v{EditedDocument.Version})";
        foreach (var n in new[] { nameof(EditedDocument), nameof(DocumentPick), nameof(DocumentCategory) }) OnPropertyChanged(n);
    }

    private async Task SaveDocumentAsync()
    {
        var ok = await SaveAsync("api/ilias/documents", new IliasDocumentRequest
        {
            Document = EditedDocument,
            FileBase64 = _pendingFile is null ? null : Convert.ToBase64String(_pendingFile)
        }, "Document enregistré.");
        if (ok) EditDocument(null);
    }

    // Formation
    public IliasFormation EditedFormation { get; private set; } = new();
    public Choice? FormationPick { get => FormationEditChoices.FirstOrDefault(c => c.Id == EditedFormation.Id); set => EditFormation(value?.Id); }
    public Choice? FormationQuiz
    {
        get => QuizChoices.FirstOrDefault(c => c.Id == EditedFormation.QuizId);
        set { EditedFormation.QuizId = value?.Id ?? string.Empty; OnPropertyChanged(); }
    }
    public ObservableCollection<ModuleEditViewModel> Modules { get; } = [];

    private void EditFormation(string? id)
    {
        EditedFormation = Clone(ArgosSession.Current?.Ilias.FindFormation(id)) ?? new IliasFormation();
        Modules.Clear();
        foreach (var m in EditedFormation.Modules)
            Modules.Add(new ModuleEditViewModel(m, DocumentChoices, x => Modules.Remove(x)));
        foreach (var n in new[] { nameof(EditedFormation), nameof(FormationPick), nameof(FormationQuiz) }) OnPropertyChanged(n);
    }

    private async Task SaveFormationAsync()
    {
        EditedFormation.Modules = Modules.Select(m => m.Module).ToList();
        if (await SaveAsync("api/ilias/formations", EditedFormation, "Formation enregistrée.")) EditFormation(null);
    }

    // Questionnaire
    public IliasQuiz EditedQuiz { get; private set; } = new();
    public Choice? QuizPick { get => QuizEditChoices.FirstOrDefault(c => c.Id == EditedQuiz.Id); set => EditQuiz(value?.Id); }
    public ObservableCollection<QuestionEditViewModel> Questions { get; } = [];

    private void EditQuiz(string? id)
    {
        EditedQuiz = Clone(ArgosSession.Current?.Ilias.FindQuiz(id)) ?? new IliasQuiz();
        Questions.Clear();
        foreach (var q in EditedQuiz.Questions)
            Questions.Add(new QuestionEditViewModel(q, x => Questions.Remove(x)));
        foreach (var n in new[] { nameof(EditedQuiz), nameof(QuizPick) }) OnPropertyChanged(n);
    }

    private async Task SaveQuizAsync()
    {
        EditedQuiz.Questions = Questions.Select(q => q.Question).ToList();
        if (await SaveAsync("api/ilias/quizzes", EditedQuiz, "Questionnaire enregistré.")) EditQuiz(null);
    }

    private async Task<bool> SaveAsync(string path, object body, string success)
    {
        var session = ArgosSession.Current;
        if (session is null) return false;
        var result = await session.Api.SendAsync(HttpMethod.Post, path, body);
        ShowMessage(result.Ok ? success : result.Error ?? "Enregistrement refusé.", !result.Ok);
        await session.Ilias.RefreshAsync();
        return result.Ok;
    }

    // ===== Outils =====

    private void ShowMessage(string text, bool error)
    {
        MessageIsError = error;
        Message = text;
    }

    private static T? Clone<T>(T? item) where T : class
        => item is null ? null : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(item, Json), Json);

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        var list = items.ToList();
        if (list.SequenceEqual(target)) return;
        target.Clear();
        foreach (var item in list) target.Add(item);
    }

    public void Dispose()
    {
        if (ArgosSession.Current is { } session) session.Ilias.Changed -= Build;
    }
}
