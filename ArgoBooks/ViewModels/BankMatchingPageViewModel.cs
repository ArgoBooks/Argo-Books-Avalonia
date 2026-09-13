using System.Collections.ObjectModel;
using ArgoBooks.Controls;
using ArgoBooks.Core;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Services;
using ArgoBooks.Helpers;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.Shared.Telemetry;
using ArgoBooks.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Bank Matching page. Imports a bank statement (via the smart importer) and
/// matches each line against recorded expenses and revenue.
/// </summary>
public partial class BankMatchingPageViewModel : SortablePageViewModelBase
{
    private readonly BankMatchingService _matcher = new();
    private readonly BankMatchingOptions _options = new();
    private BankMatchingResult? _result;

    /// <summary>All bank line rows from the last match (unfiltered, unpaged).</summary>
    private readonly List<BankLineRow> _allRows = [];

    /// <summary>All unmatched book records from the last match (unfiltered).</summary>
    private List<BookRecordRef> _allMissing = [];

    public BankMatchingPageViewModel()
    {
        SortColumn = "Date";
        SortDirection = SortDirection.Descending;

        Reload();

        if (App.NavigationService != null)
            App.NavigationService.Navigated += OnNavigated;

        if (App.BankMatchingModalsViewModel != null)
        {
            App.BankMatchingModalsViewModel.CandidateChosen += OnCandidateChosen;
            App.BankMatchingModalsViewModel.FiltersApplied += OnFiltersApplied;
            App.BankMatchingModalsViewModel.MissingFiltersApplied += OnMissingFiltersApplied;
        }
    }

