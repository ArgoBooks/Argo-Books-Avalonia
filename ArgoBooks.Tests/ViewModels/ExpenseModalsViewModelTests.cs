using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Drives the real ExpenseModalsViewModel add/edit/undo/redo flows. The edit round-trip specifically
/// guards the regression where redo re-read live (reset) ViewModel fields and wrote a $0 amount with
/// blank notes; capture/restore must round-trip the edited values exactly.
/// </summary>
public class ExpenseModalsViewModelTests : ModalViewModelTestBase
{
    /// <summary>Stands in for the file picker, which the real Attach/Change button opens.</summary>
    private sealed class ReceiptPickingExpenseVm : ExpenseModalsViewModel
    {
        public void PickReceiptFile(string path)
        {
            ReceiptFilePath = path;
            ReceiptFileName = Path.GetFileName(path);
        }
    }

    private static string TempReceiptFile(string name, byte[] bytes)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ArgoBooksTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private ReceiptPickingExpenseVm NewExpenseVmWithProduct()
    {
        Company.Settings.Localization.Currency = "USD";
        Company.Products.Add(new Product { Id = "P1", Name = "Widget", UnitPrice = 100m, CostPrice = 100m });
        var vm = new ReceiptPickingExpenseVm();
        vm.OpenAddModal(); // loads product options and seeds one empty line item
        return vm;
    }

    private void FillLineItem(ExpenseModalsViewModel vm, decimal unitPrice)
    {
        var line = vm.LineItems.First();
        line.SelectedProduct = vm.ProductOptions.First(p => p.Id == "P1");
        line.Quantity = 1;
        line.UnitPrice = unitPrice;
    }

    [Fact]
    public async Task AddExpense_CreatesExpenseWithAmountAndNotes()
    {
        var vm = NewExpenseVmWithProduct();
        FillLineItem(vm, 100m);
        vm.ModalNotes = "first";
        vm.ModalDate = new DateTimeOffset(new DateTime(2026, 3, 1), TimeSpan.Zero);

        await vm.SaveExpenseCommand.ExecuteAsync(null);

        var expense = Assert.Single(Company.Expenses);
        Assert.Equal(100m, expense.Total);
        Assert.Equal("first", expense.Notes);
    }

    [Fact]
    public async Task AddExpense_UndoThenRedo_RestoresExpenseIntact()
    {
        var vm = NewExpenseVmWithProduct();
        FillLineItem(vm, 100m);
        vm.ModalNotes = "first";
        await vm.SaveExpenseCommand.ExecuteAsync(null);

        Undo();
        Assert.Empty(Company.Expenses);

        Redo();
        var restored = Assert.Single(Company.Expenses);
        Assert.Equal(100m, restored.Total);
        Assert.Equal("first", restored.Notes);
    }

    [Fact]
    public async Task EditExpense_UndoThenRedo_KeepsEditedAmountAndNotes()
    {
        // Add a $100 expense noted "first".
        var vm = NewExpenseVmWithProduct();
        FillLineItem(vm, 100m);
        vm.ModalNotes = "first";
        await vm.SaveExpenseCommand.ExecuteAsync(null);
        var expenseId = Company.Expenses.Single().Id;

        // Edit it to $250 noted "second".
        vm.OpenEditModal(new ExpenseDisplayItem { Id = expenseId });
        vm.LineItems.First().UnitPrice = 250m;
        vm.ModalNotes = "second";
        await vm.SaveExpenseCommand.ExecuteAsync(null);

        var edited = Company.Expenses.Single();
        Assert.Equal(250m, edited.Total);
        Assert.Equal("second", edited.Notes);

        // Undo returns to the original values.
        Undo();
        var reverted = Company.Expenses.Single();
        Assert.Equal(100m, reverted.Total);
        Assert.Equal("first", reverted.Notes);

        // Redo must restore the EDITED values, not zero them out (the regression this guards).
        Redo();
        var redone = Company.Expenses.Single();
        Assert.Equal(250m, redone.Total);
        Assert.Equal("second", redone.Notes);
    }

