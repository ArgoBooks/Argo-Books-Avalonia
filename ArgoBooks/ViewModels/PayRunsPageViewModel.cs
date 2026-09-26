using System.Collections.ObjectModel;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.Payroll;
using ArgoBooks.Core.Utilities;
using ArgoBooks.Helpers;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.Shared.Telemetry;
using ArgoBooks.Utilities;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Pay runs page: the history of payrolls, and the entry point to running
/// a new one.
///
/// Voided runs stay in the list. A pay stub may already be in someone's hands, so the record
/// has to survive the correction.
/// </summary>
public partial class PayRunsPageViewModel : SortablePageViewModelBase
{
    private readonly List<PayRun> _all = [];
    private readonly PayrollService _payroll = new();

    public PayRunsTableColumnWidths ColumnWidths => App.PayRunsColumnWidths;

    public ObservableCollection<PayRunDisplayItem> PayRuns { get; } = [];

    #region Statistics

    [ObservableProperty]
    private string _yearToDateGross = "$0";

    [ObservableProperty]
    private string _yearToDateRemittance = "$0";

    [ObservableProperty]
    private int _approvedCount;

    /// <summary>
    /// What has to reach CRA next, and the label saying by when.
    ///
    /// Separate from the year-to-date figure beside it because they answer different questions.
    /// The running total says how much payroll has passed through; this one is the amount to
    /// send and the date to send it, which is the only one anybody has to act on.
    ///
    /// It cannot know whether the payment has been made, so it keeps showing the figure all
    /// month and then rolls on to the next deadline. That is deliberate: recording remittances
    /// would be a new thing to keep up to date, and one kept badly is worse than none.
    /// </summary>
    [ObservableProperty]
    private string _remittanceDue = "$0";

    [ObservableProperty]
    private string _remittanceDueLabel = "Due to CRA";

    /// <summary>
    /// What goes to Revenu Quebec for the same pay dates, when there is any. A separate payment
    /// to a separate agency, so it is named under the CRA figure rather than added to it, and it
    /// carries no date: Revenu Quebec assigns its own schedule, which this app does not record.
    /// </summary>
    [ObservableProperty]
    private string? _remittanceDueQuebec;

    /// <summary>
    /// Which CRA schedule the date above was worked out from, as a tooltip.
    ///
    /// Worth saying out loud rather than leaving as an assumption. The date is right for a
    /// regular remitter and weeks late for an accelerated one, and the only person who can tell
    /// the difference is the employer, who was told by CRA.
    /// </summary>
    [ObservableProperty]
    private string _remittanceScheduleHint = string.Empty;

    #endregion

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    partial void OnSearchQueryChanged(string value) => Filter();

    #region Premium

    /// <summary>
    /// Payroll is premium only. Two CRA rate updates a year with hard deadlines is a recurring
    /// maintenance cost, so it sits against recurring revenue.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTeaser))]
    [NotifyPropertyChangedFor(nameof(ShowPayrollUpgradePrompt))]
    private bool _hasPremium;

    /// <summary>
    /// Covers the page with the upgrade card, over sample figures, the way the Insights page
    /// does. Showing what a filled-in payroll looks like sells the feature better than any
    /// description of it, which is why the sample data exists at all.
    ///
    /// Only when there are no real pay runs. Someone whose subscription lapsed still has T4s to
    /// produce for the year they did pay, and Year end is deliberately not gated, so their page
    /// has to stay usable. They get the banner below instead.
    /// </summary>
    public bool ShowTeaser => !HasPremium && _all.Count == 0;

    /// <summary>The quieter prompt for a lapsed subscriber, whose real runs stay reachable.</summary>
    public bool ShowPayrollUpgradePrompt => !HasPremium && _all.Count > 0;

    /// <summary>Swapping the plan swaps the page between the sample figures and the real ones.</summary>
    partial void OnHasPremiumChanged(bool value) => Load();

    /// <summary>Raised when the upgrade prompt is clicked, so the shell can open the modal.</summary>
    public event EventHandler? UpgradeRequested;

