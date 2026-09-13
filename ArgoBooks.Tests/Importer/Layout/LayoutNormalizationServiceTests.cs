using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.Layout;
using ClosedXML.Excel;
using Xunit;

namespace ArgoBooks.Tests.Importer.Layout;

/// <summary>
/// Offline tests for <see cref="LayoutNormalizationService"/>: the pipeline integration
/// that rewrites messy sheets into clean tables in a temp <c>.xlsx</c> (and leaves clean
/// workbooks untouched on the original path). A fake <see cref="IGeminiService"/> returns
/// canned <see cref="LayoutDescriptor"/> JSON so no network/LLM is involved.
/// </summary>
public class LayoutNormalizationServiceTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    private string NewTempXlsxPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"argo-layout-test-{Guid.NewGuid():N}.xlsx");
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try { if (File.Exists(f)) File.Delete(f); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Fake LLM that returns a fixed response for every <see cref="SendChatAsync"/> call
    /// and records how many calls were made.
    /// </summary>
    private sealed class FakeGemini : IGeminiService
    {
        private readonly string? _response;
        public FakeGemini(string? response) => _response = response;

        public bool IsConfigured => true;
        public int CallCount { get; private set; }

        public Task<string?> SendChatAsync(
            string systemPrompt, string userPrompt,
            int maxTokens = 4000, double temperature = 0.1,
            CancellationToken cancellationToken = default,
            OperationKind operation = OperationKind.Completion,
            long? sizeFeature = null)
        {
            CallCount++;
            return Task.FromResult(_response);
        }
    }

    // ─── Workbook builders ───────────────────────────────────────────────────

    /// <summary>A clean single-header-row sheet the gate leaves alone.</summary>
    private static void AddCleanSheet(XLWorkbook wb, string name)
    {
        var ws = wb.AddWorksheet(name);
        ws.Cell(1, 1).Value = "Date";
        ws.Cell(1, 2).Value = "Amount";
        ws.Cell(1, 3).Value = "Desc";

        ws.Cell(2, 1).Value = new DateTime(2024, 5, 1);
        ws.Cell(2, 2).Value = 42.50;
        ws.Cell(2, 3).Value = "Lunch";

        ws.Cell(3, 1).Value = new DateTime(2024, 5, 2);
        ws.Cell(3, 2).Value = 99.00;
        ws.Cell(3, 3).Value = "Fuel";
    }

    /// <summary>
    /// A messy sheet: a 3-row preamble before the real header+data. The gate flags this
    /// (first dense row is well past the top).
    ///   Row 1: "Annual Report"
    ///   Row 2: (blank)
    ///   Row 3: "Note: figures in USD"
    ///   Row 4: "Date" | "Amount" | "Desc"
    ///   Row 5: 2024-01-15 | 100.50 | "Coffee"
    ///   Row 6: 2024-02-20 | 250.00 | "Office"
    ///   Row 7: 2024-03-10 | 12.00  | "Tea"
    /// </summary>
    private static void AddMessyPreambleSheet(XLWorkbook wb, string name)
    {
        var ws = wb.AddWorksheet(name);
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
    }

    /// <summary>Descriptor that selects the real table past the 3-row preamble.</summary>
    private const string PreambleDescriptorJson = """
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

    // ─── Test 1: clean workbook -> original path returned unchanged ──────────

    [Fact]
    public async Task NormalizeAsync_CleanWorkbook_ReturnsOriginalPathUnchanged()
    {
        var path = NewTempXlsxPath();
        using (var wb = new XLWorkbook())
        {
            AddCleanSheet(wb, "Expenses");
            AddCleanSheet(wb, "Revenue");
            wb.SaveAs(path);
        }

        // A fake that would throw the test off if it were ever called (it must not be).
        var fake = new FakeGemini(PreambleDescriptorJson);
        var service = new LayoutNormalizationService(fake);

        // Snapshot the argo-layout temp files that already exist (other test classes run in
        // parallel and legitimately create them) so we can assert only that THIS call adds none.
        var before = Directory.GetFiles(Path.GetTempPath(), "argo-layout-*.xlsx")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = await service.NormalizeAsync(path);

        // No interpretation needed: original path returned, no LLM call. The service only
        // creates a temp file when it returns a temp path, so a clean workbook (result ==
        // original path) must not have produced any new temp file.
        Assert.Equal(path, result);
        Assert.Equal(0, fake.CallCount);

        var newTemps = Directory.GetFiles(Path.GetTempPath(), "argo-layout-*.xlsx")
            .Where(t => !before.Contains(t) && !string.Equals(t, path, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Empty(newTemps);
    }

    // ─── Test 2: messy sheet normalized; clean sheet copied through intact ───

    [Fact]
    public async Task NormalizeAsync_MessySheet_NormalizedAndCleanSheetPreserved()
    {
        var path = NewTempXlsxPath();
        using (var wb = new XLWorkbook())
        {
            AddMessyPreambleSheet(wb, "Messy");
            AddCleanSheet(wb, "Clean");
            wb.SaveAs(path);
        }

        var fake = new FakeGemini(PreambleDescriptorJson);
        var service = new LayoutNormalizationService(fake);

        var result = await service.NormalizeAsync(path);

        // A temp file distinct from the original must be produced.
        Assert.NotEqual(path, result);
        _tempFiles.Add(result);
        Assert.True(File.Exists(result));

        using var outWb = new XLWorkbook(result);

        // The messy sheet now has clean headers in row 1 and the preamble gone.
        var messy = outWb.Worksheet("Messy");
        Assert.Equal("Date", messy.Cell(1, 1).GetString());
        Assert.Equal("Amount", messy.Cell(1, 2).GetString());
        Assert.Equal("Desc", messy.Cell(1, 3).GetString());

        // Three data rows, preamble removed.
        Assert.Equal("2024-01-15", messy.Cell(2, 1).GetString());
        Assert.Equal("Coffee", messy.Cell(2, 3).GetString());
        Assert.Equal("2024-02-20", messy.Cell(3, 1).GetString());
        Assert.Equal("Office", messy.Cell(3, 3).GetString());
        Assert.Equal("2024-03-10", messy.Cell(4, 1).GetString());
        Assert.Equal("Tea", messy.Cell(4, 3).GetString());
        // Row 5 must be empty (only 3 data rows extracted).
        Assert.True(messy.Cell(5, 1).IsEmpty());

        // The clean sheet is copied through unchanged with its values + types intact.
        var clean = outWb.Worksheet("Clean");
        Assert.Equal("Date", clean.Cell(1, 1).GetString());
        Assert.Equal("Amount", clean.Cell(1, 2).GetString());
        Assert.Equal("Desc", clean.Cell(1, 3).GetString());
        Assert.Equal(XLDataType.DateTime, clean.Cell(2, 1).DataType);
        Assert.Equal(new DateTime(2024, 5, 1), clean.Cell(2, 1).GetDateTime());
        Assert.Equal(XLDataType.Number, clean.Cell(2, 2).DataType);
        Assert.Equal(42.50, clean.Cell(2, 2).GetDouble());
        Assert.Equal("Lunch", clean.Cell(2, 3).GetString());
        Assert.Equal("Fuel", clean.Cell(3, 3).GetString());

        // The messy sheet triggered exactly one LLM call; the clean sheet triggered none.
        Assert.Equal(1, fake.CallCount);
    }

    // ─── Test 3: AI failed (null/garbage descriptor) -> sheet copied as-is ───

    [Fact]
    public async Task NormalizeAsync_DescriptorNull_CopiesSheetAsIsNoDataLost()
    {
        var path = NewTempXlsxPath();
        using (var wb = new XLWorkbook())
        {
            AddMessyPreambleSheet(wb, "Messy");
            // A second clean sheet forces the gate to flag at least one sheet (Messy),
            // so the temp-workbook path is taken even when the AI returns garbage.
            AddCleanSheet(wb, "Clean");
            wb.SaveAs(path);
        }

        // Garbage response -> SpreadsheetLayoutService returns a null descriptor.
        var fake = new FakeGemini("Sure! Here is the layout: { not json at all");
        var service = new LayoutNormalizationService(fake);

        var result = await service.NormalizeAsync(path);

        // The gate still flagged the messy sheet, so a temp workbook is produced;
        // but because the descriptor was unusable, the messy sheet is copied as-is.
        Assert.NotEqual(path, result);
        _tempFiles.Add(result);

        using var outWb = new XLWorkbook(result);
        var messy = outWb.Worksheet("Messy");

        // Original (un-normalized) content is intact: preamble + original header/data layout.
        Assert.Equal("Annual Report", messy.Cell(1, 1).GetString());
        Assert.Equal("Note: figures in USD", messy.Cell(3, 1).GetString());
        Assert.Equal("Date", messy.Cell(4, 1).GetString());
        Assert.Equal("Amount", messy.Cell(4, 2).GetString());
        Assert.Equal("Desc", messy.Cell(4, 3).GetString());
        Assert.Equal("Coffee", messy.Cell(5, 3).GetString());
        Assert.Equal("Office", messy.Cell(6, 3).GetString());
        Assert.Equal("Tea", messy.Cell(7, 3).GetString());

        // The clean sheet is also preserved.
        var clean = outWb.Worksheet("Clean");
        Assert.Equal("Lunch", clean.Cell(2, 3).GetString());
    }
}
