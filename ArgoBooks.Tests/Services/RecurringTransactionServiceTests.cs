using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// A schedule generates real transactions flagged for review, so the books stay complete while
/// each occurrence is still put in front of someone.
/// </summary>
public class RecurringTransactionServiceTests
{
    private static (CompanyData data, RecurringTransaction schedule) WithMonthlyRent(DateTime start)
    {
        var data = new CompanyData();
        var schedule = new RecurringTransaction
        {
            Id = "REC-TXN-00001",
            Type = CategoryType.Expense,
            Frequency = Frequency.Monthly,
            StartDate = start,
            NextDate = start,
            ExpenseTemplate = new Expense { Description = "Rent", Amount = 2000m, Total = 2000m }
        };
        data.RecurringTransactions.Add(schedule);
        return (data, schedule);
    }

    [Fact]
    public void NextRecurringTransactionId_UsesTheRecTxnPrefix()
    {
        var data = new CompanyData();
        var ids = new IdGenerator(data);

        Assert.Equal("REC-TXN-00001", ids.NextRecurringTransactionId());
        Assert.Equal("REC-TXN-00002", ids.NextRecurringTransactionId());
    }

    [Fact]
    public void GenerateDue_AfterALongGap_ProducesOneEntryPerMissedOccurrence()
    {
        var (data, _) = WithMonthlyRent(new DateTime(2026, 1, 1));

        var generated = RecurringTransactionService.GenerateDue(data, new DateTime(2026, 5, 15));

        Assert.Equal(5, generated.Count);
        Assert.Equal(5, data.Expenses.Count);
        Assert.All(data.Expenses, e => Assert.True(e.NeedsReview));
        Assert.Equal(new DateTime(2026, 6, 1), data.RecurringTransactions[0].NextDate);
    }

    [Fact]
    public void GenerateDue_RunTwice_ProducesNothingTheSecondTime()
    {
        var (data, _) = WithMonthlyRent(new DateTime(2026, 1, 1));
        RecurringTransactionService.GenerateDue(data, new DateTime(2026, 5, 15));

        var second = RecurringTransactionService.GenerateDue(data, new DateTime(2026, 5, 15));

        Assert.Empty(second);
        Assert.Equal(5, data.Expenses.Count);
    }

    [Fact]
    public void GenerateDue_RewoundSchedule_DoesNotDuplicateExistingOccurrences()
    {
        var (data, schedule) = WithMonthlyRent(new DateTime(2026, 1, 1));
        RecurringTransactionService.GenerateDue(data, new DateTime(2026, 5, 15));

        schedule.NextDate = new DateTime(2026, 3, 1);
        var again = RecurringTransactionService.GenerateDue(data, new DateTime(2026, 5, 15));

        Assert.Empty(again);
        Assert.Equal(5, data.Expenses.Count);
    }

    [Fact]
    public void GenerateDue_SkippedDate_AdvancesWithoutGenerating()
    {
        var (data, schedule) = WithMonthlyRent(new DateTime(2026, 1, 1));
        schedule.SkippedDates.Add(new DateTime(2026, 2, 1));

        RecurringTransactionService.GenerateDue(data, new DateTime(2026, 3, 15));

        Assert.Equal(2, data.Expenses.Count);
        Assert.DoesNotContain(data.Expenses, e => e.OccurrenceDate == new DateTime(2026, 2, 1));
    }

    [Fact]
    public void GenerateDue_PastTheEndDate_MarksTheScheduleCompleted()
    {
        var (data, schedule) = WithMonthlyRent(new DateTime(2026, 1, 1));
        schedule.EndDate = new DateTime(2026, 2, 28);

        RecurringTransactionService.GenerateDue(data, new DateTime(2026, 6, 1));

        Assert.Equal(2, data.Expenses.Count);
        Assert.Equal(RecurringTransactionStatus.Completed, schedule.Status);
    }