    [RelayCommand]
    private void Upgrade() => UpgradeRequested?.Invoke(this, EventArgs.Empty);

    #endregion

    public PayRunsPageViewModel()
    {
        Load();

        if (App.PayRunModalsViewModel is { } modals)
        {
            modals.PayRunChanged += OnPayRunChanged;
        }

        App.PlanStatusChanged += OnPlanStatusChanged;
    }

    public override void Cleanup()
    {
        base.Cleanup();

        if (App.PayRunModalsViewModel is { } modals)
        {
            modals.PayRunChanged -= OnPayRunChanged;
        }

        App.PlanStatusChanged -= OnPlanStatusChanged;
    }

    private void OnPlanStatusChanged(object? sender, PlanStatusChangedEventArgs e) => HasPremium = e.HasPremium;

    private void OnPayRunChanged(object? sender, EventArgs e) => Load();

    #region Column visibility

    private static readonly ColumnVisibilityDefaults ColumnDefaults = new("PayRuns", new Dictionary<string, bool>
    {
        ["PayDate"] = true,
        ["Period"] = true,
        ["Employees"] = true,
        ["Gross"] = true,
        ["Net"] = true,
        ["Status"] = true,
    });

    protected override ColumnVisibilityDefaults ColumnVisibility => ColumnDefaults;

    [ObservableProperty]
    private bool _showPayDateColumn = ColumnDefaults.Load("PayDate");

    [ObservableProperty]
    private bool _showPeriodColumn = ColumnDefaults.Load("Period");

    [ObservableProperty]
    private bool _showEmployeesColumn = ColumnDefaults.Load("Employees");

    [ObservableProperty]
    private bool _showGrossColumn = ColumnDefaults.Load("Gross");

    [ObservableProperty]
    private bool _showNetColumn = ColumnDefaults.Load("Net");

    [ObservableProperty]
    private bool _showStatusColumn = ColumnDefaults.Load("Status");

    #endregion

    #region Commands

    /// <summary>
    /// Year end is a modal rather than a nav item because it is used once a year, and it is
    /// not premium-gated the way running a payroll is: someone who ran payroll while
    /// subscribed must still be able to produce the T4s for that year.
    /// </summary>
    [RelayCommand]
    private void OpenYearEnd() => App.YearEndModalViewModel?.Open();

    [RelayCommand]
    private void RunPayroll()
    {
        if (!HasPremium)
        {
            UpgradeRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        App.PayRunModalsViewModel?.OpenRunModal();
    }

    /// <summary>
    /// Opens the run's stubs in the viewer, one employee at a time, picked by name.
    ///
    /// Each stub is composed only when it is selected.
    ///
    /// Still separate from downloading. This is for checking a figure on screen; the download
    /// writes one file per person, because that is what gets handed over and nobody should
    /// receive a PDF containing a colleague's pay.
    /// </summary>
    [RelayCommand]
    private void ViewStubs(PayRunDisplayItem? item)
    {
        CompanyData? data = App.CompanyManager?.CompanyData;
        PayRun? run = _all.FirstOrDefault(r => r.Id == item?.Id);

        if (data == null || run == null || run.Lines.Count == 0)
        {
            return;
        }

        try
        {
            string symbol = CurrencyService.CurrentSymbol;

            // One entry per employee, each holding a closure rather than bytes. Nothing is
            // rendered until a name is chosen, and the viewer's cache keeps a second look at
            // the same person instant.
            var documents = run.Lines.Select(line => new ViewerDocument
            {
                Name = line.EmployeeName,
                FileName = $"{run.PayDate:yyyy-MM-dd}-{SafeFileName.Create(line.EmployeeName, "export", replaceSpaces: true)}.pdf",

                // Year to date up to but not including this run, so the stub's own figures are
                // what gets added to it rather than counted twice. Same rule as the download.
                LoadAsync = () => Task.Run(() =>
                {
                    PayrollYearToDate ytd = _payroll.YearToDateFor(data, line.EmployeeId, run);
                    return PayStubPdfRenderer.Render(run, line, ytd, data, symbol);
                }),
            }).ToList();

            App.ReceiptViewerModal?.ShowDocumentSet(
                "Pay stubs: {0}".TranslateFormat(run.PayDate.ToString("d MMMM yyyy")),
                documents);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Validation, "Payroll.ViewStubs");
        }
    }

