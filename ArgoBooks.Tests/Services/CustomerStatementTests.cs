using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the Customer Statement report: what one customer owes today, and their activity between two dates.
/// </summary>
public class CustomerStatementTests
{
    private static CompanyData WithCustomer()
    {
        var data = new CompanyData();
        data.Customers.Add(new Customer { Id = "C1", Name = "Dana Smith" });
        data.Customers.Add(new Customer { Id = "C2", Name = "Someone Else" });
        return data;
    }

    private static Invoice AddInvoice(CompanyData data, string number, decimal total, DateTime issued, DateTime due,
        string customerId = "C1", InvoiceStatus status = InvoiceStatus.Sent, string currency = "USD", decimal? totalUSD = null)
    {
        var invoice = new Invoice
        {
            Id = number, InvoiceNumber = number, CustomerId = customerId, OriginalCurrency = currency,
            IssueDate = issued, DueDate = due,
            Subtotal = total, Total = total, TotalUSD = totalUSD ?? total, Balance = total, Status = status
        };
        data.Invoices.Add(invoice);
        return invoice;
    }

    private static void AddPayment(CompanyData data, Invoice invoice, decimal amount, DateTime date, decimal? amountUSD = null)
    {
        data.Payments.Add(new Payment
        {
            Id = $"PAY-{data.Payments.Count + 1}", InvoiceId = invoice.Id, CustomerId = invoice.CustomerId,
            OriginalCurrency = invoice.OriginalCurrency, Date = date, Amount = amount, AmountUSD = amountUSD ?? amount
        });
        InvoiceTotalsService.Recalculate(invoice, data.Payments);
    }

    private static AccountingTableData OwedToday(CompanyData data, string? customerId = "C1") =>
        new AccountingReportDataService(data, new ReportFilters { CustomerId = customerId })
            .GetReportData(AccountingReportType.CustomerStatement);

    private static AccountingTableData Activity(CompanyData data, DateTime start, DateTime end) =>
        new AccountingReportDataService(data, new ReportFilters
            {
                CustomerId = "C1", StatementShowsActivity = true, StartDate = start, EndDate = end
            })
            .GetReportData(AccountingReportType.CustomerStatement);

    private static List<AccountingRow> DataRows(AccountingTableData data) =>
        data.Rows.Where(r => r.RowType == AccountingRowType.DataRow).ToList();

    private static AccountingRow RowStartingWith(AccountingTableData data, string label) =>
        data.Rows.Single(r => r.Label.StartsWith(label, StringComparison.Ordinal));

    // Formatted amounts show a negative in parentheses.
    private static decimal ParseAmount(string formatted)
    {
        var value = decimal.Parse(new string(formatted.Where(c => char.IsDigit(c) || c == '.').ToArray()),
            System.Globalization.CultureInfo.InvariantCulture);
        return formatted.StartsWith('(') ? -value : value;
    }

    [Fact]
    public void WithoutACustomer_AsksForOne()
    {
        var result = OwedToday(WithCustomer(), customerId: null);

        Assert.Contains("Choose a customer", result.Subtitle);
        Assert.Equal(0m, ParseAmount(RowStartingWith(result, "Amount due").Values[^1]));
    }

