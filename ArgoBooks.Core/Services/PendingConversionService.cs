using System.Security.Cryptography;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Platform;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Manages a persistent queue of transactions saved offline that need USD conversion.
/// Uses two-layer persistence: an app-data file per company (immediate) + CompanyData (on save).
/// </summary>
public class PendingConversionService
{
    // One file per company in this folder. Earlier versions kept every company's entries in a
    // single pending_conversions.json, which is no longer read: its entries can't be told apart by
    // company, and each company file carries its own list.
    private const string QueueFolderName = "pending_conversions";

    private readonly IPlatformService _platformService;
    private readonly IErrorLogger? _errorLogger;
    private readonly ExchangeRateService? _exchangeRateService;
    private readonly List<PendingConversion> _queue = [];
    private readonly Lock _lock = new();

    // Saves are serialized and coalesced: a burst of queue changes, such as an import queuing a
    // row at a time, writes the file once or twice rather than once per row, and never twice at once.
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private long _saveRequests;
    private long _savedThrough;

    // The company the queue holds entries for, and the path of its file. See CurrentCompany.
    private CompanyData? _scopeCompany;
    private string? _scopeFilePath;

    // Currency and date pairs whose rate could not be had, and when to ask again. The app
    // retries every 15 seconds, so without this one row that could not be priced asked the
    // server four times a minute for as long as the app stayed open. Capped low enough that
    // rows still convert within minutes of the connection coming back.
    private readonly Dictionary<string, (int Misses, DateTime RetryAtUtc)> _rateBackoff = [];
    private static readonly TimeSpan RateBackoffBase = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RateBackoffCap = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static PendingConversionService? Instance { get; private set; }

    /// <summary>
    /// Fired after pending conversions are successfully processed.
    /// UI should refresh transaction lists and charts.
    /// </summary>
    public event EventHandler<PendingConversionsProcessedEventArgs>? PendingConversionsProcessed;

    public PendingConversionService(IErrorLogger? errorLogger = null)
        : this(PlatformServiceFactory.GetPlatformService(), errorLogger)
    {
    }

    public PendingConversionService(IPlatformService platformService, IErrorLogger? errorLogger = null, ExchangeRateService? exchangeRateService = null)
    {
        _platformService = platformService;
        _errorLogger = errorLogger;
        _exchangeRateService = exchangeRateService;
        Instance ??= this;
    }

    /// <summary>
    /// Returns the open company and the path of its file. Every company file numbers its records
    /// the same way (each has a PUR-2026-00005), so the queue holds only the open company's
    /// entries and keeps each company's in a file of its own. One queue matched on id let one
    /// company's entry replace another's and convert onto its record. Left unset, as in tests, the
    /// queue is a single list kept in memory.
    /// </summary>
    public Func<(CompanyData? Company, string? FilePath)>? CurrentCompany { get; set; }

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

    /// <summary>
    /// Adds a pending conversion entry, replacing any for the same record, and persists to disk.
    /// Records are normally queued through <see cref="UsdConversion"/>, which keeps the company
    /// file's list and this queue in step; this adds to the queue alone.
    /// </summary>
    public async Task AddPendingConversionAsync(PendingConversion entry)
    {
        lock (_lock)
        {
            EnsureScope();

            // A later edit's amounts win: the queue converts from this snapshot, not the live row.
            _queue.RemoveAll(p => p.Key == entry.Key);
            _queue.Add(entry);
        }

        await SaveToDiskAsync();
    }

    /// <summary>
    /// Makes the queue agree with the company file for the given records. Processing converts
    /// whatever amount the queue holds and does not check the row is still wanted, so a row changed
    /// or dropped in the company file has to change or leave here too, or the stale one converts.
    /// The queue is updated before the first await, so a caller on the UI thread reads the company
    /// file's rows on that thread.
    /// </summary>
    public async Task MirrorAsync(CompanyData companyData, IEnumerable<PendingConversionKey> keys)
    {
        var mirrored = keys.ToHashSet();
        if (mirrored.Count == 0) return;

        lock (_lock)
        {
            EnsureScope();
            if (!IsOpen(companyData)) return;

            // Nothing queued for these records on either side, as for a record saved with its rate.
            var after = companyData.PendingConversions.Where(p => mirrored.Contains(p.Key)).ToList();
            if (_queue.RemoveAll(p => mirrored.Contains(p.Key)) == 0 && after.Count == 0)
                return;

            _queue.AddRange(after);
        }

        await SaveToDiskAsync();
    }

