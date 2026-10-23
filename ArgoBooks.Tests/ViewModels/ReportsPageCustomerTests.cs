using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// The customer picked on step 1 for the Customer Statement, which needs one, and for Sales by
/// Customer, which can narrow to one.
/// </summary>
public class ReportsPageCustomerTests : ModalViewModelTestBase
{
    private void OnStep1(string templateName, Action<ReportsPageViewModel> test)
    {
        Company.Customers.Add(new Customer { Id = "CUS-1", Name = "Acme" });
        Company.Customers.Add(new Customer { Id = "CUS-2", Name = "Birch Co" });

        var templatesDir = Path.Combine(Path.GetTempPath(), "ArgoBooks_ReportCustomer_" + Guid.NewGuid().ToString("N")[..8]);
        var vm = new ReportsPageViewModel(new ReportTemplateStorage(templatesDir));
        try
        {
            vm.SelectedTemplateName = templateName;
            test(vm);
        }
        finally
        {
            vm.Cleanup();
            if (Directory.Exists(templatesDir))
                Directory.Delete(templatesDir, recursive: true);
        }
    }

    #region Customer Statement

    [Fact]
    public void Statement_NextWithoutACustomer_StaysOnStep1_AndShowsTheError()
    {
        OnStep1(ReportTemplateFactory.TemplateNames.CustomerStatement, vm =>
        {
            vm.GoToNextStepCommand.Execute(null);

            Assert.Equal(1, vm.CurrentStep);
            Assert.True(vm.HasStatementCustomerError);
        });
    }

    [Fact]
    public void Statement_ChoosingACustomer_ClearsTheError_AndNextUsesThem()
    {
        OnStep1(ReportTemplateFactory.TemplateNames.CustomerStatement, vm =>
        {
            vm.GoToNextStepCommand.Execute(null);
            vm.SelectedStatementCustomer = vm.StatementCustomerOptions.Single(c => c.Id == "CUS-2");

            Assert.False(vm.HasStatementCustomerError);

            vm.GoToNextStepCommand.Execute(null);

            Assert.Equal(2, vm.CurrentStep);
            Assert.Equal("CUS-2", vm.Configuration.Filters.CustomerId);
        });
    }

    [Fact]
    public void Statement_ChoosingAnotherTemplate_ClearsTheError()
    {
        OnStep1(ReportTemplateFactory.TemplateNames.CustomerStatement, vm =>
        {
            vm.GoToNextStepCommand.Execute(null);
            vm.SelectedTemplateName = ReportTemplateFactory.TemplateNames.IncomeStatement;

            Assert.False(vm.HasStatementCustomerError);
        });
    }

    [Fact]
    public void Statement_OnTheChartsTab_NextIsNotBlocked()
    {
        OnStep1(ReportTemplateFactory.TemplateNames.CustomerStatement, vm =>
        {
            vm.Step1TabIndex = 1;
            vm.GoToNextStepCommand.Execute(null);

            Assert.Equal(2, vm.CurrentStep);
            Assert.False(vm.HasStatementCustomerError);
        });
    }

    #endregion

    #region Sales by Customer

    [Fact]
    public void SalesByCustomer_StartsOnAllCustomers_AndReportsEveryone()
    {
        OnStep1(ReportTemplateFactory.TemplateNames.SalesByCustomer, vm =>
        {
            Assert.Equal(["All Customers", "Acme", "Birch Co"], vm.SalesCustomerOptions.Select(c => c.Name));
            Assert.Same(vm.SalesCustomerOptions[0], vm.SelectedSalesCustomer);

            vm.GoToNextStepCommand.Execute(null);

            Assert.Equal(2, vm.CurrentStep);
            Assert.Null(vm.Configuration.Filters.CustomerId);
        });
    }

    [Fact]
    public void SalesByCustomer_ChoosingACustomer_NarrowsTheReportToThem()
    {
        OnStep1(ReportTemplateFactory.TemplateNames.SalesByCustomer, vm =>
        {
            vm.SelectedSalesCustomer = vm.SalesCustomerOptions.Single(c => c.Id == "CUS-1");

            vm.GoToNextStepCommand.Execute(null);

            Assert.Equal("CUS-1", vm.Configuration.Filters.CustomerId);
        });
    }

    #endregion
}
