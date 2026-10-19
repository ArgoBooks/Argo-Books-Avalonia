using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.Integrations;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Sales and purchases that arrive through Stripe or the Argo Books API move stock and record cost
/// of goods sold the way saving them in the app does (docs/Calculations.md §14). Undoing the import
/// has to put stock, opening units, unit cost and the stock ledger back, and redoing it has to apply
/// them again without counting anything twice.
/// </summary>
public class IntegrationImportStockTests
{
    private static CompanyData Company(decimal inStock, decimal unitCost = 4m, decimal openingUnits = 0)
    {
        var data = new CompanyData();
        data.Locations.Add(new Location { Id = "LOC-1", Name = "Shop" });
        data.Products.Add(new Product { Id = "PRD-1", Name = "Widget", TrackInventory = true, CostPrice = unitCost });
        data.Inventory.Add(new InventoryItem
        {
            Id = "INV-1", ProductId = "PRD-1", LocationId = "LOC-1",
            InStock = inStock, UnitCost = unitCost, OpeningUnits = openingUnits
        });
        return data;
    }

    private static readonly ArgoImportState Pending = new("pending", null, null, null);

    private static ArgoLineItem WidgetLine(decimal quantity, long unitCents) =>
        new("li_" + quantity, "prod_w", "Widget", quantity, unitCents, 0, 0);

    private static ArgoRevenue ApiSale(decimal quantity, long unitCents) => new(
        Id: "rev_1", Description: "Order #1042",
        Amount: (long)(quantity * unitCents), Currency: "USD", TaxAmount: 0, DiscountAmount: 0, FeeAmount: 0,
        OccurredOn: "2026-08-14", Customer: null, Category: null, PaymentMethod: null,
        Reference: null, Notes: null, LineItems: [WidgetLine(quantity, unitCents)], Import: Pending);

    private static ArgoExpense ApiPurchase(decimal quantity, long unitCents) => new(
        Id: "exp_1", Description: "Restock",
        Amount: (long)(quantity * unitCents), Currency: "USD", TaxAmount: 0,
        OccurredOn: "2026-08-13", Supplier: null, Category: null, PaymentMethod: null,
        Reference: null, Notes: null, LineItems: [WidgetLine(quantity, unitCents)], Import: Pending);

    // The product was imported on an earlier occasion, so the server names it by the local id it was given then.
    private static Dictionary<string, ArgoExternalRef> WidgetRef() => new()
    {
        ["prod_w"] = new ArgoExternalRef("prod_w", "PRD-1", "Widget", null)
    };

    /// <summary>What ArgoApiSyncService.ImportPreviewAsync does locally, without the server claim.</summary>
    private static ArgoApiImportCreation ImportFromApi(CompanyData data, IReadOnlyList<ArgoExpense> purchases, IReadOnlyList<ArgoRevenue> sales)
    {
        var creation = new ArgoApiImportCreation { Pre = data.IdCounters.Clone() };
        new ArgoApiImporter().Import(data, new ArgoApiSyncPreview([], [], [], [], purchases, sales, [], WidgetRef()), creation);
        creation.ApplyStock(data);
        creation.Post = data.IdCounters.Clone();
        return creation;
    }

    /// <summary>
    /// A sale of 3 from 10 in stock, 2 of them opening units, costs only the 1 bought unit at $4.
    /// Undo gives back the stock and the 2 opening units and removes the ledger row; redo takes them
    /// again and records the same cost, not a second removal.
    /// </summary>
    [Fact]
    public void ApiSale_UndoThenRedo_PutsStockOpeningUnitsAndCostBackEachTime()
    {
        var data = Company(inStock: 10, unitCost: 4m, openingUnits: 2);
        var item = data.Inventory.Single();

        var creation = ImportFromApi(data, [], [ApiSale(3, 2000)]);

        var line = Assert.Single(data.Revenues).LineItems.Single();
        Assert.Equal(7m, item.InStock);
        Assert.Equal(0m, item.OpeningUnits);
        Assert.Equal(2m, line.OpeningUnitsUsed);
        Assert.Equal(4m, line.CostOfGoodsUSD);
        var removal = Assert.Single(data.StockAdjustments);
        Assert.Equal(AdjustmentType.Remove, removal.AdjustmentType);
        Assert.Equal(3m, removal.Quantity);
        Assert.Equal(data.Revenues[0].Id, removal.ReferenceNumber);

        creation.Undo(data);

        Assert.Empty(data.Revenues);
        Assert.Equal(10m, item.InStock);
        Assert.Equal(2m, item.OpeningUnits);
        Assert.Empty(data.StockAdjustments);

        creation.Redo(data);

        Assert.Single(data.Revenues);
        Assert.Equal(7m, item.InStock);
        Assert.Equal(0m, item.OpeningUnits);
        Assert.Equal(4m, line.CostOfGoodsUSD);
        Assert.Single(data.StockAdjustments);
    }

