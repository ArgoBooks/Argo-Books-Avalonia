using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Produces the transactions a recurring schedule has come due for. Generation is keyed on each
/// schedule's NextDate so occurrences missed while the app was closed are caught up on open.
/// Generated entries are real transactions flagged for review, not rows held outside the books:
/// holding them out would mean every total and report needed a filter to exclude them.
/// </summary>
/// <summary>
/// What correcting a schedule's past entries changed, so undo and redo put back exactly that. A
/// correction moves the amounts, the USD figures, the pending flag and the queued conversion
/// together; an undo that restored only the entered amounts left the USD figures, which every
/// report reads, at the corrected value.
/// </summary>
public sealed class OccurrenceCorrection
{
    private readonly IReadOnlyList<OccurrenceSnapshot> _before;
    private readonly IReadOnlyList<OccurrenceSnapshot> _after;

    internal OccurrenceCorrection(IReadOnlyList<OccurrenceSnapshot> before, IReadOnlyList<OccurrenceSnapshot> after)
    {
        _before = before;
        _after = after;
    }

    public void Revert(CompanyData data)
    {
        foreach (var state in _before)
            state.Restore(data);
    }

    public void Reapply(CompanyData data)
    {
        foreach (var state in _after)
            state.Restore(data);
    }
}

/// <summary>Every field a correction can write on one entry, and the conversion queued for it.</summary>
internal sealed class OccurrenceSnapshot
{
    private readonly Transaction _target;
    private readonly decimal _quantity, _unitPrice, _amount, _subtotal, _total;
    private readonly string _originalCurrency;
    private readonly decimal _totalUsd, _unitPriceUsd, _taxAmountUsd, _shippingCostUsd, _discountUsd, _feeUsd;
    private readonly bool _isPendingConversion;
    private readonly DateTime _updatedAt;
    private readonly string _lineItems;
    private readonly string? _queued;

    public OccurrenceSnapshot(CompanyData data, Transaction target)
    {
        _target = target;
        _quantity = target.Quantity;
        _unitPrice = target.UnitPrice;
        _amount = target.Amount;
        _subtotal = target is Revenue revenue ? revenue.Subtotal : 0m;
        _total = target.Total;
        _originalCurrency = target.OriginalCurrency;
        _totalUsd = target.TotalUSD;
        _unitPriceUsd = target.UnitPriceUSD;
        _taxAmountUsd = target.TaxAmountUSD;
        _shippingCostUsd = target.ShippingCostUSD;
        _discountUsd = target.DiscountUSD;
        _feeUsd = target.FeeUSD;
        _isPendingConversion = target.IsPendingConversion;
        _updatedAt = target.UpdatedAt;

        // Serialized rather than copied field by field, so a field added to either type later is
        // still carried back.
        _lineItems = JsonSerializer.Serialize(target.LineItems, RecurringTransactionService.CloneOptions);
        var queued = UsdConversion.Queued(data, UsdConversion.KeyOf(target));
        _queued = queued == null ? null : JsonSerializer.Serialize(queued, RecurringTransactionService.CloneOptions);
    }

    public void Restore(CompanyData data)
    {
        _target.Quantity = _quantity;
        _target.UnitPrice = _unitPrice;
        _target.Amount = _amount;
        if (_target is Revenue revenue)
            revenue.Subtotal = _subtotal;
        _target.Total = _total;
        _target.OriginalCurrency = _originalCurrency;
        _target.TotalUSD = _totalUsd;
        _target.UnitPriceUSD = _unitPriceUsd;
        _target.TaxAmountUSD = _taxAmountUsd;
        _target.ShippingCostUSD = _shippingCostUsd;
        _target.DiscountUSD = _discountUsd;
        _target.FeeUSD = _feeUsd;
        _target.IsPendingConversion = _isPendingConversion;
        _target.UpdatedAt = _updatedAt;
        _target.LineItems = JsonSerializer.Deserialize<List<LineItem>>(_lineItems, RecurringTransactionService.CloneOptions)!;

        UsdConversion.Set(data, UsdConversion.KeyOf(_target), _queued == null
            ? null
            : JsonSerializer.Deserialize<PendingConversion>(_queued, RecurringTransactionService.CloneOptions)!);
    }
}

public static class RecurringTransactionService
{
    /// <summary>Stops a corrupt far-past date with a short cadence from spinning.</summary>
    public const int MaxOccurrencesPerSchedulePerRun = 500;

    internal static readonly JsonSerializerOptions CloneOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static event Action<int>? ExpensesGenerated;
    public static event Action<int>? RevenuesGenerated;

    /// <summary>
    /// Counts the Expenses and Revenue pages read on construction, so a banner survives generation
    /// happening while the user is still on the dashboard.
    /// </summary>
    public static int PendingExpenseCount { get; private set; }

    public static int PendingRevenueCount { get; private set; }

