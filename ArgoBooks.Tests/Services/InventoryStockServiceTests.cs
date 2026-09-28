using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Stock and cost of goods sold for purchases, sales, edits and transfers (docs/Calculations.md §14).
/// </summary>
public class InventoryStockServiceTests
{
    private static readonly Location Shop = new() { Id = "LOC-1", Name = "Shop" };
    private static readonly Location Warehouse = new() { Id = "LOC-2", Name = "Warehouse" };

    private static CompanyData Company(params Location[] locations)
    {
        var data = new CompanyData();
        data.Locations.AddRange(locations);
        data.Products.Add(new Product { Id = "PRD-1", Name = "Flour", Sku = "FLR", TrackInventory = true, CostPrice = 2m });
        data.Products.Add(new Product { Id = "PRD-2", Name = "Delivery", Sku = "DLV" });
        return data;
    }

    private static InventoryItem Stock(CompanyData data, decimal inStock, decimal unitCost, decimal openingUnits = 0, string locationId = "LOC-1")
    {
        var item = new InventoryItem
        {
            Id = "INV-" + locationId,
            ProductId = "PRD-1",
            LocationId = locationId,
            InStock = inStock,
            UnitCost = unitCost,
            OpeningUnits = openingUnits
        };
        data.Inventory.Add(item);
        return item;
    }

    private static LineItem Line(string productId, decimal quantity, decimal unitPrice, string? locationId = null) =>
        new() { ProductId = productId, Quantity = quantity, UnitPrice = unitPrice, LocationId = locationId };

    private static Expense Purchase(params LineItem[] lines)
    {
        var total = lines.Sum(l => l.Subtotal);
        return new Expense { Id = "PUR-1", Date = new DateTime(2026, 1, 5), OriginalCurrency = "USD", Total = total, Amount = total, LineItems = [.. lines] };
    }

    private static Revenue Sale(params LineItem[] lines)
    {
        var total = lines.Sum(l => l.Subtotal);
        return new Revenue
        {
            Id = "REV-1", Date = new DateTime(2026, 1, 10), OriginalCurrency = "USD", Total = total, Amount = total,
            PaymentStatus = RevenuePaymentStatus.Paid, LineItems = [.. lines]
        };
    }

    [Fact]
    public void Purchase_AddsStock_MarksTheLineAsStock_AndSetsCost()
    {
        var data = Company(Shop);
        var purchase = Purchase(Line("PRD-1", 10, 3m));

        InventoryStockService.Apply(data, purchase.LineItems, purchase, isPurchase: true);

        var item = Assert.Single(data.Inventory);
        Assert.Equal(10m, item.InStock);
        Assert.Equal("LOC-1", item.LocationId);
        Assert.Equal(3m, item.UnitCost);
        Assert.True(purchase.LineItems[0].IsStockPurchase);
        Assert.Equal(3m, data.GetProduct("PRD-1")!.CostPrice);
    }

    [Fact]
    public void Sale_RecordsCostOfGoodsSoldAtTheStockUnitCost()
    {
        var data = Company(Shop);
        var item = Stock(data, inStock: 10, unitCost: 3m);
        var sale = Sale(Line("PRD-1", 4, 10m));

        InventoryStockService.Apply(data, sale.LineItems, sale, isPurchase: false);

        Assert.Equal(6m, item.InStock);
        Assert.Equal(12m, sale.LineItems[0].CostOfGoodsUSD);
    }

    [Fact]
    public void Sale_UsesOpeningUnitsFirst_AtNoCost()
    {
        var data = Company(Shop);
        var item = Stock(data, inStock: 10, unitCost: 2m, openingUnits: 3);
        var sale = Sale(Line("PRD-1", 5, 10m));

        InventoryStockService.Apply(data, sale.LineItems, sale, isPurchase: false);

        Assert.Equal(3m, sale.LineItems[0].OpeningUnitsUsed);
        Assert.Equal(4m, sale.LineItems[0].CostOfGoodsUSD);
        Assert.Equal(0m, item.OpeningUnits);
    }

    [Fact]
    public void UntrackedProduct_MovesNoStock_AndCarriesNoCost()
    {
        var data = Company(Shop);
        var sale = Sale(Line("PRD-2", 1, 25m));

        var changes = InventoryStockService.Apply(data, sale.LineItems, sale, isPurchase: false);

        Assert.Empty(changes);
        Assert.Empty(data.Inventory);
        Assert.Null(sale.LineItems[0].CostOfGoodsUSD);
    }

    [Fact]
    public void DecimalQuantities_MoveExactly()
    {
        var data = Company(Shop);
        var purchase = Purchase(Line("PRD-1", 2.5m, 4m));
        InventoryStockService.Apply(data, purchase.LineItems, purchase, isPurchase: true);

        var sale = Sale(Line("PRD-1", 0.75m, 10m));
        InventoryStockService.Apply(data, sale.LineItems, sale, isPurchase: false);

        var item = Assert.Single(data.Inventory);
        Assert.Equal(1.75m, item.InStock);
        Assert.Equal(3m, sale.LineItems[0].CostOfGoodsUSD);
    }

