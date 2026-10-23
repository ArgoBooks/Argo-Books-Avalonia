using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Bulk edits on the Expenses page: setting the product or supplier of many expenses, or deleting
/// them, each as one undo step, with payroll wages and expenses that can't take one product left alone.
/// </summary>
public class BulkEditExpensesTests : ModalViewModelTestBase
{
    private void AddProductsAndSuppliers()
    {
        Company.Settings.Localization.Currency = "USD";
        Company.Categories.Add(new Category { Id = "CAT-E", Name = "Office", Type = CategoryType.Expense });
        Company.Products.Add(new Product { Id = "P1", Name = "Paper", CategoryId = "CAT-E" });
        Company.Products.Add(new Product { Id = "P2", Name = "Toner", CategoryId = "CAT-E" });
        Company.Suppliers.Add(new Supplier { Id = "SUP-1", Name = "Staples" });
        Company.Suppliers.Add(new Supplier { Id = "SUP-2", Name = "Office Depot" });
    }

    private Expense AddExpense(string id, string? supplierId, params LineItem[] lines)
    {
        var expense = new Expense
        {
            Id = id,
            Date = new DateTime(2025, 3, 1),
            SupplierId = supplierId,
            OriginalCurrency = "USD",
            Description = lines.Length == 1 ? lines[0].Description : "Several items",
            LineItems = [.. lines],
            Amount = 10m,
            Total = 10m,
            TotalUSD = 10m
        };
        Company.Expenses.Add(expense);
        return expense;
    }

    private static LineItem Line(string productId, string description, bool buysStock = false) =>
        new() { ProductId = productId, Description = description, Quantity = 1, UnitPrice = 10m, IsStockPurchase = buysStock };

    private void MarkAsPayrollWages(string expenseId) =>
        Company.PayRuns.Add(new PayRun
        {
            Id = "PR-1",
            PayDate = new DateTime(2025, 3, 1),
            Status = PayRunStatus.Approved,
            Lines = [new PayRunLine { EmployeeId = "E1", EmployeeName = "Avery", GrossPay = 10m, ExpenseId = expenseId }]
        });

    [Fact]
    public void SetProduct_ChangesOneLineExpenses_AndLeavesTheOthersAsTheyWere()
    {
        AddProductsAndSuppliers();
        var plain = AddExpense("PUR-1", "SUP-1", Line("P1", "Paper"));
        var twoLines = AddExpense("PUR-2", "SUP-1", Line("P1", "Paper"), Line("P2", "Toner"));
        var stock = AddExpense("PUR-3", "SUP-1", Line("P1", "Paper", buysStock: true));
        var wages = AddExpense("PUR-4", null, Line("P1", "Paper"));
        var already = AddExpense("PUR-5", "SUP-1", Line("P2", "Toner"));
        MarkAsPayrollWages(wages.Id);

        var result = new ExpenseModalsViewModel().SetProduct([plain.Id, twoLines.Id, stock.Id, wages.Id, already.Id], "P2");

        Assert.Equal((1, 1, 1, 1, 1), (result.Changed, result.Unchanged, result.NotOneLine, result.MovesStock, result.MadeElsewhere));
        Assert.Equal("P2", plain.LineItems[0].ProductId);
        Assert.Equal("Toner", plain.LineItems[0].Description);
        Assert.Equal("Toner", plain.Description);
        Assert.Equal(["P1", "P2"], twoLines.LineItems.Select(l => l.ProductId));
        Assert.Equal("P1", stock.LineItems[0].ProductId);
        Assert.Equal("P1", wages.LineItems[0].ProductId);
    }

    [Fact]
    public void SetProduct_IsOneUndoStep_ThatUndoesAndRedoes()
    {
        AddProductsAndSuppliers();
        var first = AddExpense("PUR-1", null, Line("P1", "Paper"));
        var second = AddExpense("PUR-2", null, Line("P1", "Paper"));

        var result = new ExpenseModalsViewModel().SetProduct([first.Id, second.Id], "P2");

        Assert.Equal(1, App.UndoRedoManager.UndoCount);
        Assert.Same(result.Action, App.UndoRedoManager.SavePoint);

        Undo();
        Assert.All(new[] { first, second }, e => Assert.Equal(("P1", "Paper"), (e.LineItems[0].ProductId, e.LineItems[0].Description)));

        Redo();
        Assert.All(new[] { first, second }, e => Assert.Equal("P2", e.LineItems[0].ProductId));
    }

