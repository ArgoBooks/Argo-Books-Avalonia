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

    [Fact]
    public void GeneralLedgerOverSeveralPages_DrawsEveryRow()
    {
        // The planner picks the rows for each page and the renderer draws them. When the two measured
        // the page differently, rows planned for a page were cut off there and drawn nowhere, and the
        // row lost could be a subtotal or the grand total.
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