    [Fact]
    public void GenerateDue_CorruptFarPastDate_StopsAtTheCap()
    {
        var (data, schedule) = WithMonthlyRent(new DateTime(1900, 1, 1));
        schedule.Frequency = Frequency.Weekly;

        var generated = RecurringTransactionService.GenerateDue(data, new DateTime(2026, 1, 1));

        Assert.Equal(RecurringTransactionService.MaxOccurrencesPerSchedulePerRun, generated.Count);
    }

    [Fact]
    public void GenerateDue_RevenueSchedule_AddsToRevenues()
    {
        var (data, schedule) = WithMonthlyRent(new DateTime(2026, 1, 1));
        schedule.Type = CategoryType.Revenue;
        schedule.ExpenseTemplate = null;
        schedule.RevenueTemplate = new Revenue { Description = "Retainer", Amount = 500m, Total = 500m };

        RecurringTransactionService.GenerateDue(data, new DateTime(2026, 2, 15));

        Assert.Equal(2, data.Revenues.Count);
        Assert.Empty(data.Expenses);
    }

    [Fact]
    public void GenerateDue_PausedSchedule_GeneratesNothing()
    {
        var (data, schedule) = WithMonthlyRent(new DateTime(2026, 1, 1));
        schedule.Status = RecurringTransactionStatus.Paused;

        Assert.Empty(RecurringTransactionService.GenerateDue(data, new DateTime(2026, 5, 1)));
    }

    [Fact]
    public void GenerateDue_DoesNotCarryReceiptOrBankMatchFromTheTemplate()
    {
        var (data, schedule) = WithMonthlyRent(new DateTime(2026, 1, 1));
        schedule.ExpenseTemplate!.ReceiptId = "RCP-001";
        schedule.ExpenseTemplate.BankMatched = true;

        RecurringTransactionService.GenerateDue(data, new DateTime(2026, 1, 15));

        var entry = Assert.Single(data.Expenses);
        Assert.Null(entry.ReceiptId);
        Assert.False(entry.BankMatched);
    }

    [Fact]
    public void SkipOccurrence_RecordsTheDateOnce()
    {
        var (_, schedule) = WithMonthlyRent(new DateTime(2026, 1, 1));

        RecurringTransactionService.SkipOccurrence(schedule, new DateTime(2026, 2, 1));
        RecurringTransactionService.SkipOccurrence(schedule, new DateTime(2026, 2, 1));

        Assert.Single(schedule.SkippedDates);
    }

    [Fact]
    public void UnskipOccurrence_RemovesTheDate()
    {
        var (_, schedule) = WithMonthlyRent(new DateTime(2026, 1, 1));
        RecurringTransactionService.SkipOccurrence(schedule, new DateTime(2026, 2, 1));

        RecurringTransactionService.UnskipOccurrence(schedule, new DateTime(2026, 2, 1));

        Assert.Empty(schedule.SkippedDates);
    }

    [Fact]
    public void FindCorrectableOccurrences_ExcludesBankMatchedEntries()
    {
        var (data, schedule) = WithMonthlyRent(new DateTime(2026, 1, 1));
        RecurringTransactionService.GenerateDue(data, new DateTime(2026, 3, 15));
        data.Expenses[0].BankMatched = true;

        var correctable = RecurringTransactionService.FindCorrectableOccurrences(data, schedule, oldAmount: 2000m);

        Assert.Equal(2, correctable.Count);
        Assert.DoesNotContain(data.Expenses[0], correctable);
    }

    [Fact]
    public void FindCorrectableOccurrences_IgnoresEntriesFromOtherSchedules()
    {
        var (data, schedule) = WithMonthlyRent(new DateTime(2026, 1, 1));
        RecurringTransactionService.GenerateDue(data, new DateTime(2026, 1, 15));
        data.Expenses.Add(new Expense { Id = "PUR-2026-09999", RecurringScheduleId = "REC-TXN-00002" });

        Assert.Single(RecurringTransactionService.FindCorrectableOccurrences(data, schedule, oldAmount: 2000m));
    }

