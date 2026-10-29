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
        public void PickReceiptFile(string path) =>
            Attachments.Add(new TransactionAttachment(null, Path.GetFileName(path), path));

        /// <summary>Taking a document off the list, which the real form does with the X beside it.</summary>
        public void DropAttachments() => Attachments.Clear();
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

    // An online-only cloud file, or a phone or network drive that dropped, is gone by the time
    // the expense is saved. Attaching it anyway left a receipt with nothing in it.
    [Fact]
    public async Task AddExpense_WithAReceiptFileThatHasGone_AttachesNoReceipt()
    {
        var vm = NewExpenseVmWithProduct();
        FillLineItem(vm, 100m);
        vm.PickReceiptFile(Path.Combine(Path.GetTempPath(), "ArgoBooksTests", Guid.NewGuid().ToString("N"), "gone.pdf"));

        await vm.SaveTransactionCommand.ExecuteAsync(null);

        Assert.Null(Assert.Single(Company.Expenses).ReceiptId);
        Assert.Empty(Company.Receipts);
    }

    [Fact]
    public void PickingAProduct_FillsAnEmptySupplierBoxWithItsSupplier()
    {
        Company.Suppliers.Add(new Supplier { Id = "S1", Name = "Mill & Co" });
        Company.Products.Add(new Product { Id = "P2", Name = "Flour", CostPrice = 20m, SupplierId = "S1" });
        var vm = new ExpenseModalsViewModel();
        vm.OpenAddModal();

        vm.LineItems.First().SelectedProduct = vm.ProductOptions.First(p => p.Id == "P2");

        Assert.Equal("S1", vm.SelectedSupplier?.Id);
    }

    [Fact]
    public async Task AddExpense_CreatesExpenseWithAmountAndNotes()
    {
        var vm = NewExpenseVmWithProduct();
        FillLineItem(vm, 100m);
        vm.ModalNotes = "first";
        vm.ModalDate = new DateTimeOffset(new DateTime(2026, 3, 1), TimeSpan.Zero);

        await vm.SaveTransactionCommand.ExecuteAsync(null);

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
        await vm.SaveTransactionCommand.ExecuteAsync(null);

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
        await vm.SaveTransactionCommand.ExecuteAsync(null);
        var expenseId = Company.Expenses.Single().Id;

        // Edit it to $250 noted "second".
        vm.OpenEditModal(new ExpenseDisplayItem { Id = expenseId });
        vm.LineItems.First().UnitPrice = 250m;
        vm.ModalNotes = "second";
        await vm.SaveTransactionCommand.ExecuteAsync(null);

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

    private decimal LedgerNet() => Company.StockAdjustments.Sum(InventoryValuationService.SignedDelta);

    [Fact]
    public async Task DeleteExpense_ReversesStock_UndoReappliesIt_RedoReversesAgain()
    {
        var vm = NewExpenseVmWithProduct();
        var stock = TrackStock(10);
        FillLineItem(vm, 100m);
        vm.LineItems.First().Quantity = 5;
        await vm.SaveTransactionCommand.ExecuteAsync(null);
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
        await vm.SaveTransactionCommand.ExecuteAsync(null);
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
        await vm.SaveTransactionCommand.ExecuteAsync(null);
        var expense = Company.Expenses.Single();
        var oldReceipt = Company.Receipts.Single();

        vm.OpenEditModal(new ExpenseDisplayItem { Id = expense.Id });
        vm.DropAttachments();
        vm.PickReceiptFile(TempReceiptFile("second.png", [4, 5, 6]));
        await vm.SaveTransactionCommand.ExecuteAsync(null);

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

    /// <summary>
    /// Discarding closes the form through the same reset the save does, and a document picked but
    /// never saved has to go with everything else rather than turn up on the next one.
    /// </summary>
    [Fact]
    public void ClosingTheForm_DropsDocumentsThatWereNeverSaved()
    {
        var vm = NewExpenseVmWithProduct();
        FillLineItem(vm, 100m);
        vm.PickReceiptFile(TempReceiptFile("picked.png", [1, 2, 3]));

        vm.CloseAddEditModalCommand.Execute(null);

        Assert.Empty(vm.Attachments);
        Assert.False(vm.HasReceipt);
        Assert.Empty(Company.Receipts);
    }

    /// <summary>
    /// A discount larger than the lines would file a negative amount against the month, which the
    /// table then had to show as something. Saving stops instead and says why.
    /// </summary>
    [Fact]
    public async Task ADiscountLargerThanTheLines_IsRefused()
    {
        var vm = NewExpenseVmWithProduct();
        FillLineItem(vm, 100m);
        vm.ModalDiscount = 500m;

        await vm.SaveTransactionCommand.ExecuteAsync(null);

        Assert.Empty(Company.Expenses);
        Assert.True(vm.HasValidationMessage);
        Assert.True(vm.IsAddEditModalOpen);
    }

    [Fact]
    public async Task ADiscountEqualToTheLines_IsAllowed()
    {
        var vm = NewExpenseVmWithProduct();
        FillLineItem(vm, 100m);
        vm.ModalDiscount = 100m;

        await vm.SaveTransactionCommand.ExecuteAsync(null);

        var expense = Assert.Single(Company.Expenses);
        Assert.Equal(0m, expense.Total);
    }

    [Fact]
    public async Task AddExpense_WithTwoDocuments_KeepsBoth()
    {
        var vm = NewExpenseVmWithProduct();
        FillLineItem(vm, 100m);
        vm.PickReceiptFile(TempReceiptFile("booking-report.pdf", [1, 2, 3]));
        vm.PickReceiptFile(TempReceiptFile("bank-advice.pdf", [4, 5, 6]));

        await vm.SaveTransactionCommand.ExecuteAsync(null);

        var expense = Company.Expenses.Single();
        Assert.Equal(2, Company.Receipts.Count);
        Assert.All(Company.Receipts, r => Assert.Equal(expense.Id, r.TransactionId));

        // What the rest of the app reads when it wants one document.
        Assert.Equal(Company.Receipts[0].Id, expense.ReceiptId);
    }

    /// <summary>
    /// The receipts page lists a document by its record's amount, supplier and date, and filters and
    /// sorts on them. A document attached before an edit would otherwise keep the old figures and
    /// show the wrong amount beside the record it belongs to.
    /// </summary>
    [Fact]
    public async Task EditingAnExpenseAmount_CarriesItOntoDocumentsAlreadyAttached()
    {
        var vm = NewExpenseVmWithProduct();
        FillLineItem(vm, 100m);
        vm.PickReceiptFile(TempReceiptFile("booking-report.pdf", [1, 2, 3]));
        await vm.SaveTransactionCommand.ExecuteAsync(null);

        var expense = Company.Expenses.Single();
        Assert.Equal(100m, Company.Receipts.Single().Amount);

        vm.OpenEditModal(new ExpenseDisplayItem { Id = expense.Id });
        FillLineItem(vm, 250m);
        await vm.SaveTransactionCommand.ExecuteAsync(null);

        Assert.Equal(expense.Total, Company.Receipts.Single().Amount);
        Assert.Equal(250m, Company.Receipts.Single().Amount);
    }

    /// <summary>
    /// Undo puts the record's old amount back, so the documents on it have to come back too, or the
    /// receipts page keeps showing the amount of an edit that no longer exists.
    /// </summary>
    [Fact]
    public async Task UndoingAnAmountEdit_TakesTheDocumentsBackWithIt()
    {
        var vm = NewExpenseVmWithProduct();
        FillLineItem(vm, 100m);
        vm.PickReceiptFile(TempReceiptFile("booking-report.pdf", [1, 2, 3]));
        await vm.SaveTransactionCommand.ExecuteAsync(null);

        var expense = Company.Expenses.Single();
        vm.OpenEditModal(new ExpenseDisplayItem { Id = expense.Id });
        FillLineItem(vm, 250m);
        await vm.SaveTransactionCommand.ExecuteAsync(null);
        Assert.Equal(250m, Company.Receipts.Single().Amount);

        App.UndoRedoManager.Undo();
        Assert.Equal(100m, Company.Receipts.Single().Amount);

        App.UndoRedoManager.Redo();
        Assert.Equal(250m, Company.Receipts.Single().Amount);
    }

    [Fact]
    public async Task EditExpense_AddingASecondDocument_LeavesTheFirstAlone()
    {
        var vm = NewExpenseVmWithProduct();
        FillLineItem(vm, 100m);
        vm.PickReceiptFile(TempReceiptFile("first.png", [1, 2, 3]));
        await vm.SaveTransactionCommand.ExecuteAsync(null);
        var expense = Company.Expenses.Single();
        var first = Company.Receipts.Single();

        vm.OpenEditModal(new ExpenseDisplayItem { Id = expense.Id });
        vm.PickReceiptFile(TempReceiptFile("second.png", [4, 5, 6]));
        await vm.SaveTransactionCommand.ExecuteAsync(null);

        Assert.Equal(2, Company.Receipts.Count);
        Assert.Same(first, Company.Receipts[0]);
        Assert.Equal(first.Id, expense.ReceiptId);

        Undo();
        Assert.Same(first, Assert.Single(Company.Receipts));
    }

    [Fact]
    public async Task DeletingAnExpense_TakesEveryDocumentWithIt()
    {
        var vm = NewExpenseVmWithProduct();
        FillLineItem(vm, 100m);
        vm.PickReceiptFile(TempReceiptFile("one.png", [1]));
        vm.PickReceiptFile(TempReceiptFile("two.png", [2]));
        await vm.SaveTransactionCommand.ExecuteAsync(null);
        var expense = Company.Expenses.Single();

        vm.DeleteExpense(expense.Id);

        Assert.Empty(Company.Receipts);

        Undo();
        Assert.Equal(2, Company.Receipts.Count);
    }

    [Fact]
    public async Task EditExpense_ReceiptLeftAlone_KeepsTheSameReceipt()
    {
        var vm = NewExpenseVmWithProduct();
        FillLineItem(vm, 100m);
        vm.PickReceiptFile(TempReceiptFile("first.png", [1, 2, 3]));
        await vm.SaveTransactionCommand.ExecuteAsync(null);
        var expense = Company.Expenses.Single();
        var receipt = Company.Receipts.Single();

        vm.OpenEditModal(new ExpenseDisplayItem { Id = expense.Id });
        vm.ModalNotes = "second";
        await vm.SaveTransactionCommand.ExecuteAsync(null);

        Assert.Same(receipt, Assert.Single(Company.Receipts));
        Assert.Equal(receipt.Id, expense.ReceiptId);
    }

    [Fact]
    public async Task EditingTheYearsOfAnAssetExpense_KeepsOneAsset()
    {
        var vm = NewExpenseVmWithProduct();
        FillLineItem(vm, 1000m);
        vm.ModalIsAsset = true;
        vm.ModalAssetUsefulLife = 5;
        await vm.SaveTransactionCommand.ExecuteAsync(null);
        var expenseId = Company.Expenses.Single().Id;
        Assert.Single(Company.Assets);

        vm.OpenEditModal(new ExpenseDisplayItem { Id = expenseId });
        vm.ModalAssetUsefulLife = 7;
        await vm.SaveTransactionCommand.ExecuteAsync(null);

        Assert.Equal(7, Assert.Single(Company.Assets).UsefulLifeYears);
    }

    [Fact]
    public async Task DeletingAnAssetExpense_RemovesItsAsset()
    {
        var vm = NewExpenseVmWithProduct();
        FillLineItem(vm, 1000m);
        vm.ModalIsAsset = true;
        vm.ModalAssetUsefulLife = 5;
        await vm.SaveTransactionCommand.ExecuteAsync(null);

        vm.DeleteExpense(Company.Expenses.Single().Id);

        Assert.Empty(Company.Expenses);
        Assert.Empty(Company.Assets);

        Undo();
        Assert.Equal(Company.Expenses.Single().Id, Assert.Single(Company.Assets).ExpenseId);

        Redo();
        Assert.Empty(Company.Assets);
    }

    [Fact]
    public async Task DeletingAssetExpensesTogether_RemovesTheirAssets()
    {
        var vm = NewExpenseVmWithProduct();
        FillLineItem(vm, 1000m);
        vm.ModalIsAsset = true;
        vm.ModalAssetUsefulLife = 5;
        await vm.SaveTransactionCommand.ExecuteAsync(null);

        vm.DeleteMany([Company.Expenses.Single().Id]);
        Assert.Empty(Company.Assets);

        Undo();
        Assert.Single(Company.Assets);
    }
}
