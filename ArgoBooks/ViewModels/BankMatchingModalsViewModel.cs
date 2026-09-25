using System.Collections.ObjectModel;
using ArgoBooks.Core.Models.BankMatching;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Bank Matching modals (the "choose matching book entry" picker).
/// Hosted at the AppShell level so it overlays the whole app, like the other modals.
/// </summary>
public partial class BankMatchingModalsViewModel : ViewModelBase
{
    /// <summary>Raised when the user picks a candidate. The page applies the match.</summary>
    public event EventHandler<BankMatchChosenEventArgs>? CandidateChosen;

    [ObservableProperty]
    private bool _isCandidateModalOpen;

    [ObservableProperty]
    private bool _hasCandidates;

    [ObservableProperty]
    private string _lineDescription = string.Empty;

    /// <summary>True when there are records available to manually pick from.</summary>
    [ObservableProperty]
    private bool _hasManualOptions;

    [ObservableProperty]
    private string? _manualSearchQuery;

    private string? _lineId;

    /// <summary>Suggested (auto-detected) candidates.</summary>
    public ObservableCollection<BankMatchCandidate> CandidateOptions { get; } = [];

    /// <summary>All records the user can manually pick from (direction-filtered), before search.</summary>
    private readonly List<BankMatchCandidate> _allManualOptions = [];

    /// <summary>Manual-pick records currently shown (filtered by the search box).</summary>
    public ObservableCollection<BankMatchCandidate> ManualOptions { get; } = [];

    partial void OnManualSearchQueryChanged(string? value) => FilterManualOptions();

    /// <summary>Opens the candidate picker for a bank line, with suggestions and a full manual list.</summary>
    public void OpenCandidatePicker(string lineId, string lineDescription,
        IEnumerable<BankMatchCandidate> candidates, IEnumerable<BankMatchCandidate> manualOptions)
    {
        _lineId = lineId;
        LineDescription = lineDescription;

        CandidateOptions.Clear();
        foreach (var c in candidates)
            CandidateOptions.Add(c);
        HasCandidates = CandidateOptions.Count > 0;

        _allManualOptions.Clear();
        _allManualOptions.AddRange(manualOptions);
        HasManualOptions = _allManualOptions.Count > 0;
        ManualSearchQuery = null;
        FilterManualOptions();

        IsCandidateModalOpen = true;
    }

    private void FilterManualOptions()
    {
        ManualOptions.Clear();
        IEnumerable<BankMatchCandidate> query = _allManualOptions;
        if (!string.IsNullOrWhiteSpace(ManualSearchQuery))
        {
            var q = ManualSearchQuery.Trim();
            query = query.Where(c =>
                c.RecordDescription.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.RecordAmount.ToString("C2").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.RecordType.ToString().Contains(q, StringComparison.OrdinalIgnoreCase));
        }
        foreach (var c in query)
            ManualOptions.Add(c);
    }

    [RelayCommand]
    private void CloseCandidateModal() => IsCandidateModalOpen = false;

    [RelayCommand]
    private void ChooseCandidate(BankMatchCandidate? candidate)
    {
        if (_lineId == null || candidate == null) return;
        IsCandidateModalOpen = false;
        CandidateChosen?.Invoke(this, new BankMatchChosenEventArgs(_lineId, candidate));
    }

    #region Filter modal

    /// <summary>Raised when the user applies filters from the filter modal.</summary>
    public event EventHandler<BankFilterAppliedEventArgs>? FiltersApplied;

    /// <summary>Raised when the user clears the filter modal's filters.</summary>
    public event EventHandler? FiltersCleared;

    [ObservableProperty]
    private bool _isFilterModalOpen;

    [ObservableProperty]
    private DateTimeOffset? _filterStartDate;

    [ObservableProperty]
    private DateTimeOffset? _filterEndDate;

    [ObservableProperty]
    private string _filterStatus = "All";

    /// <summary>Status filter options for the dropdown.</summary>
    public ObservableCollection<string> StatusOptions { get; } =
        ["All", "Matched", "Suggested", "Unmatched", "Ignored"];

    private sealed record LineFilterValues(DateTimeOffset? StartDate, DateTimeOffset? EndDate, string Status)
    {
        public static readonly LineFilterValues Default = new(null, null, "All");
    }

    private FilterSnapshot<LineFilterValues>? _filters;

    private FilterSnapshot<LineFilterValues> Filters => _filters ??= new(LineFilterValues.Default,
        () => new(FilterStartDate, FilterEndDate, FilterStatus),
        v =>
        {
            FilterStartDate = v.StartDate;
            FilterEndDate = v.EndDate;
            FilterStatus = v.Status;
        });

    private static DateTimeOffset? ToOffset(DateTime? date) => date.HasValue ? new DateTimeOffset(date.Value) : null;

    /// <summary>Opens the filter modal seeded with the current filter values.</summary>
    public void OpenFilterModal(DateTime? startDate, DateTime? endDate, string status)
    {
        Filters.Set(new(ToOffset(startDate), ToOffset(endDate), string.IsNullOrEmpty(status) ? "All" : status));
        Filters.Capture();
        IsFilterModalOpen = true;
    }

