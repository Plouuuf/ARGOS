using System.Collections.ObjectModel;
using Argos.Services;
using Argos.Shared.Contracts;
using Argos.ViewModels.Ode;
using Argos.Views.Apps.Efibi;

namespace Argos.ViewModels.Efibi;

public sealed record EfibiRow(long Id, string ShareCode, string Intervention, string Victim, string Created, string Status,
    bool Transmitted, RelayCommand OpenCommand)
{
    public string Date { get; init; } = string.Empty;
    public string Time { get; init; } = string.Empty;
    public DateTimeOffset SortTime { get; init; }
    public string Author { get; init; } = string.Empty;
    public string Circumstances { get; init; } = string.Empty;
    public string StatusColor => EfibiText.StatusColor(Status);
    public Avalonia.Media.IBrush StatusBrush => Avalonia.Media.SolidColorBrush.Parse(StatusColor);
}

/// <summary>eFiBi : registre des fiches bilans et recherche avancée (lecture seule).</summary>
public sealed class EfibiAppViewModel : ViewModelBase
{
    public const string Tous = "Tous les statuts";

    private readonly DesktopViewModel _desktop;
    private string _text = string.Empty;
    private string _intervention = string.Empty;
    private string _victim = string.Empty;
    private string _id = string.Empty;
    private string _shareCode = string.Empty;
    private string _status = Tous;
    private DateTime? _from;
    private DateTime? _to;
    private bool _oldestFirst;
    private bool _advanced;
    private int _page = 1;
    private int _pageCount = 1;
    private int _total;
    private bool _loading;
    private string? _error;
    private CancellationTokenSource? _debounce;

    public EfibiAppViewModel(DesktopViewModel desktop)
    {
        _desktop = desktop;
        SearchCommand = new RelayCommand(() => _ = LoadAsync(1));
        RefreshCommand = new RelayCommand(() => _ = LoadAsync(_page));
        NextCommand = new RelayCommand(() => _ = LoadAsync(_page + 1), () => _page < _pageCount);
        PreviousCommand = new RelayCommand(() => _ = LoadAsync(_page - 1), () => _page > 1);
        ToggleAdvancedCommand = new RelayCommand(() => Advanced = !Advanced);
        ResetCommand = new RelayCommand(Reset);
        SortCommand = new RelayCommand(p => Sort(p as string ?? "date"));
        _ = LoadAsync(1);
    }

    // ===== Critères =====

    public string Text { get => _text; set { if (SetProperty(ref _text, value)) Debounce(); } }
    public string Intervention { get => _intervention; set => SetProperty(ref _intervention, value); }
    public string Victim { get => _victim; set => SetProperty(ref _victim, value); }
    public string Identifier { get => _id; set => SetProperty(ref _id, value); }
    public string ShareCode { get => _shareCode; set => SetProperty(ref _shareCode, value); }
    public string Status { get => _status; set { if (SetProperty(ref _status, value ?? Tous)) _ = LoadAsync(1); } }
    public DateTime? From { get => _from; set => SetProperty(ref _from, value); }
    public DateTime? To { get => _to; set => SetProperty(ref _to, value); }
    public bool OldestFirst { get => _oldestFirst; set { if (SetProperty(ref _oldestFirst, value)) _ = LoadAsync(1); } }
    public bool Advanced { get => _advanced; set => SetProperty(ref _advanced, value); }
    public ObservableCollection<string> Statuses { get; } = [Tous];

    // ===== Résultats =====

