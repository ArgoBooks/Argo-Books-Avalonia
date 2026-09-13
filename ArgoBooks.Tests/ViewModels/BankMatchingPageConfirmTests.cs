using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

public class BankMatchingPageConfirmTests : ModalViewModelTestBase
{
    // Two lines that both suggest the same expense.
    private (BankMatchingPageViewModel Vm, Expense Expense, BankStatementLine First, BankStatementLine Second, BankLineRow RowA, BankLineRow RowB) TwoLinesSuggestingOneExpense()
    {
        var expense = new Expense { Id = "PUR-2025-00001", Total = 100m, Date = new DateTime(2025, 1, 1), Description = "zzzz" };
        Company.Expenses.Add(expense);
        var first = new BankStatementLine { Id = "A", Date = new DateTime(2025, 1, 4), Description = "qqqq", Amount = -100m };
        var second = new BankStatementLine { Id = "B", Date = new DateTime(2025, 1, 4), Description = "qqqq", Amount = -100m };
        Company.BankImportSessions.Add(new BankImportSession { Id = "S", Lines = [first, second] });

        var vm = new BankMatchingPageViewModel();
        return (vm, expense, first, second, vm.Lines.Single(r => r.Line.Id == "A"), vm.Lines.Single(r => r.Line.Id == "B"));
    }

    // Accepting it on one has to take it off the other, or accepting the leftover suggestion there
    // links one expense to two bank lines.
    [Fact]
    public void AcceptingASuggestion_TakesTheRecordOffEveryOtherLine()
    {
        var (vm, expense, first, second, rowA, rowB) = TwoLinesSuggestingOneExpense();
        Assert.Equal(BankLineMatchStatus.Suggested, second.MatchStatus);

        vm.AcceptSuggestionCommand.Execute(rowA);
        vm.AcceptSuggestionCommand.Execute(rowB);

        Assert.Equal(BankLineMatchStatus.Matched, first.MatchStatus);
        Assert.NotEqual(BankLineMatchStatus.Matched, second.MatchStatus);
        Assert.Equal("A", expense.BankMatchedLineId);
        Assert.Null(rowB.TopCandidate);
    }

    // Undoing the accept frees the expense, so the other line offers it again without a page reload.
    [Fact]
    public void UndoingAnAcceptedSuggestion_GivesTheOtherLineItsSuggestionBack()
    {
        var (vm, expense, _, second, rowA, rowB) = TwoLinesSuggestingOneExpense();
        vm.AcceptSuggestionCommand.Execute(rowA);

        Undo();
        Assert.Equal(BankLineMatchStatus.Suggested, second.MatchStatus);
        Assert.Equal(expense.Id, rowB.TopCandidate?.RecordId);

        Redo();
        Assert.Equal("A", expense.BankMatchedLineId);
        Assert.Null(rowB.TopCandidate);

        Undo();
        vm.AcceptSuggestionCommand.Execute(rowB);
        Assert.Equal("B", expense.BankMatchedLineId);
    }
}
