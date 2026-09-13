using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// An id minted for a blank-ID row must not be one the company already has or one written on
/// another row of the same sheet. The counter only moves when the importer mints, so a sheet
/// with "CUS-001" on one row and a blank ID on another handed the blank row CUS-001 too: it was
/// then skipped as already existing, or it overwrote the other customer.
/// </summary>
public class ImportBlankIdCollisionTests
{
    private static async Task ImportAsync(CompanyData data, string csv, SpreadsheetSheetType type, bool skipExisting)
    {
        var path = Path.Combine(Path.GetTempPath(), $"imp_{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(path, csv);
        try
        {
            var analysis = new SpreadsheetAnalysisResult();
            var sheet = new SheetAnalysis
            {
                SourceSheetName = type.ToString(),
                DetectedType = type,
                Tier = ProcessingTier.Tier1_Mapping,
                IsIncluded = true
            };
            foreach (var column in csv[..csv.IndexOf('\n')].Split(','))
                sheet.ColumnMappings.Add(new ColumnMapping { SourceColumn = column, TargetColumn = column });
            analysis.Sheets.Add(sheet);

            await new SpreadsheetImportService().ImportCsvWithMappingsAsync(
                path, data, analysis, new ImportOptions { SkipExistingRecords = skipExisting });
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Csv(string header, bool blankFirst, string explicitRow, string blankRow) =>
        blankFirst
            ? $"{header}\n{blankRow}\n{explicitRow}\n"
            : $"{header}\n{explicitRow}\n{blankRow}\n";

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task Customers_BlankIdNextToAnExplicitId_ImportsBothUntouched(bool blankFirst, bool skipExisting)
    {
        var data = new CompanyData();
        var csv = Csv("ID,Name,Email", blankFirst, "CUS-001,Acme Corp,acme@x.com", ",Globex,globex@x.com");

        await ImportAsync(data, csv, SpreadsheetSheetType.Customers, skipExisting);

        Assert.Equal(2, data.Customers.Count);
        var acme = Assert.Single(data.Customers, c => c.Id == "CUS-001");
        Assert.Equal("Acme Corp", acme.Name);
        Assert.Equal("acme@x.com", acme.Email);
        var globex = Assert.Single(data.Customers, c => c.Name == "Globex");
        Assert.NotEqual("CUS-001", globex.Id);
        Assert.Equal("globex@x.com", globex.Email);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Customers_BlankId_DoesNotTakeAnIdTheCompanyAlreadyHas(bool skipExisting)
    {
        // Typed by hand, so the counter never moved past it.
        var data = new CompanyData();
        data.Customers.Add(new Customer { Id = "CUS-001", Name = "Acme Corp", Email = "acme@x.com" });

        await ImportAsync(data, "ID,Name,Email\n,Globex,globex@x.com\n", SpreadsheetSheetType.Customers, skipExisting);

        Assert.Equal(2, data.Customers.Count);
        var acme = Assert.Single(data.Customers, c => c.Id == "CUS-001");
        Assert.Equal("Acme Corp", acme.Name);
        Assert.NotEqual("CUS-001", Assert.Single(data.Customers, c => c.Name == "Globex").Id);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task Expenses_BlankIdNextToAnExplicitId_ImportsBothUntouched(bool blankFirst, bool skipExisting)
    {
        var data = new CompanyData();
        var explicitId = $"PUR-{DateTime.UtcNow:yyyy}-00001";
        var csv = Csv("ID,Date,Product,Total", blankFirst,
            $"{explicitId},2026-01-05,Paper,10", ",2026-01-06,Ink,20");

        await ImportAsync(data, csv, SpreadsheetSheetType.Expenses, skipExisting);

        Assert.Equal(2, data.Expenses.Count);
        var paper = Assert.Single(data.Expenses, e => e.Id == explicitId);
        Assert.Equal("Paper", paper.Description);
        Assert.Equal(10m, paper.Total);
        var ink = Assert.Single(data.Expenses, e => e.Description == "Ink");
        Assert.NotEqual(explicitId, ink.Id);
        Assert.Equal(20m, ink.Total);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Invoices_BlankIdAndNumber_DoNotTakeANumberOnAnotherRow(bool blankFirst)
    {
        // A row with only an invoice number is identified by that number.
        var data = new CompanyData();
        var csv = Csv("ID,Invoice #,Customer ID,Issue Date,Total", blankFirst,
            ",INV-001,CUS-001,2026-01-05,100", ",,CUS-002,2026-01-06,200");

        await ImportAsync(data, csv, SpreadsheetSheetType.Invoices, skipExisting: true);

        Assert.Equal(2, data.Invoices.Count);
        var first = Assert.Single(data.Invoices, i => i.Id == "INV-001");
        Assert.Equal("CUS-001", first.CustomerId);
        Assert.Equal(100m, first.Total);
        var second = Assert.Single(data.Invoices, i => i.CustomerId == "CUS-002");
        Assert.NotEqual("INV-001", second.Id);
        Assert.NotEqual("INV-001", second.InvoiceNumber);
    }

    [Theory]
    [InlineData(SpreadsheetSheetType.Revenue)]
    [InlineData(SpreadsheetSheetType.Expenses)]
    public async Task AProductMadeForAnUnknownName_DoesNotTakeAnIdTheCompanyAlreadyHas(SpreadsheetSheetType type)
    {
        // Brought in by an earlier import, which does not move the product counter.
        var data = new CompanyData();
        data.Products.Add(new Product { Id = "PRD-IMP-001", Name = "Widget" });

        await ImportAsync(data, "ID,Date,Product,Total\n,2026-01-05,Gadget,40\n", type, skipExisting: false);

        Assert.Equal(2, data.Products.Count);
        Assert.Equal("Widget", Assert.Single(data.Products, p => p.Id == "PRD-IMP-001").Name);
        Assert.NotEqual("PRD-IMP-001", Assert.Single(data.Products, p => p.Name == "Gadget").Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Employees_BlankIdBeforeAnExplicitId_ImportsBothUntouched(bool skipExisting)
    {
        var data = new CompanyData();

        await ImportAsync(data, "ID,Name\n,Jane Doe\nEMP-001,John Smith\n", SpreadsheetSheetType.Employees, skipExisting);

        Assert.Equal(2, data.Employees.Count);
        Assert.Equal("John Smith", Assert.Single(data.Employees, e => e.Id == "EMP-001").Name);
        Assert.NotEqual("EMP-001", Assert.Single(data.Employees, e => e.Name == "Jane Doe").Id);
    }
}
