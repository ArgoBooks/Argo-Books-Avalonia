using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Drives the real RevenueModalsViewModel add/edit/undo/redo flows. The edit round-trip guards the
/// same capture/restore asymmetry class of bug as the expense modal.
/// </summary>
public class RevenueModalsViewModelTests : ModalViewModelTestBase
{
    /// <summary>Stands in for the file picker, which the real Attach/Change button opens.</summary>
    private sealed class ReceiptPickingRevenueVm : RevenueModalsViewModel
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

    private ReceiptPickingRevenueVm NewRevenueVmWithProduct()
    {
        Company.Settings.Localization.Currency = "USD";
        Company.Products.Add(new Product { Id = "P1", Name = "Widget", UnitPrice = 100m, CostPrice = 100m });
        var vm = new ReceiptPickingRevenueVm();
        vm.OpenAddModal();
        return vm;
    }

    private void FillLineItem(RevenueModalsViewModel vm, decimal unitPrice)
    {
        var line = vm.LineItems.First();
        line.SelectedProduct = vm.ProductOptions.First(p => p.Id == "P1");
        line.Quantity = 1;
        line.UnitPrice = unitPrice;
    }

    [Fact]
    public async Task AddRevenue_CreatesRevenueWithAmountAndNotes()
    {
        var vm = NewRevenueVmWithProduct();
        FillLineItem(vm, 100m);
        vm.ModalNotes = "first";
        vm.ModalDate = new DateTimeOffset(new DateTime(2026, 3, 1), TimeSpan.Zero);

        await vm.SaveRevenueCommand.ExecuteAsync(null);

        var revenue = Assert.Single(Company.Revenues);
        Assert.Equal(100m, revenue.Total);
        Assert.Equal("first", revenue.Notes);
    }

    [Fact]
    public async Task AddRevenue_UndoThenRedo_RestoresRevenueIntact()
    {
        var vm = NewRevenueVmWithProduct();
        FillLineItem(vm, 100m);
        vm.ModalNotes = "first";
        await vm.SaveRevenueCommand.ExecuteAsync(null);

        Undo();
        Assert.Empty(Company.Revenues);

        Redo();
        var restored = Assert.Single(Company.Revenues);
        Assert.Equal(100m, restored.Total);
        Assert.Equal("first", restored.Notes);
    }

    [Fact]
    public async Task EditRevenue_UndoThenRedo_KeepsEditedAmountAndNotes()
    {
        var vm = NewRevenueVmWithProduct();
        FillLineItem(vm, 100m);
        vm.ModalNotes = "first";
        await vm.SaveRevenueCommand.ExecuteAsync(null);
        var revenueId = Company.Revenues.Single().Id;

        vm.OpenEditModal(new RevenueDisplayItem { Id = revenueId });
        vm.LineItems.First().UnitPrice = 250m;
        vm.ModalNotes = "second";
        await vm.SaveRevenueCommand.ExecuteAsync(null);

        Assert.Equal(250m, Company.Revenues.Single().Total);
        Assert.Equal("second", Company.Revenues.Single().Notes);

        Undo();
        Assert.Equal(100m, Company.Revenues.Single().Total);
        Assert.Equal("first", Company.Revenues.Single().Notes);

        Redo();
        Assert.Equal(250m, Company.Revenues.Single().Total);
        Assert.Equal("second", Company.Revenues.Single().Notes);
    }

    [Fact]
    public async Task DeleteRevenue_RestoresStock_UndoResells_RedoRestoresAgain()
    {
        var vm = NewRevenueVmWithProduct();
        Company.Products.Single(p => p.Id == "P1").TrackInventory = true;
        var stock = new InventoryItem { Id = "INV-1", ProductId = "P1", InStock = 10 };
        Company.Inventory.Add(stock);
        FillLineItem(vm, 100m);
        vm.LineItems.First().Quantity = 5;
        await vm.SaveRevenueCommand.ExecuteAsync(null);
        Assert.Equal(5, stock.InStock);

        int LedgerNet() => Company.StockAdjustments.Sum(InventoryValuationService.SignedDelta);

        vm.DeleteRevenue(Company.Revenues.Single().Id);
        Assert.Empty(Company.Revenues);
        Assert.Equal(10, stock.InStock);
        Assert.Equal(0, LedgerNet());

        Undo();
        Assert.Single(Company.Revenues);
        Assert.Equal(5, stock.InStock);
        Assert.Equal(-5, LedgerNet());

        Redo();
        Assert.Empty(Company.Revenues);
        Assert.Equal(10, stock.InStock);
        Assert.Equal(0, LedgerNet());
    }

    [Fact]
    public async Task EditRevenue_ChangeReceipt_ReplacesItAndUndoRedoSwapsIt()
    {
        var vm = NewRevenueVmWithProduct();
        FillLineItem(vm, 100m);
        vm.PickReceiptFile(TempReceiptFile("first.png", [1, 2, 3]));
        await vm.SaveRevenueCommand.ExecuteAsync(null);
        var revenue = Company.Revenues.Single();
        var oldReceipt = Company.Receipts.Single();

        vm.OpenEditModal(new RevenueDisplayItem { Id = revenue.Id });
        vm.PickReceiptFile(TempReceiptFile("second.png", [4, 5, 6]));
        await vm.SaveRevenueCommand.ExecuteAsync(null);

        var newReceipt = Assert.Single(Company.Receipts);
        Assert.Equal("second.png", newReceipt.FileName);
        Assert.Equal(newReceipt.Id, revenue.ReceiptId);

        Undo();
        Assert.Same(oldReceipt, Assert.Single(Company.Receipts));
        Assert.Equal(oldReceipt.Id, revenue.ReceiptId);

        Redo();
        Assert.Same(newReceipt, Assert.Single(Company.Receipts));
        Assert.Equal(newReceipt.Id, revenue.ReceiptId);
    }
}
