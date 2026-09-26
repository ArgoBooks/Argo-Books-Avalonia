using System.Runtime.CompilerServices;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Converts the records waiting for their exchange rate (docs/Calculations.md Rule 3a). The queue is
/// the open company's own list (<see cref="CompanyData.PendingConversions"/>), saved in the company
/// file together with the records it converts, so the file is its only source: this service holds a
/// copy of it to work from, kept in step by <see cref="Mirror"/>.
/// </summary>
public class PendingConversionService
{
    private readonly IErrorLogger? _errorLogger;
    private readonly ExchangeRateService? _exchangeRateService;
    private readonly List<PendingConversion> _queue = [];
    private readonly Lock _lock = new();

    // When each entry was last added to the queue, so a pass removes only the entries it converted
    // and not one put back meanwhile, by an undo or redo, that still waits for its rate.
    private readonly ConditionalWeakTable<PendingConversion, StrongBox<long>> _queuedAt = new();
    private long _queueSequence;

    // The company the queue holds entries for. See CurrentCompany.
    private CompanyData? _scopeCompany;

    // Currency and date pairs whose rate could not be had, and when to ask again. The app
    // retries every 15 seconds, so without this one row that could not be priced asked the
    // server four times a minute for as long as the app stayed open. Capped low enough that
    // rows still convert within minutes of the connection coming back.
    private readonly Dictionary<string, (int Misses, DateTime RetryAtUtc)> _rateBackoff = [];
    private static readonly TimeSpan RateBackoffBase = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RateBackoffCap = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static PendingConversionService? Instance { get; private set; }

    /// <summary>
    /// Fired after pending conversions are successfully processed.
    /// UI should refresh transaction lists and charts.
    /// </summary>
    public event EventHandler<PendingConversionsProcessedEventArgs>? PendingConversionsProcessed;

    public PendingConversionService(IErrorLogger? errorLogger = null, ExchangeRateService? exchangeRateService = null)
    {
        _errorLogger = errorLogger;
        _exchangeRateService = exchangeRateService;
        Instance ??= this;
    }

    /// <summary>
    /// Returns the open company. Every company file numbers its records the same way (each has a
    /// PUR-2026-00005), so the queue holds only the open company's entries, and opening another
    /// company swaps them for that company's. Left unset, as in tests, the queue is whatever the
    /// last <see cref="ReconcileWithCompanyData"/> or <see cref="Mirror"/> gave it.
    /// </summary>
    public Func<CompanyData?>? CurrentCompany { get; set; }

    /// <summary>
    /// Number of pending conversions in the queue.
    /// </summary>
    public int PendingCount
    {
        get
        {
            lock (_lock)
            {
                EnsureScope();
                return _queue.Count;
            }
        }
    }

    /// <summary>
    /// Whether there are any pending conversions.
    /// </summary>
    public bool HasPendingConversions => PendingCount > 0;

    /// <summary>The entries waiting, as the next pass will convert them.</summary>
    public List<PendingConversion> Entries
    {
        get
        {
            lock (_lock)
            {
                EnsureScope();
                return [.. _queue];
            }
        }
    }

    /// <summary>
    /// Makes the queue agree with the company file for the given records. Processing converts
    /// whatever amount the queue holds and does not check the row is still wanted, so a row changed
    /// or dropped in the company file has to change or leave here too, or the stale one converts.
    /// Call on the thread that owns the company data.
    /// </summary>
    public void Mirror(CompanyData companyData, IEnumerable<PendingConversionKey> keys)
    {
        var mirrored = keys.ToHashSet();
        if (mirrored.Count == 0) return;

        lock (_lock)
        {
            EnsureScope();
            if (!IsOpen(companyData)) return;

            _queue.RemoveAll(p => mirrored.Contains(p.Key));
            companyData.PendingConversions.Where(p => mirrored.Contains(p.Key)).ToList().ForEach(Enqueue);
        }
    }

    /// <summary>
    /// Makes the queue the company file's list, first dropping entries whose records have already
    /// converted. Opening a company does this before its first conversion pass.
    /// </summary>
    public void ReconcileWithCompanyData(CompanyData companyData)
    {
        lock (_lock)
        {
            EnsureScope();
            if (!IsOpen(companyData)) return;

            companyData.PendingConversions.RemoveAll(p => IsConverted(companyData, p));

            // An entry already queued keeps its place, so a pass under way still takes it off.
            var entries = companyData.PendingConversions.ToHashSet(ReferenceEqualityComparer.Instance);
            _queue.RemoveAll(p => !entries.Contains(p));
            foreach (var entry in companyData.PendingConversions.Where(e => !_queue.Contains(e)).ToList())
                Enqueue(entry);
        }
    }

