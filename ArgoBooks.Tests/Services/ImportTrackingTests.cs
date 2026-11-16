using System.Text.Json;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class ImportTrackingTests
{
    private static Expense Imported(DateTime createdAt) => new()
    {
        Id = "EXP-1",
        Date = new DateTime(2026, 4, 5),
        Total = 38.20m,
        SupplierId = "SUP-1",
        LineItems = [new LineItem { ProductId = "PRD-1" }],
        ImportSource = ImportTracking.BankPdf,
        CreatedAt = createdAt
    };

    [Theory]
    [InlineData("April.pdf", ImportTracking.BankPdf)]
    [InlineData("statement.JPG", ImportTracking.BankPhoto)]
    [InlineData("export.csv", ImportTracking.BankFile)]
    [InlineData("Bank sheet", ImportTracking.BankFile)]
    public void ForStatementFile_GoesByTheExtension(string fileName, string expected)
    {
        Assert.Equal(expected, ImportTracking.ForStatementFile(fileName));
    }

    [Fact]
    public void SourceOf_ARecordImportedLongAgo_IsNotCounted()
    {
        var now = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(ImportTracking.BankPdf, ImportTracking.SourceOf(Imported(now.AddDays(-29)), now));
        Assert.Null(ImportTracking.SourceOf(Imported(now.AddDays(-31)), now));
    }

    [Fact]
    public void ChangedFields_NamesEachFieldTheUserChanged()
    {
        var expense = Imported(DateTime.UtcNow);
        var before = ImportTracking.Capture(expense, expense.SupplierId);

        expense.Date = new DateTime(2026, 4, 6);
        expense.Total = 40m;
        expense.LineItems = [new LineItem { ProductId = "PRD-2" }];

        Assert.Equal("date,amount,product",
            ImportTracking.ChangedFields(before, ImportTracking.Capture(expense, "SUP-1"), "supplier"));
    }

    [Fact]
    public void ChangedFields_SavingWithoutChangesIsNoCorrection()
    {
        var expense = Imported(DateTime.UtcNow);
        var before = ImportTracking.Capture(expense, expense.SupplierId);

        // The form saves the date with the time it was opened at and rewrites the description.
        expense.Date = expense.Date.AddHours(14);
        expense.Description = "Office Supplies";

        Assert.Equal("", ImportTracking.ChangedFields(before, ImportTracking.Capture(expense, "SUP-1"), "supplier"));
    }

    [Fact]
    public void ImportSource_IsSavedOnlyWhenSet()
    {
        Assert.Contains("\"importSource\":\"bank-pdf\"", JsonSerializer.Serialize(Imported(DateTime.UtcNow)));
        Assert.DoesNotContain("importSource", JsonSerializer.Serialize(new Expense()));
    }
}

public class SpreadsheetImportSourceTests
{
    private static LlmProcessedData Expenses(params string[] entities)
    {
        var chunk = new LlmProcessedData { EntityType = SpreadsheetSheetType.Expenses };
        foreach (var entity in entities)
            chunk.Entities.Add(JsonDocument.Parse(entity).RootElement.Clone());
        return chunk;
    }

    [Fact]
    public void ANewRecordIsMarked_AndAnUpdatedOneKeepsWhatItHad()
    {
        var data = new CompanyData();
        data.Expenses.Add(new Expense { Id = "EXP-OLD", Date = new DateTime(2026, 1, 1), Total = 5m });
        var options = new ImportOptions { ImportSource = ImportTracking.Spreadsheet };

        new SpreadsheetImportService().ImportProcessedEntities(data,
            [Expenses("{\"id\":\"EXP-OLD\",\"date\":\"2026-01-02\",\"total\":6}", "{\"id\":\"EXP-NEW\",\"date\":\"2026-04-05\",\"total\":12}")],
            "Expenses", options);

        Assert.Null(data.Expenses.Single(e => e.Id == "EXP-OLD").ImportSource);
        Assert.Equal(ImportTracking.Spreadsheet, data.Expenses.Single(e => e.Id == "EXP-NEW").ImportSource);
    }
}