    [Fact]
    public void Edit_RecordsOneAdjustmentForTheNetChange()
    {
        var data = Company(Shop);
        var item = Stock(data, inStock: 10, unitCost: 2m);
        var sale = Sale(Line("PRD-1", 4, 10m));
        InventoryStockService.Apply(data, sale.LineItems, sale, isPurchase: false);

        var edited = new List<LineItem> { Line("PRD-1", 1, 10m) };
        InventoryStockService.ApplyEdit(data, sale.LineItems, edited, sale, isPurchase: false, "Revenue edited");

        Assert.Equal(9m, item.InStock);
        Assert.Equal(2, data.StockAdjustments.Count);
        Assert.Equal(AdjustmentType.Add, data.StockAdjustments[1].AdjustmentType);
        Assert.Equal(3m, data.StockAdjustments[1].Quantity);
        Assert.Equal(2m, edited[0].CostOfGoodsUSD);
    }

    // A later purchase at a new price must not re-price a sale that is only being edited.
    [Fact]
    public void Edit_KeepsTheSalesUnitCost_WhenTheStockCostChangedSince()
    {
        var data = Company(Shop);
        var item = Stock(data, inStock: 10, unitCost: 2m);
        var sale = Sale(Line("PRD-1", 4, 10m));
        InventoryStockService.Apply(data, sale.LineItems, sale, isPurchase: false);
        item.UnitCost = 5m;

        var edited = new List<LineItem> { Line("PRD-1", 5, 10m) };
        InventoryStockService.ApplyEdit(data, sale.LineItems, edited, sale, isPurchase: false, "Revenue edited");

        Assert.Equal(10m, edited[0].CostOfGoodsUSD);
    }

    [Fact]
    public void Delete_GivesBackStockAndTheOpeningUnitsItUsed()
    {
        var data = Company(Shop);
        var item = Stock(data, inStock: 10, unitCost: 2m, openingUnits: 5);
        var sale = Sale(Line("PRD-1", 4, 10m));
        InventoryStockService.Apply(data, sale.LineItems, sale, isPurchase: false);
        Assert.Equal(1m, item.OpeningUnits);

        InventoryStockService.ApplyEdit(data, sale.LineItems, [], sale, isPurchase: false, "Revenue deleted");

        Assert.Equal(10m, item.InStock);
        Assert.Equal(5m, item.OpeningUnits);
    }

    // What a transaction saved by an older version recorded when it moved stock.
    private static void Moved(CompanyData data, InventoryItem item, string transactionId, AdjustmentType type, decimal quantity) =>
        data.StockAdjustments.Add(new StockAdjustment
        {
            Id = "ADJ-OLD-" + transactionId, InventoryItemId = item.Id, AdjustmentType = type, Quantity = quantity,
            ReferenceNumber = transactionId, IsAutoGenerated = true
        });

    [Fact]
    public void Edit_OfASaleFromBeforeCostOfGoods_MovesStockButRecordsNoCost()
    {
        var data = Company(Shop);
        var item = Stock(data, inStock: 6, unitCost: 2m, openingUnits: 6);
        var oldSale = Sale(Line("PRD-1", 4, 10m));
        Moved(data, item, oldSale.Id, AdjustmentType.Remove, 4);

        var edited = new List<LineItem> { Line("PRD-1", 3, 10m) };
        InventoryStockService.ApplyEdit(data, oldSale.LineItems, edited, oldSale, isPurchase: false, "Revenue edited");

        Assert.Equal(7m, item.InStock);
        Assert.Null(edited[0].CostOfGoodsUSD);
        Assert.Equal(6m, item.OpeningUnits);
    }

    [Fact]
    public void Edit_OfAPurchaseFromBeforeCostOfGoods_StaysAnExpense()
    {
        var data = Company(Shop);
        var item = Stock(data, inStock: 10, unitCost: 2m, openingUnits: 10);
        var oldPurchase = Purchase(Line("PRD-1", 10, 2m));
        Moved(data, item, oldPurchase.Id, AdjustmentType.Add, 10);

        var edited = new List<LineItem> { Line("PRD-1", 8, 5m) };
        InventoryStockService.ApplyEdit(data, oldPurchase.LineItems, edited, oldPurchase, isPurchase: true, "Expense edited");

        Assert.Equal(8m, item.InStock);
        Assert.False(edited[0].IsStockPurchase);
        Assert.Equal(2m, item.UnitCost);
    }

