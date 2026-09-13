using ArgoBooks.Core.Services.Sync;

namespace ArgoBooks.Mobile.ViewModels;

/// <summary>
/// Home tab: money in/out/profit stat cards, a cash-flow chart built from the Revenue/Expenses
/// row lists (grouped by month), "who owes you" (customers with an outstanding balance), and a
/// short recent-activity feed. Read-only, mirrors the desktop dashboard totals.
/// </summary>
public partial class DashboardViewModel : ViewModelBase
{
    private readonly Action<RowDto> _onOpenRow;

    [ObservableProperty]
    private bool _hasData;

    [ObservableProperty]
    private string _moneyIn = "$0.00";

    [ObservableProperty]
    private string _moneyOut = "$0.00";

    [ObservableProperty]
    private string _profit = "$0.00";

    [ObservableProperty]
    private string _profitMargin = "0%";

    public ObservableCollection<RowItemViewModel> WhoOwesYou { get; } = new();

    public ObservableCollection<RowItemViewModel> RecentExpenses { get; } = new();

    public ObservableCollection<RowItemViewModel> RecentRevenue { get; } = new();

    public ObservableCollection<ISeries> CashFlowSeries { get; } = new();

    public ObservableCollection<Axis> CashFlowXAxes { get; } = new();

    /// <summary>True once a cash-flow chart could be built (needs at least one dated row).</summary>
    [ObservableProperty]
    private bool _hasCashFlowChart;

    /// <summary>True when at least one customer has an outstanding balance.</summary>
    [ObservableProperty]
    private bool _hasWhoOwesYou;

    public DashboardViewModel(Action<RowDto> onOpenRow)
    {
        _onOpenRow = onOpenRow;
    }

    public void UpdateSnapshot(MobileSnapshot? snapshot)
    {
        HasData = snapshot != null;

        var dashboard = snapshot?.Dashboard ?? new DashboardDto();
        MoneyIn = FormatMoney(dashboard.MoneyIn);
        MoneyOut = FormatMoney(dashboard.MoneyOut);
        Profit = FormatMoney(dashboard.Profit);
        ProfitMargin = dashboard.ProfitMargin.ToString("P0", CultureInfo.InvariantCulture);

        WhoOwesYou.Clear();
        RecentExpenses.Clear();
        RecentRevenue.Clear();

        if (snapshot != null)
        {
            foreach (var customer in snapshot.Customers
                         .Where(c => ParseAmount(c.Amount) > 0)
                         .OrderByDescending(c => ParseAmount(c.Amount))
                         .Take(5))
            {
                WhoOwesYou.Add(new RowItemViewModel(customer, _onOpenRow));
            }

            foreach (var expense in snapshot.Expenses.Take(5))
            {
                RecentExpenses.Add(new RowItemViewModel(expense, _onOpenRow));
            }

            foreach (var revenue in snapshot.Revenue.Take(5))
            {
                RecentRevenue.Add(new RowItemViewModel(revenue, _onOpenRow));
            }
        }

        HasWhoOwesYou = WhoOwesYou.Count > 0;
        BuildCashFlowChart(snapshot);
    }

    private void BuildCashFlowChart(MobileSnapshot? snapshot)
    {
        CashFlowSeries.Clear();
        CashFlowXAxes.Clear();
        HasCashFlowChart = false;

        if (snapshot == null)
        {
            return;
        }

        var months = new List<(DateTime Month, decimal In, decimal Out)>();

        void Accumulate(IEnumerable<RowDto> rows, bool isIn)
        {
            foreach (var row in rows)
            {
                if (!DateTime.TryParseExact(row.Subtitle, "MMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    continue;
                }

                var month = new DateTime(date.Year, date.Month, 1);
                var amount = Math.Abs(ParseAmount(row.Amount));
                var index = months.FindIndex(m => m.Month == month);
                if (index < 0)
                {
                    months.Add((month, isIn ? amount : 0m, isIn ? 0m : amount));
                }
                else
                {
                    var existing = months[index];
                    months[index] = (month, existing.In + (isIn ? amount : 0m), existing.Out + (isIn ? 0m : amount));
                }
            }
        }

        Accumulate(snapshot.Revenue, isIn: true);
        Accumulate(snapshot.Expenses, isIn: false);

        if (months.Count == 0)
        {
            return;
        }

        months.Sort((a, b) => a.Month.CompareTo(b.Month));

        CashFlowSeries.Add(new LineSeries<decimal>
        {
            Values = months.Select(m => m.In).ToArray(),
            Name = "Money in",
            Fill = null,
            GeometrySize = 5,
        });
        CashFlowSeries.Add(new LineSeries<decimal>
        {
            Values = months.Select(m => m.Out).ToArray(),
            Name = "Money out",
            Fill = null,
            GeometrySize = 5,
        });
        CashFlowXAxes.Add(new Axis
        {
            Labels = months.Select(m => m.Month.ToString("MMM", CultureInfo.InvariantCulture)).ToArray(),
        });

        HasCashFlowChart = true;
    }

    private static decimal ParseAmount(string amount)
    {
        var cleaned = new string(amount.Where(ch => char.IsDigit(ch) || ch is '.' or '-').ToArray());
        return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0m;
    }

    private static string FormatMoney(decimal amount) => amount.ToString("C2", CultureInfo.InvariantCulture);
}