    public static void RaiseGenerated(int expenses, int revenues)
    {
        PendingExpenseCount = expenses;
        PendingRevenueCount = revenues;
        if (expenses > 0) ExpensesGenerated?.Invoke(expenses);
        if (revenues > 0) RevenuesGenerated?.Invoke(revenues);
    }

    public static void ClearPendingExpenses() => PendingExpenseCount = 0;

    public static void ClearPendingRevenues() => PendingRevenueCount = 0;

    /// <summary>
    /// Generates every occurrence due on or before <paramref name="today"/>, the local calendar
    /// date. Schedule dates are calendar dates, so the UTC date ran a day early in the evening
    /// west of Greenwich and a day late in the morning east of it. The rates are injectable so
    /// this does not have to reach for the exchange rate singleton, which is set once per process
    /// and cannot be controlled by a caller.
    /// </summary>
    public static IReadOnlyList<Transaction> GenerateDue(
        CompanyData data, DateTime today, UsdRateSource? rates = null)
    {
        var generated = new List<Transaction>();
        var asOfDate = today.Date;
        var takenIds = new Lazy<TakenIds>(() => new TakenIds(
            IdGenerator.TakenSet(data.Revenues.Select(r => r.Id)),
            IdGenerator.TakenSet(data.Expenses.Select(e => e.Id))));

        foreach (var schedule in data.RecurringTransactions)
        {
            if (schedule.Template == null) continue;
            if (schedule.Status != RecurringTransactionStatus.Active) continue;

            var count = 0;
            while (schedule.NextDate.Date <= asOfDate && count < MaxOccurrencesPerSchedulePerRun)
            {
                if (schedule.EndDate != null && schedule.NextDate.Date > schedule.EndDate.Value.Date)
                {
                    schedule.Status = RecurringTransactionStatus.Completed;
                    break;
                }

                var occurrence = schedule.NextDate.Date;

                var skipped = schedule.SkippedDates.Any(d => d.Date == occurrence);
                if (!skipped && !AlreadyGenerated(data, schedule, occurrence))
                {
                    generated.Add(CloneFor(schedule, occurrence, data, rates, takenIds.Value));
                    schedule.LastGeneratedAt = DateTime.UtcNow;
                }

                schedule.NextDate = RecurrenceSchedule.AdvanceDate(
                    schedule.NextDate, schedule.Frequency, schedule.StartDate.Day);
                count++;

                if (schedule.EndDate != null && schedule.NextDate.Date > schedule.EndDate.Value.Date)
                {
                    schedule.Status = RecurringTransactionStatus.Completed;
                    break;
                }
            }
        }

        return generated;
    }

    /// <summary>
    /// Whether any schedule would be left pointing at nothing if this customer went away.
    ///
    /// <para>
    /// The reference lives on the template rather than on the schedule, and the template is what
    /// each occurrence is cloned from, so a schedule outliving its counterparty keeps generating
    /// entries against a record that no longer exists. The delete guards ask through here rather
    /// than reaching into the templates themselves, so the three of them cannot answer this
    /// differently.
    /// </para>
    /// </summary>
    public static bool IsCustomerInUse(CompanyData data, string customerId) =>
        !string.IsNullOrEmpty(customerId) && data.RecurringTransactions.Any(
            s => s.RevenueTemplate?.CustomerId == customerId);

    /// <inheritdoc cref="IsCustomerInUse"/>
    public static bool IsSupplierInUse(CompanyData data, string supplierId) =>
        !string.IsNullOrEmpty(supplierId) && data.RecurringTransactions.Any(
            s => s.ExpenseTemplate?.SupplierId == supplierId);

    /// <inheritdoc cref="IsCustomerInUse"/>
    public static bool IsProductInUse(CompanyData data, string productId) =>
        !string.IsNullOrEmpty(productId) && data.RecurringTransactions.Any(
            s => Uses(s.ExpenseTemplate, productId) || Uses(s.RevenueTemplate, productId));

    private static bool Uses(Transaction? template, string productId) =>
        template != null && template.LineItems.Any(li => li.ProductId == productId);

    /// <summary>
    /// Records an occurrence as skipped. Undoing a generated entry calls this, so undo means
    /// "not this one" rather than having it reappear on the next open.
    /// </summary>
    public static void SkipOccurrence(RecurringTransaction schedule, DateTime occurrence)
    {
        var date = occurrence.Date;
        if (!schedule.SkippedDates.Any(d => d.Date == date))
            schedule.SkippedDates.Add(date);
    }

    public static void UnskipOccurrence(RecurringTransaction schedule, DateTime occurrence)
    {
        schedule.SkippedDates.RemoveAll(d => d.Date == occurrence.Date);
    }

