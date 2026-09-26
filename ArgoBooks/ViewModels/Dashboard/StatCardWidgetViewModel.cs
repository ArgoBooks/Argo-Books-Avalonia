using ArgoBooks.Controls;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Dashboard;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.ViewModels.Dashboard;

public enum StatCardKind
{
    Revenue,
    Expenses,
    OutstandingInvoices,
    ActiveRentals,
    NetProfit,
    TotalCustomers,
    InventoryValue,
    OverdueInvoices,
    PayrollRemittance
}

public partial class StatCardWidgetViewModel : WidgetViewModelBase
{
    public StatCardKind Kind { get; }

    public override WidgetType WidgetType => Kind switch
    {
        StatCardKind.Revenue => WidgetType.StatCardRevenue,
        StatCardKind.Expenses => WidgetType.StatCardExpenses,
        StatCardKind.OutstandingInvoices => WidgetType.StatCardOutstandingInvoices,
        StatCardKind.ActiveRentals => WidgetType.StatCardActiveRentals,
        StatCardKind.NetProfit => WidgetType.StatCardNetProfit,
        StatCardKind.TotalCustomers => WidgetType.StatCardTotalCustomers,
        StatCardKind.InventoryValue => WidgetType.StatCardInventoryValue,
        StatCardKind.OverdueInvoices => WidgetType.StatCardOverdueInvoices,
        StatCardKind.PayrollRemittance => WidgetType.StatCardPayrollRemittance,
        _ => WidgetType.StatCardRevenue
    };

    [ObservableProperty]
    private string _label = "";

    [ObservableProperty]
    private string _value = "$0.00";

    [ObservableProperty]
    private Geometry? _iconGeometry;

    [ObservableProperty]
    private StatCardColor _iconColor = StatCardColor.Primary;

    [ObservableProperty]
    private double? _changeValue;

    [ObservableProperty]
    private string? _changeText;

    [ObservableProperty]
    private string _changeLabel = "";

    [ObservableProperty]
    private string? _secondaryText;

    public StatCardWidgetViewModel(StatCardKind kind)
    {
        Kind = kind;
        SetStaticProperties();
    }

    private void SetStaticProperties()
    {
        switch (Kind)
        {
            case StatCardKind.Expenses:
                Label = "Total Expenses";
                IconGeometry = Geometry.Parse(Icons.Cash);
                IconColor = StatCardColor.Danger;
                break;
            case StatCardKind.Revenue:
                Label = "Total Revenue";
                IconGeometry = Geometry.Parse(Icons.DollarCircle);
                IconColor = StatCardColor.Success;
                break;
            case StatCardKind.OutstandingInvoices:
                Label = "Outstanding Invoices";
                IconGeometry = Geometry.Parse(Icons.Reports);
                IconColor = StatCardColor.Warning;
                break;
            case StatCardKind.ActiveRentals:
                Label = "Active Rentals";
                IconGeometry = Geometry.Parse(Icons.Bookmark);
                IconColor = StatCardColor.Info;
                break;
            case StatCardKind.NetProfit:
                Label = "Net Profit";
                IconGeometry = Geometry.Parse(Icons.TrendUp);
                IconColor = StatCardColor.Success;
                break;
            case StatCardKind.TotalCustomers:
                Label = "Total Customers";
                IconGeometry = Geometry.Parse(Icons.Customers);
                IconColor = StatCardColor.Info;
                break;
            case StatCardKind.InventoryValue:
                Label = "Inventory Value";
                IconGeometry = Geometry.Parse(Icons.Warehouse);
                IconColor = StatCardColor.Warning;
                break;
            case StatCardKind.OverdueInvoices:
                Label = "Overdue Invoices";
                IconGeometry = Geometry.Parse(Icons.WarningCircle);
                IconColor = StatCardColor.Danger;
                break;
            case StatCardKind.PayrollRemittance:
                Label = "Next Remittance Due";
                IconGeometry = Geometry.Parse(Icons.Payments);
                IconColor = StatCardColor.Warning;
                break;
        }
    }