    /// <summary>
    /// Saves a pay stub PDF for every employee on the run. One file each, since a stub is
    /// handed to one person and nobody should see anyone else's pay.
    /// </summary>
    [RelayCommand]
    private async Task DownloadStubsAsync(PayRunDisplayItem? item)
    {
        CompanyData? data = App.CompanyManager?.CompanyData;
        PayRun? run = _all.FirstOrDefault(r => r.Id == item?.Id);

        if (data == null || run == null || run.Lines.Count == 0)
        {
            return;
        }

        var topLevel = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (topLevel?.StorageProvider == null)
        {
            return;
        }

        try
        {
            IReadOnlyList<IStorageFolder> folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Choose where to save the pay stubs".Translate(),
                    AllowMultiple = false,
                });

            if (folders.Count == 0)
            {
                return;
            }

            // One stub each, so anything but a single-employee run gets its own folder
            // rather than scattering PDFs through whatever the user picked.
            string directory = ExportFolderHelper.Resolve(
                folders[0].Path.LocalPath,
                $"Pay stubs {run.PayDate:yyyy-MM-dd}",
                run.Lines.Count);

            string symbol = CurrencyService.CurrentSymbol;

            foreach (PayRunLine line in run.Lines)
            {
                // Year to date up to but not including this run, so the stub's own figures
                // are what gets added to it rather than counted twice.
                PayrollYearToDate ytd = _payroll.YearToDateFor(data, line.EmployeeId, run);

                byte[] bytes = await Task.Run(() => PayStubPdfRenderer.Render(run, line, ytd, data, symbol));
                string name = $"{run.PayDate:yyyy-MM-dd}-{SafeFileName.Create(line.EmployeeName, "export", replaceSpaces: true)}.pdf";

                await File.WriteAllBytesAsync(Path.Combine(directory, name), bytes);
            }

