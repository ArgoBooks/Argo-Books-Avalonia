using System.Reflection;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for ReportRenderer. The summary calculations are private (internal steps of summary
/// rendering), so they are invoked here via reflection.
/// </summary>
public class ReportRendererTests
{
    [Fact]
    public void CalculateGrowthRate_ComparesEqualLengthPeriods()
    {
        // Current period Jan 1-31. The previous comparison window must be the SAME length (Dec 1-31),
        // not one day shorter (Dec 2-31). With equal totals in both windows a correct comparison is 0%.
        var data = new CompanyData();
        data.Revenues.Add(new Revenue { Id = "C1", Date = new DateTime(2024, 1, 15), Total = 200m, OriginalCurrency = "USD" });
        // Previous-period revenue totalling the same $200; Dec 1 lands in the correct window but
        // not the Dec 2-31 one.
        data.Revenues.Add(new Revenue { Id = "P1", Date = new DateTime(2023, 12, 1), Total = 100m, OriginalCurrency = "USD" });
        data.Revenues.Add(new Revenue { Id = "P2", Date = new DateTime(2023, 12, 15), Total = 100m, OriginalCurrency = "USD" });

        var config = new ReportConfiguration
        {
            Filters = new ReportFilters
            {
                StartDate = new DateTime(2024, 1, 1),
                EndDate = new DateTime(2024, 1, 31)
            }
        };

        using var renderer = new ReportRenderer(config, data);
        var summary = new SummaryReportElement { TransactionType = TransactionType.Revenue };

        var method = typeof(ReportRenderer).GetMethod("CalculateGrowthRate", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var growth = (double)method.Invoke(renderer, [summary])!;

        Assert.Equal(0d, growth);
    }

    [Fact]
    public void SummaryTotal_CustomRange_CountsTransactionsLaterOnTheEndDay()
    {
        // A custom range's end date is midnight, but transactions carry a time of day. Tables and
        // charts run the range to the end of that day, so the Summary box has to as well.
        var data = new CompanyData();
        data.Revenues.Add(new Revenue { Id = "R1", Date = new DateTime(2024, 1, 31, 14, 30, 0), Total = 250m, OriginalCurrency = "USD" });

        var config = new ReportConfiguration
        {
            Filters = new ReportFilters
            {
                DatePresetName = DatePresetNames.Custom,
                StartDate = new DateTime(2024, 1, 1),
                EndDate = new DateTime(2024, 1, 31)
            }
        };

        using var renderer = new ReportRenderer(config, data);
        var summary = new SummaryReportElement { TransactionType = TransactionType.Revenue };

        var method = typeof(ReportRenderer).GetMethod("CalculateTotalRevenue", BindingFlags.NonPublic | BindingFlags.Instance)!;

        Assert.Equal(250m, (decimal)method.Invoke(renderer, [summary])!);
    }

    [Theory]
    [InlineData(ReportChartStyle.Area)]
    [InlineData(ReportChartStyle.Line)]
    [InlineData(ReportChartStyle.Bar)]
    public void ProfitChart_WithOnlyLosses_StaysInsideItsBoxAndIsRed(ReportChartStyle style)
    {
        // Two days of expenses and no sales, as in a new company: every point is a loss.
        var data = new CompanyData();
        data.Expenses.Add(new Expense { Id = "EXP-1", Date = new DateTime(2026, 10, 20), OriginalCurrency = "USD", Total = 200m });
        data.Expenses.Add(new Expense { Id = "EXP-2", Date = new DateTime(2026, 10, 21), OriginalCurrency = "USD", Total = 20m });

        var config = ReportTemplateFactory.CreateFromTemplate(ReportTemplateFactory.TemplateNames.FinancialOverview);
        config.Filters.DatePresetName = DatePresetNames.Custom;
        config.Filters.StartDate = new DateTime(2026, 10, 1);
        config.Filters.EndDate = new DateTime(2026, 10, 31);
        var chart = config.Elements.OfType<ChartReportElement>().Single(c => c.ChartType == ChartDataType.TotalProfits);
        chart.ChartStyle = style;

        using var renderer = new ReportRenderer(config, data);
        renderer.ComputeContinuationPlan();
        using var page = renderer.RenderEffectivePageToBitmap(renderer.GetContinuationPlan()!.Pages[0]);

        int left = (int)chart.X, right = (int)(chart.X + chart.Width), top = (int)chart.Y, bottom = (int)(chart.Y + chart.Height);

        // Zero used to be placed above the chart, so the bars and fill reached up into the report's title.
        Assert.False(Any(page, left, right, 0, top, IsColoured), "The chart drew above its own box.");
        Assert.False(Any(page, left, right, top, bottom, IsGreen), "A loss was drawn in the profit colour.");
        Assert.True(Any(page, left, right, top, bottom, IsRed), "The losses were not drawn in red.");
    }

    private static bool Any(SkiaSharp.SKBitmap bitmap, int left, int right, int top, int bottom, Func<SkiaSharp.SKColor, bool> match)
    {
        for (var y = Math.Max(0, top); y < Math.Min(bitmap.Height, bottom); y++)
            for (var x = Math.Max(0, left); x < Math.Min(bitmap.Width, right); x++)
                if (match(bitmap.GetPixel(x, y)))
                    return true;
        return false;
    }

    // Text, grid lines and borders are black, grey or white, so any strongly tinted pixel is part of a chart.
    private static bool IsColoured(SkiaSharp.SKColor c) => Math.Max(c.Red, Math.Max(c.Green, c.Blue)) - Math.Min(c.Red, Math.Min(c.Green, c.Blue)) > 25;

    private static bool IsGreen(SkiaSharp.SKColor c) => c.Green - c.Red > 60 && c.Green - c.Blue > 30;

    private static bool IsRed(SkiaSharp.SKColor c) => c.Red - c.Green > 100 && c.Red - c.Blue > 100;

    [Fact]
    public void GeneralLedgerOverSeveralPages_DrawsEveryRow()
    {
        // The planner picks the rows for each page and the renderer draws them.
        var data = new CompanyData();
        for (var i = 0; i < 150; i++)
        {
            data.Expenses.Add(new Expense
            {
                Id = $"EXP-{i:D3}",
                Date = new DateTime(2024, 6, 1).AddHours(i),
                Description = $"Supplies {i}",
                OriginalCurrency = "USD",
                Total = 10m + i
            });
        }

        var config = ReportTemplateFactory.CreateFromTemplate(ReportTemplateFactory.TemplateNames.GeneralLedger);
        config.Filters.DatePresetName = DatePresetNames.Custom;
        config.Filters.StartDate = new DateTime(2024, 1, 1);
        config.Filters.EndDate = new DateTime(2024, 12, 31);

        using var renderer = new ReportRenderer(config, data);
        renderer.ComputeContinuationPlan();
        var plan = renderer.GetContinuationPlan()!;
        var table = config.Elements.OfType<AccountingTableReportElement>().Single();

        foreach (var page in plan.Pages)
            renderer.RenderEffectivePageToBitmap(page).Dispose();

        Assert.True(plan.Pages.Count >= 3);
        Assert.Equal(plan.CachedTableData[table.Id].Rows.Count, renderer.AccountingRowsDrawn);
    }

    [Fact]
    public void TableContinuation_HeaderHidden_UsesTheSpaceTheHeaderWouldTake()
    {
        var data = new CompanyData();
        for (var i = 0; i < 200; i++)
            data.Revenues.Add(new Revenue { Id = $"REV-{i:D3}", Date = new DateTime(2024, 6, 1).AddHours(i), Total = 10m, OriginalCurrency = "USD" });

        var table = new TableReportElement { TransactionType = TransactionType.Revenue, MaxRows = 0, X = 40, Y = 40, Width = 700, Height = 200 };
        var config = new ReportConfiguration
        {
            ShowHeader = false,
            Filters = new ReportFilters
            {
                DatePresetName = DatePresetNames.Custom,
                StartDate = new DateTime(2024, 1, 1),
                EndDate = new DateTime(2024, 12, 31)
            }
        };
        config.Elements.Add(table);

        using var renderer = new ReportRenderer(config, data);
        renderer.ComputeContinuationPlan();
        var firstContinuation = renderer.GetContinuationPlan()!.Pages[1];

        // With no header, a continuation page runs from the top margin to the footer.
        var (_, pageHeight) = PageDimensions.GetDimensions(config.PageSize, config.PageOrientation);
        var contentHeight = pageHeight - config.PageMargins.Top - PageDimensions.FooterHeight - config.PageMargins.Bottom;
        var continuedIndicatorAndHeaders = table.DataRowHeight * 0.8 + table.HeaderRowHeight;
        var expectedRows = (int)Math.Floor((contentHeight - continuedIndicatorAndHeaders) / table.DataRowHeight);

        Assert.False(firstContinuation.IsLastContinuationPage);
        Assert.Equal(expectedRows, firstContinuation.RowCount);
    }
}
