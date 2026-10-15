using System.Reflection;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

public class BankStatementImportAlreadyImportedTests : ModalViewModelTestBase
{
    private static readonly DateTime Day = new(2026, 5, 1);

    // OpenAsync needs a file and an AI pass, so the rows are built the way it builds them, through PopulateRows.
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

    /// <summary>
    /// A statement line that is already an expense in the books, by date, amount and description
    /// (ignoring case and stray spaces), is flagged so importing it again asks first. A different
    /// purchase on the same day is not flagged.
    /// </summary>
    [Fact]
    public void PopulateRows_LineAlreadyAnExpense_IsFlagged_AndOtherLinesAreNot()
    {
        Company.Expenses.Add(new Expense { Id = "EXP-1", Date = Day, Total = 50m, Description = "COFFEE SHOP" });

        var vm = Open(Line(-50m, "coffee shop "), Line(-80m, "FUEL"));

        Assert.True(vm.Rows[0].AlreadyImported);
        Assert.False(vm.Rows[1].AlreadyImported);
    }

    /// <summary>
    /// A deposit that is already a sale in the books is flagged. A payment out of the same amount,
    /// day and text is a different transaction (money going the other way), so it is not.
    /// </summary>
    [Fact]
    public void PopulateRows_DepositAlreadyARevenue_IsFlagged_ButMoneyOutOfTheSameAmountIsNot()
    {
        Company.Revenues.Add(new Revenue { Id = "REV-1", Date = Day, Total = 500m, Description = "CLIENT ACME" });

        var vm = Open(Line(500m, "CLIENT ACME"), Line(-500m, "CLIENT ACME"));

        Assert.True(vm.Rows[0].AlreadyImported);
        Assert.False(vm.Rows[1].AlreadyImported);
    }

    /// <summary>
    /// Two identical $4.50 coffees on one day are two purchases. With only one of them in the books,
    /// only one line may be flagged as already imported; flagging both invites the user to drop the
    /// second coffee, which then never reaches the books.
    /// </summary>
    [Fact]
    public void PopulateRows_TwoIdenticalLinesWithOneInTheBooks_FlagsOnlyOne()
    {
        Company.Expenses.Add(new Expense { Id = "EXP-1", Date = Day, Total = 4.50m, Description = "COFFEE" });

        var vm = Open(Line(-4.50m, "COFFEE"), Line(-4.50m, "COFFEE"));

        Assert.Equal(1, vm.Rows.Count(r => r.AlreadyImported));
    }
}
