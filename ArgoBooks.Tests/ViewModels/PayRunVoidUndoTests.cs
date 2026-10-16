using System.Reflection;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Undoing the void of a pay run from the Pay runs page. The undo has to leave the books exactly as
/// they were before the void: the run approved, its wage expenses back and linked to its lines, and
/// every year-to-date figure unchanged, because later runs work out their deductions from them.
/// </summary>
public class PayRunVoidUndoTests : ModalViewModelTestBase
{
    private static readonly DateTime PayDate = new(DateTime.Today.Year, 1, 16);

    private static readonly PropertyInfo ConfirmationDialogProperty =
        typeof(App).GetProperty(nameof(App.ConfirmationDialog))!;

    /// <summary>
    /// After a void and its undo, the run is the only run and is approved again, its lines point at
    /// the same wage expenses, the expenses are back with their amounts, and the year-to-date
    /// figures for each employee and on the page match what they were before the void.
    /// </summary>
    [Fact]
    public async Task UndoVoid_PutsTheRunItsWagesAndTheYearToDateBackAsBefore()
    {
        PayRun run = ApprovedRunWithWages();
        Company.Expenses.Add(new Expense { Id = "PUR-OTHER", Date = PayDate, OriginalCurrency = "USD", Total = 40m });
        var expensesBefore = Expenses();
        var linksBefore = run.Lines.Select(l => l.ExpenseId).ToList();
        var ytdBefore = YearToDate();
        var vm = new PayRunsPageViewModel();
        try
        {
            var pageBefore = (vm.YearToDateGross, vm.YearToDateRemittance, vm.ApprovedCount);

            await VoidThroughThePageAsync(vm, run);
            Assert.Equal(PayRunStatus.Void, run.Status);

            Undo();

            Assert.Same(run, Assert.Single(Company.PayRuns));
            Assert.Equal(PayRunStatus.Approved, run.Status);
            Assert.Equal(linksBefore, run.Lines.Select(l => l.ExpenseId).ToList());
            Assert.Equal(expensesBefore, Expenses());
            Assert.Equal(ytdBefore, YearToDate());
            Assert.Equal(pageBefore, (vm.YearToDateGross, vm.YearToDateRemittance, vm.ApprovedCount));
        }
        finally
        {
            vm.Cleanup();
        }
    }

    /// <summary>
    /// Wages recorded while their exchange rate was missing wait on the conversion queue. Voiding
    /// takes them off it, and undoing the void has to put the same entries back, or the restored
    /// wages would count as 0 forever because nothing would ever convert them.
    /// </summary>
    [Fact]
    public async Task UndoVoid_WagesStillWaitingForTheirRate_GoBackOnTheConversionQueue()
    {
        Company.Settings.Localization.Currency = "CAD";
        UseNoExchangeRates();
        PayRun run = ApprovedRunWithWages();
        var queuedBefore = Queued();
        Assert.Equal(2, queuedBefore.Count);
        var vm = new PayRunsPageViewModel();
        try
        {
            await VoidThroughThePageAsync(vm, run);
            Assert.Empty(Company.PendingConversions);

            Undo();

            Assert.Equal(queuedBefore, Queued());
            Assert.All(Company.Expenses, e => Assert.True(e.IsPendingConversion));
        }
        finally
        {
            vm.Cleanup();
        }
    }

    /// <summary>
    /// Redoing the void after undoing it reverses the run again with the same reversal, removes the
    /// wages again, and brings each employee's year-to-date back to zero.
    /// </summary>
    [Fact]
    public async Task RedoVoid_ReversesTheRunAndRemovesItsWagesAgain()
    {
        PayRun run = ApprovedRunWithWages();
        var vm = new PayRunsPageViewModel();
        try
        {
            await VoidThroughThePageAsync(vm, run);
            PayRun reversal = Company.PayRuns.Single(r => r.VoidsPayRunId == run.Id);

            Undo();
            Redo();

            Assert.Equal(PayRunStatus.Void, run.Status);
            Assert.Same(reversal, Company.PayRuns.Single(r => r.VoidsPayRunId == run.Id));
            Assert.Equal(2, Company.PayRuns.Count);
            Assert.Empty(Company.Expenses);
            Assert.All(run.Lines, l => Assert.Null(l.ExpenseId));
            Assert.All(YearToDate(), y =>
            {
                Assert.Equal(0m, y.Pensionable);
                Assert.Equal(0m, y.Cpp);
                Assert.Equal(0m, y.Ei);
                Assert.Equal(0m, y.NonPeriodic);
            });
            Assert.Equal(CurrencyService.Format(0m), vm.YearToDateGross);
        }
        finally
        {
            vm.Cleanup();
        }
    }