    public override void LoadData()
    {
        var data = CompanyManager?.CompanyData;
        if (data == null) return;

        var chartSettings = ChartSettingsService.Instance;
        var startDate = chartSettings.StartDate;
        var endDate = chartSettings.EndDate;
        ChangeLabel = chartSettings.ComparisonPeriodLabel;

        switch (Kind)
        {
            case StatCardKind.Revenue:
                LoadRevenue(data, startDate, endDate);
                break;
            case StatCardKind.Expenses:
                LoadExpenses(data, startDate, endDate);
                break;
            case StatCardKind.OutstandingInvoices:
                LoadOutstandingInvoices(data);
                break;
            case StatCardKind.ActiveRentals:
                LoadActiveRentals(data);
                break;
            case StatCardKind.NetProfit:
                LoadNetProfit(data, startDate, endDate);
                break;
            case StatCardKind.TotalCustomers:
                LoadTotalCustomers(data);
                break;
            case StatCardKind.InventoryValue:
                LoadInventoryValue(data);
                break;
            case StatCardKind.OverdueInvoices:
                LoadOverdueInvoices(data);
                break;
            case StatCardKind.PayrollRemittance:
                LoadPayrollRemittance(data);
                break;
        }
    }

    private void LoadRevenue(CompanyData data, DateTime startDate, DateTime endDate)
    {
        // Total Revenue display uses gross-of-tax USD per docs/Calculations.md
        // §2 Rule 1, paid-only per Rule 2, full refund subtraction per §8.
        var grossUSD = RevenueAggregator.SumCollectedRevenueUSD(data.Revenues, startDate, endDate);
        var refundsUSD = RefundAggregator.GetRefundedInDateRangeUSD(data.Payments, startDate, endDate);
        var currentUSD = grossUSD - refundsUSD;
        // Convert each revenue/refund at its OWN date before summing (Calculations.md Rule 3a),
        // so a non-USD display total isn't re-priced at today's rate. currentUSD is kept for the
        // currency-agnostic period-over-period change below.
        Value = CurrencyService.FormatTotalOrPending(convert =>
            RevenueAggregator.SumCollectedRevenueDisplay(data.Revenues, startDate, endDate, convert)
            - RefundAggregator.GetRefundedInDateRangeDisplay(data.Payments, startDate, endDate, convert));

        var (prevStart, prevEnd) = DashboardCalculations.GetComparisonPeriod();
        if (prevStart != DateTime.MinValue && DashboardCalculations.HasSufficientPriorData(data, prevStart))
        {
            var prevGrossUSD = RevenueAggregator.SumCollectedRevenueUSD(data.Revenues, prevStart, prevEnd);
            var prevRefundsUSD = RefundAggregator.GetRefundedInDateRangeUSD(data.Payments, prevStart, prevEnd);
            var prevUSD = prevGrossUSD - prevRefundsUSD;
            ChangeValue = DashboardCalculations.CalculatePercentageChange(prevUSD, currentUSD);
            ChangeText = DashboardCalculations.FormatPercentageChange(ChangeValue);
        }
        else
        {
            ChangeValue = null;
            ChangeText = null;
        }
    }

    private void LoadExpenses(CompanyData data, DateTime startDate, DateTime endDate)
    {
        var currentUSD = ExpenseAggregator.SumExpensesUSD(data.Expenses, startDate, endDate);
        // Convert each expense at its OWN date before summing (Calculations.md Rule 3a), so a
        // non-USD display total isn't re-priced at today's rate. The USD value above is kept for the
        // currency-agnostic period-over-period change below.
        Value = CurrencyService.FormatSumDisplayFromUSD(
            data.Expenses.Where(e => e.Date >= startDate && e.Date <= endDate),
            e => e.Total, e => e.OriginalCurrency, e => e.TotalUSD, e => e.Date);

        var (prevStart, prevEnd) = DashboardCalculations.GetComparisonPeriod();
        if (prevStart != DateTime.MinValue && DashboardCalculations.HasSufficientPriorData(data, prevStart))
        {
            var prevUSD = ExpenseAggregator.SumExpensesUSD(data.Expenses, prevStart, prevEnd);
            ChangeValue = DashboardCalculations.CalculatePercentageChange(prevUSD, currentUSD);
            ChangeText = DashboardCalculations.FormatPercentageChange(ChangeValue);
        }
        else
        {
            ChangeValue = null;
            ChangeText = null;
        }
    }

