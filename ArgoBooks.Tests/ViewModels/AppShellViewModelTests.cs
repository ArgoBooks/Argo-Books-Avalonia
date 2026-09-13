using System.Reflection;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

// Shares the ModalViewModels collection: the plan-status tests below build real modal ViewModels and
// raise the process-wide App.PlanStatusChanged event, which every other modal ViewModel listens to.
public class AppShellViewModelTests : ModalViewModelTestBase
{
    private readonly AppShellViewModel _viewModel = new();

    [Fact]
    public void ShowUpdateBanner_SetsProperties()
    {
        _viewModel.ShowUpdateBanner("2.0");

        Assert.True(_viewModel.ShowUpdateAvailableBanner);
        Assert.False(string.IsNullOrEmpty(_viewModel.UpdateBannerMessage));
        Assert.Contains("2.0", _viewModel.UpdateBannerMessage);
    }

    [Fact]
    public void DismissUpdateBanner_HidesBanner()
    {
        _viewModel.ShowUpdateBanner("Test");
        _viewModel.DismissUpdateBannerCommand.Execute(null);

        Assert.False(_viewModel.ShowUpdateAvailableBanner);
    }

    [Fact]
    public void SetPlanStatus_BeforeTheInvoiceScreenExists_StillMakesItPremium()
    {
        // The invoice modals are only built when Invoices is first opened, which for a customer with
        // a saved licence is long after startup applied the plan. Getting this wrong sends a paying
        // customer through the free-tier send limit for the rest of the session.
        _viewModel.SetPlanStatus(true);

        Assert.True(_viewModel.InvoiceModalsViewModel.HasPremium);
    }

    [Fact]
    public void SetPlanStatus_AfterTheInvoiceScreenExists_ReachesIt()
    {
        var invoiceModals = _viewModel.InvoiceModalsViewModel;

        _viewModel.SetPlanStatus(true);
        Assert.True(invoiceModals.HasPremium);

        _viewModel.SetPlanStatus(false);
        Assert.False(invoiceModals.HasPremium);
    }

    [Fact]
    public void SetPlanStatus_DropsCachedScanServicesOnAnExistingReceiptScreen()
    {
        var receiptModals = _viewModel.ReceiptsModalsViewModel;
        var usageField = typeof(ReceiptsModalsViewModel)
            .GetField("_usageService", BindingFlags.Instance | BindingFlags.NonPublic)!;
        usageField.SetValue(receiptModals, new StubReceiptUsageService());

        _viewModel.SetPlanStatus(true);

        Assert.Null(usageField.GetValue(receiptModals));
    }

    private sealed class StubReceiptUsageService : IReceiptUsageService
    {
        public Task<UsageCheckResult> CheckUsageAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UsageCheckResult());

        public Task<UsageIncrementResult> IncrementUsageAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UsageIncrementResult());

        public void InvalidateCache() { }

        public UsageStatus? GetCachedUsage() => null;

        public void Dispose() { }
    }
}