    private InventoryItem TrackStock(int inStock)
    {
        Company.Products.Single(p => p.Id == "P1").TrackInventory = true;
        var item = new InventoryItem { Id = "INV-1", ProductId = "P1", InStock = inStock };
        Company.Inventory.Add(item);
        return item;
    }

    private int LedgerNet() => Company.StockAdjustments.Sum(InventoryValuationService.SignedDelta);

    [Fact]
    public async Task DeleteExpense_ReversesStock_UndoReappliesIt_RedoReversesAgain()
    {
        var vm = NewExpenseVmWithProduct();
        var stock = TrackStock(10);
        FillLineItem(vm, 100m);
        vm.LineItems.First().Quantity = 5;
        await vm.SaveExpenseCommand.ExecuteAsync(null);
        Assert.Equal(15, stock.InStock);

        vm.DeleteExpense(Company.Expenses.Single().Id);
        Assert.Empty(Company.Expenses);
        Assert.Equal(10, stock.InStock);
        Assert.Equal(0, LedgerNet());

        Undo();
        Assert.Single(Company.Expenses);
        Assert.Equal(15, stock.InStock);
        Assert.Equal(5, LedgerNet());

        Redo();
        Assert.Empty(Company.Expenses);
        Assert.Equal(10, stock.InStock);
        Assert.Equal(0, LedgerNet());
    }

    [Fact]
    public async Task AddExpense_SameProductOnTwoLines_UndoRestoresOriginalStock()
    {
        var vm = NewExpenseVmWithProduct();
        var stock = TrackStock(10);
        FillLineItem(vm, 100m);
        vm.LineItems.First().Quantity = 5;
        vm.AddLineItemCommand.Execute(null);
        var second = vm.LineItems.Last();
        second.SelectedProduct = vm.ProductOptions.First(p => p.Id == "P1");
        second.Quantity = 5;
        second.UnitPrice = 100m;
        await vm.SaveExpenseCommand.ExecuteAsync(null);
        Assert.Equal(20, stock.InStock);

        Undo();
        Assert.Equal(10, stock.InStock);
        Assert.Empty(Company.StockAdjustments);
    }

    [Fact]
    public async Task EditExpense_ChangeReceipt_ReplacesItAndUndoRedoSwapsIt()
    {
        var vm = NewExpenseVmWithProduct();
        FillLineItem(vm, 100m);
        vm.PickReceiptFile(TempReceiptFile("first.png", [1, 2, 3]));
        await vm.SaveExpenseCommand.ExecuteAsync(null);
        var expense = Company.Expenses.Single();
        var oldReceipt = Company.Receipts.Single();

        vm.OpenEditModal(new ExpenseDisplayItem { Id = expense.Id });
        vm.PickReceiptFile(TempReceiptFile("second.png", [4, 5, 6]));
        await vm.SaveExpenseCommand.ExecuteAsync(null);

        var newReceipt = Assert.Single(Company.Receipts);
        Assert.Equal("second.png", newReceipt.FileName);
        Assert.Equal(newReceipt.Id, expense.ReceiptId);

        Undo();
        Assert.Same(oldReceipt, Assert.Single(Company.Receipts));
        Assert.Equal(oldReceipt.Id, expense.ReceiptId);

        Redo();
        Assert.Same(newReceipt, Assert.Single(Company.Receipts));
        Assert.Equal(newReceipt.Id, expense.ReceiptId);
    }

    [Fact]
    public async Task EditExpense_ReceiptLeftAlone_KeepsTheSameReceipt()
    {
        var vm = NewExpenseVmWithProduct();
        FillLineItem(vm, 100m);
        vm.PickReceiptFile(TempReceiptFile("first.png", [1, 2, 3]));
        await vm.SaveExpenseCommand.ExecuteAsync(null);
        var expense = Company.Expenses.Single();
        var receipt = Company.Receipts.Single();

        vm.OpenEditModal(new ExpenseDisplayItem { Id = expense.Id });
        vm.ModalNotes = "second";
        await vm.SaveExpenseCommand.ExecuteAsync(null);

        Assert.Same(receipt, Assert.Single(Company.Receipts));
        Assert.Equal(receipt.Id, expense.ReceiptId);
    }
}
