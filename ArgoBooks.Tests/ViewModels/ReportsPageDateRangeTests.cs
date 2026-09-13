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
}
