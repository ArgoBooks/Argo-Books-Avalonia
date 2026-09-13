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

    // The company the queue holds entries for, and the path of its file. See CurrentCompany.
    private CompanyData? _scopeCompany;
    private string? _scopeFilePath;

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
    /// Adds a pending conversion entry and immediately persists to disk.
    /// </summary>
    public async Task AddPendingConversionAsync(PendingConversion entry)
    {
        lock (_lock)
        {
            EnsureScope();

            // Replace any existing entry for this record so a later edit's amounts win. ApplyConversion
            // converts from this snapshot, not the live row, and the self-heal is the guarantee that an
            // offline row eventually gets its correct exact-date USD (Calculations.md Rule 3a) - so the
            // snapshot must reflect the row's CURRENT amounts, not the ones from its first save.
            _queue.RemoveAll(p => p.TransactionId == entry.TransactionId);
            _queue.Add(entry);
        }

        await SaveToDiskAsync();
    }

    /// <summary>
    /// Drops queued entries for records that no longer exist, and persists.
    ///
    /// Needed because undoing an import removes the rows it created while their queue
    /// entries stay behind, and nothing else prunes them: the reconcile pass only drops
    /// entries whose record exists AND is already converted, so one whose record is gone
    /// is kept forever and retried on every pass.
    ///
    /// Takes the ids to forget rather than scanning a CompanyData for orphans, because an
    /// entry can be queued a moment before its record is added to the company.
    /// </summary>
    public async Task ForgetAsync(IEnumerable<string> transactionIds)
    {
        var ids = new HashSet<string>(transactionIds, StringComparer.Ordinal);
        if (ids.Count == 0) return;

        lock (_lock)
        {
            EnsureScope();
            _queue.RemoveAll(p => ids.Contains(p.TransactionId));
        }

        await SaveToDiskAsync();
    }

    /// <summary>
    /// Makes the queue agree with the company file for the given transactions. Processing converts
    /// whatever amount the queue holds and does not check the row is still wanted, so a row changed
    /// or dropped in the company file has to change or leave here too, or the stale one converts.
    /// The queue is updated before the first await, so a caller on the UI thread reads the company
    /// file's rows on that thread.
    /// </summary>
    public async Task MirrorAsync(CompanyData companyData, IEnumerable<string> transactionIds)
    {
        var ids = new HashSet<string>(transactionIds);
        if (ids.Count == 0) return;

        lock (_lock)
        {
            EnsureScope();
            if (!IsOpen(companyData)) return;

            _queue.RemoveAll(p => ids.Contains(p.TransactionId));
            _queue.AddRange(companyData.PendingConversions.Where(p => ids.Contains(p.TransactionId)));
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

            // Build a set of all known transaction IDs
            var existingIds = new HashSet<string>(_queue.Select(p => p.TransactionId));

            // Add any entries from CompanyData that we don't already have
            foreach (var entry in companyData.PendingConversions)
            {
                if (existingIds.Add(entry.TransactionId))
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

        var processed = new List<PendingConversion>();

        foreach (var entry in toProcess)
        {
            try
            {
                // Convert ONLY at the exact transaction-date rate (fetching it if missing). Never
                // fall back to today's or any other date's rate: a row stays pending until its own
                // date's rate is available. See docs/Calculations.md (Rule 3a).
                var rate = await exchangeService.GetExchangeRateAsync(
                    entry.OriginalCurrency, "USD", entry.TransactionDate, fetchIfMissing: true);

                if (rate <= 0)
                    continue; // Exact-date rate unavailable (offline, or future-dated); stay pending

                // Apply the conversion to the matching record (a no-op if it was deleted since it
                // was enqueued); either way the entry is done and leaves the queue.
                ApplyConversion(companyData, entry, rate);
                processed.Add(entry);
            }
            catch (Exception ex)
            {
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

                foreach (var entry in processed)
                {
                    _queue.RemoveAll(p => p.TransactionId == entry.TransactionId);
                }

                // Sync back to CompanyData
                companyData.PendingConversions.Clear();
                companyData.PendingConversions.AddRange(_queue);
            }

            // A healed Payment's EffectiveAmountUSD changes from 0 to a real value, which shifts the
            // owning invoice's USD balance. Recalculate those invoices so cross-currency outstanding
            // aggregates aren't left stale until the next company open.
            var healedInvoiceIds = processed
                .Where(e => e.TransactionType == "Payment")
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

            PendingConversionsProcessed?.Invoke(this, new PendingConversionsProcessedEventArgs(processed.Count));
        }
    }

    private static Transaction? FindTransaction(CompanyData companyData, string id, string type)
    {
        return type switch
        {
            "Expense" => companyData.Expenses.FirstOrDefault(e => e.Id == id),
            "Revenue" => companyData.Revenues.FirstOrDefault(r => r.Id == id),
            _ => null
        };
    }

    /// <summary>
    /// Applies the exact-date conversion to the record named by <paramref name="entry"/>, at the
    /// supplied <paramref name="rate"/> (original currency -> USD). Handles Revenue/Expense (every
    /// money field) and Payment/PurchaseOrder (the single amount). No-ops when the record was deleted
    /// since it was enqueued. The USD base is stored full-precision (no 2dp round) and matches the
    /// import-time conversion, so an immediately-converted row and a later-healed row are identical;
    /// display rounds at the boundary. See docs/Calculations.md Rule 3.
    /// </summary>
    private static void ApplyConversion(CompanyData companyData, PendingConversion entry, decimal rate)
    {
        switch (entry.TransactionType)
        {
            case "Revenue":
            case "Expense":
                var txn = FindTransaction(companyData, entry.TransactionId, entry.TransactionType);
                if (txn == null) return;
                txn.TotalUSD = entry.Total * rate;
                txn.TaxAmountUSD = entry.TaxAmount * rate;
                txn.ShippingCostUSD = entry.ShippingCost * rate;
                txn.DiscountUSD = entry.Discount * rate;
                txn.FeeUSD = entry.Fee * rate;
                txn.UnitPriceUSD = entry.UnitPrice * rate;
                txn.IsPendingConversion = false;
                return;

            case "Payment":
                var payment = companyData.Payments.FirstOrDefault(p => p.Id == entry.TransactionId);
                if (payment == null) return;
                payment.AmountUSD = entry.Total * rate;
                payment.IsPendingConversion = false;
                return;

            case "PurchaseOrder":
                var po = companyData.PurchaseOrders.FirstOrDefault(p => p.Id == entry.TransactionId);
                if (po == null) return;
                po.TotalUSD = entry.Total * rate;
                po.IsPendingConversion = false;
                return;

            case "Invoice":
                var invoice = companyData.Invoices.FirstOrDefault(i => i.Id == entry.TransactionId);
                if (invoice == null) return;
                invoice.TotalUSD = entry.Total * rate;
                invoice.IsPendingConversion = false;
                // If payments were recorded against this invoice, recompute BalanceUSD from them: the
                // import-time snapshot (entry.Balance) is stale once a payment lands after import. With
                // no payment rows (e.g. an imported invoice whose paid amount is baked into Balance),
                // keep the snapshot so imported partial payments aren't lost.
                if (companyData.Payments.Any(p => p.InvoiceId == invoice.Id))
                    InvoiceTotalsService.Recalculate(invoice, companyData.Payments);
                else
                    invoice.BalanceUSD = entry.Balance * rate;
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
        "Revenue" => companyData.Revenues.FirstOrDefault(r => r.Id == entry.TransactionId) is { IsPendingConversion: false },
        "Expense" => companyData.Expenses.FirstOrDefault(e => e.Id == entry.TransactionId) is { IsPendingConversion: false },
        "Payment" => companyData.Payments.FirstOrDefault(p => p.Id == entry.TransactionId) is { IsPendingConversion: false },
        "PurchaseOrder" => companyData.PurchaseOrders.FirstOrDefault(p => p.Id == entry.TransactionId) is { IsPendingConversion: false },
        "Invoice" => companyData.Invoices.FirstOrDefault(i => i.Id == entry.TransactionId) is { IsPendingConversion: false },
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
            _queue.AddRange(ReadQueueFile(filePath).Where(saved => _queue.All(p => p.TransactionId != saved.TransactionId)));
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

        try
        {
            List<PendingConversion> snapshot;
            string? filePath;
            lock (_lock)
            {
                snapshot = [.. _queue];
                filePath = GetQueueFilePath(_scopeFilePath);
            }

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
