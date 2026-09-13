using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class BankMatchingServiceTests
{
    private static CompanyData NewCompany() => new();

    private static BankStatementLine Line(decimal amount, DateTime date, string desc) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Date = date,
        Amount = amount,
        Description = desc,
        MatchStatus = BankLineMatchStatus.Unmatched
    };

    [Fact]
    public void MatchDeterministic_ExactAmountAndDate_AutoMatchesExpenseAndSetsFlag()
    {
        var date = new DateTime(2025, 1, 5);
        var data = NewCompany();
        var expense = new Expense { Id = "EXP-1", Total = 263.38m, Date = date, Description = "Milwaukee Supply" };
        data.Expenses.Add(expense);

        var line = Line(-263.38m, date, "MILWAUKEE SUPPLY CO POS");
        var result = new BankMatchingService().MatchDeterministic([line], data, new BankMatchingOptions());

        Assert.Equal(BankLineMatchStatus.Matched, line.MatchStatus);
        Assert.Equal(1, result.AutoMatchedCount);
        Assert.True(expense.BankMatched);
        Assert.Equal(line.Id, expense.BankMatchedLineId);
        Assert.Equal(BookRecordType.Expense, line.MatchedRecordType);
        Assert.Equal("EXP-1", line.MatchedRecordId);
    }

    [Fact]
    public void MatchDeterministic_ExactAmountWithinWindowNoDesc_Suggests()
    {
        var data = NewCompany();
        // 3 days apart, no descriptive overlap => below auto threshold, above suggest threshold.
        data.Expenses.Add(new Expense { Id = "EXP-1", Total = 100m, Date = new DateTime(2025, 1, 1), Description = "zzzz" });

        var line = Line(-100m, new DateTime(2025, 1, 4), "qqqq");
        var result = new BankMatchingService().MatchDeterministic([line], data, new BankMatchingOptions());

        Assert.Equal(BankLineMatchStatus.Suggested, line.MatchStatus);
        Assert.Equal(1, result.SuggestedCount);
        Assert.True(result.CandidatesByLineId.ContainsKey(line.Id));
        Assert.False(data.Expenses[0].BankMatched);
    }

    [Fact]
    public void MatchDeterministic_NoAmountMatch_LineUnmatchedAndRecordListedAsUnmatched()
    {
        var data = NewCompany();
        data.Expenses.Add(new Expense { Id = "EXP-1", Total = 999m, Date = new DateTime(2025, 1, 1), Description = "unrelated" });

        var line = Line(-100m, new DateTime(2025, 1, 1), "anything");
        var result = new BankMatchingService().MatchDeterministic([line], data, new BankMatchingOptions());

        Assert.Equal(BankLineMatchStatus.Unmatched, line.MatchStatus);
        Assert.Equal(1, result.UnmatchedLineCount);
        Assert.Contains(result.UnmatchedBookRecords, r => r.Id == "EXP-1");
    }

    [Fact]
    public void MatchDeterministic_AlreadyMatchedRecord_IsNotReused()
    {
        var date = new DateTime(2025, 1, 5);
        var data = NewCompany();
        data.Expenses.Add(new Expense { Id = "EXP-1", Total = 50m, Date = date, Description = "x", BankMatched = true });

        var line = Line(-50m, date, "x");
        new BankMatchingService().MatchDeterministic([line], data, new BankMatchingOptions());

        Assert.Equal(BankLineMatchStatus.Unmatched, line.MatchStatus);
    }

    [Fact]
    public void MatchDeterministic_MoneyIn_MatchesRevenue()
    {
        var date = new DateTime(2025, 2, 10);
        var data = NewCompany();
        data.Revenues.Add(new Revenue { Id = "REV-1", Total = 1343.75m, Date = date, Description = "Alice Johnson" });

        var line = Line(1343.75m, date, "ACH DEPOSIT ALICE JOHNSON");
        new BankMatchingService().MatchDeterministic([line], data, new BankMatchingOptions());

        Assert.Equal(BankLineMatchStatus.Matched, line.MatchStatus);
        Assert.True(data.Revenues[0].BankMatched);
    }

    [Fact]
    public void ConfirmThenUnlink_TogglesRecordFlag()
    {
        var data = NewCompany();
        var expense = new Expense { Id = "EXP-1", Total = 100m, Date = new DateTime(2025, 1, 1), Description = "x" };
        data.Expenses.Add(expense);

        var line = Line(-100m, new DateTime(2025, 1, 1), "x");
        var candidate = new BankMatchCandidate
        {
            LineId = line.Id, RecordType = BookRecordType.Expense, RecordId = "EXP-1", Confidence = 0.8
        };
        var svc = new BankMatchingService();

        svc.ConfirmMatch(line, candidate, data);
        Assert.True(expense.BankMatched);
        Assert.Equal(BankLineMatchStatus.Matched, line.MatchStatus);

        svc.UnlinkMatch(line, data);
        Assert.False(expense.BankMatched);
        Assert.Null(expense.BankMatchedLineId);
        Assert.Equal(BankLineMatchStatus.Unmatched, line.MatchStatus);
    }

    private static BankMatchCandidate Candidate(BankStatementLine line, string recordId) => new()
    {
        LineId = line.Id, RecordType = BookRecordType.Expense, RecordId = recordId, Confidence = 0.8
    };

    // One record backing two lines means unlinking either clears the flag the other still relies on.
    [Fact]
    public void ConfirmMatch_RecordAlreadyMatchedToAnotherLine_IsRefused()
    {
        var data = NewCompany();
        var expense = new Expense { Id = "EXP-1", Total = 100m, Date = new DateTime(2025, 1, 1), Description = "x" };
        data.Expenses.Add(expense);
        var first = Line(-100m, new DateTime(2025, 1, 1), "x");
        var second = Line(-100m, new DateTime(2025, 1, 2), "x");
        var svc = new BankMatchingService();

        Assert.True(svc.ConfirmMatch(first, Candidate(first, "EXP-1"), data));
        var accepted = svc.ConfirmMatch(second, Candidate(second, "EXP-1"), data);

        Assert.False(accepted);
        Assert.NotEqual(BankLineMatchStatus.Matched, second.MatchStatus);
        Assert.Null(second.MatchedRecordId);
        Assert.Equal(first.Id, expense.BankMatchedLineId);
    }

    private static readonly DateTime RecordDate = new(2025, 3, 1);

    private static BankStatementLine MatchedLine(string id, BookRecordType type, string recordId) => new()
    {
        Id = id,
        Date = RecordDate,
        Amount = -12.34m,
        Description = "x",
        MatchStatus = BankLineMatchStatus.Matched,
        MatchedRecordType = type,
        MatchedRecordId = recordId
    };

    // Amount 999 so a released line can't simply re-match the same record.
    private static void AddRecord(CompanyData data, BookRecordType type, string id, bool matched, string? lineId)
    {
        switch (type)
        {
            case BookRecordType.Expense:
                data.Expenses.Add(new Expense { Id = id, Total = 999m, Date = RecordDate, BankMatched = matched, BankMatchedLineId = lineId });
                break;
            case BookRecordType.Revenue:
                data.Revenues.Add(new Revenue { Id = id, Total = 999m, Date = RecordDate, BankMatched = matched, BankMatchedLineId = lineId });
                break;
            case BookRecordType.Invoice:
                data.Invoices.Add(new Invoice { Id = id, Total = 999m, IssueDate = RecordDate, BankMatched = matched, BankMatchedLineId = lineId });
                break;
            case BookRecordType.Payment:
                data.Payments.Add(new Payment { Id = id, Amount = 999m, Date = RecordDate, BankMatched = matched, BankMatchedLineId = lineId });
                break;
        }
    }

    // Matching skips Matched lines, so a line whose record was deleted stays Matched forever
    // unless matching notices the record is gone.
    [Fact]
    public void MatchDeterministic_MatchedLineWhoseRecordWasDeleted_GoesBackToUnmatched()
    {
        var line = MatchedLine("L1", BookRecordType.Expense, "EXP-GONE");

        new BankMatchingService().MatchDeterministic([line], NewCompany(), new BankMatchingOptions());

        Assert.Equal(BankLineMatchStatus.Unmatched, line.MatchStatus);
        Assert.Null(line.MatchedRecordId);
        Assert.Null(line.MatchedRecordType);
    }

    // Undoing an import restores the id counters, so the next new record can take the id a stale
    // line still points at. That record isn't flagged to the line, so the line must be released.
    [Theory]
    [InlineData(BookRecordType.Expense)]
    [InlineData(BookRecordType.Revenue)]
    [InlineData(BookRecordType.Invoice)]
    [InlineData(BookRecordType.Payment)]
    public void MatchDeterministic_MatchedLineWhoseRecordIdWasReused_GoesBackToUnmatched(BookRecordType type)
    {
        var data = NewCompany();
        AddRecord(data, type, "REC-1", matched: false, lineId: null);
        var line = MatchedLine("L1", type, "REC-1");

        new BankMatchingService().MatchDeterministic([line], data, new BankMatchingOptions());

        Assert.Equal(BankLineMatchStatus.Unmatched, line.MatchStatus);
        Assert.Null(line.MatchedRecordId);
    }

    [Theory]
    [InlineData(BookRecordType.Expense)]
    [InlineData(BookRecordType.Revenue)]
    [InlineData(BookRecordType.Invoice)]
    [InlineData(BookRecordType.Payment)]
    public void MatchDeterministic_MatchedLineItsRecordPointsBackTo_StaysMatched(BookRecordType type)
    {
        var data = NewCompany();
        AddRecord(data, type, "REC-1", matched: true, lineId: "L1");
        var line = MatchedLine("L1", type, "REC-1");

        new BankMatchingService().MatchDeterministic([line], data, new BankMatchingOptions());

        Assert.Equal(BankLineMatchStatus.Matched, line.MatchStatus);
        Assert.Equal("REC-1", line.MatchedRecordId);
    }

    [Fact]
    public void MatchDeterministic_TwoLinesMatchedToOneRecord_ReleasesTheLineTheRecordDoesNotPointTo()
    {
        var data = NewCompany();
        AddRecord(data, BookRecordType.Expense, "EXP-1", matched: true, lineId: "B");
        var a = MatchedLine("A", BookRecordType.Expense, "EXP-1");
        var b = MatchedLine("B", BookRecordType.Expense, "EXP-1");

        new BankMatchingService().MatchDeterministic([a, b], data, new BankMatchingOptions());

        Assert.Equal(BankLineMatchStatus.Unmatched, a.MatchStatus);
        Assert.Equal(BankLineMatchStatus.Matched, b.MatchStatus);
    }
}
