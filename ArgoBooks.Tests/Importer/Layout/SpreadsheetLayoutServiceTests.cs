using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.Layout;
using ClosedXML.Excel;
using Xunit;

namespace ArgoBooks.Tests.Importer.Layout;

/// <summary>
/// Offline tests for <see cref="SpreadsheetLayoutService"/>. These exercise the
/// service's JSON parsing and the end-to-end deterministic half (descriptor ->
/// <see cref="GridExtractor"/>) using a fake <see cref="IGeminiService"/> that
/// returns a canned <see cref="LayoutDescriptor"/> JSON. The real AI quality is
/// judged separately behind a feature flag; these tests only validate
/// that a well-formed model answer is parsed and applied correctly, and that
/// null/empty/malformed answers fall back to a null descriptor.
/// </summary>
public class SpreadsheetLayoutServiceTests
{
    /// <summary>
    /// Minimal fake LLM that always returns the same canned response (or null),
    /// and records how many times it was called.
    /// </summary>
    private sealed class FakeGemini(string? response) : IGeminiService
    {
        public bool IsConfigured => true;
        private int CallCount { get; set; }

        public Task<string?> SendChatAsync(
            string systemPrompt, string userPrompt,
            int maxTokens = 4000, double temperature = 0.1,
            CancellationToken cancellationToken = default,
            OperationKind operation = OperationKind.Completion,
            long? sizeFeature = null)
        {
            CallCount++;
            return Task.FromResult(response);
        }
    }

    // ─── Sheets matching the canned descriptors ──────────────────────────────

    /// <summary>
    ///   Row 1: "Annual Report"        (preamble)
    ///   Row 2: (blank)                (preamble)
    ///   Row 3: "Note: figures in USD" (preamble)
    ///   Row 4: "Date" | "Amount" | "Desc"     (header)
    ///   Row 5: 2024-01-15 | 100.50 | "Coffee"
    ///   Row 6: 2024-02-20 | 250.00 | "Office"
    ///   Row 7: 2024-03-10 | 12.00  | "Tea"
    /// </summary>
    private static SheetGrid BuildLongGrid()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");

        ws.Cell(1, 1).Value = "Annual Report";
        ws.Cell(3, 1).Value = "Note: figures in USD";

        ws.Cell(4, 1).Value = "Date";
        ws.Cell(4, 2).Value = "Amount";
        ws.Cell(4, 3).Value = "Desc";

        ws.Cell(5, 1).Value = new DateTime(2024, 1, 15);
        ws.Cell(5, 2).Value = 100.50;
        ws.Cell(5, 3).Value = "Coffee";

        ws.Cell(6, 1).Value = new DateTime(2024, 2, 20);
        ws.Cell(6, 2).Value = 250.00;
        ws.Cell(6, 3).Value = "Office";

        ws.Cell(7, 1).Value = new DateTime(2024, 3, 10);
        ws.Cell(7, 2).Value = 12.00;
        ws.Cell(7, 3).Value = "Tea";

