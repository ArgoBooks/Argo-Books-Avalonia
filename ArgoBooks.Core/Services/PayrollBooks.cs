using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// How payroll shows in the books (docs/Calculations.md §10, Payroll). Approving a pay run records
/// each employee's net pay as an expense, and recording a remittance records what was paid to CRA
/// or Revenu Quebec. The Income Statement counts the cost of a pay run on its pay date instead, its
/// gross pay and the employer's contributions, and the Balance Sheet owes the remittance until it is paid.
/// </summary>
public static class PayrollBooks
{
    /// <summary>Who a remittance is paid to.</summary>
    public enum Agency { Cra, Quebec }

    /// <summary>One remittance recorded by <see cref="RecordRemittance"/>, with the runs it paid, for undo.</summary>
    public sealed record Remittance(Expense Expense, Agency Agency, IReadOnlyList<PayRun> Runs);

    /// <summary>The runs that count in the books: every one but drafts, so a voided run and its reversal cancel out.</summary>
    public static IEnumerable<PayRun> RecordedRuns(CompanyData data) =>
        data.PayRuns.Where(r => r.Status != PayRunStatus.Draft);

    /// <summary>The ids of every expense a pay run recorded: each employee's net pay and each remittance paid.</summary>
    public static HashSet<string> ExpenseIds(CompanyData data)
    {
        var ids = new HashSet<string>();
        foreach (var run in data.PayRuns)
        {
            foreach (var line in run.Lines)
            {
                if (!string.IsNullOrEmpty(line.ExpenseId))
                    ids.Add(line.ExpenseId);
            }
            if (!string.IsNullOrEmpty(run.CraRemittanceExpenseId))
                ids.Add(run.CraRemittanceExpenseId);
            if (!string.IsNullOrEmpty(run.QuebecRemittanceExpenseId))
                ids.Add(run.QuebecRemittanceExpenseId);
        }
        return ids;
    }

    private static decimal AmountFor(PayRun run, Agency agency) =>
        agency == Agency.Cra ? run.CraRemittance : run.QuebecRemittance;

    private static string? ExpenseIdFor(PayRun run, Agency agency) =>
        agency == Agency.Cra ? run.CraRemittanceExpenseId : run.QuebecRemittanceExpenseId;

    private static void Link(PayRun run, Agency agency, string? expenseId)
    {
        if (agency == Agency.Cra)
            run.CraRemittanceExpenseId = expenseId;
        else
            run.QuebecRemittanceExpenseId = expenseId;
    }

    /// <summary>Whether the run's remittance to <paramref name="agency"/> had been paid by the end of <paramref name="date"/>.</summary>
    public static bool IsRemitted(CompanyData data, PayRun run, Agency agency, DateTime date) =>
        ExpenseIdFor(run, agency) is { Length: > 0 } id
        && data.Expenses.FirstOrDefault(e => e.Id == id) is { } expense
        && expense.Date.Date <= date.Date;

    /// <summary>
    /// What was still owed to the agencies at the end of <paramref name="date"/>, run by run with its pay
    /// date, in the company's currency: each paid run's remittance until an expense records paying it.
    /// </summary>
    public static IEnumerable<(decimal Amount, DateTime PayDate)> OwingAsOf(CompanyData data, DateTime date)
    {
        foreach (var run in RecordedRuns(data).Where(r => r.PayDate.Date <= date.Date))
        {
            var owing = 0m;
            foreach (var agency in new[] { Agency.Cra, Agency.Quebec })
            {
                if (!IsRemitted(data, run, agency, date))
                    owing += AmountFor(run, agency);
            }
            if (owing != 0)
                yield return (owing, run.PayDate);
        }
    }

    /// <summary>The runs paid by <paramref name="date"/> whose remittance to <paramref name="agency"/> has no expense yet.</summary>
    public static List<PayRun> Unremitted(CompanyData data, Agency agency, DateTime date) =>
        RecordedRuns(data)
            .Where(r => r.PayDate.Date <= date.Date && AmountFor(r, agency) != 0
                        && !(ExpenseIdFor(r, agency) is { Length: > 0 } id && data.Expenses.Any(e => e.Id == id)))
            .ToList();

    /// <summary>
    /// Records paying every remittance still owed for runs paid by <paramref name="date"/>: one expense per
    /// agency, dated <paramref name="date"/>, which each of those runs then points at.
    /// </summary>
    public static List<Remittance> RecordRemittance(CompanyData data, DateTime date)
    {
        var currency = string.IsNullOrWhiteSpace(data.Settings.Localization.Currency) ? "USD" : data.Settings.Localization.Currency;
        var recorded = new List<Remittance>();

        foreach (var agency in new[] { Agency.Cra, Agency.Quebec })
        {
            var runs = Unremitted(data, agency, date);
            var amount = runs.Sum(r => AmountFor(r, agency));
            if (amount == 0)
                continue;

            var name = agency == Agency.Cra ? "CRA" : "Revenu Quebec";
            var expense = TransactionFactory.CreateExpense(data, new TransactionDraft(
                Date: date,
                Description: $"Payroll remittance - {name}",
                Total: amount,
                CounterpartyId: null,
                Notes: $"Deductions withheld and employer contributions for pay runs {string.Join(", ", runs.Select(r => r.Id))}.",
                OriginalCurrency: currency));
            UsdConversion.Apply(data, expense, UsdConversion.CachedRate(currency, date));
            data.Expenses.Add(expense);

            foreach (var run in runs)
                Link(run, agency, expense.Id);
            recorded.Add(new Remittance(expense, agency, runs));
        }

        return recorded;
    }

    /// <summary>Takes back what <see cref="RecordRemittance"/> recorded.</summary>
    public static void UndoRemittance(CompanyData data, IEnumerable<Remittance> remittances)
    {
        foreach (var remittance in remittances)
        {
            data.Expenses.RemoveRecord(remittance.Expense);
            UsdConversion.Set(data, UsdConversion.KeyOf(remittance.Expense), null);
            foreach (var run in remittance.Runs)
                Link(run, remittance.Agency, null);
        }
    }

    /// <summary>Puts back what <see cref="UndoRemittance"/> took back.</summary>
    public static void RedoRemittance(CompanyData data, IEnumerable<Remittance> remittances)
    {
        foreach (var remittance in remittances)
        {
            data.Expenses.RestoreRecord(remittance.Expense);
            UsdConversion.Requeue(data, remittance.Expense);
            foreach (var run in remittance.Runs)
                Link(run, remittance.Agency, remittance.Expense.Id);
        }
    }
}