    public ObservableCollection<EfibiRow> Items { get; } = [];
    public string PageLabel => $"Page {_page} / {_pageCount}";
    public string ResultLabel => _total == 0 ? "Aucune fiche" : $"{_total} fiche(s)";
    public bool Loading { get => _loading; private set => SetProperty(ref _loading, value); }
    public string? Error { get => _error; private set { if (SetProperty(ref _error, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public bool IsEmpty => Items.Count == 0 && !Loading && !HasError;

    public RelayCommand SearchCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand PreviousCommand { get; }
    public RelayCommand ToggleAdvancedCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand SortCommand { get; }

    // ===== Tri par colonne (comme CORTEX : un clic trie, un second inverse) =====

    private string _sortKey = "date";
    private bool _sortDescending = true;

    private void Sort(string key)
    {
        _sortDescending = _sortKey == key ? !_sortDescending : key == "date";
        _sortKey = key;
        ApplySort();
    }

    private void ApplySort()
    {
        Func<EfibiRow, IComparable> selector = _sortKey switch
        {
            "intervention" => r => r.Intervention,
            "author" => r => r.Author,
            "victim" => r => r.Victim,
            "circumstances" => r => r.Circumstances,
            "status" => r => r.Status,
            _ => r => r.SortTime
        };
        var sorted = (_sortDescending ? Items.OrderByDescending(selector) : Items.OrderBy(selector)).ToList();
        Items.Clear();
        foreach (var row in sorted) Items.Add(row);
    }

    private void Debounce()
    {
        _debounce?.Cancel();
        var cts = _debounce = new CancellationTokenSource();
        _ = Task.Delay(400, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled) Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = LoadAsync(1));
        }, TaskScheduler.Default);
    }

    private void Reset()
    {
        _text = _intervention = _victim = _id = _shareCode = string.Empty;
        _status = Tous;
        _from = _to = null;
        _oldestFirst = false;
        foreach (var n in new[] { nameof(Text), nameof(Intervention), nameof(Victim), nameof(Identifier), nameof(ShareCode),
                                  nameof(Status), nameof(From), nameof(To), nameof(OldestFirst) })
            OnPropertyChanged(n);
        _ = LoadAsync(1);
    }

    public async Task LoadAsync(int page)
    {
        var session = ArgosSession.Current;
        if (session is null) return;
        Loading = true;
        try
        {
            var query = new EfibiQuery
            {
                Text = Text,
                Intervention = Intervention,
                Victim = Victim,
                Id = long.TryParse(Identifier, out var id) ? id : null,
                ShareCode = ShareCode,
                Status = Status == Tous ? null : Status,
                From = From is { } f ? new DateTimeOffset(f.Date, TimeZoneInfo.Local.GetUtcOffset(f.Date)) : null,
                To = To is { } t ? new DateTimeOffset(t.Date.AddDays(1), TimeZoneInfo.Local.GetUtcOffset(t.Date)) : null,
                OldestFirst = OldestFirst,
                Page = Math.Max(1, page),
                PageSize = 25
            };
            var result = await session.Api.PostAsync<EfibiPage>("api/efibi/search", query);
            Error = result.Error;
            _page = result.Page;
            _pageCount = result.PageCount;
            _total = result.Total;
            Items.Clear();
            foreach (var s in result.Items)
            {
                var rowId = s.Id;
                var at = s.TransmittedAt ?? s.StartedAt ?? s.CreatedAt;
                Items.Add(new EfibiRow(s.Id, s.ShareCode, Or(s.InterventionNumber), Or(s.VictimName),
                    s.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), Or(s.TransmissionStatus),
                    s.TransmittedAt is not null || s.TransmissionStatus.StartsWith("Transmis", StringComparison.OrdinalIgnoreCase),
                    new RelayCommand(() => _desktop.OpenWindow(EfibiWindows.Record(_desktop, rowId))))
                {
                    Date = at.ToLocalTime().ToString("dd/MM/yyyy"),
                    Time = at.ToLocalTime().ToString("HH:mm"),
                    SortTime = at,
                    Author = Or(s.Author),
                    Circumstances = Or(s.Circumstances)
                });
            }
            if (_sortKey != "date" || !_sortDescending) ApplySort();
            var keep = Status;
            foreach (var st in result.Statuses.Where(st => !Statuses.Contains(st)))
                Statuses.Add(st);
            _status = keep;
        }
        catch (Exception ex)
        {
            Error = ex.Message.Contains("403") ? "Accès refusé : le rôle Discord eFIBI est nécessaire." : "Serveur ARGOS injoignable.";
        }
        finally
        {
            Loading = false;
            foreach (var n in new[] { nameof(PageLabel), nameof(ResultLabel), nameof(IsEmpty), nameof(Status) })
                OnPropertyChanged(n);
            NextCommand.RaiseCanExecuteChanged();
            PreviousCommand.RaiseCanExecuteChanged();
        }
    }

    private static string Or(string? v) => string.IsNullOrWhiteSpace(v) ? EfibiText.NonRenseigne : v;
}

/// <summary>Fiche bilan en lecture seule.</summary>
public sealed class EfibiRecordViewModel : ViewModelBase
{
    private readonly DesktopViewModel _desktop;
    private readonly string _key;
    private EfibiRecord? _record;
    private string? _error;
    private bool _loading = true;

    public EfibiRecordViewModel(DesktopViewModel desktop, long id, string key)
    {
        _desktop = desktop;
        _key = key;
        Id = id;
        CloseCommand = new RelayCommand(() => _desktop.CloseWindow(_key));
        _ = LoadAsync();
    }