    [Fact]
    public void CorrectOccurrences_UpdatesOnlyTheGivenEntries()
    {
        var (data, schedule) = WithMonthlyRent(new DateTime(2026, 1, 1));
        RecurringTransactionService.GenerateDue(data, new DateTime(2026, 3, 15));
        data.Expenses[0].BankMatched = true;

        var correctable = RecurringTransactionService.FindCorrectableOccurrences(data, schedule, oldAmount: 2000m);
        EditTemplateAmount(schedule, 2200m);
        RecurringTransactionService.CorrectOccurrences(data, schedule, correctable);

        Assert.Equal(2000m, data.Expenses[0].Total);
        Assert.Equal(2200m, data.Expenses[1].Total);
        Assert.Equal(2200m, data.Expenses[2].Total);
    }

    private static (CompanyData data, RecurringTransaction schedule) WithForeignMonthlyRent(DateTime start)
    {
        var (data, schedule) = WithMonthlyRent(start);
        schedule.ExpenseTemplate!.OriginalCurrency = "CAD";
        schedule.ExpenseTemplate.TotalUSD = 1500m;
        schedule.ExpenseTemplate.UnitPrice = 2000m;
        return (data, schedule);
    }

    [Fact]
    public void GenerateDue_ForeignCurrency_ConvertsEachOccurrenceAtItsOwnDate()
    {
        var (data, _) = WithForeignMonthlyRent(new DateTime(2026, 1, 1));

        // A rate that moves by month, so a stale template value would be visible.
        RecurringTransactionService.GenerateDue(data, new DateTime(2026, 3, 15),
            (amount, _, date, out usd) =>
            {
                usd = amount * (0.70m + date.Month * 0.01m);
                return true;
            });

        Assert.Equal(3, data.Expenses.Count);
        Assert.Equal(2000m * 0.71m, data.Expenses[0].TotalUSD);
        Assert.Equal(2000m * 0.72m, data.Expenses[1].TotalUSD);
        Assert.Equal(2000m * 0.73m, data.Expenses[2].TotalUSD);
        Assert.All(data.Expenses, e => Assert.False(e.IsPendingConversion));
    }

    [Fact]
    public void GenerateDue_NoRateForTheOccurrence_QueuesItRatherThanGuessing()
    {
        var (data, _) = WithForeignMonthlyRent(new DateTime(2026, 1, 1));

        RecurringTransactionService.GenerateDue(data, new DateTime(2026, 1, 15),
            (_, _, _, out usd) =>
            {
                usd = 0m;
                return false;
            });

        var entry = Assert.Single(data.Expenses);
        Assert.True(entry.IsPendingConversion);

        var queued = Assert.Single(data.PendingConversions);
        Assert.Equal(entry.Id, queued.TransactionId);
        Assert.Equal("Expense", queued.TransactionType);
        Assert.Equal("CAD", queued.OriginalCurrency);
        Assert.Equal(new DateTime(2026, 1, 1), queued.TransactionDate);
        Assert.Equal(2000m, queued.Total);
    }

    [Fact]
    public void GenerateDue_UsdSchedule_NeedsNoConversion()
    {
        var (data, _) = WithMonthlyRent(new DateTime(2026, 1, 1));

        RecurringTransactionService.GenerateDue(data, new DateTime(2026, 1, 15),
            (_, _, _, out usd) =>
            {
                usd = 0m;
                return false;
            });

        var entry = Assert.Single(data.Expenses);
        Assert.False(entry.IsPendingConversion);
        Assert.Empty(data.PendingConversions);
    }

    #region Correcting past entries

    private static decimal RateFor(DateTime date) => 0.70m + date.Month * 0.01m;