    private void LoadOutstandingInvoices(CompanyData data)
    {
        var unpaid = data.Invoices
            .Where(i => i.Status != InvoiceStatus.Paid
                     && i.Status != InvoiceStatus.Cancelled
                     && i.Status != InvoiceStatus.Draft)
            .ToList();
        // Convert each invoice balance at its OWN issue date before summing (Calculations.md Rule 3a).
        Value = CurrencyService.FormatSumDisplayFromUSD(
            unpaid, i => i.Balance, i => i.OriginalCurrency, i => i.BalanceUSD, i => i.IssueDate);
        SecondaryText = $"{unpaid.Count} invoices pending";
    }

    private void LoadActiveRentals(CompanyData data)
    {
        var active = data.Rentals
            .Where(r => r.Status == RentalStatus.Active || r.Status == RentalStatus.Overdue)
            .ToList();
        Value = active.Count.ToString();
        var overdue = active.Count(r => r.Status == RentalStatus.Overdue);
        SecondaryText = overdue > 0 ? $"{overdue} overdue" : null;
    }

    private void LoadNetProfit(CompanyData data, DateTime startDate, DateTime endDate)
    {
        // ProfitCalculator owns the formula (pre-tax revenue − expenses −
        // pre-tax refunds); see docs/Calculations.md §2 and §8.
        var profitUSD = ProfitCalculator.CalculateNetProfitUSD(data, startDate, endDate);
        // Convert each contributing transaction at its OWN date before summing (Calculations.md Rule 3a).
        // profitUSD is kept for the currency-agnostic period-over-period change below.
        Value = CurrencyService.FormatTotalOrPending(convert =>
            ProfitCalculator.CalculateNetProfitDisplay(data, startDate, endDate, convert));

        var (prevStart, prevEnd) = DashboardCalculations.GetComparisonPeriod();
        if (prevStart != DateTime.MinValue && DashboardCalculations.HasSufficientPriorData(data, prevStart))
        {
            var prevProfit = ProfitCalculator.CalculateNetProfitUSD(data, prevStart, prevEnd);
            ChangeValue = DashboardCalculations.CalculatePercentageChange(prevProfit, profitUSD);
            ChangeText = DashboardCalculations.FormatPercentageChange(ChangeValue);
        }
        else
        {
            ChangeValue = null;
            ChangeText = null;
        }
    }

    private void LoadTotalCustomers(CompanyData data)
    {
        Value = data.Customers.Count.ToString();
    }

    private void LoadInventoryValue(CompanyData data)
    {
        Value = CurrencyService.FormatStockValue(data.Inventory);
        var lowStock = data.Inventory.Count(i => i.InStock <= i.ReorderPoint && i.InStock > 0);
        SecondaryText = lowStock > 0 ? $"{lowStock} low stock" : $"{data.Inventory.Count} items";
    }

    private void LoadPayrollRemittance(CompanyData data) =>
        (Value, SecondaryText) = PayrollRemittanceCard(data, DateTime.Today);

    /// <summary>
    /// What is owed to CRA next, and the date it is due: the Pay runs page's own figure from the
    /// same call, so the two cannot disagree. It follows the company's remitter type because an
    /// accelerated remitter's deadline comes before the 15th and a quarterly one owes three
    /// months at once. Once the deadline passes the card rolls on by itself.
    ///
    /// Quebec income tax, QPP and QPIP are a separate payment to Revenu Quebec, so they are named
    /// beside the CRA figure rather than added to it.
    /// </summary>
    internal static (string Value, string SecondaryText) PayrollRemittanceCard(CompanyData data, DateTime today)
    {
        (decimal cra, decimal quebec, DateTime due) = PayrollService.NextRemittanceByAgency(
            data.PayRuns, today, data.Settings.Company.RemitterType);

        string secondary = cra <= 0 && quebec <= 0
            ? "Nothing owing"
            : quebec <= 0
                ? $"Due {due:d MMM}"
                : $"Due {due:d MMM}, plus {CurrencyService.Format(quebec)} to Revenu Quebec";

        return (CurrencyService.Format(cra), secondary);
    }

    private void LoadOverdueInvoices(CompanyData data)
    {
        var overdue = data.Invoices.Where(i => i.IsOverdue).ToList();
        // Convert each invoice balance at its OWN issue date before summing (Calculations.md Rule 3a).
        Value = CurrencyService.FormatSumDisplayFromUSD(
            overdue, i => i.Balance, i => i.OriginalCurrency, i => i.BalanceUSD, i => i.IssueDate);
        SecondaryText = $"{overdue.Count} overdue";
    }

}
