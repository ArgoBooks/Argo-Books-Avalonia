using System.Text.Json;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class ImportRescueTests
{
    private static RescueSheetResult Extracted(string sheet, int entities)
    {
        var data = new LlmProcessedData { EntityType = SpreadsheetSheetType.Expenses };
        for (int i = 0; i < entities; i++)
            data.Entities.Add(JsonDocument.Parse("{}").RootElement.Clone());
        return new RescueSheetResult { SheetName = sheet, ProcessedData = { data } };
    }

    private static RescueSheetResult Rejected(string sheet, ImportRescueRejectionReason reason)
        => new() { SheetName = sheet, Reason = reason };

    [Fact]
    public void Aggregate_AnyExtracted_OutcomeIsExtracted()
    {
        var result = ImportRescueResult.Aggregate([
            Rejected("A", ImportRescueRejectionReason.SummaryOrReport),
            Extracted("B", 3),
        ]);

        Assert.Equal(ImportRescueOutcome.Extracted, result.Outcome);
        Assert.Single(result.Extractions);
        Assert.Equal("B", result.Extractions[0].SheetName);
    }

    [Fact]
    public void Aggregate_AllRejected_UsesMostCommonReason()
    {
        var result = ImportRescueResult.Aggregate([
            Rejected("A", ImportRescueRejectionReason.SummaryOrReport),
            Rejected("B", ImportRescueRejectionReason.SummaryOrReport),
            Rejected("C", ImportRescueRejectionReason.NotArgoData),
        ]);

        Assert.Equal(ImportRescueOutcome.Rejected, result.Outcome);
        Assert.Equal(ImportRescueRejectionReason.SummaryOrReport, result.ReasonCode);
    }

    [Fact]
    public void Aggregate_ReasonTie_ResolvesToUnsupportedStructure()
    {
        var result = ImportRescueResult.Aggregate([
            Rejected("A", ImportRescueRejectionReason.SummaryOrReport),
            Rejected("B", ImportRescueRejectionReason.NotArgoData),
        ]);

        Assert.Equal(ImportRescueRejectionReason.UnsupportedStructure, result.ReasonCode);
    }

    [Fact]
    public void Aggregate_NoSheets_IsEmptyOrUnreadable()
    {
        var result = ImportRescueResult.Aggregate([]);

        Assert.Equal(ImportRescueOutcome.Rejected, result.Outcome);
        Assert.Equal(ImportRescueRejectionReason.EmptyOrUnreadable, result.ReasonCode);
    }

    [Fact]
    public void Aggregate_ClassifiedButEmpty_CountsAsUnsupportedStructure()
    {
        // A sheet that classified as a type but yielded zero entities carries Reason=UnsupportedStructure
        // (set by RescueAsync); confirm the fold treats it as a rejection, not an extraction.
        var result = ImportRescueResult.Aggregate([
            Rejected("A", ImportRescueRejectionReason.UnsupportedStructure),
        ]);

        Assert.Equal(ImportRescueOutcome.Rejected, result.Outcome);
        Assert.Equal(ImportRescueRejectionReason.UnsupportedStructure, result.ReasonCode);
    }

    private static RescueClassification InvokeParse(string response)
    {
        var method = typeof(SpreadsheetAnalysisService).GetMethod(
            "ParseRescueClassification",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (RescueClassification)method.Invoke(null, [response])!;
    }

    [Fact]
    public void ParseRescueClassification_Extract_ReturnsType()
    {
        var r = InvokeParse("""{"action":"extract","entityType":"Expenses"}""");
        Assert.Equal(SpreadsheetSheetType.Expenses, r.EntityType);
        Assert.Null(r.Reason);
    }

    [Fact]
    public void ParseRescueClassification_ExtractUnknownType_FallsBackToUnsupported()
    {
        var r = InvokeParse("""{"action":"extract","entityType":"Unknown"}""");
        Assert.Null(r.EntityType);
        Assert.Equal(ImportRescueRejectionReason.UnsupportedStructure, r.Reason);
    }

    [Fact]
    public void ParseRescueClassification_Reject_ReturnsReason()
    {
        var r = InvokeParse("""{"action":"reject","reason":"SummaryOrReport"}""");
        Assert.Null(r.EntityType);
        Assert.Equal(ImportRescueRejectionReason.SummaryOrReport, r.Reason);
    }

    [Fact]
    public void ParseRescueClassification_UnknownReason_FallsBackToUnsupported()
    {
        var r = InvokeParse("""{"action":"reject","reason":"WeirdMadeUpCode"}""");
        Assert.Equal(ImportRescueRejectionReason.UnsupportedStructure, r.Reason);
    }

    [Fact]
    public void ParseRescueClassification_Malformed_FallsBackToUnsupported()
    {
        var r = InvokeParse("this is not json");
        Assert.Null(r.EntityType);
        Assert.Equal(ImportRescueRejectionReason.UnsupportedStructure, r.Reason);
    }

    [Fact]
    public void ParseRescueClassification_Mixed_SetsMixedFlag()
    {
        var r = InvokeParse("""{"action":"mixed"}""");
        Assert.True(r.IsMixedIncomeExpense);
        Assert.Null(r.EntityType);
        Assert.Null(r.Reason);
    }

    [Fact]
    public void ParseRescueClassification_Extract_IsNotMixed()
    {
        var r = InvokeParse("""{"action":"extract","entityType":"Expenses"}""");
        Assert.False(r.IsMixedIncomeExpense);
        Assert.Equal(SpreadsheetSheetType.Expenses, r.EntityType);
    }

    // Mock that returns different payloads for the classify call vs the Tier-2 extraction call,
    // distinguished by a stable marker in the rescue classify system prompt.
    private sealed class BranchingMockGeminiService : IGeminiService
    {
        private readonly string _classifyJson;
        private readonly string _entitiesJson;
        public BranchingMockGeminiService(string classifyJson, string entitiesJson)
        {
            _classifyJson = classifyJson;
            _entitiesJson = entitiesJson;
        }

        public bool IsConfigured => true;

        public Task<string?> SendChatAsync(
            string systemPrompt, string userPrompt, int maxTokens = 4000, double temperature = 0.1,
            CancellationToken cancellationToken = default,
            OperationKind operation = OperationKind.Completion, long? sizeFeature = null)
        {
            var isClassify = systemPrompt.Contains("Decide ONE", StringComparison.Ordinal);
            return Task.FromResult<string?>(isClassify ? _classifyJson : _entitiesJson);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ArgoBooks.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }

    [Fact]
    public async Task RescueAsync_SummaryReport_RejectsWithReason()
    {
        var mock = new BranchingMockGeminiService(
            classifyJson: """{"action":"reject","reason":"SummaryOrReport"}""",
            entitiesJson: "[]");
        var service = new SpreadsheetAnalysisService(mock);
        var path = Path.Combine(RepoRoot(), "TestData", "MainImporter", "quickbooks_profit_and_loss.xlsx");
        Assert.True(File.Exists(path), $"fixture missing: {path}");

        var result = await service.RescueAsync(path, isCsv: false);

        Assert.Equal(ImportRescueOutcome.Rejected, result.Outcome);
        Assert.Equal(ImportRescueRejectionReason.SummaryOrReport, result.ReasonCode);
        Assert.Empty(result.Extractions);
    }

    [Fact]
    public async Task RescueAsync_TransactionRows_ExtractsEntities()
    {
        var mock = new BranchingMockGeminiService(
            classifyJson: """{"action":"extract","entityType":"Expenses"}""",
            entitiesJson: """[{"id":"E1","date":"2026-01-05","description":"Fuel","total":50.0}]""");
        var service = new SpreadsheetAnalysisService(mock);

        var csv = Path.Combine(Path.GetTempPath(), $"rescue_{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(csv, "Date,Vendor,Amount\n2026-01-05,Shell,50.00\n");
        try
        {
            var result = await service.RescueAsync(csv, isCsv: true);

            Assert.Equal(ImportRescueOutcome.Extracted, result.Outcome);
            Assert.Single(result.Extractions);
            Assert.True(result.Extractions[0].ProcessedData.Sum(d => d.Entities.Count) >= 1);
        }
        finally
        {
            File.Delete(csv);
        }
    }

    [Fact]
    public async Task RescueAsync_TooLarge_RejectsWithoutTrying()
    {
        // Over the hard cap: reject with TooLarge and make no AI extraction attempt.
        var mock = new BranchingMockGeminiService(
            classifyJson: """{"action":"extract","entityType":"Expenses"}""",
            entitiesJson: """[{"id":"E1"}]""");
        var service = new SpreadsheetAnalysisService(mock);

        var csv = Path.Combine(Path.GetTempPath(), $"rescue_big_{Guid.NewGuid():N}.csv");
        var sb = new System.Text.StringBuilder("Date,Vendor,Amount\n");
        for (int i = 0; i <= SpreadsheetAnalysisService.RescueMaxTotalRows; i++)
            sb.Append("2026-01-05,Shell,1.00\n");
        await File.WriteAllTextAsync(csv, sb.ToString());
        try
        {
            var result = await service.RescueAsync(csv, isCsv: true);

            Assert.Equal(ImportRescueOutcome.Rejected, result.Outcome);
            Assert.Equal(ImportRescueRejectionReason.TooLarge, result.ReasonCode);
        }
        finally
        {
            File.Delete(csv);
        }
    }

    [Theory]
    [InlineData(5, 100)]    // 2500/5 = 500, clamped to 100
    [InlineData(50, 50)]    // 2500/50 = 50
    [InlineData(200, 20)]   // 2500/200 = 12, clamped up to 20
    [InlineData(0, 100)]    // guard against divide-by-zero
    public void RescueChunkSize_ScalesWithWidth(int columns, int expected)
        => Assert.Equal(expected, SpreadsheetAnalysisService.RescueChunkSize(columns));

    private static List<MixedRowMarker> InvokeParseOutline(string response)
    {
        var m = typeof(SpreadsheetAnalysisService).GetMethod(
            "ParseMixedOutline",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (List<MixedRowMarker>)m.Invoke(null, [response])!;
    }

    [Fact]
    public void ParseMixedOutline_ParsesKindsAndRows()
    {
        var json = """
        {"markers":[
          {"row":4,"kind":"IncomeSection","text":"Income"},
          {"row":5,"kind":"Category","text":"Design income"},
          {"row":9,"kind":"Subtotal","text":"Total for Design income"},
          {"row":40,"kind":"ExpenseSection","text":"Expenses"}
        ]}
        """;
        var markers = InvokeParseOutline(json);
        Assert.Equal(4, markers.Count);
        Assert.Equal(MixedRowKind.IncomeSection, markers[0].Kind);
        Assert.Equal(4, markers[0].RowIndex);
        Assert.Equal(MixedRowKind.ExpenseSection, markers[3].Kind);
    }

    [Fact]
    public void ParseMixedOutline_Malformed_ReturnsEmpty()
    {
        Assert.Empty(InvokeParseOutline("not json"));
    }

    [Fact]
    public void ParseMixedOutline_SkipsUnknownKinds()
    {
        var json = """{"markers":[{"row":1,"kind":"Wat","text":"x"},{"row":2,"kind":"Category","text":"y"}]}""";
        var markers = InvokeParseOutline(json);
        Assert.Single(markers);
        Assert.Equal(MixedRowKind.Category, markers[0].Kind);
    }

    private static Dictionary<(SpreadsheetSheetType, string), List<int>> InvokeBucket(
        int rowCount, List<MixedRowMarker> markers)
    {
        var m = typeof(SpreadsheetAnalysisService).GetMethod(
            "BucketMixedRows",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (Dictionary<(SpreadsheetSheetType, string), List<int>>)m.Invoke(null, [rowCount, markers])!;
    }

    [Fact]
    public void BucketMixedRows_AssignsTypeAndCategoryBySection()
    {
        // rows: 0..11
        // 0 IncomeSection, 1 Category(Design), [2,3] data, 4 Subtotal,
        // 5 Category(Landscaping), [6] data, 7 ExpenseSection, 8 Category(Advertising), [9,10] data, 11 Subtotal
        var markers = new List<MixedRowMarker>
        {
            new(0, MixedRowKind.IncomeSection, "Income"),
            new(1, MixedRowKind.Category, "Design income"),
            new(4, MixedRowKind.Subtotal, "Total for Design income"),
            new(5, MixedRowKind.Category, "Landscaping Services"),
            new(7, MixedRowKind.ExpenseSection, "Expenses"),
            new(8, MixedRowKind.Category, "Advertising"),
            new(11, MixedRowKind.Subtotal, "Total for Advertising"),
        };
        var buckets = InvokeBucket(12, markers);

        Assert.Equal([2, 3], buckets[(SpreadsheetSheetType.Revenue, "Design income")]);
        Assert.Equal([6], buckets[(SpreadsheetSheetType.Revenue, "Landscaping Services")]);
        Assert.Equal([9, 10], buckets[(SpreadsheetSheetType.Expenses, "Advertising")]);
        // Subtotal rows (4, 11) and structural rows are never bucketed.
        Assert.DoesNotContain(buckets.Values.SelectMany(v => v), i => i == 4 || i == 11);
    }

    [Fact]
    public void BucketMixedRows_DropsRowsBeforeAnySection()
    {
        var markers = new List<MixedRowMarker> { new(2, MixedRowKind.IncomeSection, "Income") };
        var buckets = InvokeBucket(5, markers);
        // rows 0,1 are before any section -> dropped; rows 3,4 -> Revenue, category falls back to section
        Assert.DoesNotContain(buckets.Values.SelectMany(v => v), i => i is 0 or 1 or 2);
        Assert.Equal([3, 4], buckets[(SpreadsheetSheetType.Revenue, "Income")]);
    }

    [Fact]
    public void BucketMixedRows_CategoryFallsBackToSectionName()
    {
        var markers = new List<MixedRowMarker>
        {
            new(0, MixedRowKind.ExpenseSection, "Expenses"),
        };
        var buckets = InvokeBucket(3, markers);
        Assert.Equal([1, 2], buckets[(SpreadsheetSheetType.Expenses, "Expenses")]);
    }

    [Fact]
    public void BucketMixedRows_DuplicateRowIndex_DoesNotThrow()
    {
        // The outline is untrusted model output; a repeated row index must not throw
        // (last marker for that row wins) instead of defeating the "never throws" contract.
        var markers = new List<MixedRowMarker>
        {
            new(0, MixedRowKind.IncomeSection, "Income"),
            new(0, MixedRowKind.IncomeSection, "Income (duplicate)"),
            new(1, MixedRowKind.Category, "Design income"),
        };

        var buckets = InvokeBucket(4, markers);

        Assert.Equal([2, 3], buckets[(SpreadsheetSheetType.Revenue, "Design income")]);
    }

    [Fact]
    public void ImportProcessedEntities_MixedWithCategory_CategorizesByReportGroup()
    {
        var svc = new SpreadsheetImportService();
        var company = new CompanyData();

        var revenue = new LlmProcessedData
        {
            EntityType = SpreadsheetSheetType.Revenue,
            Entities =
            {
                JsonDocument.Parse("""{"id":"SAL-1","date":"2026-06-09","description":"Custom Design","total":300,"categoryName":"Design income"}""").RootElement.Clone(),
            }
        };
        var expense = new LlmProcessedData
        {
            EntityType = SpreadsheetSheetType.Expenses,
            Entities =
            {
                JsonDocument.Parse("""{"id":"PUR-1","date":"2026-06-16","description":"Newspaper Ad","total":50,"categoryName":"Advertising"}""").RootElement.Clone(),
            }
        };

        var result = svc.ImportProcessedEntities(company, [revenue, expense], "P&L Detail");

        Assert.Equal(2, result.Inserted);
        Assert.Single(company.Revenues);
        Assert.Single(company.Expenses);
        // Each transaction's linked product is categorized by the report group.
        var revProduct = company.Products.First(p => p.Id == company.Revenues[0].LineItems[0].ProductId);
        var expProduct = company.Products.First(p => p.Id == company.Expenses[0].LineItems[0].ProductId);
        Assert.Equal("Design income", company.Categories.First(c => c.Id == revProduct.CategoryId).Name);
        Assert.Equal("Advertising", company.Categories.First(c => c.Id == expProduct.CategoryId).Name);
    }

    // Returns a mixed classify verdict, a fixed outline for the outline call, and a single-entity
    // array for every extraction call. Distinguished by stable markers in each system prompt.
    private sealed class MixedMockGeminiService : IGeminiService
    {
        private readonly string _outlineJson;
        public MixedMockGeminiService(string outlineJson) => _outlineJson = outlineJson;

        public bool IsConfigured => true;

        public Task<string?> SendChatAsync(
            string systemPrompt, string userPrompt, int maxTokens = 4000, double temperature = 0.1,
            CancellationToken cancellationToken = default,
            OperationKind operation = OperationKind.Completion, long? sizeFeature = null)
        {
            if (systemPrompt.Contains("Decide ONE", StringComparison.Ordinal))
                return Task.FromResult<string?>("""{"action":"mixed"}""");
            if (systemPrompt.Contains("structural rows", StringComparison.Ordinal))
                return Task.FromResult<string?>(_outlineJson);
            // extraction: one entity, echoing a plausible transaction
            return Task.FromResult<string?>("""[{"id":"T1","date":"2026-06-09","description":"Item","total":100}]""");
        }
    }

    [Fact]
    public async Task RescueAsync_MixedReport_ExtractsRevenueAndExpenses()
    {
        // Outline: mark an Income section + category near the top and an Expenses section + category
        // partway down, using row indices that fall inside the fixture's data range.
        var outline = """
        {"markers":[
          {"row":0,"kind":"IncomeSection","text":"Income"},
          {"row":1,"kind":"Category","text":"Design income"},
          {"row":60,"kind":"ExpenseSection","text":"Expenses"},
          {"row":61,"kind":"Category","text":"Advertising"}
        ]}
        """;
        var mock = new MixedMockGeminiService(outline);
        var service = new SpreadsheetAnalysisService(mock);
        var path = Path.Combine(RepoRoot(), "TestData", "MainImporter", "quickbooks_profit_and_loss_detail.xlsx");
        Assert.True(File.Exists(path), $"fixture missing: {path}");

        var result = await service.RescueAsync(path, isCsv: false);

        Assert.Equal(ImportRescueOutcome.Extracted, result.Outcome);
        var data = result.Extractions.SelectMany(e => e.ProcessedData).ToList();
        Assert.Contains(data, d => d.EntityType == SpreadsheetSheetType.Revenue);
        Assert.Contains(data, d => d.EntityType == SpreadsheetSheetType.Expenses);
        // Every extracted entity carries the report category injected in Pass B.
        var withCategory = data.SelectMany(d => d.Entities)
            .Count(e => e.TryGetProperty("categoryName", out var c) && !string.IsNullOrEmpty(c.GetString()));
        Assert.True(withCategory > 0);
    }
}
