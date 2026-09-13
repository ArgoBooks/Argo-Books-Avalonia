using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for <see cref="RecurringInvoiceService"/> cadence math and idempotent catch-up generation.
/// </summary>
public class RecurringInvoiceServiceTests
{
    private static RecurringInvoice MakeSchedule(
        DateTime nextDate,
        Frequency freq = Frequency.Monthly,
        DateTime? endDate = null,
        RecurringInvoiceStatus status = RecurringInvoiceStatus.Active,
        string paymentTerms = "Net 30",
        bool withTemplate = true)
        => new()
        {
            Id = "REC-INV-00001",
            CustomerId = "CUST-1",
            Frequency = freq,
            StartDate = nextDate,
            NextInvoiceDate = nextDate,
            EndDate = endDate,
            Status = status,
            PaymentTerms = paymentTerms,
            Template = withTemplate ? new Invoice { CustomerId = "CUST-1", Total = 100m } : null
        };

    [Theory]
    [InlineData(Frequency.Weekly, 7)]
    [InlineData(Frequency.BiWeekly, 14)]
    public void AdvanceDate_AddsDays(Frequency freq, int days)
    {
        var start = new DateTime(2026, 3, 1);
        Assert.Equal(start.AddDays(days), RecurringInvoiceService.AdvanceDate(start, freq));
    }

    [Fact]
    public void AdvanceDate_Monthly_ClampsEndOfMonth()
    {
        Assert.Equal(
            new DateTime(2026, 2, 28),
            RecurringInvoiceService.AdvanceDate(new DateTime(2026, 1, 31), Frequency.Monthly));
    }

    [Fact]
    public void AdvanceDate_QuarterlyAndAnnually()
    {
        Assert.Equal(
            new DateTime(2026, 4, 1),
            RecurringInvoiceService.AdvanceDate(new DateTime(2026, 1, 1), Frequency.Quarterly));
        Assert.Equal(
            new DateTime(2027, 1, 1),
            RecurringInvoiceService.AdvanceDate(new DateTime(2026, 1, 1), Frequency.Annually));
    }

    [Theory]
    [InlineData("Net 30", 30)]
    [InlineData("Net 15", 15)]
    [InlineData("Net 7", 7)]
    [InlineData("Due on receipt", 0)]
    [InlineData("nonsense", 30)]
    [InlineData(null, 30)]
    // Arbitrary "Net N" offsets must round-trip, not collapse to the default 30.
    [InlineData("Net 20", 20)]
    [InlineData("net 45", 45)]
    [InlineData("Net 90", 90)]
    public void PaymentTermsDays_Parses(string? terms, int days)
        => Assert.Equal(days, RecurringInvoiceService.PaymentTermsDays(terms));

    [Theory]
    [InlineData(0, "Due on receipt")]
    [InlineData(7, "Net 7")]
    [InlineData(15, "Net 15")]
    [InlineData(20, "Net 20")]
    [InlineData(30, "Net 30")]
    public void FormatPaymentTerms_DerivesTermsFromTheInvoiceDates(int offsetDays, string expected)
    {
        var issue = new DateTime(2026, 1, 1);
        Assert.Equal(expected, RecurringInvoiceService.FormatPaymentTerms(issue, issue.AddDays(offsetDays)));
    }

    [Fact]
    public void FormatPaymentTerms_RoundTripsThroughPaymentTermsDays()
    {
        var issue = new DateTime(2026, 3, 10);
        var due = issue.AddDays(21);
        var terms = RecurringInvoiceService.FormatPaymentTerms(issue, due);
        Assert.Equal(21, RecurringInvoiceService.PaymentTermsDays(terms));
    }

    [Fact]
    public void AdvanceDate_Monthly_AnchorDayRecoversAfterAShortMonth()
    {
        // Anchored on the 31st: Jan 31 -> Feb 28 (clamped) must then return to Mar 31,
        // not stay stuck on the 28th for the rest of the schedule's life.
        var feb = RecurringInvoiceService.AdvanceDate(new DateTime(2026, 1, 31), Frequency.Monthly, anchorDay: 31);
        Assert.Equal(new DateTime(2026, 2, 28), feb);

        var mar = RecurringInvoiceService.AdvanceDate(feb, Frequency.Monthly, anchorDay: 31);
        Assert.Equal(new DateTime(2026, 3, 31), mar);
    }

    [Fact]
    public void GenerateDueInvoices_MonthEndAnchor_DoesNotDriftEarlier()
    {
        var data = new CompanyData();
        // Starts on Jan 31. Occurrences due by Apr 15: Jan 31, Feb 28, Mar 31 (Apr 30 is not yet due).
        data.RecurringInvoices.Add(MakeSchedule(new DateTime(2026, 1, 31)));

        var generated = RecurringInvoiceService.GenerateDueInvoices(data, new DateTime(2026, 4, 15));

        Assert.Equal(3, generated.Count);
        Assert.Equal(new DateTime(2026, 1, 31), generated[0].IssueDate);
        Assert.Equal(new DateTime(2026, 2, 28), generated[1].IssueDate);
        Assert.Equal(new DateTime(2026, 3, 31), generated[2].IssueDate); // recovered to the 31st
        Assert.Equal(new DateTime(2026, 4, 30), data.RecurringInvoices[0].NextInvoiceDate);
    }

