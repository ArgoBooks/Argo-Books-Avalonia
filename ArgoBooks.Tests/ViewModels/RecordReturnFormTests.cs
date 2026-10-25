using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>The Record Return form the Revenue and Expenses pages open, where some or all of a sale or purchase is returned.</summary>
public class RecordReturnFormTests : ModalViewModelTestBase
{
    private Revenue Sale()
    {
        Company.Settings.Localization.Currency = "USD";
        var sale = new Revenue
        {
            Id = "REV-1", Date = new DateTime(2026, 1, 10), OriginalCurrency = "USD",
            Amount = 150m, TaxAmount = 15m, ShippingCost = 5m, Total = 170m, TotalUSD = 170m,
            PaymentStatus = RevenuePaymentStatus.Paid,
            LineItems =
            [
                new LineItem { Description = "Chair", Quantity = 2, UnitPrice = 50m },
                new LineItem { Description = "Lamp", Quantity = 1, UnitPrice = 50m }
            ]
        };
        Company.Revenues.Add(sale);
        return sale;
    }

    [Fact]
    public void Opening_ChoosesEverythingLeft_ButNotTheShipping()
    {
        var sale = Sale();
        var vm = new ReturnsModalsViewModel();

        vm.OpenRecordReturnModal(sale, ["Defective"]);

        Assert.True(vm.IsRecordReturnModalOpen);
        Assert.Equal([2m, 1m], vm.ReturnLines.Select(l => l.Quantity!.Value));
        Assert.True(vm.ShowRefundShipping);
        Assert.False(vm.RefundShipping);
        Assert.Contains("165", vm.ReturnRefundTotalText);
    }

    [Fact]
    public void LoweringAQuantity_UpdatesTheRefund()
    {
        var sale = Sale();
        var vm = new ReturnsModalsViewModel();
        vm.OpenRecordReturnModal(sale, ["Defective"]);

        vm.ReturnLines[0].Quantity = 1;
        vm.ReturnLines[1].Quantity = 0;

        Assert.Contains("55", vm.ReturnRefundTotalText);
    }

    [Fact]
    public void Recording_NeedsAReason()
    {
        var sale = Sale();
        var vm = new ReturnsModalsViewModel();
        vm.OpenRecordReturnModal(sale, ["Defective"]);

        vm.RecordReturnCommand.Execute(null);

        Assert.True(vm.HasRecordReturnReasonError);
        Assert.Empty(Company.Returns);
        Assert.True(vm.IsRecordReturnModalOpen);
    }

    [Fact]
    public void Recording_AddsTheReturnAndRefund_AndUndoTakesBothOut()
    {
        var sale = Sale();
        var vm = new ReturnsModalsViewModel();
        var recorded = 0;
        vm.ReturnRecorded += (_, _) => recorded++;
        vm.OpenRecordReturnModal(sale, ["Defective"]);
        vm.ReturnLines[1].Quantity = 0;
        vm.SelectedRecordReturnReason = "Defective";

        vm.RecordReturnCommand.Execute(null);

        var record = Assert.Single(Company.Returns);
        Assert.Equal("Defective", record.Items[0].Reason);
        Assert.Equal(-110m, Assert.Single(Company.Payments).Amount);
        Assert.False(vm.IsRecordReturnModalOpen);
        Assert.Equal(1, recorded);

        Undo();
        Assert.Empty(Company.Returns);
        Assert.Empty(Company.Payments);

        Redo();
        Assert.Single(Company.Returns);
        Assert.Single(Company.Payments);
    }

    [Fact]
    public void UndoReturn_OnTheRow_TakesBackTheLatestReturn()
    {
        var sale = Sale();
        var vm = new ReturnsModalsViewModel();
        vm.OpenRecordReturnModal(sale, ["Defective"]);
        vm.ReturnLines[1].Quantity = 0;
        vm.SelectedRecordReturnReason = "Defective";
        vm.RecordReturnCommand.Execute(null);
        vm.OpenRecordReturnModal(sale, ["Defective"]);
        vm.SelectedRecordReturnReason = "Defective";
        vm.RecordReturnCommand.Execute(null);
        var latest = Company.Returns.OrderBy(r => r.CreatedAt).Last();

        vm.OpenUndoLatestReturn(sale.Id);
        vm.ConfirmUndoReturnCommand.Execute(null);

        Assert.DoesNotContain(latest, Company.Returns);
        Assert.Single(Company.Returns);
        Assert.Single(Company.Payments);
    }