    /// <summary>A rate that moves by month, so an entry converted at the wrong date shows it.</summary>
    private static bool MonthlyRate(decimal amount, string currency, DateTime date, out decimal usd)
    {
        usd = amount * RateFor(date);
        return true;
    }

    private static bool NoRate(decimal amount, string currency, DateTime date, out decimal usd)
    {
        usd = 0m;
        return false;
    }

    /// <summary>What the schedule editor does on save: a new amount, and its own USD figure.</summary>
    private static void EditTemplateAmount(RecurringTransaction schedule, decimal amount, decimal staleUsd = 0m)
    {
        var template = schedule.Template!;
        template.Amount = amount;
        template.Total = amount;
        template.UnitPrice = amount;
        template.TotalUSD = staleUsd;
        template.UnitPriceUSD = staleUsd;
        if (template.LineItems.Count > 0)
            template.LineItems = [new LineItem { Description = template.Description, Quantity = 1, UnitPrice = amount }];
    }

    [Fact]
    public void FindCorrectableOccurrences_LeavesOutEntriesNoLongerAtTheOldAmount()
    {
        var (data, schedule) = WithMonthlyRent(new DateTime(2026, 1, 1));
        RecurringTransactionService.GenerateDue(data, new DateTime(2026, 3, 15));
        data.Expenses[1].Amount = 1800m;

        var correctable = RecurringTransactionService.FindCorrectableOccurrences(data, schedule, oldAmount: 2000m);

        Assert.Equal(2, correctable.Count);
        Assert.DoesNotContain(data.Expenses[1], correctable);
    }

    [Fact]
    public void CorrectOccurrences_KeepsTheEntrysOwnTaxShippingDiscountAndFee()
    {
        var (data, schedule) = WithMonthlyRent(new DateTime(2026, 1, 1));
        RecurringTransactionService.GenerateDue(data, new DateTime(2026, 1, 15));
        var entry = Assert.Single(data.Expenses);
        entry.TaxAmount = 100m;
        entry.ShippingCost = 20m;
        entry.Discount = 5m;
        entry.Fee = 3m;
        entry.Total = 2000m + 100m + 20m + 3m - 5m;

        EditTemplateAmount(schedule, 2200m);
        RecurringTransactionService.CorrectOccurrences(data, schedule, [entry]);

        Assert.Equal(2200m, entry.Amount);
        Assert.Equal((100m, 20m, 5m, 3m), (entry.TaxAmount, entry.ShippingCost, entry.Discount, entry.Fee));
        Assert.Equal(2200m + 100m + 20m + 3m - 5m, entry.Total);
    }

    [Fact]
    public void CorrectOccurrences_UpdatesTheLineItemTheEditFormReadsTheSubtotalFrom()
    {
        var (data, schedule) = WithMonthlyRent(new DateTime(2026, 1, 1));
        schedule.ExpenseTemplate!.LineItems = [new LineItem { Description = "Rent", Quantity = 1, UnitPrice = 2000m }];
        RecurringTransactionService.GenerateDue(data, new DateTime(2026, 1, 15));
        var entry = Assert.Single(data.Expenses);

        EditTemplateAmount(schedule, 2200m);
        RecurringTransactionService.CorrectOccurrences(data, schedule, [entry]);

        var line = Assert.Single(entry.LineItems);
        Assert.Equal(2200m, line.UnitPrice);
        Assert.Equal(entry.Amount, entry.LineItems.Sum(li => li.Amount));
    }

    [Fact]
    public void CorrectOccurrences_ConvertsEachEntryAtItsOwnDate()
    {
        var (data, schedule) = WithForeignMonthlyRent(new DateTime(2026, 1, 1));
        RecurringTransactionService.GenerateDue(data, new DateTime(2026, 3, 15), MonthlyRate);

        EditTemplateAmount(schedule, 2200m, staleUsd: 2200m * RateFor(new DateTime(2026, 1, 1)));
        RecurringTransactionService.CorrectOccurrences(data, schedule, data.Expenses.ToList(), MonthlyRate);

        Assert.All(data.Expenses, e => Assert.Equal(2200m * RateFor(e.Date), e.TotalUSD));
    }

