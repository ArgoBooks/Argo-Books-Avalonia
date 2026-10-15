using System.Globalization;
using System.Reflection;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// The Analytics Taxes tab's Tax Collected, Tax Paid, Net Liability and Effective Tax Rate cards.
/// A user files sales tax from these figures or from the Tax Summary report, so for the same paid
/// sales and the same dates the two must give the same amounts (docs/Calculations.md §8, §11).
/// </summary>
public class AnalyticsTaxStatisticsTests : ModalViewModelTestBase
{
    private static readonly DateTime PeriodStart = new(2024, 1, 1);
    private static readonly DateTime PeriodLastDay = new(2024, 12, 31);

    public AnalyticsTaxStatisticsTests()
    {
        UseNoExchangeRates();
        // A custom range ends at the last tick of its last day, as the date range picker sets it.
        ChartSettingsService.Instance.StartDate = PeriodStart;
        ChartSettingsService.Instance.EndDate = PeriodLastDay.AddDays(1).AddTicks(-1);
    }

    /// <summary>
    /// Paid sales, a purchase, and a refund that handed back tax, plus rows just outside the period on
    /// both sides. Tax collected is 20 + 8 + 4 less the 10 of tax the refund gave back, which is 22;
    /// tax paid is 5; the net owed is 17. The cards and the Tax Summary report must both say so.
    /// </summary>
    [Fact]
    public void LoadTaxesStatistics_ForPaidSalesAndARefund_AgreesWithTheTaxSummaryReport()
    {
        AddRefundedInvoiceSale();
        Company.Revenues.Add(Sale("REV-CASH", new DateTime(2024, 6, 1), preTax: 100m, tax: 8m, rate: 8m));
        // Entered at a time of day on the last day of the period, which still belongs to the period.
        Company.Revenues.Add(Sale("REV-LASTDAY", PeriodLastDay.AddHours(15.5), preTax: 50m, tax: 4m, rate: 8m));
        Company.Revenues.Add(Sale("REV-BEFORE", PeriodStart.AddHours(-1), preTax: 500m, tax: 50m, rate: 10m));
        Company.Expenses.Add(Purchase("EXP-1", new DateTime(2024, 5, 1), preTax: 100m, tax: 5m, rate: 5m));
        Company.Expenses.Add(Purchase("EXP-AFTER", PeriodLastDay.AddDays(1), preTax: 100m, tax: 7m, rate: 7m));
        // A second refund issued after the period comes off the next period's tax, not this one's.
        Company.Payments.Add(Refund("PAY-T3", new DateTime(2025, 1, 5), 22m));

        var vm = LoadTaxStatistics();
        var (reportCollected, reportPaid, reportNet) = TaxSummary();

        Assert.Equal(22m, reportCollected);
        Assert.Equal(5m, reportPaid);
        Assert.Equal(17m, reportNet);
        Assert.Equal(CurrencyService.Format(reportCollected), vm.TotalTaxCollected);
        Assert.Equal(CurrencyService.Format(reportPaid), vm.TotalTaxPaid);
        Assert.Equal(CurrencyService.Format(reportNet), vm.NetTaxLiability);
    }

    /// <summary>
    /// The cards count only money actually received (Rule 2), so tax on a sale that has not been paid
    /// is not tax collected yet. The Tax Summary report counts it, since formal reports count every
    /// sale (§10); that is the one documented place the two differ.
    /// </summary>
    [Fact]
    public void LoadTaxesStatistics_UnpaidSale_IsNotCountedAsTaxCollected()
    {
        Company.Revenues.Add(Sale("REV-PAID", new DateTime(2024, 6, 1), preTax: 100m, tax: 10m, rate: 10m));
        var unpaid = Sale("REV-UNPAID", new DateTime(2024, 6, 2), preTax: 300m, tax: 30m, rate: 10m);
        unpaid.PaymentStatus = RevenuePaymentStatus.Unpaid;
        Company.Revenues.Add(unpaid);

        var vm = LoadTaxStatistics();

        Assert.Equal(CurrencyService.Format(10m), vm.TotalTaxCollected);
        Assert.Equal(CurrencyService.Format(10m), vm.NetTaxLiability);
    }

