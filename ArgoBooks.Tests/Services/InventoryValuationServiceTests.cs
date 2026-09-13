using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class InventoryValuationServiceTests
{
    private static StockAdjustment Adj(
        string itemId, AdjustmentType type, int qty, int prev, int @new,
        DateTime timestamp, string? reference = null) => new()
    {
        InventoryItemId = itemId,
        AdjustmentType = type,
        Quantity = qty,
        PreviousStock = prev,
        NewStock = @new,
        Timestamp = timestamp,
        ReferenceNumber = reference
    };

    [Fact]
    public void SignedDelta_Add_IsPositiveQuantity()
    {
        var a = Adj("I1", AdjustmentType.Add, 10, 0, 10, new DateTime(2024, 1, 1));
        Assert.Equal(10, InventoryValuationService.SignedDelta(a));
    }

    [Fact]
    public void SignedDelta_Remove_IsNegativeQuantity()
    {
        var a = Adj("I1", AdjustmentType.Remove, 4, 10, 6, new DateTime(2024, 1, 1));
        Assert.Equal(-4, InventoryValuationService.SignedDelta(a));
    }

    [Fact]
    public void SignedDelta_Set_IsNewMinusPrevious()
    {
        var a = Adj("I1", AdjustmentType.Set, 0, 10, 25, new DateTime(2024, 1, 1));
        Assert.Equal(15, InventoryValuationService.SignedDelta(a));
    }

    [Fact]
    public void StockOnHandAsOf_RollsBackOnlyAdjustmentsAfterAsOfDate()
    {
        // Baseline 0, +100 on Jan 1, -20 on Jan 10 => current InStock 80.
        var item = new InventoryItem { Id = "I1", InStock = 80 };
        var adjustments = new List<StockAdjustment>
        {
            Adj("I1", AdjustmentType.Add, 100, 0, 100, new DateTime(2024, 1, 1)),
            Adj("I1", AdjustmentType.Remove, 20, 100, 80, new DateTime(2024, 1, 10))
        };
        DateTime Effective(StockAdjustment a) => a.Timestamp;

        // After the purchase, before the sale.
        Assert.Equal(100,
            InventoryValuationService.StockOnHandAsOf(item, adjustments, Effective, new DateTime(2024, 1, 5)));
        // After both movements.
        Assert.Equal(80,
            InventoryValuationService.StockOnHandAsOf(item, adjustments, Effective, new DateTime(2024, 1, 15)));
        // Before any movement (pre-ledger baseline).
        Assert.Equal(0,
            InventoryValuationService.StockOnHandAsOf(item, adjustments, Effective, new DateTime(2023, 12, 31)));
    }

    [Fact]
    public void StockOnHandAsOf_NoAdjustments_ReturnsCurrentInStock()
    {
        var item = new InventoryItem { Id = "I1", InStock = 42 };
        Assert.Equal(42, InventoryValuationService.StockOnHandAsOf(
            item, new List<StockAdjustment>(), a => a.Timestamp, new DateTime(2024, 6, 1)));
    }

    [Fact]
    public void TotalValueAsOf_MultipliesReconstructedQuantityByUnitCost()
    {
        var data = new CompanyData();
        data.Inventory.Add(new InventoryItem { Id = "I1", InStock = 100, UnitCost = 5m });
        data.StockAdjustments.Add(
            Adj("I1", AdjustmentType.Add, 100, 0, 100, new DateTime(2024, 6, 1)));

        // As of after the add: 100 units * $5 = $500.
        Assert.Equal(500m, InventoryValuationService.TotalValueAsOf(data, new DateTime(2024, 12, 31)));
        // As of before the add: 0 units => $0.
        Assert.Equal(0m, InventoryValuationService.TotalValueAsOf(data, new DateTime(2024, 1, 1)));
    }

    [Fact]
    public void TotalValueAsOf_UsesLinkedTransactionDateNotTimestamp()
    {
        var data = new CompanyData();
        data.Inventory.Add(new InventoryItem { Id = "I1", InStock = 50, UnitCost = 2m });
        // Purchase dated Jan 1 but entered (timestamped) Jun 1 (back-dated entry).
        data.Expenses.Add(new Expense { Id = "EXP-1", Date = new DateTime(2024, 1, 1) });
        data.StockAdjustments.Add(
            Adj("I1", AdjustmentType.Add, 50, 0, 50, new DateTime(2024, 6, 1), reference: "EXP-1"));

        // As of Mar 1: by transaction date (Jan 1) the stock already exists => $100.
        // If timestamp (Jun 1) were used, it would roll back to $0.
        Assert.Equal(100m, InventoryValuationService.TotalValueAsOf(data, new DateTime(2024, 3, 1)));
    }

    [Fact]
    public void TotalValueAsOf_NoInventory_ReturnsZero()
    {
        Assert.Equal(0m, InventoryValuationService.TotalValueAsOf(new CompanyData(), new DateTime(2024, 6, 1)));
    }

    // BUG: a stock adjustment made ON the as-of day (with a real time-of-day Timestamp, as every
    // manual/rental adjustment has) must be INCLUDED in an "as of that day" valuation. The as-of date
    // is date-only (midnight), so a 9am adjustment currently counts as "after" the as-of date and is
    // wrongly rolled back. The Balance Sheet "ending today" therefore drops today's stock changes.
    [Fact]
    public void StockOnHandAsOf_SameDayTimestampAdjustment_IsIncludedNotRolledBack()
    {
        // +50 made at 9am today leaves InStock = 150.
        var item = new InventoryItem { Id = "I1", InStock = 150 };
        var adjustments = new List<StockAdjustment>
        {
            Adj("I1", AdjustmentType.Add, 50, 100, 150, new DateTime(2024, 6, 15, 9, 0, 0))
        };
        DateTime Effective(StockAdjustment a) => a.Timestamp;

        // As of 2024-06-15 (the day it happened), the add should count: 150, not 100.
        Assert.Equal(150,
            InventoryValuationService.StockOnHandAsOf(item, adjustments, Effective, new DateTime(2024, 6, 15)));
    }

    [Fact]
    public void TotalValueAsOf_SameDayManualAdjustment_IsIncludedInValue()
    {
        // A manual adjustment (no ReferenceNumber -> effective date is its Timestamp) made at 9am today.
        var data = new CompanyData();
        data.Inventory.Add(new InventoryItem { Id = "I1", InStock = 150, UnitCost = 2m });
        data.StockAdjustments.Add(
            Adj("I1", AdjustmentType.Add, 50, 100, 150, new DateTime(2024, 6, 15, 9, 0, 0)));

        // Balance Sheet ending 2024-06-15 (date-only) should value 150 units * $2 = $300.
        Assert.Equal(300m,
            InventoryValuationService.TotalValueAsOf(data, new DateTime(2024, 6, 15)));
    }

    // Manual, rental and purchase-order adjustments are stamped in UTC, but the report date is a
    // local day, so an adjustment belongs to the local day it was made on. A machine set to UTC has
    // no gap between the two, so this only catches the difference in other time zones.
    [Fact]
    public void TotalValueAsOf_UtcTimestamp_CountsOnTheLocalDayItWasMade()
    {
        var lateOnSep11 = new DateTime(2024, 9, 11, 23, 30, 0, DateTimeKind.Local).ToUniversalTime();
        var earlyOnSep12 = new DateTime(2024, 9, 12, 0, 30, 0, DateTimeKind.Local).ToUniversalTime();
        var data = new CompanyData();
        data.Inventory.Add(new InventoryItem { Id = "I1", InStock = 180, UnitCost = 1m });
        data.StockAdjustments.Add(Adj("I1", AdjustmentType.Add, 50, 100, 150, lateOnSep11));
        data.StockAdjustments.Add(Adj("I1", AdjustmentType.Add, 30, 150, 180, earlyOnSep12));

        Assert.Equal(150m, InventoryValuationService.TotalValueAsOf(data, new DateTime(2024, 9, 11)));
    }
}