    [Fact]
    public void OwedToday_ListsOnlyThisCustomersUnpaidInvoices_WithWhatIsPaidSoFar()
    {
        var today = DateTime.Today;
        var data = WithCustomer();
        var partPaid = AddInvoice(data, "INV-A", 1000m, today.AddDays(-40), today.AddDays(-10));
        AddPayment(data, partPaid, 400m, today.AddDays(-20));
        AddInvoice(data, "INV-B", 300m, today.AddDays(-5), today.AddDays(25));
        var paid = AddInvoice(data, "INV-C", 200m, today.AddDays(-30), today);
        AddPayment(data, paid, 200m, today.AddDays(-25));
        AddInvoice(data, "INV-D", 900m, today.AddDays(-5), today.AddDays(25), customerId: "C2");
        AddInvoice(data, "INV-E", 700m, today.AddDays(-5), today.AddDays(25), status: InvoiceStatus.Draft);

        var result = OwedToday(data);

        Assert.Equal("Statement for Dana Smith", result.Title);
        var rows = DataRows(result);
        Assert.Equal(["INV-A, overdue", "INV-B"], rows.Select(r => r.Label));
        Assert.Equal(400m, ParseAmount(rows[0].Values[3]));
        Assert.Equal(600m, ParseAmount(rows[0].Values[4]));
        Assert.Equal("", rows[1].Values[3]);
        Assert.Equal(600m, ParseAmount(RowStartingWith(result, "Overdue").Values[^1]));
        Assert.Equal(900m, ParseAmount(RowStartingWith(result, "Amount due").Values[^1]));
    }

    [Fact]
    public void OwedToday_WithNothingOwed_SaysSo()
    {
        var today = DateTime.Today;
        var data = WithCustomer();
        var paid = AddInvoice(data, "INV-1", 250m, today.AddDays(-30), today.AddDays(-1));
        AddPayment(data, paid, 250m, today.AddDays(-3));

        var result = OwedToday(data);

        Assert.Equal("No unpaid invoices", Assert.Single(DataRows(result)).Label);
        Assert.Equal(0m, ParseAmount(RowStartingWith(result, "Amount due").Values[^1]));
    }

    [Fact]
    public void OwedToday_LeavesOutAFullyRefundedInvoice()
    {
        var today = DateTime.Today;
        var data = WithCustomer();
        var invoice = AddInvoice(data, "INV-R", 500m, today.AddDays(-20), today.AddDays(10));
        AddPayment(data, invoice, 500m, today.AddDays(-15));
        data.Payments.Add(new Payment
        {
            Id = "PAY-REFUND", InvoiceId = "INV-R", CustomerId = "C1", OriginalCurrency = "USD",
            Date = today.AddDays(-10), Amount = -500m, AmountUSD = -500m, IsRefund = true
        });
        InvoiceTotalsService.Recalculate(invoice, data.Payments);

        var result = OwedToday(data);

        Assert.Equal("No unpaid invoices", Assert.Single(DataRows(result)).Label);
    }

    // $500 invoiced Jan 15 with $200 paid Jan 25, so $300 is owed going into February. In February
    // $1,000 is invoiced and $400 paid, and the other $600 arrives on Mar 5, after the statement ends.
    private static CompanyData OwingIntoFebruary()
    {
        var data = WithCustomer();
        var january = AddInvoice(data, "INV-1", 500m, new DateTime(2024, 1, 15), new DateTime(2024, 2, 14));
        AddPayment(data, january, 200m, new DateTime(2024, 1, 25));
        var february = AddInvoice(data, "INV-2", 1000m, new DateTime(2024, 2, 10), new DateTime(2024, 3, 11));
        AddPayment(data, february, 400m, new DateTime(2024, 2, 20));
        AddPayment(data, february, 600m, new DateTime(2024, 3, 5));
        return data;
    }

    [Fact]
    public void Activity_StartsFromTheBalanceBeforeThePeriod_AndKeepsARunningBalance()
    {
        var result = Activity(OwingIntoFebruary(), new DateTime(2024, 2, 1), new DateTime(2024, 2, 29));

        var rows = DataRows(result);
        Assert.StartsWith("Balance before", rows[0].Label);
        Assert.Equal(300m, ParseAmount(rows[0].Values[^1]));
        Assert.Equal("Invoice INV-2 (now paid)", rows[1].Label);
        Assert.Equal(1000m, ParseAmount(rows[1].Values[1]));
        Assert.Equal(1300m, ParseAmount(rows[1].Values[^1]));
        Assert.Equal("Payment for INV-2", rows[2].Label);
        Assert.Equal(400m, ParseAmount(rows[2].Values[2]));
        Assert.Equal(900m, ParseAmount(rows[2].Values[^1]));

        Assert.Equal(1000m, ParseAmount(RowStartingWith(result, "Invoiced in this period").Values[1]));
        Assert.Equal(400m, ParseAmount(RowStartingWith(result, "Paid in this period").Values[2]));
        Assert.Equal(900m, ParseAmount(RowStartingWith(result, "Balance due on").Values[^1]));
        Assert.Contains("only", result.Footnote);
    }