    /// <summary>
    /// The effective rate is all tax over all pre-tax amounts: (10 + 5) / (100 + 100) is 7.5%. A
    /// refund hands back tax and pre-tax money in the same proportion, so it leaves the rate as it was
    /// rather than dragging it down by taking the tax off alone (§11).
    /// </summary>
    [Fact]
    public void LoadTaxesStatistics_EffectiveRate_IsAllTaxOverAllPreTaxAmounts_AndARefundLeavesItUnchanged()
    {
        Company.Invoices.Add(new Invoice
        {
            Id = "INV-R", InvoiceNumber = "INV-R", CustomerId = "C1", OriginalCurrency = "USD",
            IssueDate = new DateTime(2024, 3, 1), Subtotal = 100m, TaxAmount = 10m,
            Total = 110m, TotalUSD = 110m, Status = InvoiceStatus.PartiallyRefunded
        });
        var sale = Sale("REV-R", new DateTime(2024, 3, 1), preTax: 100m, tax: 10m, rate: 10m);
        sale.InvoiceId = "INV-R";
        Company.Revenues.Add(sale);
        Company.Expenses.Add(Purchase("EXP-R", new DateTime(2024, 4, 1), preTax: 100m, tax: 5m, rate: 5m));
        var expected = string.Format(CultureInfo.CurrentCulture, "{0:F1}%", 7.5m);

        Assert.Equal(expected, LoadTaxStatistics().EffectiveTaxRate);

        Company.Payments.Add(new Payment
        {
            Id = "PAY-R", InvoiceId = "INV-R", OriginalCurrency = "USD", Date = new DateTime(2024, 5, 1),
            Amount = -55m, AmountUSD = -55m, IsRefund = true
        });

        Assert.Equal(expected, LoadTaxStatistics().EffectiveTaxRate);
    }

    #region Helpers

    // 200 + 20 tax = 220, paid Mar 1 and half refunded Apr 10, which hands back 10 of the tax.
    private void AddRefundedInvoiceSale()
    {
        Company.Invoices.Add(new Invoice
        {
            Id = "INV-T", InvoiceNumber = "INV-T", CustomerId = "C1", OriginalCurrency = "USD",
            IssueDate = new DateTime(2024, 3, 1), Subtotal = 200m, TaxAmount = 20m,
            Total = 220m, TotalUSD = 220m, Status = InvoiceStatus.PartiallyRefunded
        });
        var sale = Sale("REV-T", new DateTime(2024, 3, 1), preTax: 200m, tax: 20m, rate: 10m);
        sale.InvoiceId = "INV-T";
        Company.Revenues.Add(sale);
        Company.Payments.Add(new Payment
        {
            Id = "PAY-T1", InvoiceId = "INV-T", OriginalCurrency = "USD", Date = new DateTime(2024, 3, 1),
            Amount = 220m, AmountUSD = 220m
        });
        Company.Payments.Add(Refund("PAY-T2", new DateTime(2024, 4, 10), 110m));
    }

    private static Payment Refund(string id, DateTime date, decimal amount) => new()
    {
        Id = id, InvoiceId = "INV-T", OriginalCurrency = "USD", Date = date,
        Amount = -amount, AmountUSD = -amount, IsRefund = true
    };

    private static Revenue Sale(string id, DateTime date, decimal preTax, decimal tax, decimal rate) => new()
    {
        Id = id, Date = date, OriginalCurrency = "USD", Subtotal = preTax, TaxRate = rate,
        TaxAmount = tax, TaxAmountUSD = tax, Total = preTax + tax, TotalUSD = preTax + tax,
        PaymentStatus = RevenuePaymentStatus.Paid
    };

    private static Expense Purchase(string id, DateTime date, decimal preTax, decimal tax, decimal rate) => new()
    {
        Id = id, Date = date, OriginalCurrency = "USD", Amount = preTax, TaxRate = rate,
        TaxAmount = tax, TaxAmountUSD = tax, Total = preTax + tax, TotalUSD = preTax + tax
    };

    private AnalyticsPageViewModel LoadTaxStatistics()
    {
        var vm = new AnalyticsPageViewModel();
        try
        {
            typeof(AnalyticsPageViewModel).GetMethod("LoadTaxesStatistics", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(vm, [Company]);
        }
        finally
        {
            vm.Cleanup();
        }
        return vm;
    }

    private (decimal Collected, decimal Paid, decimal Net) TaxSummary()
    {
        var filters = new ReportFilters { StartDate = PeriodStart, EndDate = PeriodLastDay };
        var report = new AccountingReportDataService(Company, filters).GetReportData(AccountingReportType.TaxSummary);

        // The two subtotal rows are Total Tax Collected then Total Tax Paid.
        var subtotals = report.Rows.FindAll(r => r.RowType == AccountingRowType.SubtotalRow);
        var net = report.Rows.Single(r => r.RowType == AccountingRowType.GrandTotalRow);
        return (ParseAmount(subtotals[0].Values[0]), ParseAmount(subtotals[1].Values[0]), ParseAmount(net.Values[0]));
    }

    // The report shows a negative amount in parentheses.
    private static decimal ParseAmount(string formatted)
    {
        var value = decimal.Parse(new string(formatted.Where(c => char.IsDigit(c) || c == '.').ToArray()),
            CultureInfo.InvariantCulture);
        return formatted.StartsWith('(') ? -value : value;
    }

    #endregion
}
