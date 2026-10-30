using System.Collections.ObjectModel;
using System.Globalization;

namespace Argos.ViewModels.Accessoires;

/// <summary>
/// Calculatrice « Standard » de Windows 10 : calcul immédiat de gauche à droite (pas de priorité des opérateurs),
/// fonctions 1/x, x², √x, %, ±, effacement CE / C / ⌫, répétition de « = », historique et mémoire (MC, MR, M+, M−, MS).
/// </summary>
public sealed class CalculatriceViewModel : ViewModelBase
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private string _entry = "0";
    private decimal? _accumulator;
    private string? _pendingOp;
    private decimal? _lastOperand;
    private string? _lastOp;
    private bool _newEntry = true;
    private bool _error;
    private string _expression = string.Empty;
    private decimal _memory;
    private bool _hasMemory;

    public CalculatriceViewModel()
    {
        DigitCommand = new RelayCommand(p => Digit(p as string ?? "0"));
        OperatorCommand = new RelayCommand(p => Operator(p as string ?? "+"));
        EqualsCommand = new RelayCommand(Equals);
        DecimalCommand = new RelayCommand(Decimal);
        NegateCommand = new RelayCommand(Negate);
        ClearEntryCommand = new RelayCommand(ClearEntry);
        ClearCommand = new RelayCommand(Clear);
        BackspaceCommand = new RelayCommand(Backspace);
        FunctionCommand = new RelayCommand(p => Function(p as string ?? string.Empty));
        PercentCommand = new RelayCommand(Percent);
        MemoryCommand = new RelayCommand(p => Memory(p as string ?? string.Empty));
        ClearHistoryCommand = new RelayCommand(() => { History.Clear(); OnPropertyChanged(nameof(HasHistory)); });
    }

    public string Display => _error ? _entry : Format(Current, editing: !_newEntry);
    public string Expression => _expression;
    public bool HasMemory => _hasMemory;
    public string MemoryText => _hasMemory ? "M = " + Format(_memory) : string.Empty;
    public ObservableCollection<HistoryEntry> History { get; } = [];
    public bool HasHistory => History.Count > 0;
    private bool _showHistory;
    public bool ShowHistory { get => _showHistory; set => SetProperty(ref _showHistory, value); }
    public RelayCommand ToggleHistoryCommand => new(() => ShowHistory = !ShowHistory);

    /// <summary>Réutilise un résultat de l'historique (comme un clic dans l'historique de Windows).</summary>
    public void Reuse(decimal value)
    {
        if (_error) Clear();
        SetEntry(value);
        _newEntry = false;
        if (_pendingOp is null) _expression = string.Empty;
        Notify();
    }

    public RelayCommand DigitCommand { get; }
    public RelayCommand OperatorCommand { get; }
    public RelayCommand EqualsCommand { get; }
    public RelayCommand DecimalCommand { get; }
    public RelayCommand NegateCommand { get; }
    public RelayCommand ClearEntryCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand BackspaceCommand { get; }
    public RelayCommand FunctionCommand { get; }
    public RelayCommand PercentCommand { get; }
    public RelayCommand MemoryCommand { get; }
    public RelayCommand ClearHistoryCommand { get; }

    private decimal Current => decimal.TryParse(_entry, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0;

    // ===== Saisie =====

    public void Digit(string digit)
    {
        if (_error) Clear();
        if (_newEntry)
        {
            _entry = "0";
            _newEntry = false;
            if (_pendingOp is null) { _expression = string.Empty; _lastOp = null; }
        }
        if (_entry.Replace("-", "").Replace(".", "").Length >= 16) return;
        _entry = _entry == "0" ? digit : _entry == "-0" ? "-" + digit : _entry + digit;
        Notify();
    }

    public void Decimal()
    {
        if (_error) Clear();
        if (_newEntry)
        {
            _entry = "0";
            _newEntry = false;
        }
        if (!_entry.Contains('.')) _entry += ".";
        Notify();
    }

    public void Backspace()
    {
        if (_error) { Clear(); return; }
        if (_newEntry) return;
        _entry = _entry.Length <= 1 || (_entry.Length == 2 && _entry[0] == '-') ? "0" : _entry[..^1];
        Notify();
    }

    public void Negate()
    {
        if (_error) return;
        _entry = _entry.StartsWith('-') ? _entry[1..] : _entry == "0" ? "0" : "-" + _entry;
        Notify();
    }

    public void ClearEntry()
    {
        if (_error) { Clear(); return; }
        _entry = "0";
        _newEntry = true;
        Notify();
    }

    public void Clear()
    {
        _entry = "0";
        _accumulator = null;
        _pendingOp = null;
        _lastOp = null;
        _lastOperand = null;
        _newEntry = true;
        _error = false;
        _expression = string.Empty;
        Notify();
    }

    // ===== Opérations =====

    public void Operator(string op)
    {
        if (_error) return;
        if (_pendingOp is not null && !_newEntry)
        {
            if (!Apply(_accumulator ?? 0, _pendingOp, Current, out var r)) return;
            _accumulator = r;
            SetEntry(r);
        }
        else if (_pendingOp is null)
        {
            _accumulator = Current;
        }
        _pendingOp = op;
        _lastOp = null;
        _newEntry = true;
        _expression = $"{Format(_accumulator ?? 0)} {op}";
        Notify();
    }

    public void Equals()
    {
        if (_error) { Clear(); return; }
        decimal left, right;
        string op;
        if (_pendingOp is not null)
        {
            left = _accumulator ?? 0;
            right = Current;                 // « 5 + = » donne 10, comme Windows
            op = _pendingOp;
        }
        else if (_lastOp is not null && _lastOperand is { } repeat)
        {
            left = Current;                 // « = » répété : refait la dernière opération
            right = repeat;
            op = _lastOp;
        }
        else
        {
            _expression = $"{Format(Current)} =";
            _newEntry = true;
            Notify();
            return;
        }

        var text = $"{Format(left)} {op} {Format(right)} =";
        if (!Apply(left, op, right, out var result))
        {
            _expression = text;
            Notify();
            return;
        }
        _expression = text;
        var reused = result;
        History.Insert(0, new HistoryEntry(text, Format(result), new RelayCommand(() => Reuse(reused))));
        if (History.Count > 30) History.RemoveAt(History.Count - 1);
        OnPropertyChanged(nameof(HasHistory));
        SetEntry(result);
        _lastOp = op;
        _lastOperand = right;
        _pendingOp = null;
        _accumulator = null;
        _newEntry = true;
        Notify();
    }

    public void Percent()
    {
        if (_error) return;
        // Comme Windows : avec + ou −, x % = accumulateur × x / 100 ; avec × ou ÷, x % = x / 100.
        var value = Current;
        var result = _pendingOp is "+" or "−" ? (_accumulator ?? 0) * value / 100 : value / 100;
        SetEntry(result);
        _newEntry = false;
        Notify();
    }

    public void Function(string f)
    {
        if (_error) return;
        var x = Current;
        decimal r;
        string label;
        switch (f)
        {
            case "inv":
                if (x == 0) { Error("Division par zéro impossible"); return; }
                r = 1 / x; label = $"1/({Format(x)})"; break;
            case "sqr":
                try { r = x * x; } catch (OverflowException) { Error("Dépassement de capacité"); return; }
                label = $"sqr({Format(x)})"; break;
            case "sqrt":
                if (x < 0) { Error("Entrée non valide"); return; }
                r = (decimal)Math.Sqrt((double)x); label = $"√({Format(x)})"; break;
            default: return;
        }
        _expression = _pendingOp is null ? label : $"{Format(_accumulator ?? 0)} {_pendingOp} {label}";
        SetEntry(r);
        _newEntry = false;
        Notify();
    }

    public void Memory(string action)
    {
        if (_error) return;
        switch (action)
        {
            case "MC": _memory = 0; _hasMemory = false; break;
            case "MR": if (_hasMemory) { SetEntry(_memory); _newEntry = false; } break;
            case "M+": _memory += Current; _hasMemory = true; _newEntry = true; break;
            case "M-": _memory -= Current; _hasMemory = true; _newEntry = true; break;
            case "MS": _memory = Current; _hasMemory = true; _newEntry = true; break;
        }
        Notify();
    }

    /// <summary>Touches du clavier (la vue transmet le texte saisi et quelques touches spéciales).</summary>
    public bool Key(string key)
    {
        switch (key)
        {
            case { Length: 1 } when char.IsAsciiDigit(key[0]): Digit(key); return true;
            case "," or ".": Decimal(); return true;
            case "+": Operator("+"); return true;
            case "-": Operator("−"); return true;
            case "*" or "x" or "X": Operator("×"); return true;
            case "/": Operator("÷"); return true;
            case "=" or "Enter": Equals(); return true;
            case "%": Percent(); return true;
            case "Back": Backspace(); return true;
            case "Escape": Clear(); return true;
            case "Delete": ClearEntry(); return true;
            default: return false;
        }
    }

    private bool Apply(decimal a, string op, decimal b, out decimal result)
    {
        result = 0;
        try
        {
            switch (op)
            {
                case "+": result = a + b; return true;
                case "−": result = a - b; return true;
                case "×": result = a * b; return true;
                case "÷":
                    if (b == 0) { Error(a == 0 ? "Résultat non défini" : "Division par zéro impossible"); return false; }
                    result = a / b; return true;
            }
        }
        catch (OverflowException)
        {
            Error("Dépassement de capacité");
        }
        return false;
    }

    private void Error(string message)
    {
        _error = true;
        _entry = message;
        _pendingOp = null;
        _accumulator = null;
        _newEntry = true;
        Notify();
    }

    private void SetEntry(decimal value)
    {
        var text = Math.Round(value, 15).ToString(CultureInfo.InvariantCulture);
        _entry = text.Contains('.') ? text.TrimEnd('0').TrimEnd('.') : text;
    }

    /// <summary>« 1 234 567,89 » ; pendant la saisie, la virgule finale et les zéros tapés sont gardés.</summary>
    private string Format(decimal value, bool editing = false)
    {
        if (editing)
        {
            var negative = _entry.StartsWith('-');
            var raw = negative ? _entry[1..] : _entry;
            var parts = raw.Split('.');
            var integer = decimal.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var i) ? i.ToString("#,0", Fr) : parts[0];
            return (negative ? "-" : "") + integer + (parts.Length > 1 ? "," + parts[1] : "");
        }
        var rounded = Math.Round(value, 12);
        var text = rounded.ToString("#,0.############", Fr);
        return text == "-0" ? "0" : text;
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(Display));
        OnPropertyChanged(nameof(Expression));
        OnPropertyChanged(nameof(HasMemory));
        OnPropertyChanged(nameof(MemoryText));
    }
}

public sealed record HistoryEntry(string Expression, string Result, RelayCommand ReuseCommand);
