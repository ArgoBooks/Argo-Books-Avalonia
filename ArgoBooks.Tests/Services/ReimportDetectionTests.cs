using System.Text.Json;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Id-less Tier 2 rows become importable via a deterministic natural-key id, and
/// re-importing the same file UPDATES instead of duplicating. The safety invariant is
/// "no silent drops / never collapse two genuinely-distinct rows": two legitimately-identical
/// rows in ONE import are both kept (disambiguated by ordinal); a row that has no usable
/// identifying fields is surfaced as unimported rather than dropped.
/// </summary>
public class ReimportDetectionTests
{
    private static JsonElement Json(string raw) =>
        JsonDocument.Parse(raw).RootElement.Clone();

    private static LlmProcessedData Chunk(SpreadsheetSheetType type, params JsonElement[] entities)
    {
        var chunk = new LlmProcessedData { EntityType = type };
        foreach (var e in entities)
            chunk.Entities.Add(e);
        return chunk;
    }

    [Fact]
    public void TwoIdenticalRowsInOneImport_BothKept()
    {
        var data = new CompanyData();
        var svc = new SpreadsheetImportService();

        // Two genuinely-identical id-less expenses in a SINGLE import. They share a natural key
        // but must NOT be collapsed: the invariant is that distinct source rows survive.
        var e1 = Json("""{ "date": "2026-01-15", "amount": 42.00, "total": 42.00, "description": "Office supplies" }""");
        var e2 = Json("""{ "date": "2026-01-15", "amount": 42.00, "total": 42.00, "description": "Office supplies" }""");

        svc.ImportProcessedEntities(
            data, [Chunk(SpreadsheetSheetType.Expenses, e1, e2)], "Expenses");

        // Both rows kept.
        Assert.Equal(2, data.Expenses.Count);

        // ...with distinct ids (the 2nd got an ordinal suffix).
        var ids = data.Expenses.Select(x => x.Id).ToList();
        Assert.Equal(2, ids.Distinct().Count());
        Assert.All(ids, id => Assert.False(string.IsNullOrEmpty(id)));
    }

    [Fact]
    public void ReimportingSameFile_UpdatesNotDuplicates()
    {
        var data = new CompanyData();
        var svc = new SpreadsheetImportService();

        JsonElement[] Batch() =>
        [
            Json("""{ "date": "2026-02-01", "amount": 10, "total": 10, "description": "A" }"""),
            Json("""{ "date": "2026-02-02", "amount": 20, "total": 20, "description": "B" }"""),
            Json("""{ "date": "2026-02-03", "amount": 30, "total": 30, "description": "C" }"""),
        ];

        var first = svc.ImportProcessedEntities(
            data, [Chunk(SpreadsheetSheetType.Expenses, Batch())], "Expenses");
        Assert.Equal(3, data.Expenses.Count);
        Assert.Equal(3, first.Inserted);
        Assert.DoesNotContain(first.Warnings, w => w.Contains("re-import"));

        // Re-import the SAME batch: deterministic ids reproduce, so the existing merge-by-id
        // path updates the prior three rows instead of adding three more.
        var second = svc.ImportProcessedEntities(
            data, [Chunk(SpreadsheetSheetType.Expenses, Batch())], "Expenses");

        Assert.Equal(3, data.Expenses.Count); // still N, not 2N
        Assert.Equal(3, second.Updated);
        Assert.Contains(second.Warnings, w => w.Contains("re-import"));
    }

    [Fact]
    public void IdLessEntityWithSufficientFields_NowImports()
    {
        var data = new CompanyData();
        var svc = new SpreadsheetImportService();

        // Imports via a deterministic natural-key id.
        var expense = Json("""{ "date": "2026-03-10", "amount": 99.95, "total": 99.95, "description": "Hosting" }""");

        var result = svc.ImportProcessedEntities(
            data, [Chunk(SpreadsheetSheetType.Expenses, expense)], "Expenses");

        var imported = Assert.Single(data.Expenses);
        Assert.False(string.IsNullOrEmpty(imported.Id));
        Assert.StartsWith("EXP-", imported.Id);
        Assert.Equal(1, result.Inserted);
        Assert.Empty(result.UnimportedRows);
    }

    [Fact]
    public void IdLessEntityWithInsufficientFields_RecordedNotSilentlyDropped()
    {
        var data = new CompanyData();
        var svc = new SpreadsheetImportService();

        // No id and no usable identifying fields -> cannot form a natural key. Must be surfaced
        // as unimported with a clear reason, NEVER silently dropped or given an arbitrary id.
        var empty = Json("""{ "notes": "" }""");

        var result = svc.ImportProcessedEntities(
            data, [Chunk(SpreadsheetSheetType.Expenses, empty)], "Expenses");

        Assert.Empty(data.Expenses);
        Assert.Equal(0, result.Inserted);
        var row = Assert.Single(result.UnimportedRows);
        Assert.Contains("insufficient fields", row.Reason);
        Assert.Contains(result.SkipReasons, r => r.Contains("insufficient fields"));
    }
}