    private void ReturnPartOf(Transaction transaction)
    {
        var vm = new ReturnsModalsViewModel();
        vm.OpenRecordReturnModal(transaction, ["Defective"]);
        vm.ReturnLines[1].Quantity = 0;
        vm.SelectedRecordReturnReason = "Defective";
        vm.RecordReturnCommand.Execute(null);
    }

    [Fact]
    public void DeletingAReturnedSale_TakesItsReturnAndRefundWithIt_AndUndoPutsThemBack()
    {
        var sale = Sale();
        ReturnPartOf(sale);

        new RevenueModalsViewModel().DeleteRevenue(sale.Id);

        Assert.Empty(Company.Revenues);
        Assert.Empty(Company.Returns);
        Assert.Empty(Company.Payments);

        Undo();
        Assert.Single(Company.Revenues);
        Assert.Single(Company.Returns);
        Assert.Single(Company.Payments);
    }

    [Fact]
    public void DeletingReturnedPurchasesTogether_TakesTheirCreditsWithThem()
    {
        Company.Settings.Localization.Currency = "USD";
        var purchase = new Expense
        {
            Id = "PUR-1", Date = new DateTime(2026, 1, 5), OriginalCurrency = "USD",
            Amount = 30m, Total = 30m, TotalUSD = 30m,
            LineItems =
            [
                new LineItem { Description = "Paper", Quantity = 2, UnitPrice = 10m },
                new LineItem { Description = "Ink", Quantity = 1, UnitPrice = 10m }
            ]
        };
        Company.Expenses.Add(purchase);
        ReturnPartOf(purchase);
        Assert.Equal(2, Company.Expenses.Count);

        new ExpenseModalsViewModel().DeleteMany([purchase.Id]);

        Assert.Empty(Company.Expenses);
        Assert.Empty(Company.Returns);

        Undo();
        Assert.Equal(2, Company.Expenses.Count);
        Assert.Single(Company.Returns);
    }

    [Fact]
    public void ReturnedSale_CannotBeEdited_OrMarkedLost()
    {
        var sale = Sale();
        ReturnPartOf(sale);

        var page = new RevenuePageViewModel();
        var row = page.Revenue.Single();
        var modals = new RevenueModalsViewModel();
        modals.OpenEditModal(new RevenueDisplayItem { Id = sale.Id });

        Assert.True(row.IsPartialReturn);
        Assert.False(row.CanEdit);
        Assert.False(row.CanMarkAsLostDamaged);
        Assert.True(row.CanMarkAsReturned);
        Assert.False(modals.IsAddEditModalOpen);
        page.Cleanup();
    }

    [Fact]
    public void MarkingAReturnedBillPaid_PaysItsCreditToo_AndTheOwedTotalLeavesItOut()
    {
        Company.Settings.Localization.Currency = "USD";
        var bill = new Expense
        {
            Id = "PUR-1", Date = new DateTime(2026, 1, 5), DueDate = DateTime.Today.AddDays(10), OriginalCurrency = "USD",
            Amount = 30m, Total = 30m, TotalUSD = 30m,
            LineItems =
            [
                new LineItem { Description = "Paper", Quantity = 2, UnitPrice = 10m },
                new LineItem { Description = "Ink", Quantity = 1, UnitPrice = 10m }
            ]
        };
        Company.Expenses.Add(bill);
        ReturnPartOf(bill);
        var credit = Company.Expenses.Single(e => e.Id != bill.Id);

        var page = new ExpensesPageViewModel();
        Assert.Contains("10.00", page.BillsToPay);
        page.Cleanup();

        new ExpenseModalsViewModel().MarkBillPaid(new ExpenseDisplayItem { Id = bill.Id });
        Assert.True(credit.IsPaid);

        Undo();
        Assert.False(credit.IsPaid);
    }
}