    // Saved while the product didn't track stock, so the purchase never added any. Editing it used to
    // take the quantity back off before adding it again, which left stock where it was.
    [Fact]
    public void Edit_OfAPurchaseThatNeverMovedStock_AddsItsStock()
    {
        var data = Company(Shop);
        var item = Stock(data, inStock: 0, unitCost: 2m);
        var purchase = Purchase(Line("PRD-1", 10, 3m));

        InventoryStockService.ApplyEdit(data, purchase.LineItems, [Line("PRD-1", 10, 3m)], purchase, isPurchase: true, "Expense edited");

        Assert.Equal(10m, item.InStock);
    }

    [Fact]
    public void Delete_OfASaleThatNeverMovedStock_LeavesStockAlone()
    {
        var data = Company(Shop);
        var item = Stock(data, inStock: 5, unitCost: 2m);
        var sale = Sale(Line("PRD-1", 2, 10m));

        InventoryStockService.ApplyEdit(data, sale.LineItems, [], sale, isPurchase: false, "Revenue deleted");

        Assert.Equal(5m, item.InStock);
    }

    [Fact]
    public void Revert_PutsEverythingBack_AndRemovesAStockRecordItCreated()
    {
        var data = Company(Shop);
        var purchase = Purchase(Line("PRD-1", 10, 3m));
        var changes = InventoryStockService.Apply(data, purchase.LineItems, purchase, isPurchase: true);

        InventoryStockService.Revert(data, changes);

        Assert.Empty(data.Inventory);
        Assert.Empty(data.StockAdjustments);
        Assert.Equal(2m, data.GetProduct("PRD-1")!.CostPrice);
    }

    [Fact]
    public void Sale_TakesFromTheLineLocation_WhenStockedAtSeveral()
    {
        var data = Company(Shop, Warehouse);
        var shop = Stock(data, inStock: 5, unitCost: 2m, locationId: "LOC-1");
        var warehouse = Stock(data, inStock: 5, unitCost: 2m, locationId: "LOC-2");
        var sale = Sale(Line("PRD-1", 2, 10m, locationId: "LOC-2"));

        InventoryStockService.Apply(data, sale.LineItems, sale, isPurchase: false);

        Assert.Equal(5m, shop.InStock);
        Assert.Equal(3m, warehouse.InStock);
    }

    [Fact]
    public void Purchase_WithNoStockRecord_GoesToTheFirstLocation_OrANewOne()
    {
        var withLocations = Company(Warehouse, Shop);
        var purchase = Purchase(Line("PRD-1", 1, 3m));
        InventoryStockService.Apply(withLocations, purchase.LineItems, purchase, isPurchase: true);
        Assert.Equal("LOC-2", Assert.Single(withLocations.Inventory).LocationId);
        Assert.Equal("LOC-2", purchase.LineItems[0].LocationId);

        var withoutLocations = Company();
        var another = Purchase(Line("PRD-1", 1, 3m));
        InventoryStockService.Apply(withoutLocations, another.LineItems, another, isPurchase: true);
        // A company with no locations gets a real one, not a placeholder that shows as "Default"
        // in the pickers and cannot be renamed or deleted.
        var created = Assert.Single(withoutLocations.Locations);
        Assert.Equal("Main", created.Name);
        Assert.Equal(created.Id, Assert.Single(withoutLocations.Inventory).LocationId);
    }

    [Fact]
    public void ForeignCurrencyPurchase_SetsUnitCostInUsd_AndLeavesCostPriceAlone()
    {
        var data = Company(Shop);
        var purchase = Purchase(Line("PRD-1", 10, 10m));
        purchase.OriginalCurrency = "CAD";
        purchase.TotalUSD = 75m;

        InventoryStockService.Apply(data, purchase.LineItems, purchase, isPurchase: true);

        Assert.Equal(7.5m, Assert.Single(data.Inventory).UnitCost);
        Assert.Equal(2m, data.GetProduct("PRD-1")!.CostPrice);
    }

    [Fact]
    public void Transfer_MovesStockAndOpeningUnitsInProportion_AndRevertUndoesIt()
    {
        var data = Company(Shop, Warehouse);
        var source = Stock(data, inStock: 10, unitCost: 3m, openingUnits: 4, locationId: "LOC-1");

        var result = InventoryStockService.Transfer(data, source, "LOC-2", 5m, "Restock the warehouse");

        Assert.Equal(5m, source.InStock);
        Assert.Equal(2m, source.OpeningUnits);
        Assert.True(result.DestinationCreated);
        Assert.Equal(5m, result.Destination.InStock);
        Assert.Equal(2m, result.Destination.OpeningUnits);
        Assert.Equal(3m, result.Destination.UnitCost);
        Assert.Single(data.StockTransfers);
        Assert.Equal(2, data.StockAdjustments.Count);

        InventoryStockService.RevertTransfer(data, result);

        Assert.Equal(10m, source.InStock);
        Assert.Equal(4m, source.OpeningUnits);
        Assert.Single(data.Inventory);
        Assert.Empty(data.StockTransfers);
        Assert.Empty(data.StockAdjustments);
    }
}
