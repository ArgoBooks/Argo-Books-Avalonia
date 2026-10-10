using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Portal;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Keeps the payment portal's idea of what an invoice still owes in step with
/// this device.
/// </summary>
/// <remarks>
/// The portal only ever learns about payments taken through it. A payment
/// recorded here in cash, by cheque or by bank transfer is invisible to the
/// server, so without this the server would keep believing the invoice is
/// unpaid and the reminder cron would chase a customer who has already paid.
///
/// Pushes are debounced because recording a payment often touches an invoice
/// several times in quick succession (add, recalc, undo). The server applies a
/// relative delta rather than an absolute balance, so a redundant push is a
/// no-op and a dropped one is corrected by the next reconcile. That is what
/// makes it safe to treat every push as best-effort.
/// </remarks>
public sealed class PortalBalanceSyncService : IDisposable
{
    /// <summary>Matches the debounce used for the portal company-name push.</summary>
    private const int DebounceMilliseconds = 1500;

    /// <summary>
    /// Cap on one batch. Well under the server's limit of 200 because a
    /// part-paid invoice carries its re-rendered HTML, which dwarfs everything
    /// else in the payload. Anything trimmed is picked up by the next sweep.
    /// </summary>
    private const int MaxBatchSize = 25;

    /// <summary>
    /// History marker written when an invoice is published to the portal. Only
    /// published invoices exist server-side, so anything without this would
    /// come back not_found and would leak amounts for invoices the server was
    /// never told about. Matches the literal written by the publish path and
    /// the test already used in RevenueModalsViewModel.
    /// </summary>
    private const string PublishedHistoryAction = "Published to Portal";

    private readonly PaymentPortalService _portalService;
    private readonly Func<CompanyData?> _companyDataProvider;
    private readonly IErrorLogger? _errorLogger;

    private readonly object _gate = new();
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);

    /// <summary>Where the next reconcile sweep starts, so batches rotate instead of repeating.</summary>
    private int _reconcileCursor;
    private CancellationTokenSource? _debounceCts;
    private int _disposed;

    public PortalBalanceSyncService(
        PaymentPortalService portalService,
        Func<CompanyData?> companyDataProvider,
        IErrorLogger? errorLogger = null)
    {
        _portalService = portalService;
        _companyDataProvider = companyDataProvider;
        _errorLogger = errorLogger;
    }

    /// <summary>
    /// Notes that an invoice's payment state changed locally and schedules a
    /// push. Safe to call from any thread and from hot paths: it only touches
    /// an in-memory set and restarts a timer.
    /// </summary>
    public void Queue(string? invoiceId)
    {
        if (string.IsNullOrEmpty(invoiceId)) return;
        if (Volatile.Read(ref _disposed) != 0) return;
        if (!PortalSettings.IsConfigured) return;

        CancellationToken token;
        lock (_gate)
        {
            _pending.Add(invoiceId);

            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            token = _debounceCts.Token;
        }

        _ = DebounceThenFlushAsync(token);
    }

    private async Task DebounceThenFlushAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(DebounceMilliseconds, token);
            await FlushAsync(token);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer change; that push covers this one too.
        }
        catch (Exception ex)
        {
            _errorLogger?.LogWarning(
                $"Portal balance push failed: {ex.Message}", "PortalBalanceSync");
        }
    }

    /// <summary>
    /// Sends whatever is queued right now. Anything that cannot be resolved or
    /// was never published is dropped rather than retried.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!PortalSettings.IsConfigured) return;

        string[] ids;
        lock (_gate)
        {
            if (_pending.Count == 0) return;
            ids = [.. _pending];
            _pending.Clear();
        }

        CompanyData? companyData = _companyDataProvider();
        if (companyData == null) return;

        var items = new List<PortalBalanceSyncItem>();

        // Tracked apart from ids, because an id that resolves to nothing or to an unpublished invoice can never produce an item and is not worth re-queueing.
        var sent = new List<string>();

        foreach (string id in ids)
        {
            Invoice? invoice = companyData.GetInvoice(id);
            if (invoice == null) continue;
            if (!IsPublishedToPortal(invoice)) continue;

            items.Add(PaymentPortalService.BuildBalanceSyncItem(invoice, companyData));
            sent.Add(id);

            if (items.Count >= MaxBatchSize) break;
        }

        // Back into _pending if it did not land. They are taken out before the request so a
        // concurrent Queue is not lost, which also means nothing else will retry them.
        if (!await SendAsync(items, cancellationToken))
        {
            lock (_gate)
            {
                foreach (string id in sent)
                {
                    _pending.Add(id);
                }
            }
        }
    }

    /// <summary>
    /// Full sweep of every published invoice, so a device that recorded payments while offline
    /// catches up.
    /// </summary>
    /// <remarks>
    /// Called from the periodic portal sync, which also runs once on company
    /// open. Redundant by design: a push whose numbers have not changed is a
    /// zero delta server-side, so this costs one UPDATE that changes nothing
    /// rather than needing its own retry queue.
    /// </remarks>
    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        if (!PortalSettings.IsConfigured) return;

        CompanyData? companyData = _companyDataProvider();
        if (companyData?.Invoices == null) return;

        // Settled invoices included, not skipped: this sweep is the only retry, and a paid
        // invoice whose push failed leaves the server chasing a customer who already paid.
        List<Invoice> candidates = companyData.Invoices
            .Where(i => i.Status != InvoiceStatus.Draft && IsPublishedToPortal(i))
            .ToList();

        if (candidates.Count == 0) return;

        // Rotated, so more than MaxBatchSize published invoices are all covered across sweeps
        // instead of the first 25 being re-sent forever.
        int start;
        lock (_gate)
        {
            start = _reconcileCursor % candidates.Count;
        }

        var items = new List<PortalBalanceSyncItem>();

        for (int offset = 0; offset < candidates.Count && items.Count < MaxBatchSize; offset++)
        {
            Invoice invoice = candidates[(start + offset) % candidates.Count];
            items.Add(PaymentPortalService.BuildBalanceSyncItem(invoice, companyData));
        }

        // Advanced only on success, or a batch that failed offline is not retried until the cursor has gone all the way round.
        if (await SendAsync(items, cancellationToken))
        {
            lock (_gate)
            {
                _reconcileCursor = (start + items.Count) % candidates.Count;
            }
        }
    }

    /// <summary>Whether the push landed, so a caller can put its ids back if it did not.</summary>
    private async Task<bool> SendAsync(List<PortalBalanceSyncItem> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0) return true;

        try
        {
            PortalBalanceSyncResponse response =
                await _portalService.SyncInvoiceBalancesAsync(items, cancellationToken);

            if (!response.Success)
            {
                _errorLogger?.LogWarning(
                    $"Portal balance sync rejected: {response.Message}", "PortalBalanceSync");
            }

            return response.Success;
        }
        catch (OperationCanceledException)
        {
            // Shutdown or a superseding push. Not landed, so the caller re-queues.
            return false;
        }
        catch (Exception ex)
        {
            _errorLogger?.LogWarning(
                $"Portal balance sync failed: {ex.Message}", "PortalBalanceSync");
            return false;
        }
    }

    private static bool IsPublishedToPortal(Invoice invoice)
        => invoice.History.Any(h => h.Action == PublishedHistoryAction);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        lock (_gate)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
            _pending.Clear();
        }
    }
}
