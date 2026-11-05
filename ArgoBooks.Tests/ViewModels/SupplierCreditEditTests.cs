using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// A supplier credit, the refund recorded when a purchase is marked returned, carries a copy of the
/// purchase's line, so the expense form would rebuild it as the whole purchase. It is changed through its return.
/// </summary>
public class SupplierCreditEditTests : ModalViewModelTestBase
{
    private Expense Credit()
    {
        Company.Settings.Localization.Currency = "USD";
        var credit = new Expense
        {
            Id = "PUR-2", Date = new DateTime(2024, 3, 1), OriginalCurrency = "USD",
            Description = "Refund for PUR-1",
            Amount = -79.64m, TaxAmount = -10.35m, Total = -89.99m, TotalUSD = -89.99m,
            LineItems = [new() { Description = "Bundle", Quantity = 1, UnitPrice = 2437.46m }]
        };
        Company.Expenses.Add(credit);
        Company.Returns.Add(new Return
        {
            Id = "RET-1", ReturnType = "Expense", OriginalTransactionId = "PUR-1", RefundRecordId = "PUR-2",
            RefundAmount = 89.99m, Items = [new() { Quantity = 1 }]
        });
        return credit;
    }

    [Fact]
    public void EditingASupplierCredit_OpensNoForm()
    {
        var credit = Credit();
        var vm = new ExpenseModalsViewModel();

        vm.OpenEditModal(new ExpenseDisplayItem { Id = credit.Id });

        Assert.False(vm.IsAddEditModalOpen);
        Assert.Equal(-89.99m, credit.Total);
    }

    [Fact]
    public void ExpensesPage_ShowsTheReturnOnThePurchase_AndNoRowForTheCredit()
    {
        var credit = Credit();
        Company.Expenses.Add(new Expense
        {
            Id = "PUR-1", Date = new DateTime(2024, 2, 1), OriginalCurrency = "USD", Description = "Bundle",
            Amount = 79.64m, TaxAmount = 10.35m, Total = 89.99m, TotalUSD = 89.99m,
            LineItems = [new() { Description = "Bundle", Quantity = 1, UnitPrice = 79.64m }]
        });

        var page = new ExpensesPageViewModel();
        var row = page.Expenses.Single();

        Assert.Equal("PUR-1", row.Id);
        Assert.DoesNotContain(page.Expenses, e => e.Id == credit.Id);
        Assert.True(row.IsReturned);
        Assert.True(row.HasReturn);
        Assert.False(row.CanMarkAsReturned);
        Assert.False(row.CanMarkAsLostDamaged);
        page.Cleanup();
    }

    [Fact]
    public void BulkEditing_LeavesASupplierCreditAlone()
    {
        var credit = Credit();
        Company.Suppliers.Add(new() { Id = "S9", Name = "Other" });
        var vm = new ExpenseModalsViewModel();

        var result = vm.SetCounterparty([credit.Id], "S9");

        Assert.Equal(0, result.Changed);
        Assert.Null(credit.SupplierId);
    }
}
