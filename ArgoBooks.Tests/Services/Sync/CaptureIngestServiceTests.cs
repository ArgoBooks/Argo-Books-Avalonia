using ArgoBooks.Core.Data;
using ArgoBooks.Core.Services.Sync;
using Xunit;

namespace ArgoBooks.Tests.Services.Sync;

public class CaptureIngestServiceTests
{
    private static CompanyData NewCompany() => new();

    private static CapturedTransaction NewExpenseDto() => new()
    {
        Type = CapturedTransactionType.Expense,
        SupplierOrCustomer = "Office Depot",
        Date = new DateTime(2026, 6, 1),
        Total = 54.00m,
        Tax = 4.00m,
        LineItems =
        [
            new CapturedLineItem { Description = "Printer paper", Quantity = 2, UnitPrice = 25.00m, Total = 50.00m, ProductName = "Printer paper" }
        ],
        ImageBase64 = Convert.ToBase64String([1, 2, 3, 4])
    };

    private static CapturedTransaction NewRevenueDto() => new()
    {
        Type = CapturedTransactionType.Revenue,
        SupplierOrCustomer = "Acme Corp",
        Date = new DateTime(2026, 6, 2),
        Total = 220.00m,
        Tax = 20.00m,
        LineItems =
        [
            new CapturedLineItem { Description = "Consulting", Quantity = 1, UnitPrice = 200.00m, Total = 200.00m, ProductName = null }
        ]
    };

    [Fact]
    public void Ingest_Expense_AddsExpenseAndLinkedMobileReceipt()
    {
        var data = NewCompany();
        var tx = NewExpenseDto();

        var expenseId = CaptureIngestService.Ingest(data, tx);

        Assert.Single(data.Expenses);
        var expense = data.Expenses.Single();
        Assert.Equal(expenseId, expense.Id);
        Assert.StartsWith("PUR-", expense.Id);
        Assert.Equal(54.00m, expense.Total);
        Assert.Equal(4.00m, expense.TaxAmount);
        Assert.Equal(50.00m, expense.Amount);
        Assert.Equal(new DateTime(2026, 6, 1), expense.Date);
        Assert.Single(expense.LineItems);

        Assert.Single(data.Receipts);
        var receipt = data.Receipts.Single();
        Assert.Equal("Mobile", receipt.Source);
        Assert.Equal(expense.Id, receipt.TransactionId);
        Assert.Equal("Expense", receipt.TransactionType);
        Assert.Equal(54.00m, receipt.Amount);
        Assert.Equal(expense.ReceiptId, receipt.Id);
        Assert.False(string.IsNullOrEmpty(receipt.FileData));

        Assert.Equal(1, data.IdCounters.Expense);
        Assert.Equal(1, data.IdCounters.Receipt);
    }

    [Fact]
    public void Ingest_Revenue_AddsRevenueAndLinkedMobileReceipt()
    {
        var data = NewCompany();
        var tx = NewRevenueDto();

        var revenueId = CaptureIngestService.Ingest(data, tx);

        Assert.Single(data.Revenues);
        var revenue = data.Revenues.Single();
        Assert.Equal(revenueId, revenue.Id);
        Assert.StartsWith("REV-", revenue.Id);
        Assert.Equal(220.00m, revenue.Total);
        Assert.Equal(20.00m, revenue.TaxAmount);
        Assert.Equal(200.00m, revenue.Amount);
        Assert.Equal(Core.Enums.RevenuePaymentStatus.Paid, revenue.PaymentStatus);

        Assert.Single(data.Receipts);
        var receipt = data.Receipts.Single();
        Assert.Equal("Mobile", receipt.Source);
        Assert.Equal(revenue.Id, receipt.TransactionId);
        Assert.Equal("Revenue", receipt.TransactionType);
        Assert.Equal(220.00m, receipt.Amount);
        Assert.Null(receipt.FileData);

        Assert.Equal(1, data.IdCounters.Revenue);
        Assert.Equal(1, data.IdCounters.Receipt);
    }

    [Fact]
    public void Ingest_MatchesLineItemToExistingProductByName()
    {
        var data = NewCompany();
        var product = new Core.Models.Entities.Product { Id = "PRD-00001", Name = "Printer paper" };
        data.Products.Add(product);

        var tx = NewExpenseDto();
        CaptureIngestService.Ingest(data, tx);

        var expense = data.Expenses.Single();
        Assert.Equal(product.Id, expense.LineItems.Single().ProductId);
    }

    [Fact]
    public void Ingest_WithZeroTotal_ThrowsArgumentException()
    {
        var data = NewCompany();
        var tx = NewExpenseDto();
        tx.Total = 0;

        var ex = Assert.Throws<ArgumentException>(() => CaptureIngestService.Ingest(data, tx));
        Assert.Equal("tx", ex.ParamName);
        Assert.StartsWith("Captured transaction total must be positive.", ex.Message);

        Assert.Empty(data.Expenses);
        Assert.Empty(data.Receipts);
    }

    // The scan often reads a total and no items. The receipt must still arrive: a refusal is
    // taken for an unreadable capture and deleted from the phone's queue.
    [Fact]
    public void Ingest_WithNoLineItems_KeepsTheReceiptAsOneLineForTheWholeAmount()
    {
        var data = NewCompany();
        var tx = NewExpenseDto();
        tx.LineItems = [];

        CaptureIngestService.Ingest(data, tx);

        var expense = Assert.Single(data.Expenses);
        Assert.Equal(54.00m, expense.Total);
        var line = Assert.Single(expense.LineItems);
        Assert.Equal("Office Depot", line.Description);
        Assert.Equal(50.00m, line.UnitPrice);
        Assert.NotNull(Assert.Single(data.Receipts).FileData);
    }

    [Fact]
    public void Ingest_SameScanUidTwice_CreatesOnlyOneTransaction()
    {
        var data = NewCompany();
        var tx = NewExpenseDto();
        tx.ScanUid = "11111111-1111-1111-1111-111111111111";

        var firstId = CaptureIngestService.Ingest(data, tx);
        var secondId = CaptureIngestService.Ingest(data, tx);

        Assert.NotNull(firstId);
        Assert.Null(secondId);
        Assert.Single(data.Expenses);
        Assert.Single(data.Receipts);
        Assert.Single(data.IngestedScanUids);
        Assert.Equal(tx.ScanUid, data.IngestedScanUids[0]);
    }

    [Fact]
    public void Ingest_WithoutScanUid_DoesNotDeDuplicate()
    {
        var data = NewCompany();
        var tx = NewExpenseDto();
        // ScanUid left at its default (empty) - de-dupe must be a no-op so it doesn't matter which
        // captures happen to omit it.

        CaptureIngestService.Ingest(data, tx);
        CaptureIngestService.Ingest(data, tx);

        Assert.Equal(2, data.Expenses.Count);
        Assert.Empty(data.IngestedScanUids);
    }

    [Fact]
    public void Ingest_DifferentScanUids_CreatesBothTransactions()
    {
        var data = NewCompany();
        var tx1 = NewExpenseDto();
        tx1.ScanUid = "11111111-1111-1111-1111-111111111111";
        var tx2 = NewExpenseDto();
        tx2.ScanUid = "22222222-2222-2222-2222-222222222222";

        CaptureIngestService.Ingest(data, tx1);
        CaptureIngestService.Ingest(data, tx2);

        Assert.Equal(2, data.Expenses.Count);
        Assert.Equal(2, data.IngestedScanUids.Count);
    }
}
