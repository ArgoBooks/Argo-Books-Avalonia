using ArgoBooks.Core.Services.Layout;
using ClosedXML.Excel;
using Xunit;

namespace ArgoBooks.Tests.Importer.Layout;

/// <summary>
/// Proves, against the ACTUAL corpus file <c>cross-tab-sales/input.xlsx</c>, that the
/// layout-interpretation code turns its wide month columns into the 9 long-form rows the
/// importer needs. This is the file the corpus harness flags as a known gap because the
/// normal importer cannot pivot it.
/// </summary>
public class CrossTabSalesFileTests
{
    private static string CrossTabSalesInput =>
        Path.Combine(ImporterHarness.CorpusRoot, "cross-tab-sales", "input.xlsx");

    [Fact]
    public void CrossTabSales_File_GateFlagsItAndExtractorProduces9LongRows()
    {
        Assert.True(File.Exists(CrossTabSalesInput), $"missing test file: {CrossTabSalesInput}");

        using var wb = new XLWorkbook(CrossTabSalesInput);
        var ws = wb.Worksheet("Sales");
        var grid = SheetGrid.FromWorksheet(ws);

        // 1) The cheap local gate must flag this sheet as needing AI interpretation,
        //    otherwise the pipeline would never route it to the layout step.
        Assert.True(LayoutGate.NeedsInterpretation(grid),
            "LayoutGate should flag the cross-tab Sales sheet as needing interpretation");

        // The descriptor an LLM would produce for this 4x4 cross-tab: header row 0, Product as the key column, the three months transposed.
        var region = new TableRegion
        {
            HeaderRows = [0],
            FirstDataRow = 1,
            LastDataRow = 3,
            FirstCol = 0,
            LastCol = 3,
            Orientation = "wide",
            KeyColumns = [0],
        };

        var (headers, rows) = GridExtractor.Extract(grid, region);

        // 3) Long-form output: [Product, Column, Value] with 3 products x 3 months = 9 rows.
        Assert.Equal(new[] { "Product", "Column", "Value" }, headers);
        Assert.Equal(9, rows.Count);

        // Spot-check that every (product, month, amount) cell came through correctly.
        void AssertRow(string product, string month, string value) =>
            Assert.Contains(rows, r => r[0] == product && r[1] == month && r[2] == value);

        AssertRow("Widget", "Jan", "100");
        AssertRow("Widget", "Feb", "200");
        AssertRow("Widget", "Mar", "150");
        AssertRow("Gadget", "Jan", "300");
        AssertRow("Gadget", "Feb", "400");
        AssertRow("Gadget", "Mar", "350");
        AssertRow("Gizmo", "Jan", "50");
        AssertRow("Gizmo", "Feb", "75");
        AssertRow("Gizmo", "Mar", "60");
    }
}