    private static string RateKey(PendingConversion entry) =>
        entry.OriginalCurrency + "|" + entry.TransactionDate.ToString("yyyy-MM-dd");

    private bool IsBackingOff(string rateKey)
    {
        lock (_lock)
        {
            return _rateBackoff.TryGetValue(rateKey, out var state) && DateTime.UtcNow < state.RetryAtUtc;
        }
    }

    /// <summary>Doubles the wait for this currency and date, up to the cap.</summary>
    private void RecordRateMiss(string rateKey)
    {
        lock (_lock)
        {
            var misses = _rateBackoff.TryGetValue(rateKey, out var state) ? state.Misses + 1 : 1;
            var delay = TimeSpan.FromTicks(Math.Min(
                RateBackoffCap.Ticks,
                RateBackoffBase.Ticks * (1L << Math.Min(misses - 1, 10))));
            _rateBackoff[rateKey] = (misses, DateTime.UtcNow + delay);
        }
    }

    private void ClearRateMiss(string rateKey)
    {
        lock (_lock)
        {
            _rateBackoff.Remove(rateKey);
        }
    }

    // While above 0, no pass starts, and one under way stops before its next rate. See SuspendAsync.
    private int _suspended;
    private readonly List<Task> _passes = [];

    private bool IsSuspended
    {
        get
        {
            lock (_lock)
            {
                return _suspended > 0;
            }
        }
    }

    /// <summary>
    /// Holds off conversion passes until the returned handle is disposed, once any pass under way has
    /// finished. A spreadsheet import changes the company's records and queue off the UI thread, where
    /// a pass also changes them, so it runs inside one of these.
    /// </summary>
    public async Task<IDisposable> SuspendAsync()
    {
        Task[] running;
        lock (_lock)
        {
            _suspended++;
            running = [.. _passes];
        }

        await Task.WhenAll(running);
        return new Resumer(this);
    }

