using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// The Date Range box in the step-2 designer. The report's range is set when leaving step 1, so a
/// range picked in the designer only changed the box: the preview, the export and a saved template
/// kept the range from step 1.
/// </summary>
public class ReportsPageDateRangeTests : ModalViewModelTestBase
{
    private static void InDesigner(Action<ReportsPageViewModel> test)
    {
        var templatesDir = Path.Combine(Path.GetTempPath(), "ArgoBooks_ReportDateRange_" + Guid.NewGuid().ToString("N")[..8]);
        var vm = new ReportsPageViewModel(new ReportTemplateStorage(templatesDir));
        try
        {
            vm.GoToNextStepCommand.Execute(null);
            Assert.Equal(2, vm.CurrentStep);
            test(vm);
        }
        finally
        {
            vm.Cleanup();
            if (Directory.Exists(templatesDir))
                Directory.Delete(templatesDir, recursive: true);
        }
    }

    private static DatePresetOption Preset(ReportsPageViewModel vm, string name) =>
        vm.DatePresets.Single(p => p.Name == name);

    [Fact]
    public void PickingAPreset_ChangesTheReportRange()
    {
        InDesigner(vm =>
        {
            vm.SelectedDatePresetOption = Preset(vm, DatePresetNames.LastYear);

            var (start, end) = DatePresetNames.GetDateRange(DatePresetNames.LastYear);
            Assert.Equal(DatePresetNames.LastYear, vm.Configuration.Filters.DatePresetName);
            Assert.Equal(start, vm.Configuration.Filters.StartDate);
            Assert.Equal(end, vm.Configuration.Filters.EndDate);
        });
    }

    [Fact]
    public void PickingCustomDates_ChangesTheReportRange()
    {
        InDesigner(vm =>
        {
            vm.SelectedDatePresetOption = Preset(vm, DatePresetNames.Custom);
            vm.CustomStartDate = new DateTimeOffset(new DateTime(2024, 3, 1));
            vm.CustomEndDate = new DateTimeOffset(new DateTime(2024, 3, 31));

            Assert.Equal(DatePresetNames.Custom, vm.Configuration.Filters.DatePresetName);
            Assert.Equal(new DateTime(2024, 3, 1), vm.Configuration.Filters.StartDate);
            Assert.Equal(new DateTime(2024, 3, 31), vm.Configuration.Filters.EndDate);
        });
    }

    [Fact]
    public void UndoingAPresetChange_PutsTheRangeBack()
    {
        InDesigner(vm =>
        {
            var before = (vm.Configuration.Filters.DatePresetName, vm.Configuration.Filters.StartDate, vm.Configuration.Filters.EndDate);

            vm.SelectedDatePresetOption = Preset(vm, DatePresetNames.LastYear);
            Assert.Equal(DatePresetNames.LastYear, vm.Configuration.Filters.DatePresetName);

            vm.UndoRedoManager.Undo();

            Assert.Equal(before, (vm.Configuration.Filters.DatePresetName, vm.Configuration.Filters.StartDate, vm.Configuration.Filters.EndDate));
        });
    }

    private static CustomMonthOption Month(ReportsPageViewModel vm, int month) =>
        vm.CustomPeriodMonths.Single(m => m.Month == month);