    /// <summary>
    /// Unsubscribes from the events wired up in the constructor so the VM isn't kept alive (and
    /// reacting) after a company switch.
    /// </summary>
    public override void Cleanup()
    {
        base.Cleanup();
        if (App.NavigationService != null)
            App.NavigationService.Navigated -= OnNavigated;
        if (App.BankMatchingModalsViewModel != null)
        {
            App.BankMatchingModalsViewModel.CandidateChosen -= OnCandidateChosen;
            App.BankMatchingModalsViewModel.FiltersApplied -= OnFiltersApplied;
            App.BankMatchingModalsViewModel.MissingFiltersApplied -= OnMissingFiltersApplied;
        }
    }

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        if (e.PageName == PageNames.BankMatching)
            Reload();
    }

    private void OnCandidateChosen(object? sender, BankMatchChosenEventArgs e)
    {
        var row = _allRows.FirstOrDefault(r => r.Line.Id == e.LineId);
        if (row != null)
            ConfirmCandidate(row, e.Candidate);
    }

    #region State

    /// <summary>Bank line rows for the current page.</summary>
    public ObservableCollection<BankLineRow> Lines { get; } = [];

    /// <summary>Book records that no bank line matched (possibly missing from the statement).</summary>
    public ObservableCollection<BookRecordRef> UnmatchedBookRecords { get; } = [];

    /// <summary>Resizable column widths for the bank lines table.</summary>
    public Controls.ColumnWidths.BankLinesTableColumnWidths BankLineColumns { get; } = new();

    /// <summary>Resizable column widths for the missing-records table.</summary>
    public Controls.ColumnWidths.MissingRecordsTableColumnWidths MissingColumns { get; } = new();

    public ResponsiveHeaderHelper ResponsiveHeader { get; } = new();

    [ObservableProperty]
    private bool _hasSession;

    [ObservableProperty]
    private int _matchedCount;

    /// <summary>Total absolute difference between matched bank lines and the records they match.</summary>
    [ObservableProperty]
    private string _matchedDifferenceDisplay = 0m.ToString("C2");

    [ObservableProperty]
    private int _unmatchedLineCount;

    [ObservableProperty]
    private int _unmatchedBookCount;

    [ObservableProperty]
    private string _paginationText = "0 lines";

    public bool HasUnmatchedBook => UnmatchedBookCount > 0;

    partial void OnUnmatchedBookCountChanged(int value) => OnPropertyChanged(nameof(HasUnmatchedBook));

    #endregion

    #region Search, filter, tabs

    [ObservableProperty]
    private string? _searchQuery;

    partial void OnSearchQueryChanged(string? value)
        => DebounceSearch(() =>
        {
            CurrentPage = 1;
            ApplyFiltersAndPaginate();
        });

    // Bank-lines filter state (set from the filter modal).
    [ObservableProperty]
    private DateTime? _filterStartDate;

    [ObservableProperty]
    private DateTime? _filterEndDate;

    [ObservableProperty]
    private string _filterStatus = "All";

    // Missing-records search/filter/sort/pagination state (independent of the bank lines table).
    [ObservableProperty]
    private string? _missingSearchQuery;

    partial void OnMissingSearchQueryChanged(string? value)
    {
        MissingCurrentPage = 1;
        RefreshMissing();
    }

    [ObservableProperty]
    private DateTime? _missingFilterStartDate;

    [ObservableProperty]
    private DateTime? _missingFilterEndDate;

    [ObservableProperty]
    private string _missingFilterType = "All";

    [ObservableProperty]
    private string _missingSortColumn = "Date";

    [ObservableProperty]
    private SortDirection _missingSortDirection = SortDirection.Descending;

    [ObservableProperty]
    private int _missingCurrentPage = 1;

    [ObservableProperty]
    private int _missingTotalPages = 1;

    [ObservableProperty]
    private int _missingPageSize = 10;

    [ObservableProperty]
    private string _missingPaginationText = "0 records";

    partial void OnMissingCurrentPageChanged(int value) => RefreshMissing();

    partial void OnMissingPageSizeChanged(int value)
    {
        MissingCurrentPage = 1;
        RefreshMissing();
    }

    /// <summary>0 = Bank lines, 1 = Missing from statement.</summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    public bool IsLinesTab => SelectedTabIndex == 0;
    public bool IsMissingTab => SelectedTabIndex == 1;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsLinesTab));
        OnPropertyChanged(nameof(IsMissingTab));
    }

    #endregion

    #region Calendar (by-month overview)

    /// <summary>Month overview of the bank statement lines (matched/resolved vs total per month).</summary>
    public MonthlyMatchCalendar LinesCalendar { get; } = new("No bank lines".Translate());

    /// <summary>Month overview of the recorded books (matched to a bank line vs total per month).</summary>
    public MonthlyMatchCalendar MissingCalendar { get; } = new("No records".Translate());

    /// <summary>When true, the Bank lines tab shows the month calendar instead of the table.</summary>
    [ObservableProperty]
    private bool _linesShowCalendar;

    /// <summary>When true, the Missing tab shows the month calendar instead of the table.</summary>
    [ObservableProperty]
    private bool _missingShowCalendar;

    [RelayCommand]
    private void ShowLinesList() => LinesShowCalendar = false;

    [RelayCommand]
    private void ShowLinesCalendar() => LinesShowCalendar = true;

    [RelayCommand]
    private void ShowMissingList() => MissingShowCalendar = false;

    [RelayCommand]
    private void ShowMissingCalendar() => MissingShowCalendar = true;

    /// <summary>Rebuilds both month calendars from the current lines and live book-record match flags.</summary>
    private void RefreshCalendars()
    {
        LinesCalendar.SetItems(_allRows
            .Select(r => new MonthMatchItem(
                r.Line.Date,
                r.Line.MatchStatus is BankLineMatchStatus.Matched or BankLineMatchStatus.Ignored))
            .ToList());

        var data = App.CompanyManager?.CompanyData;
        var bookItems = data == null
            ? new List<MonthMatchItem>()
            : _matcher.GetBookRecordsWithStatus(data, _options)
                .Select(x => new MonthMatchItem(x.Record.Date, x.IsMatched))
                .ToList();
        MissingCalendar.SetItems(bookItems);
    }

    #endregion

    #region Loading and matching

    /// <summary>Reloads from all imported sessions (lines accumulate across imports) and matches.</summary>
    public void Reload()
    {
        var data = App.CompanyManager?.CompanyData;
        if (data == null || data.BankImportSessions.Count == 0)
        {
            _result = null;
            HasSession = false;
            _allRows.Clear();
            _allMissing = [];
            Lines.Clear();
            UnmatchedBookRecords.Clear();
            LinesCalendar.SetItems([]);
            MissingCalendar.SetItems([]);
            ResetCounts();
            return;
        }

        HasSession = true;
        RunMatchingAndRebuild();
    }

    private void RunMatchingAndRebuild()
    {
        var data = App.CompanyManager?.CompanyData;
        if (data == null) return;

        var allLines = data.BankImportSessions.SelectMany(s => s.Lines).ToList();
        _result = _matcher.MatchDeterministic(allLines, data, _options);
        _candidatesByLineId = _result.CandidatesByLineId;

        _allRows.Clear();
        foreach (var line in _result.Lines)
        {
            var top = ResolveTopCandidate(line)
                      ?? (line.MatchStatus == BankLineMatchStatus.Matched ? BuildMatchedCandidate(line) : null);
            _allRows.Add(new BankLineRow(line, top));
        }

        _allMissing = _result.UnmatchedBookRecords;
        UnmatchedBookCount = _allMissing.Count;
        RefreshMissing();

        RecomputeCounts();
        RefreshCalendars();
        ApplyFiltersAndPaginate();
    }

    /// <summary>Applies the missing-tab search/filter/sort and pagination to produce the visible records.</summary>
    private void RefreshMissing()
    {
        IEnumerable<BookRecordRef> query = _allMissing;

        if (MissingFilterType != "All")
            query = query.Where(r => r.Type.ToString() == MissingFilterType);

        if (MissingFilterStartDate.HasValue)
            query = query.Where(r => r.Date == DateTime.MinValue || r.Date.Date >= MissingFilterStartDate.Value.Date);
        if (MissingFilterEndDate.HasValue)
            query = query.Where(r => r.Date == DateTime.MinValue || r.Date.Date <= MissingFilterEndDate.Value.Date);

        if (!string.IsNullOrWhiteSpace(MissingSearchQuery))
        {
            var q = MissingSearchQuery.Trim();
            query = query.Where(r =>
                r.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Amount.ToString("C2").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Type.ToString().Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var filtered = query.ToList().ApplySort(
            MissingSortColumn,
            MissingSortDirection,
            new Dictionary<string, Func<BookRecordRef, object?>>
            {
                ["Date"] = r => r.Date,
                ["Description"] = r => r.Description,
                ["Amount"] = r => r.Amount
            },
            r => r.Date);

        var totalCount = filtered.Count;
        MissingTotalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / MissingPageSize));
        if (MissingCurrentPage > MissingTotalPages) MissingCurrentPage = MissingTotalPages;
        MissingPaginationText = PaginationTextHelper.FormatPaginationText(totalCount, MissingCurrentPage, MissingPageSize, MissingTotalPages, "record");

        UnmatchedBookRecords.Clear();
        foreach (var r in filtered.Skip((MissingCurrentPage - 1) * MissingPageSize).Take(MissingPageSize))
            UnmatchedBookRecords.Add(r);
    }

    /// <inheritdoc />
    protected override void OnSortOrPageChanged() => ApplyFiltersAndPaginate();

    /// <summary>Applies search/filter, sorting and pagination to produce the visible page of rows.</summary>
    private void ApplyFiltersAndPaginate()
    {
        IEnumerable<BankLineRow> query = _allRows;

        if (FilterStatus != "All")
            query = query.Where(r => StatusName(r.Line.MatchStatus) == FilterStatus);

        if (FilterStartDate.HasValue)
            query = query.Where(r => r.Line.Date == DateTime.MinValue || r.Line.Date.Date >= FilterStartDate.Value.Date);
        if (FilterEndDate.HasValue)
            query = query.Where(r => r.Line.Date == DateTime.MinValue || r.Line.Date.Date <= FilterEndDate.Value.Date);

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var q = SearchQuery.Trim();
            query = query.Where(r =>
                r.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.MatchedDisplay.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.AmountDisplay.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Line.RawReference.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var filtered = query.ToList().ApplySort(
            SortColumn,
            SortDirection,
            new Dictionary<string, Func<BankLineRow, object?>>
            {
                ["Date"] = r => r.Line.Date,
                ["Description"] = r => r.Description,
                ["Amount"] = r => r.Line.Amount,
                ["Status"] = r => StatusName(r.Line.MatchStatus)
            },
            r => r.Line.Date);

        var totalCount = filtered.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / PageSize));
        if (CurrentPage > TotalPages) CurrentPage = TotalPages;

        UpdatePageNumbers();
        PaginationText = PaginationTextHelper.FormatPaginationText(totalCount, CurrentPage, PageSize, TotalPages, "line");

        Lines.Clear();
        foreach (var row in filtered.Skip((CurrentPage - 1) * PageSize).Take(PageSize))
            Lines.Add(row);
    }

    protected override void UpdatePageNumbers()
    {
        PageNumbers.Clear();
        var startPage = Math.Max(1, CurrentPage - 2);
        var endPage = Math.Min(TotalPages, startPage + 4);
        startPage = Math.Max(1, endPage - 4);
        for (var i = startPage; i <= endPage; i++)
            PageNumbers.Add(i);
    }

    private Dictionary<string, List<BankMatchCandidate>> _candidatesByLineId = [];

    private BankMatchCandidate? ResolveTopCandidate(BankStatementLine line) =>
        _candidatesByLineId.TryGetValue(line.Id, out var list) ? list.FirstOrDefault() : null;

    private static string StatusName(BankLineMatchStatus status) => status switch
    {
        BankLineMatchStatus.Matched => "Matched",
        BankLineMatchStatus.Suggested => "Suggested",
        BankLineMatchStatus.Ignored => "Ignored",
        _ => "Unmatched"
    };

    /// <summary>
    /// Builds a display candidate for an already-matched line by looking up its record in the
    /// company data (used for auto-matches, which aren't in the candidate map).
    /// </summary>
    private BankMatchCandidate? BuildMatchedCandidate(BankStatementLine line)
    {
        var data = App.CompanyManager?.CompanyData;
        if (data == null || line.MatchedRecordType is not { } type || line.MatchedRecordId is not { } id)
            return null;

        var desc = type switch
        {
            BookRecordType.Expense => data.Expenses.FirstOrDefault(e => e.Id == id)?.Description,
            BookRecordType.Revenue => data.Revenues.FirstOrDefault(r => r.Id == id)?.Description,
            BookRecordType.Invoice => data.Invoices.FirstOrDefault(i => i.Id == id)?.InvoiceNumber,
            BookRecordType.Payment => data.Payments.FirstOrDefault(p => p.Id == id) is { } pmt
                ? (string.IsNullOrWhiteSpace(pmt.Notes) ? pmt.ReferenceNumber : pmt.Notes)
                : null,
            _ => null
        };
        if (desc == null) return null;

        return new BankMatchCandidate
        {
            LineId = line.Id,
            RecordType = type,
            RecordId = id,
            RecordDescription = string.IsNullOrWhiteSpace(desc) ? id : desc,
            RecordAmount = line.Amount,
            Confidence = line.MatchConfidence
        };
    }

    private void ResetCounts()
    {
        MatchedCount = UnmatchedLineCount = UnmatchedBookCount = 0;
        MatchedDifferenceDisplay = 0m.ToString("C2");
        PaginationText = PaginationTextHelper.FormatPaginationText(0, 1, PageSize, 1, "line");
    }

    private void RecomputeCounts()
    {
        MatchedCount = _allRows.Count(r => r.Line.MatchStatus == BankLineMatchStatus.Matched);
        UnmatchedLineCount = _allRows.Count(r => r.Line.MatchStatus == BankLineMatchStatus.Unmatched);

        // Total absolute gap between matched bank lines and the records they're matched to.
        // Exact (auto) matches contribute 0; manual matches to a different amount show up here.
        decimal difference = 0m;
        foreach (var row in _allRows.Where(r => r.Line.MatchStatus == BankLineMatchStatus.Matched))
        {
            var recordAmount = MatchedRecordAmount(row.Line);
            if (recordAmount.HasValue)
                difference += Math.Abs(row.Line.Amount - recordAmount.Value);
        }
        MatchedDifferenceDisplay = difference.ToString("C2");
    }

    /// <summary>Returns the matched record's amount aligned to bank sign convention, or null.</summary>
    private decimal? MatchedRecordAmount(BankStatementLine line)
    {
        var data = App.CompanyManager?.CompanyData;
        if (data == null || line.MatchedRecordType is not { } type || line.MatchedRecordId is not { } id)
            return null;

        return type switch
        {
            BookRecordType.Expense => data.Expenses.FirstOrDefault(e => e.Id == id) is { } e ? -e.Total : null,
            BookRecordType.Revenue => data.Revenues.FirstOrDefault(r => r.Id == id) is { } r ? r.Total : null,
            BookRecordType.Invoice => data.Invoices.FirstOrDefault(i => i.Id == id) is { } inv ? inv.Total : null,
            BookRecordType.Payment => data.Payments.FirstOrDefault(p => p.Id == id) is { } p ? p.Amount : null,
            _ => null
        };
    }

    #endregion

    #region Commands

    [RelayCommand]
    private void ImportStatement() => ImportRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenFilterModal() =>
        App.BankMatchingModalsViewModel?.OpenFilterModal(FilterStartDate, FilterEndDate, FilterStatus);

    private void OnFiltersApplied(object? sender, BankFilterAppliedEventArgs e)
    {
        FilterStartDate = e.StartDate?.DateTime;
        FilterEndDate = e.EndDate?.DateTime;
        FilterStatus = e.Status;
        CurrentPage = 1;
        ApplyFiltersAndPaginate();
    }

    [RelayCommand]
    private void OpenMissingFilterModal() =>
        App.BankMatchingModalsViewModel?.OpenMissingFilterModal(MissingFilterStartDate, MissingFilterEndDate, MissingFilterType);

    private void OnMissingFiltersApplied(object? sender, MissingFilterAppliedEventArgs e)
    {
        MissingFilterStartDate = e.StartDate?.DateTime;
        MissingFilterEndDate = e.EndDate?.DateTime;
        MissingFilterType = e.Type;
        MissingCurrentPage = 1;
        RefreshMissing();
    }

    /// <summary>Sorts the missing-records table by a column (toggles direction on repeat clicks).</summary>
    [RelayCommand]
    private void MissingSortBy(string? column)
    {
        if (string.IsNullOrEmpty(column)) return;

        if (MissingSortColumn == column)
        {
            MissingSortDirection = MissingSortDirection switch
            {
                SortDirection.None => SortDirection.Ascending,
                SortDirection.Ascending => SortDirection.Descending,
                SortDirection.Descending => SortDirection.None,
                _ => SortDirection.Ascending
            };
        }
        else
        {
            MissingSortColumn = column;
            MissingSortDirection = SortDirection.Ascending;
        }

        MissingCurrentPage = 1;
        RefreshMissing();
    }

    [RelayCommand]
    private void MissingGoToPreviousPage()
    {
        if (MissingCurrentPage > 1) MissingCurrentPage--;
    }

    [RelayCommand]
    private void MissingGoToNextPage()
    {
        if (MissingCurrentPage < MissingTotalPages) MissingCurrentPage++;
    }

    [RelayCommand]
    private void MissingGoToPage(int page)
    {
        if (page >= 1 && page <= MissingTotalPages && page != MissingCurrentPage)
            MissingCurrentPage = page;
    }

    [RelayCommand]
    private void AcceptSuggestion(BankLineRow? row)
    {
        if (row?.TopCandidate == null) return;
        ConfirmCandidate(row, row.TopCandidate);
    }

    /// <summary>Marks a line as ignored (e.g., internal transfer). Tracked in undo/redo.</summary>
    [RelayCommand]
    private void IgnoreLine(BankLineRow? row)
    {
        if (row == null) return;
        var line = row.Line;
        SetLineIgnored(line);
        App.UndoRedoManager.RecordAction(new DelegateAction(
            "Ignore bank line".Translate(),
            () => SetLineRestored(line),
            () => SetLineIgnored(line)));
        App.CompanyManager?.MarkAsChanged();
    }

    /// <summary>Restores a previously ignored line, recomputing its candidate matches. Tracked in undo/redo.</summary>
    [RelayCommand]
    private void RestoreLine(BankLineRow? row)
    {
        if (row == null) return;
        var line = row.Line;
        SetLineRestored(line);
        App.UndoRedoManager.RecordAction(new DelegateAction(
            "Restore bank line".Translate(),
            () => SetLineIgnored(line),
            () => SetLineRestored(line)));
        App.CompanyManager?.MarkAsChanged();
    }

    /// <summary>Unlinks a confirmed match, clearing the flag on the book record. Tracked in undo/redo.</summary>
    [RelayCommand]
    private void UnlinkMatch(BankLineRow? row)
    {
        var data = App.CompanyManager?.CompanyData;
        if (row == null || data == null) return;
        var line = row.Line;

        var before = CaptureMatchState(line);
        _matcher.UnlinkMatch(line, data);
        var after = CaptureMatchState(line);

        App.UndoRedoManager.RecordAction(new DelegateAction(
            "Unlink bank match".Translate(),
            () => { RestoreMatchState(line, before, data); RefreshAfterMatchChange(line); },
            () => { RestoreMatchState(line, after, data); RefreshAfterMatchChange(line); }));

        RefreshAfterMatchChange(line);
    }

    /// <summary>Opens the AppShell-hosted picker: suggestions plus a full manual list to pick from.</summary>
    [RelayCommand]
    private void OpenCandidatePicker(BankLineRow? row)
    {
        if (row == null) return;
        var data = App.CompanyManager?.CompanyData;
        var candidates = _candidatesByLineId.TryGetValue(row.Line.Id, out var list) ? list : [];
        var manual = data != null ? _matcher.GetManualMatchOptions(row.Line, data, _options) : [];
        var context = $"{row.Description}  ·  {row.AmountDisplay}  ·  {row.DateDisplay}";
        App.BankMatchingModalsViewModel?.OpenCandidatePicker(row.Line.Id, context, candidates, manual);
    }

    private void SetLineIgnored(BankStatementLine line)
    {
        line.MatchStatus = BankLineMatchStatus.Ignored;
        _candidatesByLineId.Remove(line.Id);
        RefreshRow(line, null);
        App.CompanyManager?.CompanyData?.MarkAsModified();
        RecomputeCounts();
        RefreshCalendars();
        ApplyFiltersAndPaginate();
    }

    private void SetLineRestored(BankStatementLine line)
    {
        var data = App.CompanyManager?.CompanyData;
        var candidates = data != null ? _matcher.FindCandidates(line, data, _options) : [];
        if (candidates.Count > 0)
        {
            _candidatesByLineId[line.Id] = candidates;
            line.MatchStatus = BankLineMatchStatus.Suggested;
            RefreshRow(line, null, candidates[0]);
        }
        else
        {
            _candidatesByLineId.Remove(line.Id);
            line.MatchStatus = BankLineMatchStatus.Unmatched;
            RefreshRow(line, null);
        }
        data?.MarkAsModified();
        RecomputeCounts();
        RefreshCalendars();
        ApplyFiltersAndPaginate();
    }

    private void RefreshRow(BankStatementLine line, BankMatchCandidate? confirmed, BankMatchCandidate? suggested = null)
    {
        var row = _allRows.FirstOrDefault(r => r.Line.Id == line.Id);
        row?.Refresh(confirmed, suggested);
    }

    private void ConfirmCandidate(BankLineRow row, BankMatchCandidate candidate)
    {
        var data = App.CompanyManager?.CompanyData;
        if (data == null) return;
        var line = row.Line;

        var before = CaptureMatchState(line);
        if (!_matcher.ConfirmMatch(line, candidate, data))
        {
            DropRecordFromCandidates(candidate.RecordType, candidate.RecordId);
            RefreshAfterMatchChange(line);
            _ = App.ShowInfoMessageBoxAsync(
                "Bank Matching".Translate(),
                "That record is already matched to another bank line.".Translate());
            return;
        }

        _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.BankMatchConfirmed);
        _candidatesByLineId.Remove(line.Id);
        var othersBefore = CaptureLinesOffering(candidate.RecordType, candidate.RecordId);
        DropRecordFromCandidates(candidate.RecordType, candidate.RecordId);
        var after = CaptureMatchState(line);

        App.UndoRedoManager.RecordAction(new DelegateAction(
            "Match bank line".Translate(),
            () =>
            {
                RestoreMatchState(line, before, data);
                RestoreLinesOffering(othersBefore);
                RefreshAfterMatchChange(line);
            },
            () =>
            {
                RestoreMatchState(line, after, data);
                DropRecordFromCandidates(candidate.RecordType, candidate.RecordId);
                RefreshAfterMatchChange(line);
            }));

        RefreshAfterMatchChange(line);
    }

    /// <summary>The unmatched lines suggesting a record, captured so undoing its match can give it back to them.</summary>
    private List<OfferingLine> CaptureLinesOffering(BookRecordType type, string id)
    {
        var captured = new List<OfferingLine>();
        foreach (var (lineId, list) in _candidatesByLineId)
        {
            if (!list.Any(c => c.RecordType == type && c.RecordId == id)) continue;
            var row = _allRows.FirstOrDefault(r => r.Line.Id == lineId);
            if (row != null && row.Line.MatchStatus != BankLineMatchStatus.Matched)
                captured.Add(new OfferingLine(row, row.Line.MatchStatus, new List<BankMatchCandidate>(list)));
        }
        return captured;
    }

    private void RestoreLinesOffering(List<OfferingLine> captured)
    {
        foreach (var (row, status, candidates) in captured)
        {
            if (row.Line.MatchStatus == BankLineMatchStatus.Matched) continue;
            _candidatesByLineId[row.Line.Id] = candidates;
            row.Line.MatchStatus = status;
            row.TopCandidate = candidates.FirstOrDefault();
            row.Refresh(null);
        }
    }

    private sealed record OfferingLine(BankLineRow Row, BankLineMatchStatus Status, List<BankMatchCandidate> Candidates);

    /// <summary>
    /// Takes a record out of every line's suggestions once it is matched, so no other line can offer
    /// it. A line left with no suggestions drops back to Unmatched.
    /// </summary>
    private void DropRecordFromCandidates(BookRecordType type, string id)
    {
        foreach (var (lineId, list) in _candidatesByLineId.ToList())
        {
            if (!list.Any(c => c.RecordType == type && c.RecordId == id)) continue;

            // Replaced rather than edited in place: a restored undo snapshot's list is the live one.
            var kept = list.Where(c => c.RecordType != type || c.RecordId != id).ToList();
            var row = _allRows.FirstOrDefault(r => r.Line.Id == lineId);
            if (kept.Count > 0)
            {
                _candidatesByLineId[lineId] = kept;
            }
            else
            {
                _candidatesByLineId.Remove(lineId);
                if (row?.Line.MatchStatus == BankLineMatchStatus.Suggested)
                    row.Line.MatchStatus = BankLineMatchStatus.Unmatched;
            }

            if (row != null && row.Line.MatchStatus != BankLineMatchStatus.Matched)
            {
                row.TopCandidate = kept.FirstOrDefault();
                row.Refresh(null);
            }
        }
    }

    /// <summary>Captures a line's match state (and any suggestions) so it can be restored by undo/redo.</summary>
    private MatchStateSnapshot CaptureMatchState(BankStatementLine line) => new(
        line.MatchStatus,
        line.MatchedRecordType,
        line.MatchedRecordId,
        line.MatchedDate,
        line.MatchConfidence,
        _candidatesByLineId.TryGetValue(line.Id, out var c) ? new List<BankMatchCandidate>(c) : null);

    /// <summary>
    /// Restores a line to a captured match state. Reuses the matcher's confirm/unlink so the book
    /// records' persisted flags are released and re-set correctly (including re-match cases).
    /// </summary>
    private void RestoreMatchState(BankStatementLine line, MatchStateSnapshot s, CompanyData data)
    {
        // Release whatever the line currently points to (clears the current record's flag, if any).
        _matcher.UnlinkMatch(line, data);

        // Re-establish the previously matched record's flag and the line's match fields.
        if (s.MatchedType is { } type && s.MatchedId is { } id)
        {
            var relinked = _matcher.ConfirmMatch(line, new BankMatchCandidate
            {
                LineId = line.Id,
                RecordType = type,
                RecordId = id,
                Confidence = s.Confidence
            }, data);

            // Another line took the record in the meantime, so this one stays unmatched.
            if (!relinked)
            {
                _candidatesByLineId.Remove(line.Id);
                return;
            }

            line.MatchedDate = s.MatchedDate; // keep the original timestamp, not "now"
        }

        line.MatchStatus = s.Status;
        line.MatchConfidence = s.Confidence;
        if (s.Candidates != null) _candidatesByLineId[line.Id] = s.Candidates;
        else _candidatesByLineId.Remove(line.Id);
    }

    /// <summary>Refreshes the row display, counts and paging after a match is confirmed/unlinked or undone.</summary>
    private void RefreshAfterMatchChange(BankStatementLine line)
    {
        var row = _allRows.FirstOrDefault(r => r.Line.Id == line.Id);
        if (row != null)
        {
            BankMatchCandidate? top = line.MatchStatus == BankLineMatchStatus.Matched
                ? BuildMatchedCandidate(line)
                : (_candidatesByLineId.TryGetValue(line.Id, out var c) ? c.FirstOrDefault() : null);
            row.Refresh(top);
        }

        App.CompanyManager?.MarkAsChanged();
        RecomputeCounts();
        RefreshCalendars();
        ApplyFiltersAndPaginate();
    }

    #endregion

    #region Events

    public event EventHandler? ImportRequested;

    #endregion

    /// <summary>Immutable capture of a bank line's match state, used to undo/redo confirm and unlink.</summary>
    private sealed record MatchStateSnapshot(
        BankLineMatchStatus Status,
        BookRecordType? MatchedType,
        string? MatchedId,
        DateTime? MatchedDate,
        double Confidence,
        List<BankMatchCandidate>? Candidates);
}