    private void CloseFilterModal() => IsFilterModalOpen = false;

    [RelayCommand]
    private async Task RequestCloseFilterModalAsync()
    {
        if (await Filters.ConfirmDiscardAsync(ConfirmDiscardFiltersAsync))
            CloseFilterModal();
    }

    /// <summary>How many filters are applied, for the page's Filter button.</summary>
    public int ActiveFilterCount { get; private set; }

    [RelayCommand]
    private void ApplyFilters()
    {
        CloseFilterModal();
        ActiveFilterCount = Filters.ActiveCount;
        FiltersApplied?.Invoke(this, new BankFilterAppliedEventArgs(FilterStartDate, FilterEndDate, FilterStatus));
    }

    [RelayCommand]
    private void ClearFilters()
    {
        Filters.Reset();
        CloseFilterModal();
        ActiveFilterCount = 0;
        FiltersCleared?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Missing-records filter modal

    /// <summary>Raised when the user applies filters from the missing-records filter modal.</summary>
    public event EventHandler<MissingFilterAppliedEventArgs>? MissingFiltersApplied;

    /// <summary>Raised when the user clears the missing-records filter modal's filters.</summary>
    public event EventHandler? MissingFiltersCleared;

    [ObservableProperty]
    private bool _isMissingFilterModalOpen;

    [ObservableProperty]
    private DateTimeOffset? _missingFilterStartDate;

    [ObservableProperty]
    private DateTimeOffset? _missingFilterEndDate;

    [ObservableProperty]
    private string _missingFilterType = "All";

    /// <summary>Record-type filter options for the missing-records dropdown.</summary>
    public ObservableCollection<string> TypeOptions { get; } =
        ["All", "Expense", "Revenue"];

    private sealed record MissingFilterValues(DateTimeOffset? StartDate, DateTimeOffset? EndDate, string Type)
    {
        public static readonly MissingFilterValues Default = new(null, null, "All");
    }

    private FilterSnapshot<MissingFilterValues>? _missingFilters;

    private FilterSnapshot<MissingFilterValues> MissingFilters => _missingFilters ??= new(MissingFilterValues.Default,
        () => new(MissingFilterStartDate, MissingFilterEndDate, MissingFilterType),
        v =>
        {
            MissingFilterStartDate = v.StartDate;
            MissingFilterEndDate = v.EndDate;
            MissingFilterType = v.Type;
        });

    public void OpenMissingFilterModal(DateTime? startDate, DateTime? endDate, string type)
    {
        MissingFilters.Set(new(ToOffset(startDate), ToOffset(endDate), string.IsNullOrEmpty(type) ? "All" : type));
        MissingFilters.Capture();
        IsMissingFilterModalOpen = true;
    }

    private void CloseMissingFilterModal() => IsMissingFilterModalOpen = false;

    [RelayCommand]
    private async Task RequestCloseMissingFilterModalAsync()
    {
        if (await MissingFilters.ConfirmDiscardAsync(ConfirmDiscardFiltersAsync))
            CloseMissingFilterModal();
    }

    /// <summary>How many missing-records filters are applied, for that table's Filter button.</summary>
    public int MissingActiveFilterCount { get; private set; }

    [RelayCommand]
    private void ApplyMissingFilters()
    {
        CloseMissingFilterModal();
        MissingActiveFilterCount = MissingFilters.ActiveCount;
        MissingFiltersApplied?.Invoke(this, new MissingFilterAppliedEventArgs(MissingFilterStartDate, MissingFilterEndDate, MissingFilterType));
    }

    [RelayCommand]
    private void ClearMissingFilters()
    {
        MissingFilters.Reset();
        CloseMissingFilterModal();
        MissingActiveFilterCount = 0;
        MissingFiltersCleared?.Invoke(this, EventArgs.Empty);
    }

    #endregion
}

/// <summary>Event args carrying the applied bank-line filters.</summary>
public class BankFilterAppliedEventArgs(DateTimeOffset? startDate, DateTimeOffset? endDate, string status) : EventArgs
{
    public DateTimeOffset? StartDate { get; } = startDate;
    public DateTimeOffset? EndDate { get; } = endDate;
    public string Status { get; } = status;
}

/// <summary>Event args carrying the applied missing-records filters.</summary>
public class MissingFilterAppliedEventArgs(DateTimeOffset? startDate, DateTimeOffset? endDate, string type) : EventArgs
{
    public DateTimeOffset? StartDate { get; } = startDate;
    public DateTimeOffset? EndDate { get; } = endDate;
    public string Type { get; } = type;
}

/// <summary>Event args carrying the user's chosen candidate for a bank line.</summary>
public class BankMatchChosenEventArgs(string lineId, BankMatchCandidate candidate) : EventArgs
{
    public string LineId { get; } = lineId;
    public BankMatchCandidate Candidate { get; } = candidate;
}
