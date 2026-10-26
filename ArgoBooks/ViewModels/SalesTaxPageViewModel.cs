using System.Collections.ObjectModel;
using ArgoBooks.Controls;
using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.Helpers;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// ViewModel for the Sales Tax page, which lists each filing period with what it owes, and the returns
/// filed for them (docs/Calculations.md §17).
/// </summary>
public partial class SalesTaxPageViewModel : SortablePageViewModelBase
{
    #region Statistics

    [ObservableProperty]
    private string _owedNow = "$0";

    [ObservableProperty]
    private string _owedNowDetail = string.Empty;

    [ObservableProperty]
    private string _nextDue = "-";

    [ObservableProperty]
    private string _nextDueDetail = string.Empty;

    [ObservableProperty]
    private string _thisPeriodSoFar = "$0";

    [ObservableProperty]
    private string _thisPeriodName = string.Empty;

    [ObservableProperty]
    private string _paidThisYear = "$0";

    #endregion

    public SalesTaxTableColumnWidths ColumnWidths => App.SalesTaxColumnWidths;

    #region Filing Frequency

    public ObservableCollection<string> FrequencyOptions { get; } = ["Monthly", "Quarterly", "Yearly"];

    /// <summary>How often the business files, or null until it chooses, while the periods are listed quarterly.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFrequencyChosen))]
    private string? _selectedFrequency;

    public bool IsFrequencyChosen => SelectedFrequency != null;

    private bool _loadingFrequency;

    // Choosing how often the business files is saved with the company, and turns on the dashboard's reminder.
    partial void OnSelectedFrequencyChanged(string? value)
    {
        if (_loadingFrequency) return;
        var data = App.CompanyManager?.CompanyData;
        if (data != null && Enum.TryParse<SalesTaxFrequency>(value, out var frequency) && data.Settings.Company.SalesTaxFrequency != frequency)
        {
            data.Settings.Company.SalesTaxFrequency = frequency;
            App.CompanyManager?.MarkAsChanged();
        }
        CurrentPage = 1;
        LoadPeriods();
    }

    private SalesTaxFrequency Frequency => Enum.TryParse<SalesTaxFrequency>(SelectedFrequency, out var f) ? f : SalesTaxFrequency.Quarterly;