    [Fact]
    public void SetProduct_KeepsADescriptionTheUserTyped()
    {
        AddProductsAndSuppliers();
        var expense = AddExpense("PUR-1", null, Line("P1", "March order from the warehouse"));

        new ExpenseModalsViewModel().SetProduct([expense.Id], "P2");

        Assert.Equal("P2", expense.LineItems[0].ProductId);
        Assert.Equal("March order from the warehouse", expense.LineItems[0].Description);
    }

    [Fact]
    public void SetCounterparty_SetsAndClears_AndUndoesToEachExpensesOwnSupplier()
    {
        AddProductsAndSuppliers();
        var withSupplier = AddExpense("PUR-1", "SUP-1", Line("P1", "Paper"));
        var withNone = AddExpense("PUR-2", null, Line("P1", "Paper"));
        var vm = new ExpenseModalsViewModel();

        var result = vm.SetCounterparty([withSupplier.Id, withNone.Id], "SUP-2");

        Assert.Equal(2, result.Changed);
        Assert.Equal(("SUP-2", "SUP-2"), (withSupplier.SupplierId, withNone.SupplierId));

        Undo();
        Assert.Equal(("SUP-1", (string?)null), (withSupplier.SupplierId, withNone.SupplierId));

        vm.SetCounterparty([withSupplier.Id], null);
        Assert.Null(withSupplier.SupplierId);
    }

    [Fact]
    public void SetCounterparty_UpdatesTheSupplierOnTheExpensesReceipts()
    {
        AddProductsAndSuppliers();
        var expense = AddExpense("PUR-1", "SUP-1", Line("P1", "Paper"));
        var receipt = new Receipt { Id = "RCP-1", TransactionId = expense.Id, TransactionType = "Expense", Supplier = "Staples" };
        Company.Receipts.Add(receipt);
        expense.ReceiptId = receipt.Id;

        new ExpenseModalsViewModel().SetCounterparty([expense.Id], "SUP-2");

        Assert.Equal("Office Depot", receipt.Supplier);
    }

    [Fact]
    public void DeleteMany_RemovesTheExpensesAndTheirReceipts_AsOneUndoStep()
    {
        AddProductsAndSuppliers();
        var first = AddExpense("PUR-1", "SUP-1", Line("P1", "Paper"));
        var second = AddExpense("PUR-2", "SUP-1", Line("P1", "Paper"));
        var kept = AddExpense("PUR-3", "SUP-1", Line("P1", "Paper"));
        Company.Receipts.Add(new Receipt { Id = "RCP-1", TransactionId = first.Id, TransactionType = "Expense" });

        var result = new ExpenseModalsViewModel().DeleteMany([first.Id, second.Id]);

        Assert.Equal(2, result.Changed);
        Assert.Equal([kept.Id], Company.Expenses.Select(e => e.Id));
        Assert.Empty(Company.Receipts);
        Assert.Equal(1, App.UndoRedoManager.UndoCount);

        Undo();
        Assert.Equal(["PUR-1", "PUR-2", "PUR-3"], Company.Expenses.Select(e => e.Id).Order());
        Assert.Single(Company.Receipts);

        Redo();
        Assert.Equal([kept.Id], Company.Expenses.Select(e => e.Id));
    }

    [Fact]
    public void DeleteMany_LeavesPayrollWages()
    {
        AddProductsAndSuppliers();
        var plain = AddExpense("PUR-1", null, Line("P1", "Paper"));
        var wages = AddExpense("PUR-2", null, Line("P1", "Paper"));
        MarkAsPayrollWages(wages.Id);

        var result = new ExpenseModalsViewModel().DeleteMany([plain.Id, wages.Id]);

        Assert.Equal((1, 1), (result.Changed, result.MadeElsewhere));
        Assert.Equal([wages.Id], Company.Expenses.Select(e => e.Id));
    }
}