    /// <summary>
    /// Entries this schedule generated that still carry the old amount, which is what the prompt
    /// tells the user it will change. Bank-matched entries are excluded: rewriting a matched amount
    /// breaks the match without telling anyone. So is an entry whose amount or line items were
    /// changed by hand, since correcting it would overwrite that change.
    /// </summary>
    public static IReadOnlyList<Transaction> FindCorrectableOccurrences(
        CompanyData data, RecurringTransaction schedule, decimal oldAmount)
    {
        var source = schedule.Type == CategoryType.Revenue
            ? data.Revenues.Cast<Transaction>()
            : data.Expenses.Cast<Transaction>();

        return source
            .Where(t => t.RecurringScheduleId == schedule.Id
                        && !t.BankMatched
                        && t.Amount == oldAmount
                        && t.LineItems.Count <= 1
                        && t.LineItems.All(IsTemplateShaped))
            .ToList();
    }

    /// <summary>
    /// Moves the given entries to the schedule's new amount. Only the amount belongs to the
    /// schedule: an entry's tax, shipping, discount and fee were added by hand while reviewing it,
    /// and the template carries none of them, so they are kept and the total rebuilt around them.
    /// Each entry is then converted at its own date, the way generation does it, rather than taking
    /// the template's single start-date figure, which is also a raw unconverted amount whenever the
    /// schedule was saved without a rate to hand.
    /// </summary>
    public static OccurrenceCorrection CorrectOccurrences(
        CompanyData data, RecurringTransaction schedule, IReadOnlyList<Transaction> targets, UsdRateSource? rates = null)
    {
        var before = targets.Select(t => new OccurrenceSnapshot(data, t)).ToList();

        var template = schedule.Template;
        if (template != null)
        {
            foreach (var target in targets)
            {
                target.Quantity = template.Quantity;
                target.UnitPrice = template.UnitPrice;
                target.Amount = template.Amount;
                if (target is Revenue revenue)
                    revenue.Subtotal = template.Amount;

                // The edit form rebuilds the subtotal from the line items, so a line left at the
                // old price would put the old amount back the next time the entry was saved.
                if (target.LineItems.Count == 1 && IsTemplateShaped(target.LineItems[0]))
                    target.LineItems[0].UnitPrice = template.Amount;

                target.Total = target.Amount + target.TaxAmount + target.ShippingCost + target.Fee - target.Discount;
                target.OriginalCurrency = template.OriginalCurrency;
                target.UpdatedAt = DateTime.UtcNow;

                UsdConversion.Apply(data, target, UsdConversion.CachedRate(target.OriginalCurrency, target.Date, rates));
            }
        }

        var after = targets.Select(t => new OccurrenceSnapshot(data, t)).ToList();
        return new OccurrenceCorrection(before, after);
    }

    /// <summary>The single line a schedule generates: one unit, no discount, no line tax.</summary>
    private static bool IsTemplateShaped(LineItem line) =>
        line.Quantity == 1 && line.Discount == 0 && line.TaxRate == 0;

    /// <summary>
    /// Keys on the schedule and occurrence date rather than a counter, so a restored backup, a
    /// second run, or a crash mid-run cannot produce a second copy.
    /// </summary>
    private static bool AlreadyGenerated(CompanyData data, RecurringTransaction schedule, DateTime occurrence)
    {
        bool Matches(Transaction t) =>
            t.RecurringScheduleId == schedule.Id && t.OccurrenceDate?.Date == occurrence;

        return schedule.Type == CategoryType.Revenue
            ? data.Revenues.Any(Matches)
            : data.Expenses.Any(Matches);
    }

    /// <summary>The revenue and expense ids taken, built once per run rather than once per occurrence.</summary>
    private sealed record TakenIds(HashSet<string> Revenues, HashSet<string> Expenses);

    private static Transaction CloneFor(
        RecurringTransaction schedule, DateTime occurrence, CompanyData data, UsdRateSource? rates, TakenIds taken)
    {
        Transaction entry;

        if (schedule.Type == CategoryType.Revenue)
        {
            var revenue = Clone(schedule.RevenueTemplate!);
            revenue.Id = new IdGenerator(data).NextRevenueId(occurrence, taken.Revenues);
            data.Revenues.Add(revenue);
            entry = revenue;
        }
        else
        {
            var expense = Clone(schedule.ExpenseTemplate!);
            expense.Id = new IdGenerator(data).NextExpenseId(occurrence, taken.Expenses);
            data.Expenses.Add(expense);
            entry = expense;
        }

        entry.Date = occurrence;
        entry.OccurrenceDate = occurrence;
        entry.RecurringScheduleId = schedule.Id;
        entry.NeedsReview = true;

        // A template is built from a real transaction, so it can carry links that belong to that
        // one occurrence rather than to the schedule.
        entry.ReceiptId = null;
        entry.BankMatched = false;
        entry.BankMatchedDate = null;
        entry.BankMatchedLineId = null;

        entry.CreatedAt = DateTime.UtcNow;
        entry.UpdatedAt = DateTime.UtcNow;

        // The template was priced at the schedule's start date. Each occurrence falls on a
        // different day, so it is priced at its own, or queued, the way a hand-entered one is.
        UsdConversion.Apply(data, entry, UsdConversion.CachedRate(entry.OriginalCurrency, occurrence, rates));
        return entry;
    }

    private static T Clone<T>(T source) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(source, CloneOptions), CloneOptions)!;
}
