using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Drives the real PurchaseOrdersModalsViewModel edit/undo/redo flow. Guards the fix where editing a
/// PO recomputed its USD total but undo/redo never restored the currency fields, so undo left a stale
/// TotalUSD (priced for the edited state) on a reverted order.
/// </summary>
public class PurchaseOrdersModalsViewModelTests : ModalViewModelTestBase
{
    private PurchaseOrder SeedOrder()
    {
        Company.Settings.Localization.Currency = "USD";
        Company.Suppliers.Add(new Supplier { Id = "S1", Name = "Acme" });
        Company.Products.Add(new Product { Id = "P1", Name = "Widget", UnitPrice = 100m, CostPrice = 100m });
        var order = new PurchaseOrder
        {
            Id = "PO-1",
            SupplierId = "S1",
            OrderDate = new DateTime(2026, 3, 1),
            ExpectedDeliveryDate = new DateTime(2026, 3, 8),
            Subtotal = 100m,
            ShippingCost = 0m,
            Total = 100m,
            OriginalCurrency = "USD",
            TotalUSD = 100m,
            LineItems =
            [
                new() { ProductId = "P1", Quantity = 1, UnitCost = 100m }
            ]
        };
        Company.PurchaseOrders.Add(order);
        return order;
    }

    [Fact]
    public async Task EditOrder_UndoThenRedo_RestoresTotalAndUsdTotal()
    {
        var order = SeedOrder();
        var vm = new PurchaseOrdersModalsViewModel();

        vm.OpenEditModal(new PurchaseOrderDisplayItem { Id = "PO-1" });
        vm.LineItems.First().UnitCost = "250.00"; // raise the total to 250
        await vm.SaveOrderCommand.ExecuteAsync(null);

        Assert.Equal(250m, order.Total);
        Assert.Equal(250m, order.TotalUSD);

        // Undo must revert BOTH the native total and the USD total (the fix); a stale TotalUSD of 250
        // on a reverted 100 order was the bug.
        Undo();
        Assert.Equal(100m, order.Total);
        Assert.Equal(100m, order.TotalUSD);
        Assert.False(order.IsPendingConversion);

        Redo();
        Assert.Equal(250m, order.Total);
        Assert.Equal(250m, order.TotalUSD);
    }

    private PurchaseOrder SeedOrderToReceive(int quantity, int alreadyReceived)
    {
        Company.Settings.Localization.Currency = "USD";
        Company.Products.Add(new Product { Id = "P1", Name = "Widget", UnitPrice = 10m, CostPrice = 10m, TrackInventory = true });
        var order = new PurchaseOrder
        {
            Id = "PO-1",
            PoNumber = "PO-2026-001",
            SupplierId = "S1",
            Status = alreadyReceived > 0 ? PurchaseOrderStatus.PartiallyReceived : PurchaseOrderStatus.OnOrder,
            LineItems = [new() { ProductId = "P1", Quantity = quantity, QuantityReceived = alreadyReceived, UnitCost = 10m }]
        };
        Company.PurchaseOrders.Add(order);
        return order;
    }

    private static void Receive(PurchaseOrdersModalsViewModel vm, string quantity)
    {
        vm.OpenReceiveModal(new PurchaseOrderDisplayItem { Id = "PO-1" });
        vm.ReceiveLineItems.Single().ReceivingQuantity = quantity;
        vm.ConfirmReceiveCommand.Execute(null);
    }

    [Fact]
    public void PartiallyReceivedOrder_CanBeReceivedAgain()
    {
        Assert.True(new PurchaseOrderDisplayItem { Status = PurchaseOrderStatus.PartiallyReceived }.CanReceive);
    }

    [Fact]
    public void ReceivingPartiallyReceivedOrder_OffersOnlyRemaining_AndRejectsOverReceive()
    {
        var order = SeedOrderToReceive(quantity: 5, alreadyReceived: 2);
        var stock = new InventoryItem { Id = "INV-1", ProductId = "P1", InStock = 2 };
        Company.Inventory.Add(stock);
        var vm = new PurchaseOrdersModalsViewModel();

        Receive(vm, "4");
        Assert.Equal(3, vm.ReceiveLineItems.Single().Remaining);
        Assert.NotNull(vm.ReceiveModalError);
        Assert.Equal(2, order.LineItems[0].QuantityReceived);
        Assert.Equal(2, stock.InStock);

        Receive(vm, "3");
        Assert.Equal(5, order.LineItems[0].QuantityReceived);
        Assert.Equal(PurchaseOrderStatus.Received, order.Status);
        Assert.Equal(5, stock.InStock);
    }

    [Fact]
    public void Receive_TrackedProductWithNoInventoryRow_CreatesRowAndLedgerEntry_UndoRemovesBoth()
    {
        var order = SeedOrderToReceive(quantity: 5, alreadyReceived: 0);
        var vm = new PurchaseOrdersModalsViewModel();

        Receive(vm, "5");
        var row = Assert.Single(Company.Inventory);
        Assert.Equal("P1", row.ProductId);
        Assert.Equal(5, row.InStock);
        var adjustment = Assert.Single(Company.StockAdjustments);
        Assert.Equal(AdjustmentType.Add, adjustment.AdjustmentType);
        Assert.Equal(5, adjustment.Quantity);
        Assert.Equal(row.Id, adjustment.InventoryItemId);
        Assert.Equal("PO-2026-001", adjustment.ReferenceNumber);

        Undo();
        Assert.Empty(Company.Inventory);
        Assert.Empty(Company.StockAdjustments);
        Assert.Equal(0, order.LineItems[0].QuantityReceived);

        Redo();
        Assert.Equal(5, Assert.Single(Company.Inventory).InStock);
        Assert.Single(Company.StockAdjustments);
        Assert.Equal(5, order.LineItems[0].QuantityReceived);
    }

    [Fact]
    public void Receive_ExistingInventoryRow_WritesLedgerEntry_UndoRestoresStockExactly()
    {
        SeedOrderToReceive(quantity: 5, alreadyReceived: 0);
        var stock = new InventoryItem { Id = "INV-1", ProductId = "P1", InStock = 10 };
        Company.Inventory.Add(stock);
        var vm = new PurchaseOrdersModalsViewModel();

        Receive(vm, "3");
        Assert.Equal(13, stock.InStock);
        Assert.Equal(3, Company.StockAdjustments.Sum(InventoryValuationService.SignedDelta));

        Undo();
        Assert.Equal(10, stock.InStock);
        Assert.Empty(Company.StockAdjustments);

        Redo();
        Assert.Equal(13, stock.InStock);
        Assert.Equal(3, Company.StockAdjustments.Sum(InventoryValuationService.SignedDelta));
    }
}
