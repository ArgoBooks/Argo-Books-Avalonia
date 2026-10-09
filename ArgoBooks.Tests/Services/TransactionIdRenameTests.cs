using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Models.Rentals;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// An expense or a sale can be given the number a business already uses for it, and its id is what
/// other records point at. A rename that misses one of them leaves a receipt, a payment or a pay run
/// pointing at a record that no longer exists, which is silent and only shows up much later.
/// </summary>
public class TransactionIdRenameTests
{
    private static CompanyManager OpenCompany(CompanyData data) =>
        CompanyManager.CreateForTesting(data);

    [Fact]
    public void RenamingASale_CarriesEveryRecordThatPointsAtIt()
    {
        var data = new CompanyData();
        var revenue = new Revenue { Id = "REV-1", Amount = 100m };
        data.Revenues.Add(revenue);

        data.Payments.Add(new Payment { Id = "PAY-1", RevenueId = "REV-1" });
        data.Rentals.Add(new RentalRecord { Id = "REN-1", RevenueId = "REV-1" });
        data.Receipts.Add(new Receipt { Id = "RCP-1", TransactionId = "REV-1" });
        data.Returns.Add(new Return { Id = "RET-1", OriginalTransactionId = "REV-1" });
        data.PendingConversions.Add(new PendingConversion { TransactionId = "REV-1", TransactionType = "Revenue" });
        data.Invoices.Add(new Invoice
        {
            Id = "INV-1",
            LineItems = { new LineItem { Description = "Work", RevenueRecordId = "REV-1" } }
        });
        data.BankImportSessions.Add(new BankImportSession
        {
            Lines = { new BankStatementLine { MatchedRecordId = "REV-1", MatchedRecordType = BookRecordType.Revenue } }
        });

        OpenCompany(data).ChangeRevenueId(revenue, "2026-0042");

        Assert.Equal("2026-0042", revenue.Id);
        Assert.Equal("2026-0042", data.Payments[0].RevenueId);
        Assert.Equal("2026-0042", data.Rentals[0].RevenueId);
        Assert.Equal("2026-0042", data.Receipts[0].TransactionId);
        Assert.Equal("2026-0042", data.Returns[0].OriginalTransactionId);
        Assert.Equal("2026-0042", data.PendingConversions[0].TransactionId);
        Assert.Equal("2026-0042", data.Invoices[0].LineItems[0].RevenueRecordId);
        Assert.Equal("2026-0042", data.BankImportSessions[0].Lines[0].MatchedRecordId);
    }

    [Fact]
    public void RenamingAnExpense_CarriesEveryRecordThatPointsAtIt()
    {
        var data = new CompanyData();
        var expense = new Expense { Id = "PUR-1", Amount = 50m };
        data.Expenses.Add(expense);

        data.Receipts.Add(new Receipt { Id = "RCP-1", TransactionId = "PUR-1" });
        data.Returns.Add(new Return { Id = "RET-1", OriginalTransactionId = "PUR-1" });
        data.PendingConversions.Add(new PendingConversion { TransactionId = "PUR-1", TransactionType = "Expense" });
        data.PayRuns.Add(new PayRun { Id = "RUN-1", Lines = { new PayRunLine { ExpenseId = "PUR-1" } } });
        data.BankImportSessions.Add(new BankImportSession
        {
            Lines = { new BankStatementLine { MatchedRecordId = "PUR-1", MatchedRecordType = BookRecordType.Expense } }
        });

        OpenCompany(data).ChangeExpenseId(expense, "BILL-77");

        Assert.Equal("BILL-77", expense.Id);
        Assert.Equal("BILL-77", data.Receipts[0].TransactionId);
        Assert.Equal("BILL-77", data.Returns[0].OriginalTransactionId);
        Assert.Equal("BILL-77", data.PendingConversions[0].TransactionId);
        Assert.Equal("BILL-77", data.PayRuns[0].Lines[0].ExpenseId);
        Assert.Equal("BILL-77", data.BankImportSessions[0].Lines[0].MatchedRecordId);
    }