    [Fact]
    public void GenerateDueInvoices_PastDates_GeneratesEachDueOccurrenceAndAdvances()
    {
        var data = new CompanyData();
        // Jan 15 and Feb 15 are on/before Feb 20; Mar 15 is not.
        data.RecurringInvoices.Add(MakeSchedule(new DateTime(2026, 1, 15)));

        var generated = RecurringInvoiceService.GenerateDueInvoices(data, new DateTime(2026, 2, 20));

        Assert.Equal(2, generated.Count);
        Assert.Equal(2, data.Invoices.Count);
        Assert.Equal(new DateTime(2026, 3, 15), data.RecurringInvoices[0].NextInvoiceDate);
        Assert.All(generated, i => Assert.Equal(InvoiceStatus.Draft, i.Status));
        Assert.All(generated, i => Assert.Equal("REC-INV-00001", i.RecurringInvoiceId));
        Assert.All(generated, i => Assert.StartsWith("INV-", i.Id));
        Assert.Equal(generated[0].IssueDate.AddDays(30), generated[0].DueDate); // Net 30
    }

    [Fact]
    public void GenerateDueInvoices_IsIdempotent()
    {
        var data = new CompanyData();
        data.RecurringInvoices.Add(MakeSchedule(new DateTime(2026, 1, 15)));

        var asOf = new DateTime(2026, 2, 20);
        var first = RecurringInvoiceService.GenerateDueInvoices(data, asOf);
        var second = RecurringInvoiceService.GenerateDueInvoices(data, asOf);

        Assert.Equal(2, first.Count);
        Assert.Empty(second);
        Assert.Equal(2, data.Invoices.Count);
    }

    [Fact]
    public void GenerateDueInvoices_PausedSchedule_GeneratesNothing()
    {
        var data = new CompanyData();
        data.RecurringInvoices.Add(MakeSchedule(new DateTime(2026, 1, 15), status: RecurringInvoiceStatus.Paused));

        var generated = RecurringInvoiceService.GenerateDueInvoices(data, new DateTime(2026, 6, 1));

        Assert.Empty(generated);
        Assert.Empty(data.Invoices);
    }

    [Fact]
    public void GenerateDueInvoices_PastEndDate_CompletesAfterLastOccurrence()
    {
        var data = new CompanyData();
        // Ends the same day as the first occurrence: generate that one, then complete.
        data.RecurringInvoices.Add(MakeSchedule(
            new DateTime(2026, 1, 15), endDate: new DateTime(2026, 1, 15)));

        var generated = RecurringInvoiceService.GenerateDueInvoices(data, new DateTime(2026, 6, 1));

        Assert.Single(generated);
        Assert.Equal(RecurringInvoiceStatus.Completed, data.RecurringInvoices[0].Status);
    }

    /// <summary>What resuming does: move the next date to the first occurrence on or after today.</summary>
    private static IReadOnlyList<Invoice> ResumeAndGenerate(RecurringInvoice schedule, DateTime today)
    {
        var data = new CompanyData();
        data.RecurringInvoices.Add(schedule);
        schedule.NextInvoiceDate = RecurrenceSchedule.FirstOnOrAfter(
            schedule.NextInvoiceDate, schedule.Frequency, schedule.StartDate.Day, today);
        return RecurringInvoiceService.GenerateDueInvoices(data, today);
    }

    [Fact]
    public void Resume_AfterAPause_DoesNotGenerateThePausedMonths()
    {
        // Paused in May with June 1 next; resumed September 5.
        var schedule = MakeSchedule(new DateTime(2026, 1, 1));
        schedule.NextInvoiceDate = new DateTime(2026, 6, 1);

        var generated = ResumeAndGenerate(schedule, new DateTime(2026, 9, 5));

        Assert.Empty(generated);
        Assert.Equal(new DateTime(2026, 10, 1), schedule.NextInvoiceDate);
    }

    [Fact]
    public void Resume_OnAnOccurrenceDay_StillGeneratesThatDaysInvoice()
    {
        var schedule = MakeSchedule(new DateTime(2026, 1, 1));
        schedule.NextInvoiceDate = new DateTime(2026, 6, 1);

        var generated = ResumeAndGenerate(schedule, new DateTime(2026, 9, 1));

        Assert.Equal(new DateTime(2026, 9, 1), Assert.Single(generated).IssueDate);
    }

    [Fact]
    public void GenerateDueInvoices_NullTemplate_IsSkipped()
    {
        var data = new CompanyData();
        data.RecurringInvoices.Add(MakeSchedule(new DateTime(2026, 1, 15), withTemplate: false));

        var generated = RecurringInvoiceService.GenerateDueInvoices(data, new DateTime(2026, 6, 1));

        Assert.Empty(generated);
    }
}