    public long Id { get; }
    public bool Loading { get => _loading; private set => SetProperty(ref _loading, value); }
    public string? Error { get => _error; private set { if (SetProperty(ref _error, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public bool HasRecord => _record is not null && !HasError;
    public RelayCommand CloseCommand { get; }

    public string Title => $"Fiche bilan n° {Id}";
    public string Victim => Or(_record?.Summary.VictimName);
    public string Intervention => Or(_record?.Summary.InterventionNumber);
    public string ShareCode => Or(_record?.Summary.ShareCode);
    public string Status => Or(_record?.Summary.TransmissionStatus);
    public string Author => Or(_record?.Summary.Author);
    public string CitizenId => Or(_record?.Summary.CitizenId);
    public string Circumstances => Or(_record?.Summary.Circumstances);
    public string StatusColor => EfibiText.StatusColor(_record?.Summary.TransmissionStatus);
    public Avalonia.Media.IBrush StatusBrush => Avalonia.Media.SolidColorBrush.Parse(StatusColor);
    public string DisplayTimestamp => (_record?.Summary.TransmittedAt ?? _record?.Summary.StartedAt ?? _record?.Summary.CreatedAt) is { } at
        && at > DateTimeOffset.MinValue ? at.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : EfibiText.NonRenseigne;
    public string Created => _record is null ? EfibiText.NonRenseigne : _record.Summary.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy à HH:mm");
    public string Started => Date(_record?.Summary.StartedAt);
    public string Updated => _record is null ? EfibiText.NonRenseigne : _record.Summary.UpdatedAt.ToLocalTime().ToString("dd/MM/yyyy à HH:mm");
    public string TransmissionDate => _record?.Summary.TransmittedAt is { } t ? t.ToLocalTime().ToString("dd/MM/yyyy") : EfibiText.NonRenseigne;
    public string TransmissionTime => _record?.Summary.TransmittedAt is { } t ? t.ToLocalTime().ToString("HH:mm:ss") : EfibiText.NonRenseigne;
    public bool Transmitted => _record?.Summary.TransmittedAt is not null;

    /// <summary>Destination : rubriques de régulation et de transmission de la fiche.</summary>
    public IReadOnlyList<EfibiField> Destination => _record?.Sections
        .Where(s => s.Key is "regulationForm" or "transmissionForm")
        .SelectMany(s => s.Fields).Where(f => f.Value != EfibiText.NonRenseigne).ToList() ?? [];
    public bool HasDestination => Destination.Count > 0;

    public IReadOnlyList<EfibiSection> Sections => _record?.Sections ?? [];

    private async Task LoadAsync()
    {
        var session = ArgosSession.Current;
        if (session is null) { Loading = false; return; }
        try
        {
            _record = await session.Api.GetAsync<EfibiRecord>($"api/efibi/records/{Id}");
            Error = _record.Error;
        }
        catch (Exception ex)
        {
            Error = ex.Message.Contains("403") ? "Accès refusé : le rôle Discord eFIBI est nécessaire." : "Fiche indisponible : " + ex.Message;
        }
        Loading = false;
        foreach (var n in new[] { nameof(HasRecord), nameof(Victim), nameof(Intervention), nameof(ShareCode), nameof(Status), nameof(Author),
                                  nameof(Created), nameof(Started), nameof(Updated), nameof(TransmissionDate), nameof(TransmissionTime),
                                  nameof(Transmitted), nameof(Destination), nameof(HasDestination), nameof(Sections),
                                  nameof(CitizenId), nameof(Circumstances), nameof(StatusColor), nameof(StatusBrush), nameof(DisplayTimestamp) })
            OnPropertyChanged(n);
    }

    /// <summary>Chargement direct d'une fiche (captures d'écran de test).</summary>
    public void Apply(EfibiRecord record)
    {
        _record = record;
        Error = record.Error;
        Loading = false;
        foreach (var n in new[] { nameof(HasRecord), nameof(Victim), nameof(Intervention), nameof(ShareCode), nameof(Status), nameof(Author),
                                  nameof(Created), nameof(Started), nameof(Updated), nameof(TransmissionDate), nameof(TransmissionTime),
                                  nameof(Transmitted), nameof(Destination), nameof(HasDestination), nameof(Sections),
                                  nameof(CitizenId), nameof(Circumstances), nameof(StatusColor), nameof(StatusBrush), nameof(DisplayTimestamp) })
            OnPropertyChanged(n);
    }

    private static string Or(string? v) => string.IsNullOrWhiteSpace(v) ? EfibiText.NonRenseigne : v;
    private static string Date(DateTimeOffset? d) => d is { } x ? x.ToLocalTime().ToString("dd/MM/yyyy à HH:mm") : EfibiText.NonRenseigne;
}
