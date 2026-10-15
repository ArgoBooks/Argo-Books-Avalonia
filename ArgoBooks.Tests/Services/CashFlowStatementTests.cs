using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The cash flow statement counts each dollar that came in or went out once. Money for an invoice
/// is counted when the invoice payment arrives, so the revenue row the invoice made is left out of
/// Cash from Sales.
/// </summary>
public class CashFlowStatementTests
{
    private static readonly ReportFilters June2026 = new()
    {
        StartDate = new DateTime(2026, 6, 1),
        EndDate = new DateTime(2026, 6, 30)
    };

    private static Revenue Sale(string id, decimal total, DateTime date,
        RevenuePaymentStatus status = RevenuePaymentStatus.Paid, string? invoiceId = null) => new()
    {
        Id = id, Date = date, OriginalCurrency = "USD", Subtotal = total, Total = total, Amount = total,
        PaymentStatus = status, InvoiceId = invoiceId
    };

    private static Expense Spent(string id, decimal total, DateTime date) => new()
    {
        Id = id, Date = date, OriginalCurrency = "USD", Total = total, Amount = total
    };

    /// <summary>A $500 invoice issued June 3, its revenue row, and the payment for it on June 10.</summary>
    private static void AddPaidInvoice(CompanyData data)
    {
        data.Invoices.Add(new Invoice
        {
            Id = "INV-1", InvoiceNumber = "INV-1", CustomerId = "C1", OriginalCurrency = "USD",
            IssueDate = new DateTime(2026, 6, 3), Subtotal = 500m, Total = 500m, TotalUSD = 500m,
            Status = InvoiceStatus.Paid
        });
        data.Revenues.Add(Sale("REV-1", 500m, new DateTime(2026, 6, 3), invoiceId: "INV-1"));
        data.Payments.Add(new Payment
        {
            Id = "PAY-1", InvoiceId = "INV-1", CustomerId = "C1", OriginalCurrency = "USD",
            Date = new DateTime(2026, 6, 10), Amount = 500m, AmountUSD = 500m
        });
    }

    private static AccountingTableData CashFlow(CompanyData data) =>
        new AccountingReportDataService(data, June2026).GetReportData(AccountingReportType.CashFlowStatement);

    private static decimal AmountOf(AccountingTableData table, string label) =>
        ParseAmount(table.Rows.Single(r => r.Label == label).Values[0]);

    // Formatted amounts show a negative in parentheses.
    private static decimal ParseAmount(string formatted)
    {
        var value = decimal.Parse(new string(formatted.Where(c => char.IsDigit(c) || c == '.').ToArray()),
            System.Globalization.CultureInfo.InvariantCulture);
        return formatted.StartsWith('(') ? -value : value;
    }

    /// <summary>
    /// The invoice's $500 arrives once, as its payment. Counting its revenue row under Cash from
    /// Sales as well would show $1,300 coming in when $800 did.
    /// </summary>
    [Fact]
    public void CashFromSales_LeavesOutRevenueLinkedToAnInvoice()
    {
        var data = new CompanyData();
        AddPaidInvoice(data);
        data.Revenues.Add(Sale("REV-2", 300m, new DateTime(2026, 6, 12)));

        var table = CashFlow(data);

        Assert.Equal(300m, AmountOf(table, "Cash from Sales"));
        Assert.Equal(500m, AmountOf(table, "Cash from Invoice Payments"));
        Assert.Equal(800m, AmountOf(table, "NET CHANGE IN CASH"));
    }

    /// <summary>
    /// A small June: $300 of cash sales, the $500 invoice payment, $120 of rent and $60 of stock
    /// bought. Stock bought is not an expense on the income statement until it sells, but the cash
    /// still left the business in June, so it is paid out here. An unpaid sale brings in no cash, and
    /// anything dated outside June is left out. Net change is 300 + 500 - 180 = 620.
    /// </summary>
    [Fact]
    public void CashFlow_ForAKnownMonth_ShowsExpensesPaidAndTheNetChange()
    {
        var data = new CompanyData();
        AddPaidInvoice(data);
        data.Revenues.Add(Sale("REV-2", 300m, new DateTime(2026, 6, 12)));
        data.Revenues.Add(Sale("REV-3", 200m, new DateTime(2026, 6, 15), RevenuePaymentStatus.Unpaid));
        data.Revenues.Add(Sale("REV-4", 999m, new DateTime(2026, 7, 2)));
        data.Expenses.Add(Spent("EXP-1", 120m, new DateTime(2026, 6, 5)));
        var stock = Spent("EXP-2", 60m, new DateTime(2026, 6, 7));
        stock.LineItems = [new LineItem { ProductId = "PRD-1", Quantity = 10, UnitPrice = 6m, IsStockPurchase = true }];
        data.Expenses.Add(stock);
        data.Expenses.Add(Spent("EXP-3", 450m, new DateTime(2026, 5, 30)));

        var table = CashFlow(data);

        Assert.Equal(300m, AmountOf(table, "Cash from Sales"));
        Assert.Equal(500m, AmountOf(table, "Cash from Invoice Payments"));
        Assert.Equal(-180m, AmountOf(table, "Cash Paid for Expenses"));
        Assert.Equal(620m, AmountOf(table, "Net Cash from Operating Activities"));
        Assert.Equal(620m, AmountOf(table, "NET CHANGE IN CASH"));
    }
}
