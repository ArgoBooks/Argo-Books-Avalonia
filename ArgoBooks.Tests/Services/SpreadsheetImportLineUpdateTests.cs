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
/// An import updating an existing revenue or expense follows one rule for its lines in both the
/// column import and the AI import, and keeps the cost of goods sold its lines carry unless the
/// stock they took changes (docs/Calculations.md §5 and §14).
/// </summary>
public class SpreadsheetImportLineUpdateTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"line-update-{Guid.NewGuid():N}.xlsx");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
        GC.SuppressFinalize(this);
    }

    private static CompanyData WithPaperExpense()
    {
        var data = new CompanyData();
        data.Settings.Localization.Currency = "USD";
        data.Products.Add(new Product { Id = "PRD-1", Name = "Paper" });
        data.Expenses.Add(new Expense
        {
            Id = "PUR-1", Date = new DateTime(2026, 3, 1), Description = "Paper", Quantity = 3, UnitPrice = 10m,
            Amount = 30m, Total = 30m, TotalUSD = 30m, OriginalCurrency = "USD",
            LineItems = [new LineItem { ProductId = "PRD-1", Description = "Paper", Quantity = 3, UnitPrice = 10m, Discount = 1m }]
        });
        return data;
    }

    /// <summary>A tracked product with 10 in stock at 3 each, and a sale of 4 that took them, costed at 12.</summary>
    private static CompanyData WithStockedSale()
    {
        var data = new CompanyData();
        data.Settings.Localization.Currency = "USD";
        data.Locations.Add(new Location { Id = "LOC-1", Name = "Shop" });
        data.Products.Add(new Product { Id = "PRD-1", Name = "Flour", TrackInventory = true });
        data.Inventory.Add(new InventoryItem { Id = "ITM-1", ProductId = "PRD-1", LocationId = "LOC-1", InStock = 10, UnitCost = 3m });
        var sale = new Revenue
        {
            Id = "REV-1", Date = new DateTime(2026, 3, 1), Description = "Flour", Quantity = 4, UnitPrice = 10m,
            Amount = 40m, Total = 40m, TotalUSD = 40m, OriginalCurrency = "USD", PaymentStatus = RevenuePaymentStatus.Paid,
            LineItems = [new LineItem { ProductId = "PRD-1", Description = "Flour", Quantity = 4, UnitPrice = 10m }]
        };
        InventoryStockService.Apply(data, sale.LineItems, sale, isPurchase: false);
        data.Revenues.Add(sale);
        return data;
    }

    private static void ImportAi(CompanyData data, SpreadsheetSheetType type, string row)
    {
        var chunk = new LlmProcessedData { EntityType = type };
        chunk.Entities.Add(JsonDocument.Parse(row).RootElement.Clone());
        new SpreadsheetImportService().ImportProcessedEntities(data, [chunk], type.ToString());
    }

    private async Task ImportSheetAsync(CompanyData data, string sheet, string[] headers, params object[][] rows)
    {
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet(sheet);
            for (var c = 0; c < headers.Length; c++)
                ws.Cell(1, c + 1).Value = headers[c];
            for (var r = 0; r < rows.Length; r++)
                for (var c = 0; c < rows[r].Length; c++)
                    ws.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(rows[r][c]);
            wb.SaveAs(_path);
        }

        await new SpreadsheetImportService().ImportFromExcelAsync(_path, data, new ImportOptions { SkipExistingRecords = false });
    }

    // The AI import copied the old lines over and only rebuilt a line when there was none, so a new
    // quantity and price left the line at 3 x 10 while the expense said 5 x 10.
    [Fact]
    public void AiUpdate_GivingQuantityAndPrice_RebuildsTheLine_KeepingWhatTheRowLeavesOut()
    {
        var data = WithPaperExpense();

        ImportAi(data, SpreadsheetSheetType.Expenses, """{ "id": "PUR-1", "quantity": 5, "unitPrice": 12, "total": 60 }""");

        var line = Assert.Single(Assert.Single(data.Expenses).LineItems);
        Assert.Equal(("PRD-1", 5m, 12m, 1m), (line.ProductId, line.Quantity, line.UnitPrice, line.Discount));
    }

    [Fact]
    public async Task SheetUpdate_GivingQuantityAndPrice_RebuildsTheLineTheSameWay()
    {
        var data = WithPaperExpense();

        await ImportSheetAsync(data, "Expenses", ["ID", "Quantity", "Unit Price", "Total"], ["PUR-1", 5, 12, 60]);

        var line = Assert.Single(Assert.Single(data.Expenses).LineItems);
        Assert.Equal(("PRD-1", 5m, 12m, 1m), (line.ProductId, line.Quantity, line.UnitPrice, line.Discount));
    }

    // A new description created the product "Ink" but left the line pointing at Paper.
    [Fact]
    public void AiUpdate_GivingANewDescription_PointsTheLineAtThatProduct()
    {
        var data = WithPaperExpense();

        ImportAi(data, SpreadsheetSheetType.Expenses, """{ "id": "PUR-1", "description": "Ink" }""");

        var ink = Assert.Single(data.Products, p => p.Name == "Ink");
        var line = Assert.Single(Assert.Single(data.Expenses).LineItems);
        Assert.Equal((ink.Id, "Ink", 3m, 10m), (line.ProductId, line.Description, line.Quantity, line.UnitPrice));
    }

    [Fact]
    public async Task SheetUpdate_GivingANewProduct_PointsTheLineAtThatProduct()
    {
        var data = WithPaperExpense();

        await ImportSheetAsync(data, "Expenses", ["ID", "Product"], ["PUR-1", "Ink"]);

        var ink = Assert.Single(data.Products, p => p.Name == "Ink");
        var line = Assert.Single(Assert.Single(data.Expenses).LineItems);
        Assert.Equal((ink.Id, "Ink", 3m, 10m), (line.ProductId, line.Description, line.Quantity, line.UnitPrice));
    }

    [Fact]
    public void AiUpdate_GivingNoLineField_LeavesTheLineAlone()
    {
        var data = WithPaperExpense();
        var before = data.Expenses[0].LineItems[0];

        ImportAi(data, SpreadsheetSheetType.Expenses, """{ "id": "PUR-1", "notes": "x", "taxAmount": null, "lineItems": [] }""");

        Assert.Same(before, Assert.Single(data.Expenses[0].LineItems));
        Assert.Single(data.Products);
    }

    [Fact]
    public async Task BothImports_GivingAQuantity_KeepARecordWithSeveralLines()
    {
        List<LineItem> ThreeLines() =>
        [
            new() { ProductId = "PRD-1", Description = "Paper", Quantity = 1, UnitPrice = 10m },
            new() { ProductId = "PRD-2", Description = "Pens", Quantity = 1, UnitPrice = 10m },
            new() { ProductId = "PRD-3", Description = "Ink", Quantity = 1, UnitPrice = 10m }
        ];
        var ai = WithPaperExpense();
        ai.Expenses[0].LineItems = ThreeLines();
        var sheet = WithPaperExpense();
        sheet.Expenses[0].LineItems = ThreeLines();

        ImportAi(ai, SpreadsheetSheetType.Expenses, """{ "id": "PUR-1", "quantity": 5 }""");
        await ImportSheetAsync(sheet, "Expenses", ["ID", "Quantity"], ["PUR-1", 5]);

        Assert.Equal(["PRD-1", "PRD-2", "PRD-3"], ai.Expenses[0].LineItems.Select(l => l.ProductId));
        Assert.Equal(["PRD-1", "PRD-2", "PRD-3"], sheet.Expenses[0].LineItems.Select(l => l.ProductId));
    }

    [Fact]
    public void AiUpdate_OfASalesPriceOnly_KeepsItsCostOfGoodsSold()
    {
        var data = WithStockedSale();

        ImportAi(data, SpreadsheetSheetType.Revenue, """{ "id": "REV-1", "unitPrice": 12 }""");

        var line = Assert.Single(Assert.Single(data.Revenues).LineItems);
        Assert.Equal((12m, 12m), (line.UnitPrice, line.CostOfGoodsUSD));
        Assert.Equal(6m, Assert.Single(data.Inventory).InStock);
    }

    // A rebuilt line lost the cost of goods sold of a sale that took stock, so its profit read high.
    [Fact]
    public void AiUpdate_OfASalesQuantity_MovesItsStockAndCostsItAgain()
    {
        var data = WithStockedSale();

        ImportAi(data, SpreadsheetSheetType.Revenue, """{ "id": "REV-1", "quantity": 5 }""");

        var line = Assert.Single(Assert.Single(data.Revenues).LineItems);
        Assert.Equal((5m, 15m), (line.Quantity, line.CostOfGoodsUSD));
        Assert.Equal(5m, Assert.Single(data.Inventory).InStock);
    }

    [Fact]
    public async Task SheetUpdate_OfASalesQuantity_MovesItsStockAndCostsItAgain()
    {
        var data = WithStockedSale();

        await ImportSheetAsync(data, "Revenue", ["ID", "Quantity"], ["REV-1", 5]);

        var line = Assert.Single(Assert.Single(data.Revenues).LineItems);
        Assert.Equal((5m, 15m), (line.Quantity, line.CostOfGoodsUSD));
        Assert.Equal(5m, Assert.Single(data.Inventory).InStock);
    }

    private static void AssertSaleKeepsItsStock(CompanyData data)
    {
        var line = Assert.Single(Assert.Single(data.Revenues).LineItems);
        Assert.Equal(("PRD-1", 12m), (line.ProductId, line.CostOfGoodsUSD));
        Assert.Equal(6m, Assert.Single(data.Inventory).InStock);
        Assert.DoesNotContain(data.Products, p => p.Id != "PRD-1" && p.Name.Equals("Flour", StringComparison.OrdinalIgnoreCase));
    }

    // The export's Product column is the sale's description, so re-importing it after the product
    // was renamed looked "Flour" up by name, created a new product, and gave the sale's stock back.
    [Fact]
    public async Task SheetUpdate_RepeatingTheDescriptionOfARenamedProduct_KeepsTheSalesProductAndStock()
    {
        var data = WithStockedSale();
        data.Products[0].Name = "Flour 5kg";

        await ImportSheetAsync(data, "Revenue", ["ID", "Date", "Product", "Quantity", "Unit Price", "Tax", "Total"],
            ["REV-1", new DateTime(2026, 3, 1), "Flour", 4, 10, 0, 40]);

        AssertSaleKeepsItsStock(data);
        Assert.Single(data.Products);
    }

    [Theory]
    [InlineData("Flour")]
    [InlineData(" flour 5KG ")]
    public void AiUpdate_RepeatingTheDescriptionOrProductNameOfARenamedProduct_KeepsTheSalesProductAndStock(string text)
    {
        var data = WithStockedSale();
        data.Products[0].Name = "Flour 5kg";

        ImportAi(data, SpreadsheetSheetType.Revenue, $$"""{ "id": "REV-1", "description": "{{text}}", "notes": "x" }""");

        AssertSaleKeepsItsStock(data);
        Assert.Single(data.Products);
    }

    // Another "Flour" on the sales side of the books won the name lookup over the sale's own product.
    [Fact]
    public async Task SheetUpdate_RepeatingANameTwoProductsShare_KeepsTheSalesOwnProduct()
    {
        var data = WithStockedSale();
        data.Categories.Add(new Category { Id = "CAT-E", Name = "Supplies", Type = CategoryType.Expense });
        data.Categories.Add(new Category { Id = "CAT-R", Name = "Baking", Type = CategoryType.Revenue });
        data.Products[0].CategoryId = "CAT-E";
        data.Products.Add(new Product { Id = "PRD-2", Name = "Flour", CategoryId = "CAT-R" });

        await ImportSheetAsync(data, "Revenue", ["ID", "Date", "Product", "Quantity", "Unit Price", "Tax", "Total"],
            ["REV-1", new DateTime(2026, 3, 1), "Flour", 4, 10, 0, 40]);

        var line = Assert.Single(Assert.Single(data.Revenues).LineItems);
        Assert.Equal(("PRD-1", 12m), (line.ProductId, line.CostOfGoodsUSD));
        Assert.Equal(6m, Assert.Single(data.Inventory).InStock);
    }

    // A new revenue took its invoice's lines through the edit path, which gave back the stock an
    // adjustment already naming its id had taken.
    [Fact]
    public async Task SheetImport_NewRevenueTakingItsInvoicesLines_MovesNoStock_EvenWhenAnAdjustmentNamesItsId()
    {
        var data = new CompanyData();
        data.Settings.Localization.Currency = "USD";
        data.Locations.Add(new Location { Id = "LOC-1", Name = "Shop" });
        data.Products.Add(new Product { Id = "PRD-1", Name = "Flour", TrackInventory = true });
        data.Inventory.Add(new InventoryItem { Id = "ITM-1", ProductId = "PRD-1", LocationId = "LOC-1", InStock = 6, UnitCost = 3m, OpeningUnits = 6 });
        data.StockAdjustments.Add(new StockAdjustment
        {
            Id = "ADJ-1", InventoryItemId = "ITM-1", AdjustmentType = AdjustmentType.Remove, Quantity = 4,
            PreviousStock = 10, NewStock = 6, ReferenceNumber = "REV-1", IsAutoGenerated = true
        });
        data.Invoices.Add(new Invoice
        {
            Id = "INV-1", InvoiceNumber = "INV-1", CustomerId = "CUS-1", IssueDate = new DateTime(2026, 3, 1), Total = 30, TotalUSD = 30,
            AmountPaid = 30, Status = InvoiceStatus.Paid, OriginalCurrency = "USD",
            LineItems = [new LineItem { ProductId = "PRD-1", Description = "Flour", Quantity = 3, UnitPrice = 10 }]
        });

        await ImportSheetAsync(data, "Revenue", ["ID", "Date", "Invoice ID", "Product", "Quantity", "Unit Price", "Tax", "Total"],
            ["REV-1", new DateTime(2026, 3, 1), "INV-1", "Flour", 3, 10, 0, 30]);

        var line = Assert.Single(data.Revenues.Single(r => r.Id == "REV-1").LineItems);
        Assert.Equal((3m, null), (line.Quantity, line.CostOfGoodsUSD));
        var item = Assert.Single(data.Inventory);
        Assert.Equal((6m, 6m), (item.InStock, item.OpeningUnits));
        Assert.Single(data.StockAdjustments);
    }

    // The export writes a multi-line record's total quantity and average unit price, and the import
    // made their product its pre-tax amount: 1 x 100 + 10 x 1 came back as 11 x 50.5 = 555.5.
    [Fact]
    public async Task ExportThenReimport_OfMultiLineRecords_KeepsTheirPreTaxAmount()
    {
        List<LineItem> TwoLines() =>
        [
            new() { Description = "A", Quantity = 1, UnitPrice = 100m },
            new() { Description = "B", Quantity = 10, UnitPrice = 1m }
        ];
        var data = new CompanyData();
        data.Settings.Localization.Currency = "USD";
        data.Revenues.Add(new Revenue
        {
            Id = "REV-1", Date = new DateTime(2026, 3, 1), Description = "A, B", Quantity = 11, UnitPrice = 50.5m,
            Amount = 110m, Total = 110m, TotalUSD = 110m, OriginalCurrency = "USD", LineItems = TwoLines()
        });
        data.Expenses.Add(new Expense
        {
            Id = "PUR-1", Date = new DateTime(2026, 3, 1), Description = "A, B", Quantity = 11, UnitPrice = 50.5m,
            Amount = 110m, Total = 110m, TotalUSD = 110m, OriginalCurrency = "USD", LineItems = TwoLines()
        });

        await new SpreadsheetExportService().ExportToExcelAsync(_path, data, ["Revenue", "Expenses"], null, null);
        await new SpreadsheetImportService().ImportFromExcelAsync(_path, data, new ImportOptions());

        Transaction[] records = [Assert.Single(data.Revenues), Assert.Single(data.Expenses)];
        Assert.All(records, r => Assert.Equal((110m, 110m, 2), (r.Amount, r.Total, r.LineItems.Count)));
    }

    [Fact]
    public void AiUpdate_GivingAMultiLineRecordsQuantity_TakesItsPreTaxAmountFromItsLines()
    {
        var data = WithPaperExpense();
        data.Expenses[0].LineItems =
        [
            new() { Description = "A", Quantity = 1, UnitPrice = 100m },
            new() { Description = "B", Quantity = 10, UnitPrice = 1m }
        ];

        ImportAi(data, SpreadsheetSheetType.Expenses, """{ "id": "PUR-1", "quantity": 11, "unitPrice": 50.5, "total": 110 }""");

        Assert.Equal(110m, Assert.Single(data.Expenses).Amount);
    }

    private static CompanyData WithSaleNamingAnInvoice()
    {
        var data = new CompanyData();
        data.Settings.Localization.Currency = "USD";
        data.Invoices.Add(new Invoice
        {
            Id = "INV-1", InvoiceNumber = "#INV-1", CustomerId = "CUS-1", IssueDate = new DateTime(2026, 3, 1), Total = 20,
            AmountPaid = 20, Status = InvoiceStatus.Paid, OriginalCurrency = "USD",
            LineItems = [new LineItem { ProductId = "PRD-1", Description = "Widget", Quantity = 2, UnitPrice = 10 }]
        });
        data.Revenues.Add(new Revenue
        {
            Id = "REV-1", InvoiceId = "INV-1", Date = new DateTime(2026, 3, 1), Total = 20, TotalUSD = 20, Amount = 20,
            OriginalCurrency = "USD", Notes = "old", ReferenceNumber = "R1",
            LineItems = [new LineItem { ProductId = "PRD-1", Description = "Widget", Quantity = 2, UnitPrice = 10, CostOfGoodsUSD = 8m }]
        });
        return data;
    }

    // Every update of a sale naming an invoice copied the invoice's lines over its own, dropping the
    // cost of goods sold they carried, so its profit read high.
    [Fact]
    public void AiUpdate_OfASaleNamingAnInvoice_KeepsItsLinesAndTheirCost()
    {
        var data = WithSaleNamingAnInvoice();

        ImportAi(data, SpreadsheetSheetType.Revenue, """{ "id": "REV-1", "notes": "new" }""");

        var revenue = Assert.Single(data.Revenues);
        Assert.Equal("new", revenue.Notes);
        Assert.Equal(8m, Assert.Single(revenue.LineItems).CostOfGoodsUSD);
    }

    [Fact]
    public async Task SheetUpdate_OfASaleNamingAnInvoice_KeepsItsLinesAndTheirCost()
    {
        var data = WithSaleNamingAnInvoice();

        await ImportSheetAsync(data, "Revenue", ["ID", "Reference", "Invoice ID"], ["REV-1", "R2", "INV-1"]);

        var revenue = Assert.Single(data.Revenues);
        Assert.Equal("R2", revenue.ReferenceNumber);
        Assert.Equal(8m, Assert.Single(revenue.LineItems).CostOfGoodsUSD);
    }

    [Fact]
    public void AiUpdate_MovingASaleToAnotherInvoice_TakesItsLines_AndCostsTheStockTheyTake()
    {
        var data = WithStockedSale();
        data.Invoices.Add(new Invoice
        {
            Id = "INV-2", InvoiceNumber = "#INV-2", CustomerId = "CUS-1", IssueDate = new DateTime(2026, 3, 1), Total = 20,
            OriginalCurrency = "USD",
            LineItems = [new LineItem { ProductId = "PRD-1", Description = "Flour", Quantity = 2, UnitPrice = 10 }]
        });

        ImportAi(data, SpreadsheetSheetType.Revenue, """{ "id": "REV-1", "invoiceId": "INV-2" }""");

        var line = Assert.Single(Assert.Single(data.Revenues).LineItems);
        Assert.Equal((2m, 6m), (line.Quantity, line.CostOfGoodsUSD));
        Assert.Equal(8m, Assert.Single(data.Inventory).InStock);
    }

    private static CompanyData WithCustomer()
    {
        var data = new CompanyData();
        data.Customers.Add(new Customer
        {
            Id = "CUS-001", Name = "Acme",
            Address = new Address { Street = "1 Main St", City = "Old", Country = "CA" }
        });
        return data;
    }

    // A row giving only the city replaced the whole address, wiping the street and country.
    [Fact]
    public void AiUpdate_GivingPartOfAnAddress_KeepsTheRest()
    {
        var data = WithCustomer();

        ImportAi(data, SpreadsheetSheetType.Customers, """{ "id": "CUS-001", "address": { "city": "New", "street": "" } }""");

        var address = Assert.Single(data.Customers).Address;
        Assert.Equal(("1 Main St", "New", "CA"), (address.Street, address.City, address.Country));
    }

    [Fact]
    public void AiUpdate_GivingABlankAddress_LeavesItAlone()
    {
        var data = WithCustomer();

        ImportAi(data, SpreadsheetSheetType.Customers, """{ "id": "CUS-001", "address": { "city": "", "country": null } }""");

        var address = Assert.Single(data.Customers).Address;
        Assert.Equal(("1 Main St", "Old", "CA"), (address.Street, address.City, address.Country));
    }

    [Fact]
    public async Task SheetUpdate_GivingPartOfAnAddress_KeepsTheRest()
    {
        var data = WithCustomer();

        await ImportSheetAsync(data, "Customers", ["ID", "City"], ["CUS-001", "New"]);

        var address = Assert.Single(data.Customers).Address;
        Assert.Equal(("1 Main St", "New", "CA"), (address.Street, address.City, address.Country));
    }

    // The AI import named a product with no category after itself, so the categorizing step that
    // runs after the import never saw it.
    [Fact]
    public async Task AiImport_ProductWithNoCategory_IsLeftForTheCategorizingStep()
    {
        var data = new CompanyData();
        var service = new SpreadsheetImportService();
        var chunk = new LlmProcessedData { EntityType = SpreadsheetSheetType.Products };
        chunk.Entities.Add(JsonDocument.Parse("""{ "id": "PRD-9", "name": "Drill" }""").RootElement.Clone());

        service.ImportProcessedEntities(data, [chunk], "Products");
        Assert.Null(Assert.Single(data.Products).CategoryId);

        await service.AiCategorizeMissingProductsAsync(data, CancellationToken.None);
        Assert.Equal("Drill", data.GetCategory(Assert.Single(data.Products).CategoryId!)!.Name);
    }
}