            _ = App.TelemetryManager?.TrackFeatureAsync(FeatureName.PayStubsExported);
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.Validation, "Payroll.DownloadStubs");
        }
    }

    /// <summary>
    /// Cancels an approved run by writing a reversing one. Nothing is deleted, because a stub
    /// may already be in someone's hands.
    /// </summary>
    [RelayCommand]
    private async Task VoidRunAsync(PayRunDisplayItem? item)
    {
        CompanyData? data = App.CompanyManager?.CompanyData;
        PayRun? run = _all.FirstOrDefault(r => r.Id == item?.Id);

        if (data == null || run == null || run.Status != PayRunStatus.Approved)
        {
            return;
        }

        if (App.ConfirmationDialog is { } confirm)
        {
            ConfirmationResult result = await confirm.ShowAsync(new ConfirmationDialogOptions
            {
                Title = "Void this pay run?".Translate(),
                Message = ("Pay run {0} will be reversed and the wage expenses it created will be " +
                           "removed. The run stays in the list so the history survives.").TranslateFormat(run.Id),
                PrimaryButtonText = "Void".Translate(),
                CancelButtonText = "Cancel".Translate(),
                IsPrimaryDestructive = true,
            });

            if (result != ConfirmationResult.Primary)
            {
                return;
            }
        }

        // Snapshot what void is about to remove, so undo can put it back.
        List<Core.Models.Transactions.Expense> removed = run.Lines
            .Where(l => l.ExpenseId is { Length: > 0 })
            .Select(l => data.Expenses.FirstOrDefault(e => e.Id == l.ExpenseId))
            .Where(e => e != null)
            .Select(e => e!)
            .ToList();

        List<(PayRunLine Line, string? ExpenseId)> expenseIds =
            run.Lines.Select(l => (l, l.ExpenseId)).ToList();

        PayRun? reversal = _payroll.Void(data, run);
        if (reversal == null)
        {
            return;
        }

        App.CompanyManager?.MarkAsChanged();

        App.UndoRedoManager.RecordAction(new DelegateAction(
            $"Void pay run {run.Id}",
            () =>
            {
                data.PayRuns.Remove(reversal);
                PayrollService.RestoreWageExpenses(data, removed);
                run.Status = PayRunStatus.Approved;
                foreach ((PayRunLine line, string? expenseId) in expenseIds)
                {
                    line.ExpenseId = expenseId;
                }

                App.CompanyManager?.MarkAsChanged();
                Load();
            },
            () =>
            {
                data.PayRuns.Add(reversal);
                PayrollService.RemoveWageExpenses(data, removed);
                run.Status = PayRunStatus.Void;
                foreach ((PayRunLine line, string? _) in expenseIds)
                {
                    line.ExpenseId = null;
                }

                App.CompanyManager?.MarkAsChanged();
                Load();
            }));

        Load();
    }

    #endregion

    protected override void OnSortOrPageChanged() => Filter();

    public void Load()
    {
        _all.Clear();

        List<PayRun>? runs = App.CompanyManager?.CompanyData?.PayRuns;
        if (runs != null)
        {
            _all.AddRange(runs);
        }

        // Both prompts key off whether anything real is here, so they have to be re-read after
        // the list is rebuilt and not only when the plan changes.
        OnPropertyChanged(nameof(ShowTeaser));
        OnPropertyChanged(nameof(ShowPayrollUpgradePrompt));

        if (ShowTeaser)
        {
            PopulateSampleData();
            return;
        }

        UpdateStatistics();
        Filter();
    }

    /// <summary>
    /// Fills the page with figures for the upgrade teaser to sit over, so a free user sees the
    /// shape of a payroll rather than an empty table.
    ///
    /// Deliberately unmistakable: every row is marked Sample and every amount is a round number.
    /// These are wages, and a plausible-looking figure someone might read as their own is a
    /// worse outcome than a less convincing teaser. Nothing here is written anywhere; the rows
    /// go straight into the display collection and never into <see cref="_all"/>, so no command
    /// can find a run behind them.
    /// </summary>
    private void PopulateSampleData()
    {
        ApprovedCount = 3;
        YearToDateGross = CurrencyService.Format(54000m);
        YearToDateRemittance = CurrencyService.Format(14850m);
        RemittanceDue = CurrencyService.Format(4950m);
        RemittanceDueLabel = "Due to CRA";
        RemittanceDueQuebec = null;

        // Semi-monthly, all in the past, so nothing reads as a deadline the owner has missed.
        var firstOfMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        DateTime lastMonth = firstOfMonth.AddMonths(-1);
        DateTime monthBefore = firstOfMonth.AddMonths(-2);

        PayRuns.Clear();

        PayRuns.Add(SampleRun(firstOfMonth.AddDays(-1), lastMonth.AddDays(15), firstOfMonth.AddDays(-1), 9000m, 6480m));
        PayRuns.Add(SampleRun(lastMonth.AddDays(14), lastMonth, lastMonth.AddDays(14), 9000m, 6480m));
        PayRuns.Add(SampleRun(lastMonth.AddDays(-1), monthBefore.AddDays(15), lastMonth.AddDays(-1), 8500m, 6140m));

        TotalPages = 1;
        CurrentPage = 1;
        PaginationText = PaginationTextHelper.FormatPaginationText(
            PayRuns.Count, 1, PageSize, 1, "pay run", "pay runs");

        NotifyPaginationChanged();
    }

    private static PayRunDisplayItem SampleRun(
        DateTime payDate, DateTime periodStart, DateTime periodEnd, decimal gross, decimal net) => new()
    {
        Id = string.Empty,
        PayDate = payDate.ToString("yyyy-MM-dd"),
        Period = $"{periodStart:yyyy-MM-dd} to {periodEnd:yyyy-MM-dd}",
        Employees = "3",
        Gross = CurrencyService.Format(gross),
        Net = CurrencyService.Format(net),
        Status = "Sample",

        // Nothing to view, download or void, and the teaser blocks hit-testing anyway.
        IsApproved = false,
    };

    private void UpdateStatistics()
    {
        int year = DateTime.Today.Year;

        // Money totals include voided runs, because their reversals are included too and the
        // pair cancels to zero. The count does not: a voided run and its reversal are not two
        // more payrolls the owner ran.
        List<PayRun> thisYear = _all
            .Where(r => r.Status != PayRunStatus.Draft && r.PayDate.Year == year)
            .ToList();

        ApprovedCount = thisYear.Count(r => r.Status == PayRunStatus.Approved
                                            && r.VoidsPayRunId is not { Length: > 0 });
        YearToDateGross = CurrencyService.Format(thisYear.Sum(r => r.TotalGross));
        YearToDateRemittance = CurrencyService.Format(thisYear.Sum(r => r.TotalRemittance));

        // Read off every run rather than off thisYear, because the deadline in the first half of
        // January covers the previous December.
        RemitterType remitter = App.CompanyManager?.CompanyData?.Settings.Company.RemitterType
                                ?? RemitterType.Regular;

        (decimal due, decimal quebec, DateTime dueDate) =
            PayrollService.NextRemittanceByAgency(_all, DateTime.Today, remitter);

        RemittanceDue = CurrencyService.Format(due);
        RemittanceDueLabel = $"Due to CRA by {dueDate:d MMMM}";
        RemittanceDueQuebec = quebec > 0 ? $"Plus {CurrencyService.Format(quebec)} to Revenu Quebec" : null;
        RemittanceScheduleHint =
            $"{remitter.DisplayName()}. {remitter.Description()} Change it under Year end if CRA "
            + "has you on a different schedule.";
    }

    private void Filter()
    {
        // The teaser owns the collection while it is up. Sorting or paging would otherwise clear
        // the sample rows and leave the upgrade card floating over an empty table.
        if (ShowTeaser)
        {
            return;
        }

        PayRuns.Clear();

        IEnumerable<PayRun> filtered = _all;

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string q = SearchQuery.Trim();
            filtered = filtered.Where(r =>
                r.Id.Contains(q, StringComparison.OrdinalIgnoreCase)
                || r.Lines.Any(l => l.EmployeeName.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        // Newest first: the run someone wants is almost always the last one.
        List<PayRun> ordered = filtered
            .OrderByDescending(r => r.PayDate)
            .ThenByDescending(r => r.Id, StringComparer.Ordinal)
            .ToList();

        foreach (PayRun run in Paginate(ordered, "pay run", "pay runs"))
        {
            PayRuns.Add(PayRunDisplayItem.From(run));
        }
    }

}

/// <summary>One row of the pay runs table, already formatted for display.</summary>
public partial class PayRunDisplayItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _payDate = string.Empty;

    [ObservableProperty]
    private string _period = string.Empty;

    [ObservableProperty]
    private string _employees = string.Empty;

    [ObservableProperty]
    private string _gross = string.Empty;

    [ObservableProperty]
    private string _net = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>Only an approved run can be voided, and only it has stubs worth printing.</summary>
    [ObservableProperty]
    private bool _isApproved;

    public static PayRunDisplayItem From(PayRun run) => new()
    {
        Id = run.Id,
        PayDate = run.PayDate.ToString("yyyy-MM-dd"),
        Period = $"{run.PeriodStart:yyyy-MM-dd} to {run.PeriodEnd:yyyy-MM-dd}",
        Employees = run.Lines.Count.ToString(),
        Gross = CurrencyService.Format(run.TotalGross),
        Net = CurrencyService.Format(run.TotalNetPay),
        Status = run.Status switch
        {
            PayRunStatus.Draft => "Draft",
            PayRunStatus.Void => "Void",
            _ => run.VoidsPayRunId is { Length: > 0 } ? "Reversal" : "Approved",
        },
        IsApproved = run.Status == PayRunStatus.Approved && run.VoidsPayRunId is not { Length: > 0 },
    };
}
