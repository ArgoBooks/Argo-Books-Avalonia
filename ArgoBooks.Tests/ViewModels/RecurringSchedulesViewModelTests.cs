using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Drives the Recurring tab's pause and resume against an in-memory company, where the date the
/// schedule resumes from decides which entries land in the books.
/// </summary>
public class RecurringSchedulesViewModelTests : ModalViewModelTestBase
{
    /// <summary>A monthly rent schedule paused months ago, its next date still in the past.</summary>
    private RecurringTransaction PausedRent()
    {
        var start = DateTime.Today.AddMonths(-8);
        var schedule = new RecurringTransaction
        {
            Id = "REC-TXN-00001",
            Type = CategoryType.Expense,
            Frequency = Frequency.Monthly,
            StartDate = start,
            NextDate = start.AddMonths(4),
            Status = RecurringTransactionStatus.Paused,
            ExpenseTemplate = new Expense { Description = "Rent", Amount = 2000m, Total = 2000m, OriginalCurrency = "USD" }
        };
        Company.RecurringTransactions.Add(schedule);
        return schedule;
    }

    private static void Toggle(RecurringSchedulesViewModel vm) =>
        vm.TogglePauseCommand.Execute(vm.Schedules.Single());

    [Fact]
    public void Resume_DoesNotGenerateTheOccurrencesMissedWhilePaused()
    {
        var schedule = PausedRent();
        var vm = new RecurringSchedulesViewModel(CategoryType.Expense);

        Toggle(vm);
        var generated = RecurringTransactionService.GenerateDue(Company, DateTime.Today);
        vm.Cleanup();

        Assert.Equal(RecurringTransactionStatus.Active, schedule.Status);
        Assert.All(generated, e => Assert.True(e.Date >= DateTime.Today, $"Generated {e.Date:d} from the paused months"));
        Assert.True(schedule.NextDate >= DateTime.Today);
    }

    [Fact]
    public void Resume_UndoPutsBackTheDateItWasPausedAt_AndRedoMovesItForwardAgain()
    {
        var schedule = PausedRent();
        var pausedAt = schedule.NextDate;
        var vm = new RecurringSchedulesViewModel(CategoryType.Expense);

        Toggle(vm);
        var resumedAt = schedule.NextDate;

        Undo();
        var afterUndo = (schedule.Status, schedule.NextDate);
        Redo();
        vm.Cleanup();

        Assert.Equal((RecurringTransactionStatus.Paused, pausedAt), afterUndo);
        Assert.Equal((RecurringTransactionStatus.Active, resumedAt), (schedule.Status, schedule.NextDate));
        Assert.True(resumedAt >= DateTime.Today);
    }
}