    /// <summary>The months a tax year can start in, which only matters to yearly and quarterly filers.</summary>
    public ObservableCollection<string> YearStartOptions { get; } =
        new(Enumerable.Range(1, 12).Select(m => System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m)));

    [ObservableProperty]
    private string? _selectedYearStart;

    public bool ShowYearStart => Frequency != SalesTaxFrequency.Monthly;

    partial void OnSelectedYearStartChanged(string? value)
    {
        if (_loadingFrequency || value == null) return;
        var data = App.CompanyManager?.CompanyData;
        var month = YearStartOptions.IndexOf(value) + 1;
        if (data != null && month > 0 && SalesTaxPeriods.YearStartMonth(data) != month)
        {
            data.Settings.Company.SalesTaxYearStartMonth = month;
            App.CompanyManager?.MarkAsChanged();
        }
        CurrentPage = 1;
        LoadPeriods();
    }

    #endregion

    #region Periods

    private readonly List<SalesTaxPeriodRow> _allPeriods = [];

    public BatchObservableCollection<SalesTaxPeriodRow> Periods { get; } = [];

    #endregion

    protected override void OnSortOrPageChanged() => ShowPage();

    public SalesTaxPageViewModel()
    {
        SortColumn = "Period";
        SortDirection = SortDirection.Descending;
        LoadPeriods();

        // Statuses move on with the date and with changes made on other pages, so it reloads each time it is opened.
        EnableDeferredUndoRefresh(p => p == PageNames.SalesTax, LoadPeriods, onNavigatedHere: LoadPeriods);

        if (App.SalesTaxModalsViewModel != null)
            App.SalesTaxModalsViewModel.FilingChanged += OnFilingChanged;
        CurrencyService.CurrencyChanged += OnFilingChanged;
        DateFormatService.DateFormatChanged += OnFilingChanged;
    }

    public override void Cleanup()
    {
        base.Cleanup();
        if (App.SalesTaxModalsViewModel != null)
            App.SalesTaxModalsViewModel.FilingChanged -= OnFilingChanged;
        CurrencyService.CurrencyChanged -= OnFilingChanged;
        DateFormatService.DateFormatChanged -= OnFilingChanged;
    }

    private void OnFilingChanged(object? sender, EventArgs e) => LoadPeriods();

    private void LoadPeriods()
    {
        _allPeriods.Clear();
        var data = App.CompanyManager?.CompanyData;
        if (data == null)
        {
            Periods.Clear();
            return;
        }

        _loadingFrequency = true;
        SelectedFrequency = data.Settings.Company.SalesTaxFrequency?.ToString();
        SelectedYearStart = YearStartOptions[SalesTaxPeriods.YearStartMonth(data) - 1];
        _loadingFrequency = false;
        OnPropertyChanged(nameof(ShowYearStart));

        var today = DateTime.Today;
        foreach (var row in SalesTaxPeriods.Rows(data, Frequency, today))
        {
            // An amount whose rate isn't in yet counts as 0 and marks the period waiting, never as its USD figure.
            var rateMissing = false;
            decimal ToDisplay(decimal usd, DateTime date)
            {
                if (CurrencyService.TryDisplayFromUSD(usd, date, out var amount)) return amount;
                rateMissing |= usd != 0;
                return 0m;
            }
            var now = SalesTaxPeriods.For(data, row.Period, ToDisplay);
            var waiting = rateMissing || SalesTaxPeriods.IsWaitingForRate(data, row.Period);
            _allPeriods.Add(new SalesTaxPeriodRow(row, now, today, waiting));
        }

        UpdateStatistics(today);
        ShowPage();
    }

    private void UpdateStatistics(DateTime today)
    {
        var waiting = _allPeriods.Where(p => p.Status is "Due" or "Overdue").ToList();
        var changed = _allPeriods.Where(p => p.HasChanged).ToList();
        OwedNow = CurrencyService.Format(waiting.Sum(p => p.Amounts.Net) + changed.Sum(p => p.ChangedBy));
        OwedNowDetail = waiting.Count switch
        {
            0 when changed.Count > 0 => "From changes to filed periods".Translate(),
            0 => "Nothing to file".Translate(),
            1 => "1 period to file".Translate(),
            _ => "{0} periods to file".TranslateFormat(waiting.Count)
        };

        var next = waiting.OrderBy(p => p.Period.DueDate).FirstOrDefault()
                   ?? _allPeriods.FirstOrDefault(p => p.Status == "Open");
        if (next != null)
        {
            NextDue = DateFormatService.Format(next.Period.DueDate);
            var days = (next.Period.DueDate - today).Days;
            NextDueDetail = days < 0
                ? "Overdue by {0} days".TranslateFormat(-days)
                : days == 0 ? "Due today".Translate() : "In {0} days, for {1}".TranslateFormat(days, next.PeriodName);
        }
        else
        {
            NextDue = "-";
            NextDueDetail = string.Empty;
        }

        var open = _allPeriods.FirstOrDefault(p => p.Status == "Open");
        ThisPeriodSoFar = CurrencyService.Format(open?.Amounts.Net ?? 0m);
        ThisPeriodName = open?.PeriodName ?? string.Empty;

        PaidThisYear = CurrencyService.Format(_allPeriods
            .Where(p => p.Filing is { } filing && filing.PaidDate.Year == today.Year)
            .Sum(p => p.Amounts.Net));
    }

    private void ShowPage()
    {
        var sorted = _allPeriods.ApplySort(
            SortColumn,
            SortDirection,
            new Dictionary<string, Func<SalesTaxPeriodRow, object?>>
            {
                ["Period"] = p => p.Period.Start,
                ["Collected"] = p => p.Amounts.Collected - p.Amounts.Refunded,
                ["ClaimedBack"] = p => p.Amounts.ClaimedBack,
                ["Net"] = p => p.Amounts.Net,
                ["Due"] = p => p.Period.DueDate,
                ["Status"] = p => p.Status
            },
            p => p.Period.Start);

        Periods.ReplaceAll(Paginate(sorted, "period"));
    }

    [RelayCommand]
    private void FilePeriod(SalesTaxPeriodRow? row)
    {
        if (row != null)
            App.SalesTaxModalsViewModel?.OpenFileModal(row);
    }

    [RelayCommand]
    private void UndoFiling(SalesTaxPeriodRow? row)
    {
        if (row?.Filing != null)
            App.SalesTaxModalsViewModel?.RemoveFiling(row.Filing);
    }
}

