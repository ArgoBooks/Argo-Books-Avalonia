using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// An expenses sheet with no ID column must still import every distinct row.
/// </summary>
public class ExpenseImportIdGenerationTests
{
    [Fact]
    public async Task ImportExpenses_NoIdColumn_ImportsEveryRow_AndSkipsSummaryRows()
    {
        // Three real expenses plus a "Subtotal" summary row, and no ID column.
        var csv = "Date,Vendor,Amount\n" +
                  "2024-01-05,Staples,142.50\n" +
                  "2024-01-18,Verizon,210\n" +
                  "Subtotal,,693.25\n" +
                  "2024-02-20,Amazon,88.20\n";
        var path = Path.Combine(Path.GetTempPath(), $"exp_{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(path, csv);
        try
        {
            var analysis = new SpreadsheetAnalysisResult();
            analysis.Sheets.Add(new SheetAnalysis
            {
                SourceSheetName = "Expenses",
                DetectedType = SpreadsheetSheetType.Expenses,
                Tier = ProcessingTier.Tier1_Mapping,
                IsIncluded = true,
                ColumnMappings =
                [
                    new ColumnMapping { SourceColumn = "Date", TargetColumn = "Date" },
                    new ColumnMapping { SourceColumn = "Vendor", TargetColumn = "Description" },
                    new ColumnMapping { SourceColumn = "Amount", TargetColumn = "Total" },
                ]
            });

            var data = new CompanyData();
            var svc = new SpreadsheetImportService();
            // SkipExistingRecords ON reproduces the original report (blank IDs collapsed to one).
            await svc.ImportCsvWithMappingsAsync(
                path, data, analysis, new ImportOptions { SkipExistingRecords = true });

            // All three real expenses import; the summary row is dropped, not imported as junk.
            Assert.Equal(3, data.Expenses.Count);
            Assert.DoesNotContain(data.Expenses, e => e.Total == 693.25m);
            // Each gets a unique, non-empty id so they aren't deduped against each other.
            Assert.All(data.Expenses, e => Assert.False(string.IsNullOrWhiteSpace(e.Id)));
            Assert.Equal(3, data.Expenses.Select(e => e.Id).Distinct().Count());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