    /// <summary>
    /// A purchase and a sale pushed in one batch: the purchase lands first, so the sale is costed at
    /// the $6 just paid. Undo has to restore the $4 unit cost and cost price as well as the stock,
    /// or every later sale is costed at a purchase that is no longer in the books.
    /// </summary>
    [Fact]
    public void ApiPurchaseAndSaleInOneBatch_SaleCostsThePurchasePrice_AndUndoRestoresTheOldCost()
    {
        var data = Company(inStock: 0, unitCost: 4m);
        var item = data.Inventory.Single();
        var product = data.Products.Single();

        var creation = ImportFromApi(data, [ApiPurchase(5, 600)], [ApiSale(3, 2000)]);

        var sale = Assert.Single(data.Revenues).LineItems.Single();
        Assert.True(Assert.Single(data.Expenses).LineItems.Single().IsStockPurchase);
        Assert.Equal(2m, item.InStock);
        Assert.Equal(6m, item.UnitCost);
        Assert.Equal(6m, product.CostPrice);
        Assert.Equal(18m, sale.CostOfGoodsUSD);

        creation.Undo(data);

        Assert.Empty(data.Revenues);
        Assert.Empty(data.Expenses);
        Assert.Equal(0m, item.InStock);
        Assert.Equal(4m, item.UnitCost);
        Assert.Equal(4m, product.CostPrice);
        Assert.Empty(data.StockAdjustments);

        creation.Redo(data);

        Assert.Equal(2m, item.InStock);
        Assert.Equal(6m, item.UnitCost);
        Assert.Equal(18m, sale.CostOfGoodsUSD);
        Assert.Equal(2, data.StockAdjustments.Count);
    }

    /// <summary>
    /// Undoing the import must take back only what the import moved. A sale saved after the import
    /// stays in the books when the import is undone, so its 2 units must stay out of stock: 10 less
    /// the 2 is 8, not the 10 that stood before the import.
    /// </summary>
    [Fact]
    public void ApiImportUndo_KeepsTheStockALaterSaleTookOut()
    {
        var data = Company(inStock: 10, unitCost: 4m);
        var item = data.Inventory.Single();
        var creation = ImportFromApi(data, [], [ApiSale(3, 2000)]);
        var later = new Revenue
        {
            Id = "REV-LATER", Date = new DateTime(2026, 8, 20), OriginalCurrency = "USD",
            Total = 40m, Amount = 40m, PaymentStatus = RevenuePaymentStatus.Paid,
            LineItems = [new LineItem { ProductId = "PRD-1", Quantity = 2, UnitPrice = 20m }]
        };
        InventoryStockService.Apply(data, later.LineItems, later, isPurchase: false);
        data.Revenues.Add(later);
        Assert.Equal(5m, item.InStock);

        creation.Undo(data);

        Assert.Equal(8m, item.InStock);
        Assert.Equal("REV-LATER", Assert.Single(data.StockAdjustments).ReferenceNumber);
    }

    private static StripeChargeDetail WidgetCharge() => new(
        ChargeId: "ch_1",
        CreatedUnix: new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
        GrossCents: 2500, FeeCents: 100, Currency: "usd",
        CustomerName: null, CustomerEmail: null, ProductName: "Widget",
        TaxCents: 0, DiscountCents: 0, AmountRefundedCents: 0);

    /// <summary>
    /// A Stripe charge for a tracked product sells one unit of it at the stock's $4 cost, and its
    /// processing fee, which has no product, moves no stock. Undo puts the unit back and redo takes
    /// it again, with exactly one ledger row each time it is in the books.
    /// </summary>
    [Fact]
    public void StripeSale_UndoThenRedo_KeepsStockAndLedgerInStep()
    {
        var data = Company(inStock: 5, unitCost: 4m);
        var item = data.Inventory.Single();
        var svc = new StripeSyncService(new StripeApiClient(new HttpClient()));

        var creation = svc.ImportPreview(data, new StripeSyncPreview([WidgetCharge()], "ch_1", [], [], null, []));

        var line = Assert.Single(data.Revenues).LineItems.Single();
        Assert.Equal("PRD-1", line.ProductId);
        Assert.Equal(4m, line.CostOfGoodsUSD);
        Assert.Single(data.Expenses);
        Assert.Equal(4m, item.InStock);
        Assert.Single(data.StockAdjustments);

        creation.Undo(data);

        Assert.Empty(data.Revenues);
        Assert.Equal(5m, item.InStock);
        Assert.Empty(data.StockAdjustments);

        creation.Redo(data);

        Assert.Equal(4m, item.InStock);
        Assert.Equal(4m, line.CostOfGoodsUSD);
        Assert.Single(data.StockAdjustments);
    }
}
