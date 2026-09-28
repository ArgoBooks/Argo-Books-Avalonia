using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Undo of a spreadsheet import restores the company from a snapshot taken before it, and redo
/// from one taken after. Anything the snapshot leaves out survives an undo: employees the import
/// added stayed on the payroll, and the conversions it queued stayed behind for rows that were
/// gone, so the conversion service dropped them and a redo brought those rows back pending with
/// nothing left to convert them.
/// </summary>
public class ImportUndoSnapshotTests
{
    [Fact]
    public void UndoingAnImport_TakesItsEmployeesAndQueuedConversions_AndRedoBringsThemBack()
    {
        var data = new CompanyData();
        data.Employees.Add(new Employee { Id = "EMP-001", Name = "Already here" });
        var before = App.CreateCompanyDataSnapshot(data);

        var revenueId = $"REV-{Guid.NewGuid():N}";
        data.Employees.Add(new Employee { Id = "EMP-002", Name = "Imported" });
        data.Revenues.Add(new Revenue { Id = revenueId, Total = 100m, OriginalCurrency = "CAD", IsPendingConversion = true });
        data.PendingConversions.Add(new PendingConversion
        {
            TransactionId = revenueId, TransactionType = "Revenue", OriginalCurrency = "CAD",
            TransactionDate = new DateTime(2026, 3, 1), Total = 100m
        });
        var after = App.CreateCompanyDataSnapshot(data);

        App.RestoreCompanyDataFromSnapshot(data, before);

        Assert.Equal("EMP-001", Assert.Single(data.Employees).Id);
        Assert.Empty(data.Revenues);
        Assert.Empty(data.PendingConversions);

        App.RestoreCompanyDataFromSnapshot(data, after);

        Assert.Equal(2, data.Employees.Count);
        Assert.True(Assert.Single(data.Revenues).IsPendingConversion);
        Assert.Equal(revenueId, Assert.Single(data.PendingConversions).TransactionId);
    }

    // Restoring a snapshot swaps every record for a copy. A bank import's undo, further back on the
    // stack, removed its rows by reference, so after that it left them in the books, and its redo
    // added a second row with the same id.
    [Fact]
    public void BankImportUndoAndRedo_AfterASnapshotRestore_MatchTheRowsById()
    {
        var data = new CompanyData();
        var expense = new Expense { Id = "PUR-2026-00001", Total = 10m, OriginalCurrency = "USD" };
        var creation = new BankImportCreation();
        creation.CreatedTransactions.Add(expense);
        data.Expenses.Add(expense);
        App.RestoreCompanyDataFromSnapshot(data, App.CreateCompanyDataSnapshot(data));

        creation.Undo(data);

        Assert.Empty(data.Expenses);

        creation.Redo(data);
        App.RestoreCompanyDataFromSnapshot(data, App.CreateCompanyDataSnapshot(data));
        creation.Redo(data);

        Assert.Equal(expense.Id, Assert.Single(data.Expenses).Id);
    }

    // An edit's undo writes the old values back onto the record it holds. After an import's undo had
    // swapped every record for a copy, that write landed on an object no longer in the books.
    [Fact]
    public void UndoingAnEdit_AfterUndoingAnImport_ChangesTheRecordInTheBooks()
    {
        var data = new CompanyData();
        var expense = new Expense { Id = "PUR-2026-00001", Total = 10m, OriginalCurrency = "USD" };
        data.Expenses.Add(expense);

        expense.Total = 25m;
        var beforeImport = App.CreateCompanyDataSnapshot(data);
        data.Expenses.Add(new Expense { Id = "PUR-2026-00002", Total = 99m, OriginalCurrency = "USD" });
        var afterImport = App.CreateCompanyDataSnapshot(data);

        App.RestoreCompanyDataFromSnapshot(data, beforeImport);
        expense.Total = 10m;

        Assert.Same(expense, Assert.Single(data.Expenses));
        Assert.Equal(10m, expense.Total);

        expense.Total = 25m;
        App.RestoreCompanyDataFromSnapshot(data, afterImport);

        Assert.Same(expense, data.Expenses[0]);
        Assert.Equal(["PUR-2026-00001", "PUR-2026-00002"], data.Expenses.Select(e => e.Id));
    }

    // Undoing a bank match writes back onto the statement line it held, which sits inside an import
    // session, so the lines keep their identity through a restore as well.
    [Fact]
    public void ARestore_KeepsTheStatementLinesABankMatchUndoHolds()
    {
        var data = new CompanyData();
        var line = new BankStatementLine { Id = "L1", MatchStatus = BankLineMatchStatus.Unmatched };
        data.BankImportSessions.Add(new BankImportSession { Id = "S1", Lines = [line, new BankStatementLine { Id = "L2" }] });
        var snapshot = App.CreateCompanyDataSnapshot(data);

        data.BankImportSessions[0].Lines.RemoveAt(1);
        App.RestoreCompanyDataFromSnapshot(data, snapshot);
        line.MatchStatus = BankLineMatchStatus.Ignored;

        var lines = Assert.Single(data.BankImportSessions).Lines;
        Assert.Same(line, lines[0]);
        Assert.Equal(BankLineMatchStatus.Ignored, lines[0].MatchStatus);
        Assert.Equal("L2", lines[1].Id);
    }
}
