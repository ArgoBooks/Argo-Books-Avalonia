using System.Text.Json;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ClosedXML.Excel;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Both spreadsheet imports update stock records the same way, and neither may undo a cost still
/// waiting for its rate (docs/Calculations.md §14). A pending cost exports as 0, so the AI import
/// used to put 0 in its place and drop the queued cost, and either import could move a record whose
/// sales were waiting on its cost, which left those sales waiting for good.
/// </summary>
public class InventoryImportPendingCostTests : IDisposable
{
    private readonly List<string> _files = [];

    public InventoryImportPendingCostTests()
    {
        _ = PendingConversionService.Instance ?? new PendingConversionService();
    }

    public void Dispose()
    {
        foreach (var file in _files)
            File.Delete(file);
    }

    /// <summary>A stock record whose cost waits for its rate, with a sale waiting on it.</summary>
    private static (CompanyData Data, InventoryItem Item, PendingConversion Queued, LineItem WaitingLine) PendingStock()
    {
        var data = new CompanyData();
        data.Locations.Add(new Location { Id = "LOC-1", Name = "Shop" });
        data.Locations.Add(new Location { Id = "LOC-2", Name = "Store room" });
        data.Products.Add(new Product { Id = "PRD-1", Name = "Flour", TrackInventory = true });
        data.Products.Add(new Product { Id = "PRD-2", Name = "Sugar", TrackInventory = true });

        var item = new InventoryItem { Id = "INV-1", ProductId = "PRD-1", LocationId = "LOC-1", InStock = 3, IsPendingConversion = true };
        data.Inventory.Add(item);
        var queued = new PendingConversion
        {
            TransactionId = "INV-1", TransactionType = PendingConversionType.InventoryItem,
            OriginalCurrency = "EUR", TransactionDate = new DateTime(2026, 3, 1), Total = 10m
        };
        data.PendingConversions.Add(queued);

        var line = new LineItem { ProductId = "PRD-1", LocationId = "LOC-1", Quantity = 2, IsCostOfGoodsPending = true, CostOfGoodsUSD = 0m };
        data.Revenues.Add(new Revenue { Id = "REV-1", Date = new DateTime(2026, 3, 2), OriginalCurrency = "USD", Total = 40m, LineItems = [line] });
        return (data, item, queued, line);
    }

    private static void AiImport(CompanyData data, string json)
    {
        var chunk = new LlmProcessedData { EntityType = SpreadsheetSheetType.Inventory };
        chunk.Entities.Add(JsonDocument.Parse(json).RootElement.Clone());
        new SpreadsheetImportService().ImportProcessedEntities(data, [chunk], "Inventory");
    }

    [Fact]
    public void AiImport_ZeroUnitCost_KeepsThePendingCostAndItsQueuedEntry()
    {
        var (data, item, queued, _) = PendingStock();

        AiImport(data, """{ "id": "INV-1", "productId": "PRD-1", "locationId": "LOC-1", "inStock": 7, "unitCost": 0 }""");

        Assert.Same(item, Assert.Single(data.Inventory));
        Assert.Equal(7m, item.InStock);
        Assert.True(item.IsPendingConversion);
        Assert.Same(queued, Assert.Single(data.PendingConversions));
    }

    [Fact]
    public void AiImport_ADifferentUnitCost_ReplacesThePendingCost_AndSalesStillGetTheQueuedOne()
    {
        var (data, item, queued, _) = PendingStock();

        AiImport(data, """{ "id": "INV-1", "productId": "PRD-1", "locationId": "LOC-1", "inStock": 3, "unitCost": 12 }""");

        Assert.False(item.IsPendingConversion);
        Assert.Equal(12m, item.UnitCost);
        Assert.Same(queued, Assert.Single(data.PendingConversions));
    }

    [Fact]
    public void AiImport_MovingARecordWithASaleWaitingOnItsCost_LeavesItsProductAndLocation()
    {
        var (data, item, _, line) = PendingStock();

        AiImport(data, """{ "id": "INV-1", "productId": "PRD-2", "locationId": "LOC-2", "inStock": 5, "unitCost": 0 }""");

        Assert.Equal(("PRD-1", "LOC-1"), (item.ProductId, item.LocationId));
        Assert.Equal(5m, item.InStock);

        InventoryStockService.ApplyConvertedCost(data, Assert.Single(data.PendingConversions), 1.25m);
        Assert.False(line.IsCostOfGoodsPending);
        Assert.Equal(25m, line.CostOfGoodsUSD);
    }

    [Fact]
    public async Task SheetImport_MovingARecordWithASaleWaitingOnItsCost_LeavesItsLocation()
    {
        var (data, item, queued, _) = PendingStock();

        await new SpreadsheetImportService().ImportFromExcelAsync(InventorySheet(
            ["ID", "Product ID", "Location ID", "In Stock", "Unit Cost"],
            ["INV-1", "PRD-1", "LOC-2", "6", "0"]), data);

        Assert.Equal("LOC-1", item.LocationId);
        Assert.Equal(6m, item.InStock);
        Assert.True(item.IsPendingConversion);
        Assert.Same(queued, Assert.Single(data.PendingConversions));
    }

    [Fact]
    public void AiImport_MovingARecordWithNothingWaiting_TakesTheNewLocation()
    {
        var (data, item, _, _) = PendingStock();
        data.Revenues.Clear();

        AiImport(data, """{ "id": "INV-1", "productId": "PRD-1", "locationId": "LOC-2", "inStock": 3, "unitCost": 0 }""");

        Assert.Equal("LOC-2", item.LocationId);
    }

    private string InventorySheet(params string[][] cells)
    {
        var path = Path.Combine(Path.GetTempPath(), $"argo-inventory-{Guid.NewGuid():N}.xlsx");
        _files.Add(path);
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Inventory");
        for (var r = 0; r < cells.Length; r++)
            for (var c = 0; c < cells[r].Length; c++)
                sheet.Cell(r + 1, c + 1).Value = cells[r][c];
        workbook.SaveAs(path);
        return path;
    }
}
