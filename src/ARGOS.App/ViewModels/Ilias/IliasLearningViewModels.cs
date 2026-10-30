using System.Collections.ObjectModel;
using Argos.Services;
using Argos.Shared.Contracts;
using Avalonia.Threading;

namespace Argos.ViewModels.Ilias;

/// <summary>Module d'une formation : contenu, support PDF, case « terminé ».</summary>
public sealed class ModuleViewModel(IliasModule module, int number, bool done, Func<ModuleViewModel, bool, Task> toggle, Action<string> openDocument)
    : ViewModelBase
{
    private bool _done = done;
    public IliasModule Module { get; } = module;
    public string Title => $"Module {number} · {Module.Title}";
    public string Content => Module.Content;
    public bool HasDocument => !string.IsNullOrEmpty(Module.DocumentId);
    public bool Done
    {
        get => _done;
        set { if (SetProperty(ref _done, value)) _ = toggle(this, value); }
    }
    private RelayCommand? _open;
    public RelayCommand OpenDocumentCommand => _open ??= new RelayCommand(() => openDocument(Module.DocumentId));
}

/// <summary>Formation : objectifs, prérequis, modules, progression, évaluation finale.</summary>
public sealed class FormationViewModel : ViewModelBase, IDisposable
{
    private readonly DesktopViewModel _desktop;
    private readonly string _id;

    public FormationViewModel(DesktopViewModel desktop, string id, Func<string, Task>? openDocument)
    {
        _desktop = desktop;
        _id = id;
        OpenDocument = openDocument;
        StartQuizCommand = new RelayCommand(() =>
        {
            if (Formation?.QuizId is { Length: > 0 } quiz) _desktop.OpenWindow(Argos.Views.Apps.Ilias.IliasWindows.Quiz(_desktop, quiz));
        }, () => HasQuiz);
        if (ArgosSession.Current is { } s) s.Ilias.Changed += Build;
        Build();
    }

    public Func<string, Task>? OpenDocument { get; set; }
    private IliasFormation? Formation => ArgosSession.Current?.Ilias.FindFormation(_id);
    public string Title => Formation?.Title ?? "Formation";
    public string Description => Formation?.Description ?? string.Empty;
    public string Objectives => Or(Formation?.Objectives);
    public string Prerequisites => Or(Formation?.Prerequisites);
    public bool HasQuiz => !string.IsNullOrEmpty(Formation?.QuizId);
    public string QuizTitle => ArgosSession.Current?.Ilias.FindQuiz(Formation?.QuizId)?.Title ?? string.Empty;
    public ObservableCollection<ModuleViewModel> Modules { get; } = [];
    public double Progress { get; private set; }
    public string ProgressLabel { get; private set; } = string.Empty;
    public string State { get; private set; } = string.Empty;
    public RelayCommand StartQuizCommand { get; }

    private void Build()
    {
        var f = Formation;
        var progress = ArgosSession.Current?.Ilias.Snapshot.MyProgress.FirstOrDefault(p => p.FormationId == _id);
        Modules.Clear();
        if (f is not null)
        {
            var n = 1;
            foreach (var m in f.Modules)
                Modules.Add(new ModuleViewModel(m, n++, progress?.CompletedModules.Contains(m.Id) == true, ToggleAsync,
                    doc => { if (OpenDocument is { } open) _ = open(doc); }));
        }
        var done = Modules.Count(m => m.Done);
        Progress = Modules.Count == 0 ? 0 : (double)done / Modules.Count;
        ProgressLabel = $"{done}/{Modules.Count} modules terminés";
        State = progress?.CompletedAt is { } at ? $"Formation validée le {at.ToLocalTime():dd/MM/yyyy}"
            : done == Modules.Count && HasQuiz ? "Modules terminés : passe l'évaluation finale pour valider la formation."
            : done > 0 ? "Formation en cours : reprends là où tu t'es arrêté." : "Formation non commencée.";
        foreach (var name in new[] { nameof(Title), nameof(Description), nameof(Objectives), nameof(Prerequisites), nameof(HasQuiz),
                                     nameof(QuizTitle), nameof(Progress), nameof(ProgressLabel), nameof(State) })
            OnPropertyChanged(name);
        StartQuizCommand.RaiseCanExecuteChanged();
    }

    private async Task ToggleAsync(ModuleViewModel module, bool done)
    {
        var session = ArgosSession.Current;
        if (session is null) return;
        await session.Api.SendAsync(HttpMethod.Post, $"api/ilias/formations/{_id}/modules",
            new IliasModuleRequest { ModuleId = module.Module.Id, Done = done });
        await session.Ilias.RefreshAsync();
    }

    private static string Or(string? v) => string.IsNullOrWhiteSpace(v) ? "—" : v;

    public void Dispose()
    {
        if (ArgosSession.Current is { } s) s.Ilias.Changed -= Build;
    }
}

