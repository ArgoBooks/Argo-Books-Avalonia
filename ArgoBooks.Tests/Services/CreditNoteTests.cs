using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Returns on sales the customer has not paid in full: the return comes off what they still owe, as a
/// credit note, before anything is paid back (docs/Calculations.md §10). Every amount is in USD.
/// </summary>
public class CreditNoteTests
{
    private static readonly DateTime SaleDate = new(2024, 3, 1);
    private static readonly DateTime ReturnDate = new(2024, 3, 10);
    private static readonly (DateTime Start, DateTime End) Year = (new DateTime(2024, 1, 1), new DateTime(2024, 12, 31));

    // Ten chairs at 10, no tax, invoiced and part paid.
    private static (CompanyData Data, Invoice Invoice, Revenue Sale) InvoicedSale(decimal paid)
    {
        var data = new CompanyData();
        var invoice = new Invoice
        {
            Id = "INV-1", InvoiceNumber = "INV-1", CustomerId = "CUS-1", IssueDate = SaleDate, DueDate = SaleDate.AddDays(30),
            OriginalCurrency = "USD", Total = 100m, TotalUSD = 100m, Status = InvoiceStatus.Sent
        };
        data.Invoices.Add(invoice);
        var sale = new Revenue
        {
            Id = "SAL-1", Date = SaleDate, OriginalCurrency = "USD", CustomerId = "CUS-1", InvoiceId = invoice.Id,
            Amount = 100m, Total = 100m, TotalUSD = 100m, PaymentStatus = RevenuePaymentStatus.Unpaid,
            LineItems = [new LineItem { Description = "Chair", Quantity = 10, UnitPrice = 10m }]
        };
        data.Revenues.Add(sale);
        if (paid > 0)
            Pay(data, invoice, paid, SaleDate.AddDays(1));
        return (data, invoice, sale);
    }

    private static void Pay(CompanyData data, Invoice invoice, decimal amount, DateTime date)
    {
        data.Payments.Add(new Payment
        {
            Id = $"PAY-{data.Payments.Count + 100}", InvoiceId = invoice.Id, CustomerId = "CUS-1", Date = date,
            Amount = amount, AmountUSD = amount, OriginalCurrency = "USD"
        });
        InvoiceTotalsService.Recalculate(invoice, data.Payments);
        InvoiceTotalsService.SyncLinkedRevenueStatus(invoice, data.Revenues);
    }

    private static TransactionReturns.Change Return(CompanyData data, Transaction sale, decimal chairs) =>
        TransactionReturns.Record(data, sale, new TransactionReturns.Request(
            new Dictionary<int, decimal> { [0] = chairs }, false, false, 0m, false, "Defective", "", ReturnDate));

    private static decimal BalanceSheet(CompanyData data, string label)
    {
        var report = new AccountingReportDataService(data, new ReportFilters { StartDate = Year.Start, EndDate = Year.End })
            .GetReportData(AccountingReportType.BalanceSheet);
        var formatted = report.Rows.Single(r => r.Label == label).Values[0];
        return decimal.Parse(new string(formatted.Where(c => char.IsDigit(c) || c == '.').ToArray()),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void PartlyPaidInvoice_ReturnComesOffWhatIsOwed_BeforeAnyRefund()
    {
        var (data, invoice, sale) = InvoicedSale(paid: 60m);

        var change = Return(data, sale, 3);

        Assert.Null(change.Refund!.Payment);
        Assert.Equal(-30m, change.Refund.CreditNote!.Amount);
        Assert.Equal(30m, invoice.AmountCredited);
        Assert.Equal(10m, invoice.Balance);
        Assert.Equal(InvoiceStatus.Partial, invoice.Status);
        Assert.Equal(10m, BalanceSheet(data, "Accounts Receivable"));
        Assert.Equal(60m, BalanceSheet(data, "Cash (Estimated)"));
        Assert.Equal(60m + 30m, PaymentPortalService.BuildBalanceSyncItem(invoice, data).ExternalPaid);
    }

    [Fact]
    public void PayingTheRest_CountsTheSaleLessItsCreditNote()
    {
        var (data, invoice, sale) = InvoicedSale(paid: 60m);
        Return(data, sale, 3);
        Assert.Equal(0m, ProfitCalculator.CalculateNetProfitUSD(data, Year.Start, Year.End));

        Pay(data, invoice, 10m, ReturnDate.AddDays(1));

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.True(RevenueAggregator.IsCollected(sale));
        Assert.Equal(70m, ProfitCalculator.CalculateNetProfitUSD(data, Year.Start, Year.End));
    }

    [Fact]
    public void ReturningMoreThanIsOwed_CreditsWhatIsOwed_AndRefundsTheRest()
    {
        var (data, invoice, sale) = InvoicedSale(paid: 60m);

        var change = Return(data, sale, 5);

        Assert.Equal(-40m, change.Refund!.CreditNote!.Amount);
        Assert.Equal(-10m, change.Refund.Payment!.Amount);
        Assert.Equal(0m, invoice.Balance);
        Assert.True(RevenueAggregator.IsCollected(sale));
        Assert.Equal(50m, ProfitCalculator.CalculateNetProfitUSD(data, Year.Start, Year.End));
        Assert.Equal(50m, BalanceSheet(data, "Cash (Estimated)"));
    }

    [Fact]
    public void UnpaidInvoiceReturnedInFull_IsSettledByItsCreditNote()
    {
        var (data, invoice, sale) = InvoicedSale(paid: 0m);

        Return(data, sale, 10);

        Assert.Equal(0m, invoice.Balance);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.False(invoice.IsOverdue);
        Assert.Equal(0m, BalanceSheet(data, "Accounts Receivable"));
        Assert.Equal(0m, ProfitCalculator.CalculateNetProfitUSD(data, Year.Start, Year.End));
    }

    [Fact]
    public void UndoingTheReturn_OwesTheInvoiceAgain()
    {
        var (data, invoice, sale) = InvoicedSale(paid: 60m);
        var change = Return(data, sale, 3);

        change.TakeOut(data);

        Assert.Equal(0m, invoice.AmountCredited);
        Assert.Equal(40m, invoice.Balance);
        Assert.Single(data.Payments);

        change.PutBack(data);
        Assert.Equal(10m, invoice.Balance);
    }

    [Fact]
    public void UnpaidSaleWithNoInvoice_CountsItsCreditNoteOnceItIsPaid()
    {
        var data = new CompanyData();
        var sale = new Revenue
        {
            Id = "SAL-1", Date = SaleDate, OriginalCurrency = "USD", Amount = 100m, Total = 100m, TotalUSD = 100m,
            PaymentStatus = RevenuePaymentStatus.Unpaid,
            LineItems = [new LineItem { Description = "Chair", Quantity = 10, UnitPrice = 10m }]
        };
        data.Revenues.Add(sale);

        var change = Return(data, sale, 3);

        Assert.Equal(-30m, change.Refund!.CreditNote!.Amount);
        Assert.Equal(0m, ProfitCalculator.CalculateNetProfitUSD(data, Year.Start, Year.End));

        sale.PaymentStatus = RevenuePaymentStatus.Paid;
        Assert.Equal(70m, ProfitCalculator.CalculateNetProfitUSD(data, Year.Start, Year.End));

        // A later return of a sale now paid is paid back, as the customer paid what was left.
        var later = Return(data, sale, 2);
        Assert.Null(later.Refund!.CreditNote);
        Assert.Equal(-20m, later.Refund.Payment!.Amount);
    }
}