    [Fact]
    public void Activity_ShowsWhatHasBeenPaidSince_SoAnOldStatementDoesNotReadAsStillOwed()
    {
        var result = Activity(OwingIntoFebruary(), new DateTime(2024, 2, 1), new DateTime(2024, 2, 29));

        Assert.Equal(600m, ParseAmount(RowStartingWith(result, "Paid since").Values[2]));
        Assert.Equal(300m, ParseAmount(RowStartingWith(result, "Still owed from this statement").Values[^1]));
    }

    [Fact]
    public void Activity_WithNothingPaidSince_HasNoPaidSinceLines()
    {
        var result = Activity(OwingIntoFebruary(), new DateTime(2024, 1, 1), new DateTime(2024, 3, 31));

        Assert.Equal(300m, ParseAmount(RowStartingWith(result, "Balance due on").Values[^1]));
        Assert.DoesNotContain(result.Rows, r => r.Label.StartsWith("Paid since", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Rows, r => r.Label.StartsWith("Still owed", StringComparison.Ordinal));
    }

    // A CAD invoice converted at its issue date and paid in full at another day's rate still comes to zero.
    [Fact]
    public void Activity_AForeignInvoicePaidInFull_ComesToZero()
    {
        var data = WithCustomer();
        var invoice = AddInvoice(data, "INV-1", 1000m, new DateTime(2024, 1, 5), new DateTime(2024, 2, 4),
            currency: "CAD", totalUSD: 730m);
        AddPayment(data, invoice, 1000m, new DateTime(2024, 1, 25), amountUSD: 720m);

        var result = Activity(data, new DateTime(2024, 1, 1), new DateTime(2024, 1, 31));

        Assert.Equal(0m, ParseAmount(RowStartingWith(result, "Balance due on").Values[^1]));
        Assert.Equal("No unpaid invoices", Assert.Single(DataRows(OwedToday(data))).Label);
    }

    [Fact]
    public void Activity_APaidAmountImportedWithoutPayments_SaysItsDateIsNotRecorded()
    {
        var data = WithCustomer();
        var invoice = AddInvoice(data, "INV-9", 400m, new DateTime(2024, 1, 5), new DateTime(2024, 2, 4), status: InvoiceStatus.Paid);
        invoice.Balance = 0m;

        var result = Activity(data, new DateTime(2024, 1, 1), new DateTime(2024, 1, 31));

        Assert.Contains(result.Rows, r => r.Label == "Payment for INV-9 (date not recorded)");
        Assert.Equal(0m, ParseAmount(RowStartingWith(result, "Balance due on").Values[^1]));
    }

    [Fact]
    public void Filters_CloneKeepsTheStatementCustomerAndMode()
    {
        var config = new ReportConfiguration { Filters = { CustomerId = "C1", StatementShowsActivity = true } };

        var clone = config.Clone();

        Assert.Equal("C1", clone.Filters.CustomerId);
        Assert.True(clone.Filters.StatementShowsActivity);
    }

    [Fact]
    public void Template_IsBuiltInAndPrintsItsDatesInTheTable()
    {
        Assert.True(ReportTemplateFactory.IsBuiltInTemplate(ReportTemplateFactory.TemplateNames.CustomerStatement));

        var config = ReportTemplateFactory.CreateFromTemplate(ReportTemplateFactory.TemplateNames.CustomerStatement);

        var table = Assert.Single(config.Elements.OfType<AccountingTableReportElement>());
        Assert.Equal(AccountingReportType.CustomerStatement, table.ReportType);
        Assert.Empty(config.Elements.OfType<DateRangeReportElement>());
    }
}