    [Fact]
    public void CorrectOccurrences_NoRateForTheEntrysDate_QueuesItInsteadOfCopyingTheTemplateFigure()
    {
        var (data, schedule) = WithForeignMonthlyRent(new DateTime(2026, 1, 1));
        RecurringTransactionService.GenerateDue(data, new DateTime(2026, 1, 15), MonthlyRate);
        var entry = Assert.Single(data.Expenses);

        // Edited offline: the editor could not convert, so the template holds the raw amount.
        EditTemplateAmount(schedule, 2200m, staleUsd: 2200m);
        RecurringTransactionService.CorrectOccurrences(data, schedule, [entry], NoRate);

        Assert.True(entry.IsPendingConversion);
        Assert.Equal(0m, entry.EffectiveTotalUSD);
        var queued = Assert.Single(data.PendingConversions);
        Assert.Equal((entry.Id, 2200m), (queued.TransactionId, queued.Total));
    }

    [Fact]
    public void CorrectOccurrences_PendingEntry_ReplacesItsQueuedAmount()
    {
        var (data, schedule) = WithForeignMonthlyRent(new DateTime(2026, 1, 1));
        RecurringTransactionService.GenerateDue(data, new DateTime(2026, 1, 15), NoRate);
        var entry = Assert.Single(data.Expenses);

        EditTemplateAmount(schedule, 2200m, staleUsd: 2200m);
        RecurringTransactionService.CorrectOccurrences(data, schedule, [entry], NoRate);

        Assert.Equal(2200m, Assert.Single(data.PendingConversions).Total);
    }

    [Fact]
    public void CorrectOccurrences_Revert_RestoresTheUsdFiguresTheBooksRead()
    {
        var (data, schedule) = WithForeignMonthlyRent(new DateTime(2026, 1, 1));
        RecurringTransactionService.GenerateDue(data, new DateTime(2026, 1, 15), MonthlyRate);
        var entry = Assert.Single(data.Expenses);
        var original = (entry.Amount, entry.Total, entry.TotalUSD, entry.UnitPriceUSD, entry.EffectiveTotalUSD);

        EditTemplateAmount(schedule, 2200m, staleUsd: 1650m);
        var correction = RecurringTransactionService.CorrectOccurrences(data, schedule, [entry], MonthlyRate);
        correction.Revert(data);

        Assert.Equal(original, (entry.Amount, entry.Total, entry.TotalUSD, entry.UnitPriceUSD, entry.EffectiveTotalUSD));
    }

    [Fact]
    public void CorrectOccurrences_UndoThenRedo_MovesTheQueuedAmountWithTheEntry()
    {
        var (data, schedule) = WithForeignMonthlyRent(new DateTime(2026, 1, 1));
        RecurringTransactionService.GenerateDue(data, new DateTime(2026, 1, 15), NoRate);
        var entry = Assert.Single(data.Expenses);
        EditTemplateAmount(schedule, 2200m, staleUsd: 2200m);
        var correction = RecurringTransactionService.CorrectOccurrences(data, schedule, [entry], NoRate);

        correction.Revert(data);
        Assert.Equal(2000m, Assert.Single(data.PendingConversions).Total);
        Assert.True(entry.IsPendingConversion);

        correction.Reapply(data);
        Assert.Equal(2200m, Assert.Single(data.PendingConversions).Total);
    }

