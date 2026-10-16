using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class BankMatchingRepointTests
{
    private static readonly DateTime Day = new(2025, 6, 2);

    private static BankStatementLine Line() => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Date = Day,
        Amount = -50m,
        Description = "x",
        MatchStatus = BankLineMatchStatus.Unmatched
    };

    private static BankMatchCandidate Candidate(BankStatementLine line, string recordId) => new()
    {
        LineId = line.Id, RecordType = BookRecordType.Expense, RecordId = recordId, Confidence = 0.8
    };

    private static (CompanyData Data, Expense First, Expense Second) TwoExpenses()
    {
        var data = new CompanyData();
        var first = new Expense { Id = "EXP-1", Total = 50m, Date = Day, Description = "x" };
        var second = new Expense { Id = "EXP-2", Total = 50m, Date = Day, Description = "x" };
        data.Expenses.Add(first);
        data.Expenses.Add(second);
        return (data, first, second);
    }

    /// <summary>
    /// Moving a line's match to a different record must release the record it was matched to before.
    /// Otherwise the old record stays flagged as matched with no line behind it and drops out of the
    /// unmatched list for good.
    /// </summary>
    [Fact]
    public void ConfirmMatch_DifferentRecordForAMatchedLine_ReleasesThePreviousRecord()
    {
        var (data, first, second) = TwoExpenses();
        var line = Line();
        var svc = new BankMatchingService();

        svc.ConfirmMatch(line, Candidate(line, "EXP-1"), data);
        var accepted = svc.ConfirmMatch(line, Candidate(line, "EXP-2"), data);

        Assert.True(accepted);
        Assert.False(first.BankMatched);
        Assert.Null(first.BankMatchedLineId);
        Assert.Null(first.BankMatchedDate);
        Assert.True(second.BankMatched);
        Assert.Equal(line.Id, second.BankMatchedLineId);
        Assert.Equal("EXP-2", line.MatchedRecordId);
    }

    /// <summary>
    /// When the new record already belongs to another line, the move is refused, and a refused move
    /// must leave everything as it was: the line keeps its old record and that record stays matched.
    /// </summary>
    [Fact]
    public void ConfirmMatch_RepointToARecordTakenByAnotherLine_KeepsTheExistingMatch()
    {
        var (data, first, second) = TwoExpenses();
        var line = Line();
        var other = Line();
        var svc = new BankMatchingService();
        svc.ConfirmMatch(other, Candidate(other, "EXP-2"), data);
        svc.ConfirmMatch(line, Candidate(line, "EXP-1"), data);

        var accepted = svc.ConfirmMatch(line, Candidate(line, "EXP-2"), data);

        Assert.False(accepted);
        Assert.Equal("EXP-1", line.MatchedRecordId);
        Assert.True(first.BankMatched);
        Assert.Equal(line.Id, first.BankMatchedLineId);
        Assert.Equal(other.Id, second.BankMatchedLineId);
    }

    /// <summary>
    /// Confirming the record a line is already matched to is not a move, so the record must stay
    /// matched to that line rather than being released.
    /// </summary>
    [Fact]
    public void ConfirmMatch_SameRecordAgain_StaysMatched()
    {
        var (data, first, _) = TwoExpenses();
        var line = Line();
        var svc = new BankMatchingService();

        svc.ConfirmMatch(line, Candidate(line, "EXP-1"), data);
        var accepted = svc.ConfirmMatch(line, Candidate(line, "EXP-1"), data);

        Assert.True(accepted);
        Assert.True(first.BankMatched);
        Assert.Equal(line.Id, first.BankMatchedLineId);
        Assert.Equal(BankLineMatchStatus.Matched, line.MatchStatus);
    }
}