    #region Helpers

    // Two employees, one with a bonus, approved so each gets a wage expense at net pay.
    private PayRun ApprovedRunWithWages()
    {
        var run = new PayRun
        {
            Id = "PR-0001",
            PayDate = PayDate,
            PeriodStart = PayDate.AddDays(-13),
            PeriodEnd = PayDate,
            Lines =
            {
                new PayRunLine
                {
                    EmployeeId = "EMP-001", EmployeeName = "Dana Smith", Province = "AB", PayPeriodsPerYear = 26,
                    BasePay = 2000m, GrossPay = 2000m, CppEmployee = 110.99m, CppEmployer = 110.99m,
                    EiEmployee = 32.60m, EiEmployer = 45.64m, FederalTax = 180m, ProvincialTax = 70m, NetPay = 1606.41m,
                },
                new PayRunLine
                {
                    EmployeeId = "EMP-002", EmployeeName = "Alex Jones", Province = "AB", PayPeriodsPerYear = 26,
                    BasePay = 1200m, Bonus = 300m, GrossPay = 1500m, CppEmployee = 80m, CppEmployer = 80m,
                    EiEmployee = 24.60m, EiEmployer = 34.44m, FederalTax = 120m, ProvincialTax = 45m, NetPay = 1230.40m,
                },
            },
        };
        Company.PayRuns.Add(run);
        Assert.Equal(2, new PayrollService().ApproveAndRecord(Company, run).Count);
        return run;
    }

    // With no dialog the page voids straight away, as it does once the user has confirmed.
    private static async Task VoidThroughThePageAsync(PayRunsPageViewModel vm, PayRun run)
    {
        var priorDialog = ConfirmationDialogProperty.GetValue(null);
        ConfirmationDialogProperty.SetValue(null, null);
        try
        {
            await vm.VoidRunCommand.ExecuteAsync(vm.PayRuns.Single(r => r.Id == run.Id));
        }
        finally
        {
            ConfirmationDialogProperty.SetValue(null, priorDialog);
        }
    }

    private List<(string Id, DateTime Date, string Currency, decimal Total, decimal TotalUSD)> Expenses() =>
        Company.Expenses
            .Select(e => (e.Id, e.Date, e.OriginalCurrency, e.Total, e.TotalUSD))
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .ToList();

    private List<(string Id, string Type, string Currency, DateTime Date, decimal Total)> Queued() =>
        Company.PendingConversions
            .Select(p => (p.TransactionId, p.TransactionType, p.OriginalCurrency, p.TransactionDate, p.Total))
            .OrderBy(p => p.TransactionId, StringComparer.Ordinal)
            .ToList();

    private List<(string EmployeeId, decimal Pensionable, decimal Insurable, decimal Cpp, decimal Cpp2, decimal Ei, decimal Qpip, decimal NonPeriodic)> YearToDate()
    {
        var payroll = new PayrollService();
        return new[] { "EMP-001", "EMP-002" }
            .Select(id =>
            {
                PayrollYearToDate ytd = payroll.YearToDateFor(Company, id);
                return (id, ytd.PensionableEarnings, ytd.InsurableEarnings, ytd.CppEmployee,
                    ytd.Cpp2Employee, ytd.EiEmployee, ytd.QpipEmployee, ytd.NonPeriodicPay);
            })
            .ToList();
    }

    #endregion
}
