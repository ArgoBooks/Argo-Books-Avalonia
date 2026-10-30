using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// The expense form's Paid box, which makes an expense a bill to pay by a due date, and marking a
/// bill paid from the Expenses page.
/// </summary>
public class ExpenseBillTests : ModalViewModelTestBase
{
    private ExpenseModalsViewModel NewForm()
    {
        Company.Settings.Localization.Currency = "USD";
        Company.Products.Add(new Product { Id = "P1", Name = "Rent", CostPrice = 500m });
        var vm = new ExpenseModalsViewModel();
        vm.OpenAddModal();
        var line = vm.LineItems.First();
        line.SelectedProduct = vm.ProductOptions.First(p => p.Id == "P1");
        line.Quantity = 1;
        line.UnitPrice = 500m;
        return vm;
    }

    private static ExpenseDisplayItem Row(Expense expense) => new() { Id = expense.Id };

    [Fact]
    public async Task SavingWithPaidTicked_RecordsAnOrdinaryExpense()
    {
        var vm = NewForm();

        await vm.SaveTransactionCommand.ExecuteAsync(null);

        var expense = Assert.Single(Company.Expenses);
        Assert.Null(expense.DueDate);
        Assert.Null(expense.PaidDate);
        Assert.True(expense.IsPaid);
    }

    [Fact]
    public async Task SavingWithPaidUnticked_RecordsABillDueAMonthLater()
    {
        var vm = NewForm();
        var date = new DateTime(2024, 5, 1);
        vm.ModalDate = new DateTimeOffset(date);

        vm.ModalBillPaid = false;
        await vm.SaveTransactionCommand.ExecuteAsync(null);

        var expense = Assert.Single(Company.Expenses);
        Assert.Equal(date.AddDays(30), expense.DueDate);
        Assert.False(expense.IsPaid);
    }

    [Fact]
    public async Task EditingABill_AndTickingPaid_RecordsTheDayItWasPaid_AndKeepsTheDueDate()
    {
        var vm = NewForm();
        vm.ModalBillPaid = false;
        vm.ModalBillDueDate = new DateTimeOffset(new DateTime(2024, 6, 1));
        await vm.SaveTransactionCommand.ExecuteAsync(null);
        var expense = Assert.Single(Company.Expenses);

        vm.OpenEditModal(Row(expense));
        Assert.True(vm.IsEditingBill);
        Assert.False(vm.ModalBillPaid);
        vm.ModalBillPaid = true;
        Assert.True(vm.ShowBillPaidDate);
        vm.ModalBillPaidDate = new DateTimeOffset(new DateTime(2024, 5, 28));
        await vm.SaveTransactionCommand.ExecuteAsync(null);

        Assert.Equal(new DateTime(2024, 6, 1), expense.DueDate);
        Assert.Equal(new DateTime(2024, 5, 28), expense.PaidDate);

        Undo();
        Assert.Null(expense.PaidDate);
        Assert.Equal(new DateTime(2024, 6, 1), expense.DueDate);
    }

    [Fact]
    public async Task MarkingABillPaid_PaysItToday_WithUndoAndRedo()
    {
        var vm = NewForm();
        vm.ModalBillPaid = false;
        await vm.SaveTransactionCommand.ExecuteAsync(null);
        var expense = Assert.Single(Company.Expenses);

        vm.MarkBillPaid(Row(expense));
        Assert.Equal(DateTime.Today, expense.PaidDate);

        Undo();
        Assert.Null(expense.PaidDate);

        Redo();
        Assert.Equal(DateTime.Today, expense.PaidDate);
    }

    [Fact]
    public void ExpensesPage_ShowsUnpaidAndOverdueBills_AndWhatIsOwed()
    {
        Company.Settings.Localization.Currency = "USD";
        Company.Expenses.Add(new Expense
        {
            Id = "B1", Date = DateTime.Today.AddDays(-40), OriginalCurrency = "USD", Total = 200m, TotalUSD = 200m,
            DueDate = DateTime.Today.AddDays(-10)
        });
        Company.Expenses.Add(new Expense
        {
            Id = "B2", Date = DateTime.Today.AddDays(-5), OriginalCurrency = "USD", Total = 100m, TotalUSD = 100m,
            DueDate = DateTime.Today.AddDays(25)
        });
        Company.Expenses.Add(new Expense
        {
            Id = "E1", Date = DateTime.Today, OriginalCurrency = "USD", Total = 50m, TotalUSD = 50m
        });

        var page = new ExpensesPageViewModel();

        Assert.Equal("Overdue", page.Expenses.Single(e => e.Id == "B1").StatusDisplay);
        Assert.Equal("Unpaid", page.Expenses.Single(e => e.Id == "B2").StatusDisplay);
        Assert.Equal("Completed", page.Expenses.Single(e => e.Id == "E1").StatusDisplay);
        Assert.Contains("300", page.BillsToPay);
        Assert.Contains("200", page.BillsToPaySubtext);
        page.Cleanup();
    }
}