    /// <summary>Tax on a USD entry falls back to TotalUSD over Total, so both must move together.</summary>
    [Fact]
    public void CorrectOccurrences_UsdEntryWithTax_ReportsTheTaxItCarries()
    {
        var (data, schedule) = WithMonthlyRent(new DateTime(2026, 1, 1));
        schedule.ExpenseTemplate!.TotalUSD = 2000m;
        RecurringTransactionService.GenerateDue(data, new DateTime(2026, 1, 15));
        var entry = Assert.Single(data.Expenses);
        entry.TaxAmount = 100m;
        entry.Total = 2100m;

        EditTemplateAmount(schedule, 2200m, staleUsd: 2200m);
        RecurringTransactionService.CorrectOccurrences(data, schedule, [entry]);

        Assert.Equal(100m, entry.EffectiveTaxAmountUSD);
        Assert.Equal(entry.Total, entry.EffectiveTotalUSD);
    }

    #endregion

    #region In-use checks

    /// <summary>
    /// A schedule outliving its counterparty keeps generating entries against a record that is
    /// gone, so the delete guards have to see the reference even though it sits on the template
    /// rather than on the schedule itself.
    /// </summary>
    [Fact]
    public void IsCustomerInUse_SeesTheCustomerOnARevenueTemplate()
    {
        var data = new CompanyData();
        data.RecurringTransactions.Add(new RecurringTransaction
        {
            Id = "REC-TXN-00001",
            Type = CategoryType.Revenue,
            RevenueTemplate = new Revenue { CustomerId = "CUS-001" }
        });

        Assert.True(RecurringTransactionService.IsCustomerInUse(data, "CUS-001"));
        Assert.False(RecurringTransactionService.IsCustomerInUse(data, "CUS-002"));
        Assert.False(RecurringTransactionService.IsCustomerInUse(data, string.Empty));
    }

    [Fact]
    public void IsSupplierInUse_SeesTheSupplierOnAnExpenseTemplate()
    {
        var data = new CompanyData();
        data.RecurringTransactions.Add(new RecurringTransaction
        {
            Id = "REC-TXN-00002",
            Type = CategoryType.Expense,
            ExpenseTemplate = new Expense { SupplierId = "SUP-001" }
        });

        Assert.True(RecurringTransactionService.IsSupplierInUse(data, "SUP-001"));
        Assert.False(RecurringTransactionService.IsSupplierInUse(data, "SUP-002"));
    }

    /// <summary>A product can sit on either side, so both templates are searched.</summary>
    [Fact]
    public void IsProductInUse_SeesLineItemsOnEitherTemplate()
    {
        var expenseSide = new CompanyData();
        expenseSide.RecurringTransactions.Add(new RecurringTransaction
        {
            Id = "REC-TXN-00003",
            Type = CategoryType.Expense,
            ExpenseTemplate = new Expense { LineItems = [new LineItem { ProductId = "PRD-001" }] }
        });

        var revenueSide = new CompanyData();
        revenueSide.RecurringTransactions.Add(new RecurringTransaction
        {
            Id = "REC-TXN-00004",
            Type = CategoryType.Revenue,
            RevenueTemplate = new Revenue { LineItems = [new LineItem { ProductId = "PRD-001" }] }
        });

        Assert.True(RecurringTransactionService.IsProductInUse(expenseSide, "PRD-001"));
        Assert.True(RecurringTransactionService.IsProductInUse(revenueSide, "PRD-001"));
        Assert.False(RecurringTransactionService.IsProductInUse(expenseSide, "PRD-002"));
    }

    /// <summary>A schedule with no template at all must not be read as using everything.</summary>
    [Fact]
    public void InUseChecks_TolerateAnEmptySchedule()
    {
        var data = new CompanyData();
        data.RecurringTransactions.Add(new RecurringTransaction { Id = "REC-TXN-00005" });

        Assert.False(RecurringTransactionService.IsCustomerInUse(data, "CUS-001"));
        Assert.False(RecurringTransactionService.IsSupplierInUse(data, "SUP-001"));
        Assert.False(RecurringTransactionService.IsProductInUse(data, "PRD-001"));
    }

    #endregion
}