        return SheetGrid.FromWorksheet(ws);
    }

    /// <summary>
    ///   Row 1: "Product" | "Jan" | "Feb"   (header)
    ///   Row 2: "Widget"  | 10    | 20      (data)
    ///   Row 3: "Gadget"  | 5     | 8       (data)
    /// </summary>
    private static SheetGrid BuildCrossTabGrid()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");

        ws.Cell(1, 1).Value = "Product";
        ws.Cell(1, 2).Value = "Jan";
        ws.Cell(1, 3).Value = "Feb";

        ws.Cell(2, 1).Value = "Widget";
        ws.Cell(2, 2).Value = 10;
        ws.Cell(2, 3).Value = 20;

        ws.Cell(3, 1).Value = "Gadget";
        ws.Cell(3, 2).Value = 5;
        ws.Cell(3, 3).Value = 8;

        return SheetGrid.FromWorksheet(ws);
    }

    // ─── Test 1: canned long descriptor parses + extracts end-to-end ─────────

    [Fact]
    public async Task GetLayoutDescriptorAsync_LongDescriptor_ParsesAndExtracts()
    {
        // Canned model answer: locate the header band past the 3-row preamble.
        const string json = """
        {
          "tables": [
            {
              "firstDataRow": 4,
              "lastDataRow": 6,
              "firstCol": 0,
              "lastCol": 2,
              "headerRows": [3],
              "orientation": "long",
              "ignoreRows": [],
              "keyColumns": null
            }
          ]
        }
        """;
        var service = new SpreadsheetLayoutService(new FakeGemini(json));
        var grid = BuildLongGrid();

        var descriptor = await service.GetLayoutDescriptorAsync(grid);

        Assert.NotNull(descriptor);
        var region = Assert.Single(descriptor.Tables);
        Assert.Equal(4, region.FirstDataRow);
        Assert.Equal(6, region.LastDataRow);
        Assert.Equal(0, region.FirstCol);
        Assert.Equal(2, region.LastCol);
        Assert.Equal(new[] { 3 }, region.HeaderRows);
        Assert.Equal("long", region.Orientation);

        // End-to-end: the deterministic extractor turns the AI descriptor into a clean table.
        var (headers, rows) = GridExtractor.Extract(grid, region);
        Assert.Equal(new[] { "Date", "Amount", "Desc" }, headers);
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "2024-01-15", "100.5", "Coffee" }, rows[0]);
        Assert.Equal(new[] { "2024-02-20", "250", "Office" }, rows[1]);
        Assert.Equal(new[] { "2024-03-10", "12", "Tea" }, rows[2]);
    }

    // ─── Test 2: canned wide/cross-tab descriptor parses + transposes ────────

    [Fact]
    public async Task GetLayoutDescriptorAsync_WideDescriptor_ParsesAndTransposes()
    {
        const string json = """
        ```json
        {
          "tables": [
            {
              "firstDataRow": 1,
              "lastDataRow": 2,
              "firstCol": 0,
              "lastCol": 2,
              "headerRows": [0],
              "orientation": "wide",
              "keyColumns": [0]
            }
          ]
        }
        ```
        """;
        var service = new SpreadsheetLayoutService(new FakeGemini(json));
        var grid = BuildCrossTabGrid();

        var descriptor = await service.GetLayoutDescriptorAsync(grid);

        Assert.NotNull(descriptor);
        var region = Assert.Single(descriptor.Tables);
        Assert.Equal("wide", region.Orientation);
        Assert.NotNull(region.KeyColumns);
        Assert.Equal(new[] { 0 }, region.KeyColumns!);

        var (headers, rows) = GridExtractor.Extract(grid, region);
        Assert.Equal(new[] { "Product", "Column", "Value" }, headers);
        Assert.Equal(4, rows.Count);
        Assert.Equal(new[] { "Widget", "Jan", "10" }, rows[0]);
        Assert.Equal(new[] { "Widget", "Feb", "20" }, rows[1]);
        Assert.Equal(new[] { "Gadget", "Jan", "5" }, rows[2]);
        Assert.Equal(new[] { "Gadget", "Feb", "8" }, rows[3]);
    }

    // ─── Test 3: null/empty model response -> null (caller falls back) ───────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetLayoutDescriptorAsync_NullOrEmptyResponse_ReturnsNull(string? response)
    {
        var service = new SpreadsheetLayoutService(new FakeGemini(response));
        var grid = BuildLongGrid();

        var descriptor = await service.GetLayoutDescriptorAsync(grid);

        Assert.Null(descriptor);
    }

    // ─── Test 4: malformed JSON -> null, no throw ────────────────────────────

    [Fact]
    public async Task GetLayoutDescriptorAsync_MalformedJson_ReturnsNull()
    {
        const string garbage = "Sure! Here is the layout: { tables: [ oops not json";
        var service = new SpreadsheetLayoutService(new FakeGemini(garbage));
        var grid = BuildLongGrid();

        var descriptor = await service.GetLayoutDescriptorAsync(grid);

        Assert.Null(descriptor);
    }
}