/// <summary>
/// One filing period on the Sales Tax page. A filed period shows what its return reported, and how much
/// the books for it have changed since, which the next return has to put right.
/// </summary>
public class SalesTaxPeriodRow
{
    public SalesTaxPeriodRow(SalesTaxPeriods.Row row, SalesTaxPeriods.Amounts now, DateTime today, bool waitingForRate = false)
    {
        Period = row.Period;
        Frequency = row.Frequency;
        Filing = row.Filing;
        Now = now;
        IsWaitingForRate = waitingForRate;

        if (Filing is { } filing)
        {
            var code = CurrencyService.CurrentCurrencyCode;
            decimal Display(decimal amount) => DisplayCurrency.FromNative(amount, filing.Currency, code, filing.PaidDate.Date) ?? 0m;
            Amounts = new SalesTaxPeriods.Amounts(Display(filing.Collected), Display(filing.Refunded), Display(filing.ClaimedBack));
            Status = "Filed";
        }
        else
        {
            // A period with nothing to pay can still have a nil return recorded, but it isn't chased as overdue.
            Amounts = now;
            Status = Period.End >= today ? "Open"
                : now.Net == 0 && !waitingForRate ? "Nothing to pay"
                : Period.DueDate < today ? "Overdue"
                : "Due";
        }
    }

    /// <summary>Whether something in the period is waiting for its exchange rate, so what it owes isn't known yet.</summary>
    public bool IsWaitingForRate { get; }

    public SalesTaxPeriods.Period Period { get; }

    public SalesTaxFrequency Frequency { get; }

    public SalesTaxFiling? Filing { get; }

    /// <summary>What the period's books show now.</summary>
    public SalesTaxPeriods.Amounts Now { get; }

    /// <summary>What the row shows: the return as filed, or the books as they are for a period not filed yet.</summary>
    public SalesTaxPeriods.Amounts Amounts { get; }

    public string Status { get; }

    /// <summary>How much more the filed period owes than its return paid, from sales or purchases changed since.</summary>
    public decimal ChangedBy => Filing == null || IsWaitingForRate ? 0m : Math.Round(Now.Net - Amounts.Net, 2);

    public bool HasChanged => ChangedBy != 0;

    public string ChangedHint => ChangedBy > 0
        ? "The books for this period now owe {0} more than this return paid. Add it to your next return.".TranslateFormat(CurrencyService.Format(ChangedBy))
        : "The books for this period now owe {0} less than this return paid. Take it off your next return.".TranslateFormat(CurrencyService.Format(-ChangedBy));

    public string PeriodName => SalesTaxPeriods.NameOf(Period, Frequency);

    public string DatesText => $"{DateFormatService.Format(Period.Start)} - {DateFormatService.Format(Period.End)}";

    public string CollectedFormatted => CurrencyService.Format(Amounts.Collected - Amounts.Refunded);

    public string ClaimedBackFormatted => CurrencyService.Format(Amounts.ClaimedBack);

    public string NetFormatted => IsWaitingForRate && Filing == null ? CurrencyService.PendingMarker : CurrencyService.Format(Math.Abs(Amounts.Net));

    public string WaitingHint => IsWaitingForRate && Filing == null
        ? "Some of this period's amounts are waiting for their exchange rate, so it can be filed once they convert.".Translate()
        : string.Empty;

    /// <summary>A period whose purchases carried more tax than its sales is owed a refund, or was refunded once filed.</summary>
    public bool IsRefundDue => Amounts.Net < 0;

    public string RefundLabel => Filing == null ? "Refund due".Translate() : "Refunded".Translate();

    public string DueFormatted => DateFormatService.Format(Period.DueDate);

    public bool CanFile => Status is "Due" or "Overdue" or "Nothing to pay" && !IsWaitingForRate;

    public bool IsFiled => Filing != null;

    public string FiledHint => Filing is { } filing
        ? string.IsNullOrWhiteSpace(filing.ConfirmationNumber)
            ? "Filed on {0}".TranslateFormat(DateFormatService.Format(filing.PaidDate))
            : "Filed on {0}, confirmation {1}".TranslateFormat(DateFormatService.Format(filing.PaidDate), filing.ConfirmationNumber)
        : string.Empty;
}