    private static string PeriodShownOnThePage(ReportsPageViewModel vm)
    {
        using var renderer = new ReportRenderer(vm.Configuration, null);
        var text = typeof(ReportRenderer).GetMethod("GetDateRangeText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (string)text.Invoke(renderer, ["yyyy-MM-dd"])!;
    }

    [Fact]
    public void PickingAYear_MakesTheReportThatWholeYear()
    {
        InDesigner(vm =>
        {
            vm.SelectedDatePresetOption = Preset(vm, DatePresetNames.Custom);

            vm.CustomPeriodYear = 2024;

            Assert.Equal(DatePresetNames.Custom, vm.Configuration.Filters.DatePresetName);
            Assert.Equal(new DateTime(2024, 1, 1), vm.Configuration.Filters.StartDate);
            Assert.Equal(new DateTime(2024, 12, 31), vm.Configuration.Filters.EndDate);
            Assert.Contains("2024-01-01", PeriodShownOnThePage(vm));
            Assert.Contains("2024-12-31", PeriodShownOnThePage(vm));
        });
    }

    [Fact]
    public void PickingAMonth_NarrowsTheYearToThatMonth_AndTheWholeYearWidensItAgain()
    {
        InDesigner(vm =>
        {
            vm.SelectedDatePresetOption = Preset(vm, DatePresetNames.Custom);
            vm.CustomPeriodYear = 2024;

            vm.CustomPeriodMonth = Month(vm, 2);

            Assert.Equal(new DateTime(2024, 2, 1), vm.Configuration.Filters.StartDate);
            Assert.Equal(new DateTime(2024, 2, 29), vm.Configuration.Filters.EndDate);

            vm.CustomPeriodMonth = vm.CustomPeriodMonths[0];

            Assert.Equal(new DateTime(2024, 12, 31), vm.Configuration.Filters.EndDate);
        });
    }

    [Fact]
    public void PickingAMonthWithNoYear_MeansThisYear()
    {
        InDesigner(vm =>
        {
            vm.SelectedDatePresetOption = Preset(vm, DatePresetNames.Custom);

            vm.CustomPeriodMonth = Month(vm, 1);

            Assert.Equal(DateTime.Today.Year, vm.CustomPeriodYear);
            Assert.Equal(new DateTime(DateTime.Today.Year, 1, 1), vm.Configuration.Filters.StartDate);
        });
    }

    [Fact]
    public void DatesTypedByHand_ShowAsTheYearOrMonthTheyMakeUp()
    {
        InDesigner(vm =>
        {
            vm.SelectedDatePresetOption = Preset(vm, DatePresetNames.Custom);

            vm.CustomStartDate = new DateTimeOffset(new DateTime(2024, 3, 1));
            vm.CustomEndDate = new DateTimeOffset(new DateTime(2024, 3, 31));

            Assert.Equal(2024, vm.CustomPeriodYear);
            Assert.Equal(3, vm.CustomPeriodMonth?.Month);

            vm.CustomEndDate = new DateTimeOffset(new DateTime(2024, 3, 20));

            Assert.Null(vm.CustomPeriodYear);
            Assert.Null(vm.CustomPeriodMonth);
            Assert.Equal(new DateTime(2024, 3, 20), vm.Configuration.Filters.EndDate);
        });
    }

    [Fact]
    public void ThePeriodOnThePage_FollowsEveryChangeOfDates()
    {
        InDesigner(vm =>
        {
            var lastYear = DateTime.Today.Year - 1;

            vm.SelectedDatePresetOption = Preset(vm, DatePresetNames.LastYear);
            Assert.Contains($"{lastYear}-01-01", PeriodShownOnThePage(vm));

            vm.SelectedDatePresetOption = Preset(vm, DatePresetNames.Custom);
            vm.CustomPeriodYear = 2021;
            Assert.Contains("2021-01-01", PeriodShownOnThePage(vm));

            vm.CustomPeriodMonth = Month(vm, 7);
            Assert.Contains("2021-07-31", PeriodShownOnThePage(vm));

            vm.SelectedDatePresetOption = Preset(vm, DatePresetNames.LastYear);
            Assert.Contains($"{lastYear}-12-31", PeriodShownOnThePage(vm));
        });
    }

    [Fact]
    public void TheComparisonColumns_UseThePeriodOnThePage()
    {
        InDesigner(vm =>
        {
            vm.SelectedDatePresetOption = Preset(vm, DatePresetNames.Custom);
            vm.CustomPeriodYear = 2024;
            vm.CustomPeriodMonth = Month(vm, 3);
            var data = new ArgoBooks.Core.Data.CompanyData();
            data.Settings.Localization.Currency = "USD";
            data.Revenues.Add(new ArgoBooks.Core.Models.Transactions.Revenue { Id = "R1", Date = new DateTime(2024, 3, 10), OriginalCurrency = "USD", Subtotal = 100m, Total = 100m, TotalUSD = 100m });
            data.Revenues.Add(new ArgoBooks.Core.Models.Transactions.Revenue { Id = "R2", Date = new DateTime(2024, 4, 10), OriginalCurrency = "USD", Subtotal = 900m, Total = 900m, TotalUSD = 900m });
            data.Revenues.Add(new ArgoBooks.Core.Models.Transactions.Revenue { Id = "R3", Date = new DateTime(2023, 3, 10), OriginalCurrency = "USD", Subtotal = 70m, Total = 70m, TotalUSD = 70m });

            var report = new AccountingReportDataService(data, vm.Configuration.Filters)
                .GetReportData(ArgoBooks.Core.Enums.AccountingReportType.IncomeStatement, ArgoBooks.Core.Enums.IncomeComparison.LastYear);

            var revenue = report.Rows.Single(r => r.Label == "Total Revenue").Values;
            Assert.Equal(["$100.00", "$70.00", "$30.00"], revenue);
        });
    }
}