public sealed class AnswerOption(int index, string text) : ViewModelBase
{
    private bool _selected;
    public int Index { get; } = index;
    public string Text { get; } = text;
    public bool Selected { get => _selected; set => SetProperty(ref _selected, value); }
}

public sealed class QuestionRunViewModel(int number, IliasQuestion question) : ViewModelBase
{
    public string Title => $"Question {number}";
    public string Text => question.Text;
    public ObservableCollection<AnswerOption> Options { get; } = new(question.Options.Select((o, i) => new AnswerOption(i, o)));
    public int Answer => Options.FirstOrDefault(o => o.Selected)?.Index ?? -1;
    public string Group { get; } = "q" + number + "-" + Guid.NewGuid().ToString("N")[..6];
}

/// <summary>Passage d'un questionnaire : chronomètre, réponses, note et corrections.</summary>
public sealed class QuizRunViewModel : ViewModelBase, IDisposable
{
    private readonly DesktopViewModel _desktop;
    private readonly string _key;
    private readonly IliasQuiz? _quiz;
    private readonly DateTime _started = DateTime.Now;
    private readonly DispatcherTimer? _timer;
    private string _timeLeft = string.Empty;
    private IliasResult? _result;
    private bool _busy;

    public QuizRunViewModel(DesktopViewModel desktop, string quizId, string key)
    {
        _desktop = desktop;
        _key = key;
        _quiz = ArgosSession.Current?.Ilias.FindQuiz(quizId);
        if (_quiz is not null)
            for (var i = 0; i < _quiz.Questions.Count; i++)
                Questions.Add(new QuestionRunViewModel(i + 1, _quiz.Questions[i]));
        SubmitCommand = new RelayCommand(() => _ = SubmitAsync(), () => !IsFinished);
        CloseCommand = new RelayCommand(() => _desktop.CloseWindow(_key));
        if (_quiz?.TimeLimitMinutes > 0)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
            Tick();
        }
    }

    public string Title => _quiz?.Title ?? "Questionnaire introuvable";
    public string Description => _quiz?.Description ?? string.Empty;
    public bool HasTimer => _quiz?.TimeLimitMinutes > 0;
    public string TimeLeft { get => _timeLeft; private set => SetProperty(ref _timeLeft, value); }
    public string Rules => _quiz is null ? string.Empty
        : $"{_quiz.Questions.Count} question(s) · réussite à partir de {_quiz.PassPercent} %" +
          (_quiz.TimeLimitMinutes > 0 ? $" · {_quiz.TimeLimitMinutes} minutes" : " · sans limite de temps");
    public ObservableCollection<QuestionRunViewModel> Questions { get; } = [];
    public bool IsFinished => _result is not null;
    public bool IsRunning => !IsFinished;
    public string ResultTitle => _result is null ? string.Empty : _result.Passed ? "Évaluation réussie" : "Évaluation échouée";
    public string ResultScore => _result is null ? string.Empty : $"{_result.Score} / {_result.Total} · {_result.Percent} %";
    public bool Passed => _result?.Passed == true;
    public IReadOnlyList<IliasCorrection> Corrections => _result?.Corrections ?? [];
    public RelayCommand SubmitCommand { get; }
    public RelayCommand CloseCommand { get; }

    private void Tick()
    {
        if (_quiz is null) return;
        var left = TimeSpan.FromMinutes(_quiz.TimeLimitMinutes) - (DateTime.Now - _started);
        if (left <= TimeSpan.Zero)
        {
            TimeLeft = "00:00";
            _timer?.Stop();
            if (!IsFinished) _ = SubmitAsync();     // temps écoulé : copie rendue
            return;
        }
        TimeLeft = $"{(int)left.TotalMinutes:00}:{left.Seconds:00}";
    }

    private async Task SubmitAsync()
    {
        var session = ArgosSession.Current;
        if (session is null || _quiz is null || _busy) return;
        _busy = true;
        try
        {
            _result = await session.Api.PostAsync<IliasResult>($"api/ilias/quizzes/{_quiz.Id}/submit", new IliasSubmitRequest
            {
                Answers = Questions.Select(q => q.Answer).ToList(),
                DurationSeconds = (int)(DateTime.Now - _started).TotalSeconds
            });
            _timer?.Stop();
            foreach (var n in new[] { nameof(IsFinished), nameof(IsRunning), nameof(ResultTitle), nameof(ResultScore), nameof(Passed), nameof(Corrections) })
                OnPropertyChanged(n);
            SubmitCommand.RaiseCanExecuteChanged();
            _ = session.Ilias.RefreshAsync();
        }
        catch (Exception ex)
        {
            _desktop.ShowNotification("ILIAS", "Envoi des réponses impossible : " + ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    public void Dispose() => _timer?.Stop();
}
