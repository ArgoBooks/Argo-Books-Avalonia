using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Drives the recurring schedule editor's save against an in-memory company. Saving generates any
/// occurrence already due, so what it does to the schedule's dates, status and the conversion
/// queue decides which entries reach the books.
/// </summary>
public class RecurringScheduleEditorViewModelTests : ModalViewModelTestBase
{
    public RecurringScheduleEditorViewModelTests()
    {
        Company.Settings.Localization.Currency = "USD";
        Company.Suppliers.Add(new Supplier { Id = "SUP-001", Name = "Landlord" });
    }

    // With no exchange rate service, an entry in JPY is saved pending (Calculations.md Rule 3a).
    private void UseCurrencyWithNoRate()
    {
        Company.Settings.Localization.Currency = "JPY";
        UseNoExchangeRates();
    }

    /// <summary>
    /// The conversion service whose queue the view model's saves mirror into.
    /// </summary>
    private static PendingConversionService ConversionService() =>
        PendingConversionService.Instance ?? new PendingConversionService();

    private static bool ServiceHasQueued(string transactionId) =>
        ConversionService().Entries.Any(p => p.TransactionId == transactionId);

    private RecurringScheduleEditorViewModel NewRentSchedule(DateTime start)
    {
        var vm = new RecurringScheduleEditorViewModel();
        vm.ShowNew(CategoryType.Expense);
        vm.Amount = "2000";
        vm.StartDate = new DateTimeOffset(start);
        vm.SelectedCounterparty = vm.CounterpartyOptions.Single();
        return vm;
    }

    [Fact]
    public async Task Save_EntryDueNowWithNoRate_IsQueuedWithTheConversionService()
    {
        UseCurrencyWithNoRate();
        ConversionService();
        var vm = NewRentSchedule(DateTime.Today);

        await vm.SaveCommand.ExecuteAsync(null);

        var entry = Assert.Single(Company.Expenses);
        Assert.True(entry.IsPendingConversion);
        Assert.True(ServiceHasQueued(entry.Id), "Only the company file queued it, so the next conversion pass drops it");
    }

    [Fact]
    public async Task Save_UndoForgetsTheQueuedEntry_AndRedoQueuesItAgain()
    {
        UseCurrencyWithNoRate();
        ConversionService();
        var vm = NewRentSchedule(DateTime.Today);
        await vm.SaveCommand.ExecuteAsync(null);
        var entry = Assert.Single(Company.Expenses);

        Undo();
        var afterUndo = (Company.PendingConversions.Any(p => p.TransactionId == entry.Id), ServiceHasQueued(entry.Id));
        Redo();
        var afterRedo = (Company.PendingConversions.Any(p => p.TransactionId == entry.Id), ServiceHasQueued(entry.Id));

        Assert.Equal((false, false), afterUndo);
        Assert.Equal((true, true), afterRedo);
    }

    /// <summary>
    /// Monthly rent that ran to its end date two months ago. Generation left the next date on the
    /// first occurrence past the end and marked it Completed.
    /// </summary>
    private RecurringTransaction CompletedRent()
    {
        var start = DateTime.Today.AddMonths(-5);
        var end = DateTime.Today.AddMonths(-2);
        var schedule = new RecurringTransaction
        {
            Id = "REC-TXN-00001",
            Type = CategoryType.Expense,
            Frequency = Frequency.Monthly,
            StartDate = start,
            EndDate = end,
            NextDate = RecurrenceSchedule.FirstOnOrAfter(start, Frequency.Monthly, start.Day, end.AddDays(1)),
            Status = RecurringTransactionStatus.Completed,
            ExpenseTemplate = new Expense
            {
                Description = "Rent", Amount = 2000m, Total = 2000m, OriginalCurrency = "USD",
                SupplierId = "SUP-001"
            }
        };
        Company.RecurringTransactions.Add(schedule);
        return schedule;
    }

    [Fact]
    public async Task Save_EndDateMovedPastTheNextDate_RestartsACompletedSchedule()
    {
        var schedule = CompletedRent();
        var nextBefore = schedule.NextDate;
        var vm = new RecurringScheduleEditorViewModel();
        vm.ShowEdit(schedule);
        vm.EndDate = new DateTimeOffset(DateTime.Today.AddYears(1));

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(RecurringTransactionStatus.Active, schedule.Status);
        Assert.Contains(Company.Expenses, e => e.OccurrenceDate == nextBefore);
        Assert.True(schedule.NextDate > DateTime.Today);
    }

    [Fact]
    public async Task Save_RestartingACompletedSchedule_UndoesBackToCompleted()
    {
        var schedule = CompletedRent();
        var nextBefore = schedule.NextDate;
        var vm = new RecurringScheduleEditorViewModel();
        vm.ShowEdit(schedule);
        vm.EndDate = null;
        await vm.SaveCommand.ExecuteAsync(null);
        var restarted = (schedule.Status, schedule.NextDate, Company.Expenses.Count);

        Undo();
        var afterUndo = (schedule.Status, schedule.NextDate, Company.Expenses.Count);
        Redo();

        Assert.Equal((RecurringTransactionStatus.Completed, nextBefore, 0), afterUndo);
        Assert.Equal(restarted, (schedule.Status, schedule.NextDate, Company.Expenses.Count));
    }

