using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The formal reports agree for a US company, where tax paid on purchases is a cost rather than
/// claimed back, so it never comes off Sales Tax Payable.
/// </summary>
public class UsCompanyReconciliationTests
{
    private static readonly ReportFilters ToEndOf2024 = new() { EndDate = new DateTime(2024, 12, 31) };

    // A cash sale, a part-paid invoice, a purchase with tax and a refund with its tax.
    private static CompanyData UsCompany()
    {
        var data = new CompanyData();
        data.Settings.Company.Country = "United States";
        data.Settings.Localization.Currency = "USD";
        data.Customers.Add(new Customer { Id = "C1", Name = "Acme" });
        data.Customers.Add(new Customer { Id = "C2", Name = "Birch Co" });
        data.Suppliers.Add(new Supplier { Id = "S1", Name = "Paper Mill" });

        data.Revenues.Add(new Revenue
        {
            Id = "R1", CustomerId = "C1", Date = new DateTime(2024, 1, 10), OriginalCurrency = "USD",
            Subtotal = 1000m, TaxAmount = 80m, TaxAmountUSD = 80m, Total = 1080m, TotalUSD = 1080m
        });

        var invoice = new Invoice
        {
            Id = "INV-1", InvoiceNumber = "INV-1", CustomerId = "C2", OriginalCurrency = "USD",
            IssueDate = new DateTime(2024, 2, 1), DueDate = new DateTime(2024, 3, 1),
            Subtotal = 2000m, TaxAmount = 160m, Total = 2160m, TotalUSD = 2160m, Status = InvoiceStatus.Sent
        };
        data.Invoices.Add(invoice);
        data.Revenues.Add(new Revenue
        {
            Id = "R2", CustomerId = "C2", InvoiceId = "INV-1", Date = new DateTime(2024, 2, 1), OriginalCurrency = "USD",
            Subtotal = 2000m, TaxAmount = 160m, TaxAmountUSD = 160m, Total = 2160m, TotalUSD = 2160m,
            PaymentStatus = RevenuePaymentStatus.Unpaid
        });
        data.Payments.Add(new Payment
        {
            Id = "PAY-1", InvoiceId = "INV-1", CustomerId = "C2", Date = new DateTime(2024, 3, 1), OriginalCurrency = "USD",
            Amount = 1000m, AmountUSD = 1000m
        });
        InvoiceTotalsService.Recalculate(invoice, data.Payments);

        data.Expenses.Add(new Expense
        {
            Id = "E1", SupplierId = "S1", Date = new DateTime(2024, 2, 5), OriginalCurrency = "USD",
            Amount = 500m, TaxAmount = 40m, TaxAmountUSD = 40m, Total = 540m, TotalUSD = 540m
        });

        data.Payments.Add(new Payment
        {
            Id = "REF-1", CustomerId = "C1", Date = new DateTime(2024, 3, 10), OriginalCurrency = "USD",
            Amount = -108m, AmountUSD = -108m, IsRefund = true, RefundTaxAmount = 8m
        });
        return data;
    }

    private static AccountingTableData Report(AccountingReportType type) =>
        new AccountingReportDataService(UsCompany(), ToEndOf2024).GetReportData(type);

    // Formatted amounts show a negative in parentheses.
    private static decimal ParseAmount(string formatted)
    {
        var value = decimal.Parse(new string(formatted.Where(c => char.IsDigit(c) || c == '.').ToArray()),
            System.Globalization.CultureInfo.InvariantCulture);
        return formatted.StartsWith('(') ? -value : value;
    }

    private static decimal Amount(AccountingTableData table, string label, int column = 0) =>
        ParseAmount(table.Rows.Single(r => r.Label == label).Values[column]);

    private static decimal GrandTotal(AccountingTableData table) =>
        ParseAmount(table.Rows.Single(r => r.RowType == AccountingRowType.GrandTotalRow).Values[0]);

    [Fact]
    public void IncomeStatement_CountsPurchaseTaxAsPartOfTheCost()
    {
        var incomeStatement = Report(AccountingReportType.IncomeStatement);

        Assert.Equal(2900m, Amount(incomeStatement, "Total Revenue"));
        Assert.Equal(540m, Amount(incomeStatement, "Total Operating Expenses"));
        Assert.Equal(2360m, Amount(incomeStatement, "NET INCOME"));
    }

    [Fact]
    public void BalanceSheet_SalesTaxPayable_IsTaxCollectedLessTaxRefunded_WithNothingClaimedBack()
    {
        var balanceSheet = Report(AccountingReportType.BalanceSheet);

        Assert.Equal(232m, Amount(balanceSheet, "Sales Tax Payable"));
    }

    [Fact]
    public void BalanceSheet_Balances_WithCashReceivablesAndEarningsAsExpected()
    {
        var balanceSheet = Report(AccountingReportType.BalanceSheet);

        Assert.Equal(1432m, Amount(balanceSheet, "Cash (Estimated)"));
        Assert.Equal(1160m, Amount(balanceSheet, "Accounts Receivable"));
        Assert.Equal(Amount(balanceSheet, "TOTAL ASSETS"), Amount(balanceSheet, "TOTAL LIABILITIES & EQUITY"));
        Assert.Equal(0m, Amount(balanceSheet, "Owner's Equity"));
    }

    [Fact]
    public void BalanceSheet_RetainedEarnings_AreTheIncomeStatementsNetIncome()
    {
        Assert.Equal(
            Amount(Report(AccountingReportType.IncomeStatement), "NET INCOME"),
            Amount(Report(AccountingReportType.BalanceSheet), "Retained Earnings"));
    }

    [Fact]
    public void BalanceSheet_Cash_IsTheCashFlowStatementsNetChange()
    {
        Assert.Equal(
            GrandTotal(Report(AccountingReportType.CashFlowStatement)),
            Amount(Report(AccountingReportType.BalanceSheet), "Cash (Estimated)"));
    }

    [Fact]
    public void BalanceSheet_TaxOwed_IsTheTaxSummarysNetTax()
    {
        Assert.Equal(
            GrandTotal(Report(AccountingReportType.TaxSummary)),
            Amount(Report(AccountingReportType.BalanceSheet), "Sales Tax Payable"));
    }

    [Fact]
    public void SalesByCustomer_AddsUpToTheIncomeStatementsRevenue()
    {
        Assert.Equal(
            Amount(Report(AccountingReportType.IncomeStatement), "Total Revenue"),
            Amount(Report(AccountingReportType.SalesByCustomer), "Total", 2));
    }
}