/// <summary>Display wrapper for a bank statement line in the table.</summary>
public partial class BankLineRow : ObservableObject
{
    public BankStatementLine Line { get; }

    public BankLineRow(BankStatementLine line, BankMatchCandidate? topCandidate)
    {
        Line = line;
        TopCandidate = topCandidate;
        Refresh(topCandidate);
    }

    [ObservableProperty]
    private BankMatchCandidate? _topCandidate;

    [ObservableProperty]
    private string _matchedDisplay = string.Empty;

    public string DateDisplay => Line.Date == DateTime.MinValue ? "-" : Line.Date.ToString("MMM dd, yyyy");
    public string Description => string.IsNullOrWhiteSpace(Line.Description) ? "-" : Line.Description;
    public string AmountDisplay => Line.Amount.ToString("C2");
    public string AmountColor => Line.Amount < 0 ? AppColors.ExpenseRed : AppColors.Success;

    public string StatusDisplay => Line.MatchStatus switch
    {
        BankLineMatchStatus.Matched => "Matched",
        BankLineMatchStatus.Suggested => "Suggested",
        BankLineMatchStatus.Ignored => "Ignored",
        _ => "Unmatched"
    };

    public string StatusColor => Line.MatchStatus switch
    {
        BankLineMatchStatus.Matched => AppColors.Success,
        BankLineMatchStatus.Suggested => AppColors.Warning,
        BankLineMatchStatus.Ignored => AppColors.GrayMedium,
        _ => AppColors.ExpenseRed
    };

