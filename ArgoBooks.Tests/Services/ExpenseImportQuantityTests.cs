using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// A sheet with a separate quantity column must import the quantity so the line-item subtotal
/// (Quantity * UnitPrice) reconciles with the stored Total.
/// </summary>
public class ExpenseImportQuantityTests
{
    private static SheetAnalysis ExpenseSheet(bool withQuantity)
    {
        var mappings = new List<ColumnMapping>
        {
            new() { SourceColumn = "Date", TargetColumn = "Date" },
            new() { SourceColumn = "Item", TargetColumn = "Description" },
            new() { SourceColumn = "Unit Cost", TargetColumn = "Unit Price" },
            new() { SourceColumn = "Tax", TargetColumn = "Tax" },
            new() { SourceColumn = "Total", TargetColumn = "Total" },
        };
        if (withQuantity)
            mappings.Insert(2, new ColumnMapping { SourceColumn = "Qty", TargetColumn = "Quantity" });

        return new SheetAnalysis
        {
            SourceSheetName = "Expenses",
            DetectedType = SpreadsheetSheetType.Expenses,
            Tier = ProcessingTier.Tier1_Mapping,
            IsIncluded = true,
            ColumnMappings = mappings
        };
    }

    [Fact]
    public async Task ImportExpenses_WithQuantityColumn_AppliesQuantityAndReconciles()
    {
        var csv = "Date,Item,Qty,Unit Cost,Tax,Total\n" +
                  "2024-01-05,Widget,2,176.87,28.30,382.04\n";
        var path = Path.Combine(Path.GetTempPath(), $"expq_{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(path, csv);
        try
        {
            var analysis = new SpreadsheetAnalysisResult();
            analysis.Sheets.Add(ExpenseSheet(withQuantity: true));

            var data = new CompanyData();
            await new SpreadsheetImportService().ImportCsvWithMappingsAsync(path, data, analysis, new ImportOptions());

            var exp = Assert.Single(data.Expenses);
            Assert.Equal(2m, exp.Quantity);
            Assert.Equal(176.87m, exp.UnitPrice);
            Assert.Equal(353.74m, exp.Amount);       // Quantity * UnitPrice
            Assert.Equal(382.04m, exp.Total);
            // Subtotal + tax reconciles to the stored total (no mismatch warning).
            Assert.Equal(exp.Total, exp.Amount + exp.TaxAmount);

            var li = Assert.Single(exp.LineItems);
            Assert.Equal(2m, li.Quantity);
            Assert.Equal(176.87m, li.UnitPrice);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ImportExpenses_NoQuantityColumn_DefaultsToOne()
    {
        // No Qty column: unit price is the whole pre-tax amount, quantity defaults to 1.
        var csv = "Date,Item,Unit Cost,Tax,Total\n" +
                  "2024-01-05,Widget,100.00,13.00,113.00\n";
        var path = Path.Combine(Path.GetTempPath(), $"expq_{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(path, csv);
        try
        {
            var analysis = new SpreadsheetAnalysisResult();
            analysis.Sheets.Add(ExpenseSheet(withQuantity: false));

            var data = new CompanyData();
            await new SpreadsheetImportService().ImportCsvWithMappingsAsync(path, data, analysis, new ImportOptions());

            var exp = Assert.Single(data.Expenses);
            Assert.Equal(1m, exp.Quantity);
            Assert.Equal(100m, exp.UnitPrice);
            Assert.Equal(100m, exp.Amount);
            Assert.Equal(113m, exp.Total);
        }
        finally { File.Delete(path); }
    }
}