    /// <summary>
    /// A sale and an expense can hold the same id, and a bank line or a pending conversion says
    /// which kind it matched, so renaming one must leave the other's references alone.
    /// </summary>
    [Fact]
    public void RenamingASale_LeavesAnExpenseWithTheSameIdAlone()
    {
        var data = new CompanyData();
        var revenue = new Revenue { Id = "T-1", Amount = 100m };
        data.Revenues.Add(revenue);
        data.Expenses.Add(new Expense { Id = "T-1", Amount = 50m });

        data.PendingConversions.Add(new PendingConversion { TransactionId = "T-1", TransactionType = "Expense" });
        data.BankImportSessions.Add(new BankImportSession
        {
            Lines = { new BankStatementLine { MatchedRecordId = "T-1", MatchedRecordType = BookRecordType.Expense } }
        });

        OpenCompany(data).ChangeRevenueId(revenue, "T-2");

        Assert.Equal("T-2", revenue.Id);
        Assert.Equal("T-1", data.Expenses[0].Id);
        Assert.Equal("T-1", data.PendingConversions[0].TransactionId);
        Assert.Equal("T-1", data.BankImportSessions[0].Lines[0].MatchedRecordId);
    }

    /// <summary>
    /// The stock a sale moved is recorded as adjustments naming it, and the inventory service reads
    /// them back when the sale is edited. Left behind by a rename, the edit cannot tell what the sale
    /// already moved and moves the stock a second time.
    /// </summary>
    [Fact]
    public void RenamingASale_CarriesTheStockItMoved()
    {
        var data = new CompanyData();
        var revenue = new Revenue { Id = "REV-1", Amount = 100m };
        data.Revenues.Add(revenue);
        data.StockAdjustments.Add(new StockAdjustment { Id = "ADJ-1", ReferenceNumber = "REV-1", IsAutoGenerated = true });
        data.StockAdjustments.Add(new StockAdjustment { Id = "ADJ-2", ReferenceNumber = "REV-1", IsAutoGenerated = false });

        OpenCompany(data).ChangeRevenueId(revenue, "SALE-9");

        Assert.Equal("SALE-9", data.StockAdjustments[0].ReferenceNumber);

        // One entered by hand that happens to quote the old number is the user's text, not a link.
        Assert.Equal("REV-1", data.StockAdjustments[1].ReferenceNumber);
    }

    [Fact]
    public void RenamingToAnIdAnotherRecordHolds_IsRefused()
    {
        var data = new CompanyData();
        var revenue = new Revenue { Id = "REV-1" };
        data.Revenues.Add(revenue);
        data.Revenues.Add(new Revenue { Id = "REV-2" });

        var manager = OpenCompany(data);

        Assert.Throws<InvalidOperationException>(() => manager.ChangeRevenueId(revenue, "REV-2"));
        Assert.Equal("REV-1", revenue.Id);
    }

    /// <summary>
    /// Undoing an edit renames the record back through the same method rather than writing the old
    /// id onto it, so everything pointing at it has to come back with it.
    /// </summary>
    [Fact]
    public void RenamingBackAgain_PutsEveryReferenceBack()
    {
        var data = new CompanyData();
        var revenue = new Revenue { Id = "REV-1", Amount = 100m };
        data.Revenues.Add(revenue);
        data.Payments.Add(new Payment { Id = "PAY-1", RevenueId = "REV-1" });
        data.Receipts.Add(new Receipt { Id = "RCP-1", TransactionId = "REV-1" });
        data.StockAdjustments.Add(new StockAdjustment { Id = "ADJ-1", ReferenceNumber = "REV-1", IsAutoGenerated = true });

        var manager = OpenCompany(data);
        manager.ChangeRevenueId(revenue, "SALE-9");
        manager.ChangeRevenueId(revenue, "REV-1");

        Assert.Equal("REV-1", revenue.Id);
        Assert.Equal("REV-1", data.Payments[0].RevenueId);
        Assert.Equal("REV-1", data.Receipts[0].TransactionId);
        Assert.Equal("REV-1", data.StockAdjustments[0].ReferenceNumber);
    }

    [Fact]
    public void RenamingToNothing_IsRefused()
    {
        var data = new CompanyData();
        var expense = new Expense { Id = "PUR-1" };
        data.Expenses.Add(expense);

        var manager = OpenCompany(data);

        Assert.Throws<ArgumentException>(() => manager.ChangeExpenseId(expense, "   "));
        Assert.Equal("PUR-1", expense.Id);
    }

    [Fact]
    public void RenamingToTheSameId_ChangesNothing()
    {
        var data = new CompanyData();
        var expense = new Expense { Id = "PUR-1" };
        data.Expenses.Add(expense);
        data.Receipts.Add(new Receipt { Id = "RCP-1", TransactionId = "PUR-1" });

        OpenCompany(data).ChangeExpenseId(expense, "PUR-1");

        Assert.Equal("PUR-1", expense.Id);
        Assert.Equal("PUR-1", data.Receipts[0].TransactionId);
    }
}
