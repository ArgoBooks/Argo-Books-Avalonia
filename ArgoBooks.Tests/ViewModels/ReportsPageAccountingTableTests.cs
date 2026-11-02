using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// The designer's settings for an accounting table, which depend on the table selected and the report it shows.
/// </summary>
public class ReportsPageAccountingTableTests : ModalViewModelTestBase
{
    private static void InDesigner(Action<ReportsPageViewModel> test)
    {
        var templatesDir = Path.Combine(Path.GetTempPath(), "ArgoBooks_ReportTable_" + Guid.NewGuid().ToString("N")[..8]);
        var vm = new ReportsPageViewModel(new ReportTemplateStorage(templatesDir));
        try
        {
            vm.GoToNextStepCommand.Execute(null);
            test(vm);
        }
        finally
        {
            vm.Cleanup();
            if (Directory.Exists(templatesDir))
                Directory.Delete(templatesDir, recursive: true);
        }
    }

    [Fact]
    public void SelectingAnotherReportsTable_TellsTheComparisonOptionToUpdate()
    {
        InDesigner(vm =>
        {
            var income = new AccountingTableReportElement { ReportType = AccountingReportType.IncomeStatement };
            var balanceSheet = new AccountingTableReportElement { ReportType = AccountingReportType.BalanceSheet };
            var told = new List<string?>();
            vm.PropertyChanged += (_, e) => told.Add(e.PropertyName);

            vm.SelectedElement = balanceSheet;

            Assert.False(vm.ShowIncomeComparison);
            Assert.Contains(nameof(vm.ShowIncomeComparison), told);

            told.Clear();
            vm.SelectedElement = income;

            Assert.True(vm.ShowIncomeComparison);
            Assert.Contains(nameof(vm.ShowIncomeComparison), told);
        });
    }

    [Fact]
    public void ChangingTheSelectedTablesReport_TellsTheComparisonOptionToUpdate()
    {
        InDesigner(vm =>
        {
            var table = new AccountingTableReportElement { ReportType = AccountingReportType.IncomeStatement };
            vm.SelectedElement = table;
            var told = new List<string?>();
            vm.PropertyChanged += (_, e) => told.Add(e.PropertyName);

            table.ReportType = AccountingReportType.TrialBalance;

            Assert.False(vm.ShowIncomeComparison);
            Assert.Contains(nameof(vm.ShowIncomeComparison), told);
        });
    }

    [Fact]
    public void EveryAccountingTemplateCard_OpensABuiltInReport()
    {
        InDesigner(vm =>
        {
            foreach (var option in vm.AccountingTemplateOptions)
            {
                Assert.True(ReportTemplateFactory.IsBuiltInTemplate(option.TemplateName), option.TemplateName);
                Assert.Single(ReportTemplateFactory.CreateFromTemplate(option.TemplateName).Elements.OfType<AccountingTableReportElement>());
            }
        });
    }

    [Fact]
    public void EveryAccountingReportType_HasATemplateCard()
    {
        InDesigner(vm =>
        {
            var offered = vm.AccountingTemplateOptions
                .Select(o => ReportTemplateFactory.CreateFromTemplate(o.TemplateName).Elements.OfType<AccountingTableReportElement>().Single().ReportType)
                .ToHashSet();

            Assert.Empty(Enum.GetValues<AccountingReportType>().Where(t => !offered.Contains(t)));
        });
    }
}
