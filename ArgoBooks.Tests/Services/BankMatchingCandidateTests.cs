using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class BankMatchingCandidateTests
{
    private static readonly DateTime Day = new(2025, 4, 10);

    private static BankStatementLine Line(decimal amount, DateTime date, string desc) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Date = date,
        Amount = amount,
        Description = desc,
        MatchStatus = BankLineMatchStatus.Unmatched
    };

    #region Ambiguity

    /// <summary>
    /// Two $50 coffee expenses on the same day both fit a single $50 bank line equally well. Picking
    /// one silently would leave the other looking unpaid, so the line must be offered as a suggestion
    /// with both records and neither record may be flagged as matched.
    /// </summary>
    [Fact]
    public void MatchDeterministic_TwoEqualRecords_SuggestsBothAndMatchesNeither()
    {
        var data = new CompanyData();
        var a = new Expense { Id = "EXP-1", Total = 50m, Date = Day, Description = "Coffee" };
        var b = new Expense { Id = "EXP-2", Total = 50m, Date = Day, Description = "Coffee" };
        data.Expenses.Add(a);
        data.Expenses.Add(b);
        var line = Line(-50m, Day, "COFFEE");

        var result = new BankMatchingService().MatchDeterministic([line], data, new BankMatchingOptions());

        Assert.Equal(BankLineMatchStatus.Suggested, line.MatchStatus);
        Assert.Equal(0, result.AutoMatchedCount);
        Assert.False(a.BankMatched);
        Assert.False(b.BankMatched);
        Assert.Equal(["EXP-1", "EXP-2"], result.CandidatesByLineId[line.Id].Select(c => c.RecordId).Order());
    }

    /// <summary>
    /// When one of two same-amount records is on the line's date and the other is days away, the
    /// choice is clear, so the line is auto-matched and it is the same-day record that gets flagged.
    /// </summary>
    [Fact]
    public void MatchDeterministic_OneRecordClearlyCloser_AutoMatchesTheCloserOne()
    {
        var data = new CompanyData();
        var sameDay = new Expense { Id = "EXP-1", Total = 50m, Date = Day, Description = "Coffee" };
        var later = new Expense { Id = "EXP-2", Total = 50m, Date = Day.AddDays(4), Description = "Coffee" };
        data.Expenses.Add(later);
        data.Expenses.Add(sameDay);
        var line = Line(-50m, Day, "COFFEE");

        new BankMatchingService().MatchDeterministic([line], data, new BankMatchingOptions());

        Assert.Equal(BankLineMatchStatus.Matched, line.MatchStatus);
        Assert.Equal("EXP-1", line.MatchedRecordId);
        Assert.True(sameDay.BankMatched);
        Assert.False(later.BankMatched);
    }

    #endregion

    #region Amount and direction

    /// <summary>
    /// With no tolerance set, a line that is one cent off is a different amount, so it must not be
    /// offered as a match at all.
    /// </summary>
    [Fact]
    public void FindCandidates_OneCentOffWithNoTolerance_ReturnsNothing()
    {
        var data = new CompanyData();
        data.Expenses.Add(new Expense { Id = "EXP-1", Total = 50m, Date = Day, Description = "Staples" });

        var candidates = new BankMatchingService().FindCandidates(Line(-50.01m, Day, "STAPLES"), data, new BankMatchingOptions());

        Assert.Empty(candidates);
    }

    /// <summary>
    /// An amount that only fits within the tolerance is close but not equal, so the user has to
    /// confirm it: the line is suggested and the record stays unmatched.
    /// </summary>
    [Fact]
    public void MatchDeterministic_AmountWithinTolerance_SuggestsButNeverAutoMatches()
    {
        var data = new CompanyData();
        var expense = new Expense { Id = "EXP-1", Total = 50m, Date = Day, Description = "Staples" };
        data.Expenses.Add(expense);
        var line = Line(-50.03m, Day, "STAPLES");

        new BankMatchingService().MatchDeterministic([line], data, new BankMatchingOptions { AmountTolerance = 0.05m });

        Assert.Equal(BankLineMatchStatus.Suggested, line.MatchStatus);
        Assert.False(expense.BankMatched);
    }

    /// <summary>
    /// A near amount is only worth suggesting when the dates are close too; outside the date window
    /// a near amount is a coincidence and must not be offered.
    /// </summary>
    [Fact]
    public void FindCandidates_AmountWithinToleranceButOutsideDateWindow_ReturnsNothing()
    {
        var data = new CompanyData();
        data.Expenses.Add(new Expense { Id = "EXP-1", Total = 50m, Date = Day, Description = "Staples" });
        var options = new BankMatchingOptions { AmountTolerance = 0.05m };

        var candidates = new BankMatchingService().FindCandidates(Line(-50.03m, Day.AddDays(10), "STAPLES"), data, options);

        Assert.Empty(candidates);
    }

    /// <summary>
    /// A $50 deposit is money coming in and a $50 expense is money going out, so the two must never
    /// be paired, however well the date and text fit.
    /// </summary>
    [Fact]
    public void FindCandidates_DepositOfAnExpensesAmount_ReturnsNothing()
    {
        var data = new CompanyData();
        data.Expenses.Add(new Expense { Id = "EXP-1", Total = 50m, Date = Day, Description = "Staples" });

        var candidates = new BankMatchingService().FindCandidates(Line(50m, Day, "STAPLES"), data, new BankMatchingOptions());

        Assert.Empty(candidates);
    }

    #endregion

    #region Money-in ranking

    /// <summary>
    /// A deposit that fits a payment, the revenue and the invoice equally well is one sum of money.
    /// The payment is the actual cash event, so it must rank first, then revenue, then the invoice,
    /// so the same money isn't matched to the invoice as well as to its payment.
    /// </summary>
    [Fact]
    public void FindCandidates_DepositTiedAcrossRecordTypes_RanksPaymentThenRevenueThenInvoice()
    {
        var data = new CompanyData();
        data.Invoices.Add(new Invoice { Id = "INV-1", InvoiceNumber = "INV-1", Total = 500m, IssueDate = Day });
        data.Revenues.Add(new Revenue { Id = "REV-1", Total = 500m, Date = Day });
        data.Payments.Add(new Payment { Id = "PAY-1", Amount = 500m, Date = Day });
        var options = new BankMatchingOptions
        {
            Scope = [BookRecordType.Revenue, BookRecordType.Invoice, BookRecordType.Payment]
        };

        var candidates = new BankMatchingService().FindCandidates(Line(500m, Day, "qqqq"), data, options);

        Assert.Equal(
            [BookRecordType.Payment, BookRecordType.Revenue, BookRecordType.Invoice],
            candidates.Select(c => c.RecordType));
    }

    #endregion

    #region Possible duplicates

    /// <summary>
    /// Two unmatched expenses of the same amount a couple of days apart may be the same purchase
    /// entered twice, so both must be flagged for the user to check.
    /// </summary>
    [Fact]
    public void MatchDeterministic_SameAmountRecordsDaysApart_FlagsBothAsPossibleDuplicates()
    {
        var data = new CompanyData();
        data.Expenses.Add(new Expense { Id = "EXP-1", Total = 80m, Date = Day, Description = "Fuel" });
        data.Expenses.Add(new Expense { Id = "EXP-2", Total = 80m, Date = Day.AddDays(2), Description = "Fuel" });

        var result = new BankMatchingService().MatchDeterministic([], data, new BankMatchingOptions());

        Assert.Equal(2, result.UnmatchedBookRecords.Count);
        Assert.All(result.UnmatchedBookRecords, r => Assert.True(r.IsPossibleDuplicate));
    }

    /// <summary>
    /// A monthly charge of the same amount is not a duplicate. Only the pair that falls within the
    /// date window is flagged, and the record a month earlier is left alone.
    /// </summary>
    [Fact]
    public void MatchDeterministic_SameAmountMonthApart_FlagsOnlyTheClosePair()
    {
        var data = new CompanyData();
        data.Expenses.Add(new Expense { Id = "EXP-MAR", Total = 30m, Date = Day.AddDays(-30), Description = "Hosting" });
        data.Expenses.Add(new Expense { Id = "EXP-APR", Total = 30m, Date = Day, Description = "Hosting" });
        data.Expenses.Add(new Expense { Id = "EXP-APR2", Total = 30m, Date = Day.AddDays(1), Description = "Hosting" });

        var result = new BankMatchingService().MatchDeterministic([], data, new BankMatchingOptions());

        var flagged = result.UnmatchedBookRecords.Where(r => r.IsPossibleDuplicate).Select(r => r.Id).Order();
        Assert.Equal(["EXP-APR", "EXP-APR2"], flagged);
    }

    /// <summary>
    /// A $40 expense and a $40 sale on the same day move money in opposite directions, so they are
    /// not duplicates of each other.
    /// </summary>
    [Fact]
    public void MatchDeterministic_ExpenseAndRevenueOfSameAmount_AreNotFlaggedAsDuplicates()
    {
        var data = new CompanyData();
        data.Expenses.Add(new Expense { Id = "EXP-1", Total = 40m, Date = Day, Description = "x" });
        data.Revenues.Add(new Revenue { Id = "REV-1", Total = 40m, Date = Day, Description = "x" });

        var result = new BankMatchingService().MatchDeterministic([], data, new BankMatchingOptions());

        Assert.DoesNotContain(result.UnmatchedBookRecords, r => r.IsPossibleDuplicate);
    }

    #endregion

    #region Manual match options

    /// <summary>
    /// The manual picker for money going out offers only unmatched expenses, and for money coming in
    /// only unmatched revenue. An expense already matched to another line is never offered, since
    /// one record backing two lines would count the same money twice.
    /// </summary>
    [Fact]
    public void GetManualMatchOptions_FiltersByDirectionAndLeavesOutMatchedRecords()
    {
        var data = new CompanyData();
        data.Expenses.Add(new Expense { Id = "EXP-1", Total = 50m, Date = Day, Description = "x" });
        data.Expenses.Add(new Expense { Id = "EXP-TAKEN", Total = 50m, Date = Day, Description = "x", BankMatched = true, BankMatchedLineId = "OTHER" });
        data.Revenues.Add(new Revenue { Id = "REV-1", Total = 50m, Date = Day, Description = "x" });
        var svc = new BankMatchingService();
        var options = new BankMatchingOptions();

        var forMoneyOut = svc.GetManualMatchOptions(Line(-50m, Day, "x"), data, options);
        var forMoneyIn = svc.GetManualMatchOptions(Line(50m, Day, "x"), data, options);

        Assert.Equal(["EXP-1"], forMoneyOut.Select(c => c.RecordId));
        Assert.Equal(["REV-1"], forMoneyIn.Select(c => c.RecordId));
    }

    #endregion
}