    /// <summary>
    /// Loads the open company's queue from its app-data file. Opening a company does this too,
    /// so there is nothing to load before one is open.
    /// </summary>
    public Task LoadAsync()
    {
        lock (_lock)
        {
            EnsureScope();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reconciles the in-memory queue with the CompanyData's PendingConversions list.
    /// Merges entries from both sources (app-data file may have entries not yet in .argo file and vice versa).
    /// Also removes entries for transactions that have already been converted (IsPendingConversion = false).
    /// </summary>
    public async Task ReconcileWithCompanyDataAsync(CompanyData companyData)
    {
        lock (_lock)
        {
            EnsureScope();
            if (!IsOpen(companyData)) return;

            // Add any entries from CompanyData that we don't already have. Keyed on id and type:
            // imported sheets keep their own ids, so a stock record and a revenue can share one.
            var existingKeys = _queue.Select(p => p.Key).ToHashSet();
            foreach (var entry in companyData.PendingConversions)
            {
                if (existingKeys.Add(entry.Key))
                {
                    _queue.Add(entry);
                }
            }

            // Remove entries for records that have already been converted
            _queue.RemoveAll(p => IsConverted(companyData, p));

            // Sync back to CompanyData
            companyData.PendingConversions.Clear();
            companyData.PendingConversions.AddRange(_queue);
        }

        await SaveToDiskAsync();
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

    /// <summary>
    /// Attempts to process all pending conversions by fetching exchange rates.
    /// Only processes entries where rates are available (online).
    /// </summary>
    public async Task ProcessPendingConversionsAsync(CompanyData companyData)
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

        foreach (var entry in toProcess)
        {
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
                lock (_lock)
                {
                    if (!_queue.Contains(entry))
                        continue;
                }

                // Apply the conversion to the matching record (a no-op if it was deleted since it
                // was enqueued); either way the entry is done and leaves the queue.
                ApplyConversion(companyData, entry, rate);
                entry.ConvertedRate = rate;
                processed.Add(entry);
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

                // Only the entries converted: one queued for the same record since is newer.
                var done = processed.ToHashSet(ReferenceEqualityComparer.Instance);
                _queue.RemoveAll(done.Contains);

                // Sync back to CompanyData
                companyData.PendingConversions.Clear();
                companyData.PendingConversions.AddRange(_queue);
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

            await SaveToDiskAsync();

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
    /// Makes the queue the open company's: loads that company's entries when another one opens.
    /// When the open company's path changes (set once it has loaded, then by Save As or a rename)
    /// its entries stay, and its file goes with it. Call under <see cref="_lock"/>.
    /// </summary>
    private void EnsureScope()
    {
        if (CurrentCompany == null)
            return;

        var (company, filePath) = CurrentCompany();
        if (!ReferenceEquals(company, _scopeCompany))
        {
            _scopeCompany = company;
            _scopeFilePath = filePath;
            _queue.Clear();
            _queue.AddRange(ReadQueueFile(filePath));
            return;
        }

        if (_platformService.PathComparer.Equals(filePath, _scopeFilePath))
            return;

        if (_scopeFilePath == null)
            _queue.AddRange(ReadQueueFile(filePath).Where(saved => _queue.All(p => p.Key != saved.Key)));
        else
            MoveQueueFile(_scopeFilePath, filePath);
        _scopeFilePath = filePath;
    }

    private List<PendingConversion> ReadQueueFile(string? companyFilePath)
    {
        var filePath = GetQueueFilePath(companyFilePath);
        if (!_platformService.SupportsFileSystem || filePath == null || !File.Exists(filePath))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<PendingConversion>>(File.ReadAllText(filePath), JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            _errorLogger?.LogWarning($"Failed to load pending conversions: {ex.Message}", "PendingConversionService");
            return [];
        }
    }

    private void MoveQueueFile(string fromCompanyPath, string? toCompanyPath)
    {
        var from = GetQueueFilePath(fromCompanyPath);
        var to = GetQueueFilePath(toCompanyPath);
        if (!_platformService.SupportsFileSystem || from == null || to == null || !File.Exists(from))
            return;

        try
        {
            File.Move(from, to, overwrite: true);
        }
        catch (Exception ex)
        {
            _errorLogger?.LogWarning($"Failed to move pending conversions: {ex.Message}", "PendingConversionService");
        }
    }

    private async Task SaveToDiskAsync()
    {
        if (!_platformService.SupportsFileSystem)
            return;

        var request = Interlocked.Increment(ref _saveRequests);
        await _saveGate.WaitAsync();
        try
        {
            // A save that began after this request was made has already written what it asked for.
            if (Interlocked.Read(ref _savedThrough) >= request)
                return;
            var through = Interlocked.Read(ref _saveRequests);

            List<PendingConversion> snapshot;
            string? filePath;
            lock (_lock)
            {
                snapshot = [.. _queue];
                filePath = GetQueueFilePath(_scopeFilePath);
            }
            Interlocked.Exchange(ref _savedThrough, through);

            if (filePath == null)
                return;

            if (snapshot.Count == 0)
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
                return;
            }

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                _platformService.EnsureDirectoryExists(directory);
            }

            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            await File.WriteAllTextAsync(filePath, json);
        }
        catch (Exception ex)
        {
            _errorLogger?.LogWarning($"Failed to save pending conversions: {ex.Message}", "PendingConversionService");
        }
        finally
        {
            _saveGate.Release();
        }
    }

    /// <summary>
    /// The company's queue file, named by a hash of the company's path so any path gives a valid
    /// file name. Null when the company has no file yet.
    /// </summary>
    private string? GetQueueFilePath(string? companyFilePath)
    {
        if (string.IsNullOrEmpty(companyFilePath))
            return null;

        var key = _platformService.NormalizePath(companyFilePath);
        if (_platformService.PathComparer.Equals("a", "A"))
            key = key.ToUpperInvariant();
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32];
        return _platformService.CombinePaths(_platformService.GetAppDataPath(), QueueFolderName, name + ".json");
    }
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