    public bool IsMatched => Line.MatchStatus == BankLineMatchStatus.Matched;
    public bool IsSuggested => Line.MatchStatus == BankLineMatchStatus.Suggested;
    public bool IsUnmatched => Line.MatchStatus == BankLineMatchStatus.Unmatched;
    public bool IsIgnored => Line.MatchStatus == BankLineMatchStatus.Ignored;

    /// <summary>True when the line can still be matched or ignored (not matched, not ignored).</summary>
    public bool IsActionable => IsUnmatched || IsSuggested;

    /// <summary>Recomputes display properties after the line's status changes.</summary>
    public void Refresh(BankMatchCandidate? confirmed, BankMatchCandidate? suggested = null)
    {
        if (confirmed != null) TopCandidate = confirmed;
        else if (suggested != null) TopCandidate = suggested;

        MatchedDisplay = Line.MatchStatus switch
        {
            BankLineMatchStatus.Matched => TopCandidate?.RecordDescription is { Length: > 0 } d ? d : "Matched record",
            BankLineMatchStatus.Suggested => TopCandidate != null
                ? $"{TopCandidate.RecordDescription} ({TopCandidate.Confidence:P0})"
                : "-",
            _ => "-"
        };

        OnPropertyChanged(nameof(StatusDisplay));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(IsMatched));
        OnPropertyChanged(nameof(IsSuggested));
        OnPropertyChanged(nameof(IsUnmatched));
        OnPropertyChanged(nameof(IsIgnored));
        OnPropertyChanged(nameof(IsActionable));
    }
}
