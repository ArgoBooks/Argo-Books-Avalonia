using ArgoBooks.Core.Data;
using ArgoBooks.Core.Services;
using ClosedXML.Excel;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// An id minted during an import must not repeat a number already used by that record type, in the
/// company or on any sheet of the import, whichever order the sheets come in and whatever width
/// their ids are written in.
/// </summary>
public class ImportIdNumberingTests : IDisposable
{
    private static int Year => DateTime.UtcNow.Year;

    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (var file in _files)
            File.Delete(file);
    }

    private string Workbook(params (string Name, string[] Headers, string[][] Rows)[] sheets)
    {
        var path = Path.Combine(Path.GetTempPath(), $"argo-ids-{Guid.NewGuid():N}.xlsx");
        _files.Add(path);

        using var workbook = new XLWorkbook();
        foreach (var (name, headers, rows) in sheets)
        {
            var worksheet = workbook.AddWorksheet(name);
            for (var c = 0; c < headers.Length; c++)
                worksheet.Cell(1, c + 1).Value = headers[c];
            for (var r = 0; r < rows.Length; r++)
                for (var c = 0; c < rows[r].Length; c++)
                    worksheet.Cell(r + 2, c + 1).Value = rows[r][c];
        }
        workbook.SaveAs(path);
        return path;
    }

    private static void AssertNumbersDistinct(IEnumerable<string> ids, string prefix)
    {
        var list = ids.ToList();
        var numbers = list.Select(id => IdGenerator.HighestNumber([id], prefix)).ToList();
        Assert.True(numbers.Distinct().Count() == numbers.Count, $"Repeated numbers among: {string.Join(", ", list)}");
    }

    [Fact]
    public async Task BlankRows_AreNumberedPastLegacyWidthIdsOnTheSameSheet()
    {
        // The blank rows come first, so the sheet's ADJ-001..ADJ-003 have not been added yet when
        // they are given ids.
        var path = Workbook(("Stock Adjustments",
            ["ID", "Inventory Item ID", "Type", "Quantity"],
            [
                ["", "INV-ITM-001", "Add", "1"],
                ["", "INV-ITM-001", "Add", "2"],
                ["ADJ-001", "INV-ITM-001", "Add", "3"],
                ["ADJ-002", "INV-ITM-001", "Add", "4"],
                ["ADJ-003", "INV-ITM-001", "Add", "5"],
            ]));
        var data = new CompanyData();

        await new SpreadsheetImportService().ImportFromExcelAsync(path, data);

        Assert.Equal(5, data.StockAdjustments.Count);
        Assert.Equal(["ADJ-00004", "ADJ-00005"],
            data.StockAdjustments.Where(a => a.Quantity <= 2).Select(a => a.Id).Order().ToList());
        AssertNumbersDistinct(data.StockAdjustments.Select(a => a.Id), "ADJ-");
    }

    [Fact]
    public async Task RentalStockMadeBeforeTheInventorySheet_IsNotTakenOverByIt()
    {
        // The Rental Inventory sheet comes first, so its product's stock row is made before the
        // Inventory sheet's INV-ITM-00001 is read. Numbered from a counter that had not seen it, the
        // new row was INV-ITM-00001 too, and the Inventory row then overwrote it with another product.
        var path = Workbook(
            ("Products", ["ID", "Name"],
            [
                ["PRD-001", "Tent"],
                ["PRD-002", "Lantern"],
            ]),
            ("Rental Inventory", ["ID", "Product ID", "Total Qty", "Daily Rate"],
            [
                ["RNT-ITM-001", "PRD-001", "4", "25"],
            ]),
            ("Inventory", ["ID", "Product ID", "In Stock"],
            [
                ["INV-ITM-00001", "PRD-002", "7"],
            ]));
        var data = new CompanyData();

        await new SpreadsheetImportService().ImportFromExcelAsync(path, data,
            new ImportOptions { AutoCreateMissingReferences = true });

        Assert.Equal(2, data.Inventory.Count);
        var rental = Assert.Single(data.RentalInventory);
        var rentalStock = Assert.Single(data.Inventory, i => i.Id == rental.InventoryItemId);
        Assert.Equal("PRD-001", rentalStock.ProductId);
        Assert.Equal(4, rentalStock.InStock);
        Assert.Equal("INV-ITM-00002", rentalStock.Id);

        var lantern = Assert.Single(data.Inventory, i => i.Id == "INV-ITM-00001");
        Assert.Equal("PRD-002", lantern.ProductId);
        Assert.Equal(7, lantern.InStock);
    }

    [Fact]
    public async Task SampleCompany_GivesEveryStockRowItsOwnNumber()
    {
        var path = SampleInventoryResolutionTests.SampleXlsxPath();
        Assert.True(File.Exists(path), "Sample workbook not found");
        var data = new CompanyData();

        await new SpreadsheetImportService().ImportFromExcelAsync(path, data,
            new ImportOptions { AutoCreateMissingReferences = true });

        AssertNumbersDistinct(data.Inventory.Select(i => i.Id), "INV-ITM-");
        Assert.All(data.RentalInventory.Where(r => !string.IsNullOrEmpty(r.InventoryItemId)),
            r => Assert.Contains(data.Inventory, i => i.Id == r.InventoryItemId));
    }

    [Fact]
    public async Task AnInvoiceMintedForABlankRow_TakesTheCreateScreensNumber()
    {
        var path = Workbook(("Invoices", ["ID", "Invoice #", "Customer ID", "Total"],
        [
            ["", "", "CUS-001", "100"],
        ]));
        var data = new CompanyData();

        await new SpreadsheetImportService().ImportFromExcelAsync(path, data);

        var invoice = Assert.Single(data.Invoices);
        Assert.Equal($"INV-{Year}-00001", invoice.Id);
        Assert.Equal($"#INV-{Year}-00001", invoice.InvoiceNumber);
    }

    [Fact]
    public async Task AnImportedInvoiceNumber_IsNotReusedByTheNextInvoice()
    {
        // Its id is something else, so only the printed number carries the 3.
        var path = Workbook(("Invoices", ["ID", "Invoice #", "Customer ID", "Total"],
        [
            ["legacy-a", $"#INV-{Year}-0003", "CUS-001", "100"],
        ]));
        var data = new CompanyData();

        await new SpreadsheetImportService().ImportFromExcelAsync(path, data);

        Assert.Equal(($"INV-{Year}-00004", $"#INV-{Year}-00004"), new IdGenerator(data).PeekNextInvoice());
    }
}
