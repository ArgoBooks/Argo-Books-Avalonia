namespace ArgoBooks.Core.Services;

/// <summary>
/// The free plan's monthly allowances, as last reported by the server.
///
/// The server owns these numbers (config/pricing.php on the website, served by
/// /api/pricing/plans.php and enforced by the usage endpoints). The values here are what the app
/// quotes on screen before the first fetch lands, or when it is offline, so they are a fallback
/// rather than a second source of truth: changing a limit is a website change, not a release.
/// </summary>
public static class FreePlanLimits
{
    /// <summary>Invoices a free company can send per calendar month.</summary>
    public static int InvoiceMonthly { get; private set; } = 25;

    /// <summary>AI receipt scans a free company gets per calendar month.</summary>
    public static int ReceiptScanMonthly { get; private set; } = 10;

    /// <summary>
    /// Raised once the limits have been refreshed, so anything already rendered with the
    /// fallback values can re-read them.
    /// </summary>
    public static event EventHandler? Changed;

    /// <summary>
    /// Applies the limits from a plans response. A null or non-positive figure leaves the
    /// current value alone, so a partial response cannot blank out a limit.
    /// </summary>
    public static void Apply(int? invoiceMonthly, int? receiptScanMonthly)
    {
        var changed = false;

        if (invoiceMonthly is > 0 && invoiceMonthly.Value != InvoiceMonthly)
        {
            InvoiceMonthly = invoiceMonthly.Value;
            changed = true;
        }

        if (receiptScanMonthly is > 0 && receiptScanMonthly.Value != ReceiptScanMonthly)
        {
            ReceiptScanMonthly = receiptScanMonthly.Value;
            changed = true;
        }

        if (changed)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