    private RecurringTransaction Rent(DateTime start)
    {
        var schedule = new RecurringTransaction
        {
            Id = "REC-TXN-00001",
            Type = CategoryType.Expense,
            Frequency = Frequency.Monthly,
            StartDate = start,
            NextDate = start,
            ExpenseTemplate = new Expense
            {
                Description = "Rent", Amount = 2000m, Total = 2000m, OriginalCurrency = "USD",
                SupplierId = "SUP-001"
            }
        };
        Company.RecurringTransactions.Add(schedule);
        return schedule;
    }

    /// <summary>Created ahead of its start, so nothing is generated and the start is still the next date.</summary>
    [Theory]
    [InlineData(50)]
    [InlineData(20)]
    public async Task Save_StartDateMovedBeforeAnythingIsGenerated_NextDateFollowsIt(int newStartInDays)
    {
        var schedule = Rent(DateTime.Today.AddDays(35));
        var vm = new RecurringScheduleEditorViewModel();
        vm.ShowEdit(schedule);
        vm.StartDate = new DateTimeOffset(DateTime.Today.AddDays(newStartInDays));

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(DateTime.Today.AddDays(newStartInDays), schedule.NextDate);
    }

    /// <summary>
    /// Rent booked on the 1st for months, then moved to the 15th. This month is already booked, so
    /// the next one is the 15th of next month, not a second entry this month.
    /// </summary>
    [Fact]
    public async Task Save_StartDateMovedAfterEntriesExist_MovesToTheNewDayWithoutRepeatingAMonth()
    {
        var start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-3);
        var schedule = Rent(start);
        RecurringTransactionService.GenerateDue(Company, DateTime.Today);
        var (nextBefore, entriesBefore) = (schedule.NextDate, Company.Expenses.Count);
        var vm = new RecurringScheduleEditorViewModel();
        vm.ShowEdit(schedule);
        vm.StartDate = new DateTimeOffset(start.AddDays(14));

        await vm.SaveCommand.ExecuteAsync(null);
        var saved = (schedule.NextDate, Company.Expenses.Count);
        Undo();

        Assert.Equal((nextBefore.AddDays(14), entriesBefore), saved);
        Assert.Equal((start, nextBefore), (schedule.StartDate, schedule.NextDate));
    }

    /// <summary>
    /// Months of rent were booked and then deleted to tidy up. Those months were still booked once,
    /// so nudging the start must not bring every one of them back as a back-dated entry.
    /// </summary>
    [Fact]
    public async Task Save_StartDateMovedAfterGeneratedEntriesWereDeleted_DoesNotRebookThePast()
    {
        var start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-3);
        var schedule = Rent(start);
        RecurringTransactionService.GenerateDue(Company, DateTime.Today);
        var nextBefore = schedule.NextDate;
        Company.Expenses.Clear();
        var vm = new RecurringScheduleEditorViewModel();
        vm.ShowEdit(schedule);
        vm.StartDate = new DateTimeOffset(start.AddDays(14));

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(Company.Expenses);
        Assert.Equal(nextBefore.AddDays(14), schedule.NextDate);
    }

    /// <summary>
    /// Paused before its first date and resumed months later, so it never generated anything but
    /// the paused months are behind it. Nudging the start must not book them.
    /// </summary>
    [Fact]
    public async Task Save_StartDateMovedAfterResumingPastPausedMonths_DoesNotBookThem()
    {
        var start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-3);
        var schedule = Rent(start);
        schedule.NextDate = RecurrenceSchedule.FirstOnOrAfter(start, Frequency.Monthly, start.Day, DateTime.Today);
        var vm = new RecurringScheduleEditorViewModel();
        vm.ShowEdit(schedule);
        vm.StartDate = new DateTimeOffset(start.AddDays(14));

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.DoesNotContain(Company.Expenses, e => e.OccurrenceDate < DateTime.Today);
    }

    /// <summary>
    /// The only generation was undone, so the schedule is new again and an earlier start should
    /// book from that start.
    /// </summary>
    [Fact]
    public async Task Save_StartDateMovedEarlierAfterUndoingItsOnlyGeneration_BooksFromTheNewStart()
    {
        var schedule = Rent(DateTime.Today.AddDays(35));
        var first = new RecurringScheduleEditorViewModel();
        first.ShowEdit(schedule);
        first.StartDate = new DateTimeOffset(DateTime.Today);
        await first.SaveCommand.ExecuteAsync(null);
        Undo();
        var newStart = DateTime.Today.AddMonths(-2);
        var second = new RecurringScheduleEditorViewModel();
        second.ShowEdit(schedule);
        second.StartDate = new DateTimeOffset(newStart);

        await second.SaveCommand.ExecuteAsync(null);

        Assert.Contains(Company.Expenses, e => e.OccurrenceDate == newStart);
    }
}