    private sealed class Resumer(PendingConversionService service) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (service._lock)
            {
                service._suspended--;
            }
        }
    }

    /// <summary>
    /// Attempts to process all pending conversions by fetching exchange rates.
    /// Only processes entries where rates are available (online). Does nothing while suspended.
    /// </summary>
    public async Task ProcessPendingConversionsAsync(CompanyData companyData)
    {
        var pass = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            if (_suspended > 0) return;
            _passes.Add(pass.Task);
        }

        try
        {
            await RunPassAsync(companyData);
        }
        finally
        {
            lock (_lock)
            {
                _passes.Remove(pass.Task);
            }
            pass.SetResult();
        }
    }

    private async Task RunPassAsync(CompanyData companyData)
    {
        var exchangeService = _exchangeRateService ?? ExchangeRateService.Instance;
        if (exchangeService == null)
            return;

        List<PendingConversion> toProcess;
        lock (_lock)
        {
            EnsureScope();
            if (!IsOpen(companyData)) return;

            toProcess = [.. _queue];
        }

        if (toProcess.Count == 0)
            return;

        // Price every date the loop below needs in one bulk request, so it converts from the
        // cache. Each entry otherwise costs its own web request on a cache miss, which is one
        // per dated row: a few hundred for a company with a year of history.
        var datesToPrice = toProcess
            .Where(e => e.TransactionDate.Date <= DateTime.Today
                        && !string.Equals(e.OriginalCurrency, "USD", StringComparison.OrdinalIgnoreCase)
                        && !IsBackingOff(RateKey(e)))
            .Select(e => e.TransactionDate.Date)
            .Distinct()
            .ToList();

        if (datesToPrice.Count > 1)
        {
            try
            {
                await exchangeService.PreloadRatesAsync(datesToPrice);
            }
            catch (RateLimitedException)
            {
                // The loop would hit the same limit one request at a time. Back off only what the
                // preload was actually asking for: an entry it never covered, a USD one above all,
                // has nothing to wait for and should still convert on this pass.
                var refused = datesToPrice.ToHashSet();
                foreach (var entry in toProcess.Where(e => refused.Contains(e.TransactionDate.Date)))
                    RecordRateMiss(RateKey(entry));
            }
            catch (Exception ex)
            {
                // The per-entry path below still works, just a request at a time.
                _errorLogger?.LogWarning($"Bulk rate preload failed: {ex.Message}", "PendingConversionService");
            }
        }

        var processed = new List<PendingConversion>();
        var processedAt = new Dictionary<PendingConversion, long>(ReferenceEqualityComparer.Instance);

        foreach (var entry in toProcess)
        {
            if (IsSuspended)
                break;

            // No rate exists yet for a date that has not happened, so a row dated ahead stays queued
            // until its own date arrives rather than asking every pass for something that cannot
            // come back. Compared against the local date, which is what the row was entered in.
            if (entry.TransactionDate.Date > DateTime.Today)
                continue;

            var rateKey = RateKey(entry);
            if (IsBackingOff(rateKey))
                continue;

            try
            {
                // Convert ONLY at the exact transaction-date rate (fetching it if missing). Never
                // fall back to today's or any other date's rate: a row stays pending until its own
                // date's rate is available. See docs/Calculations.md (Rule 3a).
                var rate = await exchangeService.GetExchangeRateAsync(
                    entry.OriginalCurrency, "USD", entry.TransactionDate, fetchIfMissing: true);

                if (rate <= 0)
                {
                    RecordRateMiss(rateKey); // Exact-date rate unavailable (offline); stay pending
                    continue;
                }

                ClearRateMiss(rateKey);

                // The record may have been saved again while the rate was fetched, replacing this
                // entry with newer amounts. Those convert on the next pass; these must not overwrite them.
                long queuedAt;
                lock (_lock)
                {
                    if (!_queue.Contains(entry))
                        continue;
                    queuedAt = QueuedAt(entry);
                }

                // Apply the conversion to the matching record (a no-op if it was deleted since it
                // was enqueued); either way the entry is done and leaves the queue.
                ApplyConversion(companyData, entry, rate);
                entry.ConvertedRate = rate;
                processed.Add(entry);
                processedAt[entry] = queuedAt;
            }
            catch (Exception ex)
            {
                RecordRateMiss(rateKey);
                _errorLogger?.LogWarning($"Failed to process pending conversion for {entry.TransactionId}: {ex.Message}", "PendingConversionService");
            }
        }

        if (processed.Count > 0)
        {
            lock (_lock)
            {
                // Another company may have opened while the rates were fetched.
                EnsureScope();
                if (!IsOpen(companyData)) return;

                // Only the entries converted, as they were then: one queued for the same record
                // since is newer, and one put back since, even the same entry, waits again.
                var done = _queue.Where(p => processedAt.TryGetValue(p, out var at) && QueuedAt(p) == at)
                    .ToHashSet(ReferenceEqualityComparer.Instance);
                _queue.RemoveAll(done.Contains);
                companyData.PendingConversions.RemoveAll(done.Contains);
            }

            // A healed Payment's EffectiveAmountUSD changes from 0 to a real value, which shifts the
            // owning invoice's USD balance. Recalculate those invoices so cross-currency outstanding
            // aggregates aren't left stale until the next company open.
            var healedInvoiceIds = processed
                .Where(e => e.TransactionType == PendingConversionType.Payment)
                .Select(e => companyData.Payments.FirstOrDefault(p => p.Id == e.TransactionId)?.InvoiceId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList();
            foreach (var invoiceId in healedInvoiceIds)
            {
                var invoice = companyData.Invoices.FirstOrDefault(i => i.Id == invoiceId);
                if (invoice != null)
                    InvoiceTotalsService.Recalculate(invoice, companyData.Payments);
            }

            // Mark company data as changed so the next save includes the updated USD values
            companyData.MarkAsModified();

            // A stock cost converts alongside the purchase that set it, so it isn't counted as a transaction of its own.
            PendingConversionsProcessed?.Invoke(this, new PendingConversionsProcessedEventArgs(
                processed.Count(e => e.TransactionType != PendingConversionType.InventoryItem)));
        }
    }


    /// <summary>
    /// Applies the exact-date conversion to the record named by <paramref name="entry"/>, at the
    /// supplied <paramref name="rate"/> (original currency -> USD). Handles Revenue/Expense (every
    /// money field), Payment/PurchaseOrder (the single amount) and a stock record's unit cost
    /// (<see cref="InventoryStockService.ApplyConvertedCost"/>). No-ops when the record was deleted
    /// since it was enqueued. The fields are written by <see cref="UsdConversion"/>, as when a record
    /// converts on save, so a record converted straight away and one converted later are identical.
    /// </summary>
    private static void ApplyConversion(CompanyData companyData, PendingConversion entry, decimal rate)
    {
        switch (entry.TransactionType)
        {
            case PendingConversionType.Revenue:
                if (companyData.Revenues.FirstOrDefault(r => r.Id == entry.TransactionId) is { } revenue)
                    UsdConversion.Write(revenue, entry, rate);
                return;

            case PendingConversionType.Expense:
                if (companyData.Expenses.FirstOrDefault(e => e.Id == entry.TransactionId) is { } expense)
                    UsdConversion.Write(expense, entry, rate);
                return;

            case PendingConversionType.Payment:
                if (companyData.Payments.FirstOrDefault(p => p.Id == entry.TransactionId) is { } payment)
                    UsdConversion.Write(payment, entry, rate);
                return;

            case PendingConversionType.PurchaseOrder:
                if (companyData.PurchaseOrders.FirstOrDefault(p => p.Id == entry.TransactionId) is { } po)
                    UsdConversion.Write(po, entry, rate);
                return;

            case PendingConversionType.Invoice:
                var invoice = companyData.Invoices.FirstOrDefault(i => i.Id == entry.TransactionId);
                if (invoice == null) return;
                UsdConversion.Write(invoice, entry, rate);
                // A payment recorded since the entry was queued makes its balance stale, so the
                // balance comes from the payments. With none (an imported invoice whose paid amount
                // is baked into its balance) the queued balance stands.
                if (companyData.Payments.Any(p => p.InvoiceId == invoice.Id))
                    InvoiceTotalsService.Recalculate(invoice, companyData.Payments);
                return;

            case PendingConversionType.InventoryItem:
                InventoryStockService.ApplyConvertedCost(companyData, entry, rate);
                return;
        }
    }

    /// <summary>
    /// True when the record named by <paramref name="entry"/> still exists and is no longer pending,
    /// so its queue entry can be dropped. A deleted record returns false (kept; the process pass
    /// removes it). Mirrors the type set handled by <see cref="ApplyConversion"/>.
    /// </summary>
    private static bool IsConverted(CompanyData companyData, PendingConversion entry) => entry.TransactionType switch
    {
        PendingConversionType.Revenue => companyData.Revenues.FirstOrDefault(r => r.Id == entry.TransactionId) is { IsPendingConversion: false },
        PendingConversionType.Expense => companyData.Expenses.FirstOrDefault(e => e.Id == entry.TransactionId) is { IsPendingConversion: false },
        PendingConversionType.Payment => companyData.Payments.FirstOrDefault(p => p.Id == entry.TransactionId) is { IsPendingConversion: false },
        PendingConversionType.PurchaseOrder => companyData.PurchaseOrders.FirstOrDefault(p => p.Id == entry.TransactionId) is { IsPendingConversion: false },
        PendingConversionType.Invoice => companyData.Invoices.FirstOrDefault(i => i.Id == entry.TransactionId) is { IsPendingConversion: false },
        PendingConversionType.InventoryItem => InventoryStockService.IsCostSettled(companyData, entry.TransactionId),
        _ => false
    };

    /// <summary>Whether <paramref name="companyData"/> is the company the queue holds entries for.</summary>
    private bool IsOpen(CompanyData companyData) =>
        CurrentCompany == null || ReferenceEquals(companyData, _scopeCompany);

    /// <summary>
    /// Makes the queue the open company's: takes that company's list when another one opens.
    /// Call under <see cref="_lock"/>.
    /// </summary>
    private void EnsureScope()
    {
        if (CurrentCompany == null)
            return;

        var company = CurrentCompany();
        if (ReferenceEquals(company, _scopeCompany))
            return;

        _scopeCompany = company;
        _queue.Clear();
        company?.PendingConversions.ForEach(Enqueue);
    }

    /// <summary>Adds an entry to the queue and notes when. Call under <see cref="_lock"/>.</summary>
    private void Enqueue(PendingConversion entry)
    {
        _queuedAt.AddOrUpdate(entry, new StrongBox<long>(++_queueSequence));
        _queue.Add(entry);
    }

    private long QueuedAt(PendingConversion entry) =>
        _queuedAt.TryGetValue(entry, out var at) ? at.Value : 0;
}

/// <summary>
/// Event args for when pending conversions are processed.
/// </summary>
public class PendingConversionsProcessedEventArgs(int convertedCount) : EventArgs
{
    /// <summary>
    /// The number of transactions that were successfully converted.
    /// </summary>
    public int ConvertedCount { get; } = convertedCount;
}
