using ArgoBooks.Localization;
using ArgoBooks.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>A table row that can be ticked in select mode.</summary>
public interface ISelectableRow
{
    string Id { get; }
    bool IsSelected { get; set; }
}

/// <summary>What a bulk change did: how many records changed, already matched or were left alone and why, and the undo step it recorded.</summary>
public sealed record BulkEditResult(int Changed, int Unchanged, int MadeElsewhere, int NotOneLine, int MovesStock, IUndoableAction? Action)
{
    public static readonly BulkEditResult Nothing = new(0, 0, 0, 0, 0, null);

    public int Skipped => MadeElsewhere + NotOneLine + MovesStock;
}

/// <summary>Select mode for a list page: ticked records across every page of the filter, Shift ranges, and the last bulk change with its Undo.</summary>
public partial class TableSelection : ObservableObject
{
    private readonly Func<IEnumerable<ISelectableRow>> _rowsOnPage;
    private readonly HashSet<string> _selected = [];
    private List<string> _shown = [];
    private string? _lastToggledId;
    private IUndoableAction? _lastAction;

    public TableSelection(Func<IEnumerable<ISelectableRow>> rowsOnPage)
    {
        _rowsOnPage = rowsOnPage;
        App.UndoRedoManager.StateChanged += OnUndoStateChanged;
    }

    /// <summary>Raised when select mode turns on or off, so the page can show its checkbox column.</summary>
    public event Action<bool>? ActiveChanged;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string? _resultText;

    public IReadOnlyCollection<string> SelectedIds => _selected;

    public bool HasSelection => _selected.Count > 0;

    public string CountText => "{0} selected".TranslateFormat(_selected.Count);

    public bool HasResult => !string.IsNullOrEmpty(ResultText);

    /// <summary>Only while the bulk change is still the latest undo step, so Undo never takes back something else.</summary>
    public bool CanUndo => _lastAction != null && ReferenceEquals(App.UndoRedoManager.SavePoint, _lastAction);

    /// <summary>The header checkbox: ticked when every record the filter shows is, empty when none are, and in between otherwise.</summary>
    public bool? IsAllSelected
    {
        get
        {
            var shown = _shown.Count(_selected.Contains);
            return shown == 0 ? false : shown == _shown.Count ? true : null;
        }
        set
        {
            if (value == true)
                _selected.UnionWith(_shown);
            else
                _selected.Clear();
            MarkRows();
        }
    }

    partial void OnIsActiveChanged(bool value)
    {
        _selected.Clear();
        _lastToggledId = null;
        ClearResult();
        MarkRows();
        ActiveChanged?.Invoke(value);
    }

    partial void OnResultTextChanged(string? value) => OnPropertyChanged(nameof(HasResult));

    [RelayCommand]
    private void Enter() => IsActive = true;

    [RelayCommand]
    private void Exit() => IsActive = false;

    /// <summary>Called after the page filters: the records shown in order, and every record that still exists.</summary>
    public void UpdateShown(IEnumerable<string> shownIds, IEnumerable<string> existingIds)
    {
        _shown = shownIds.ToList();
        _selected.IntersectWith(existingIds);
        Notify();
    }

    /// <summary>A row's checkbox was clicked; with Shift held, every row between it and the last one clicked takes its new state.</summary>
    public void OnRowChecked(ISelectableRow row, bool extendRange)
    {
        var ids = new List<string> { row.Id };
        if (extendRange && _lastToggledId != null)
        {
            var from = _shown.IndexOf(_lastToggledId);
            var to = _shown.IndexOf(row.Id);
            if (from >= 0 && to >= 0)
                ids = _shown.GetRange(Math.Min(from, to), Math.Abs(to - from) + 1);
        }

        if (row.IsSelected)
            _selected.UnionWith(ids);
        else
            _selected.ExceptWith(ids);
        _lastToggledId = row.Id;
        MarkRows();
    }

    /// <summary>Ticks the rows on the current page that are selected.</summary>
    public void MarkRows()
    {
        foreach (var row in _rowsOnPage())
            row.IsSelected = _selected.Contains(row.Id);
        Notify();
    }

    /// <summary>Shows what a bulk change did, and why any records were left as they were.</summary>
    public void ShowResult(BulkEditResult result, string changedText, string alreadyText, Func<int, string> madeElsewhere)
    {
        _lastAction = result.Action;
        var parts = new List<string>();
        if (result.Changed > 0)
            parts.Add(changedText);
        else if (result.Unchanged > 0 && result.Skipped == 0)
            parts.Add(alreadyText);

        var skipped = new List<string>();
        if (result.MadeElsewhere > 0)
            skipped.Add(madeElsewhere(result.MadeElsewhere));
        if (result.NotOneLine > 0)
            skipped.Add(result.NotOneLine == 1 ? "1 without exactly one line".Translate() : "{0} without exactly one line".TranslateFormat(result.NotOneLine));
        if (result.MovesStock > 0)
            skipped.Add(result.MovesStock == 1 ? "1 that moves stock".Translate() : "{0} that move stock".TranslateFormat(result.MovesStock));
        if (skipped.Count > 0)
            parts.Add("Left as they were: {0}.".TranslateFormat(string.Join(", ", skipped)));

        ResultText = string.Join(" ", parts);
        Notify();
    }

    private void ClearResult()
    {
        _lastAction = null;
        ResultText = null;
    }

    [RelayCommand]
    private void Undo()
    {
        if (!CanUndo) return;
        App.UndoRedoManager.Undo();
        ClearResult();
        Notify();
    }

    private void OnUndoStateChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(CanUndo));

    private void Notify()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsAllSelected));
        OnPropertyChanged(nameof(CanUndo));
    }

    /// <summary>Stops listening to undo changes, for when the page is cleaned up.</summary>
    public void Detach() => App.UndoRedoManager.StateChanged -= OnUndoStateChanged;
}
