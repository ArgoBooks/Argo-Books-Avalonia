using System.Reflection;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// A card statement lists purchases as positive amounts, so every purchase would come in as a
/// sale. Swapping money in and out for the whole statement fixes that in one step.
/// </summary>
public class BankStatementImportSwapSignsTests : ModalViewModelTestBase
{
    private static readonly DateTime Day = new(2026, 5, 1);

    private static readonly MethodInfo PopulateRows =
        typeof(BankStatementImportModalViewModel).GetMethod("PopulateRows", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static BankStatementLine Line(decimal amount, string description) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Date = Day,
        Description = description,
        Amount = amount
    };

    private static BankStatementImportModalViewModel Open(params BankStatementLine[] lines)
    {
        var vm = new BankStatementImportModalViewModel();
        PopulateRows.Invoke(vm, [lines.ToList()]);
        return vm;
    }

    [Fact]
    public void Swapping_TurnsCardPurchasesIntoExpenses_AndTheCardPaymentIntoMoneyIn()
    {
        var vm = Open(Line(45.50m, "GROCER"), Line(-300m, "PAYMENT THANK YOU"));

        vm.SwapMoneyInAndOutCommand.Execute(null);

        Assert.Equal(-45.50m, vm.Rows[0].Amount);
        Assert.False(vm.Rows[0].CreateAsRevenue);
        Assert.Equal(300m, vm.Rows[1].Amount);
        Assert.True(vm.Rows[1].CreateAsRevenue);
    }

    [Fact]
    public void Swapping_ChecksAgainWhichLinesAreAlreadyInTheBooks()
    {
        Company.Expenses.Add(new Expense { Id = "EXP-1", Date = Day, Total = 45.50m, Description = "GROCER" });
        var vm = Open(Line(45.50m, "GROCER"));
        Assert.False(vm.Rows[0].AlreadyImported);

        vm.SwapMoneyInAndOutCommand.Execute(null);

        Assert.True(vm.Rows[0].AlreadyImported);
    }
}
