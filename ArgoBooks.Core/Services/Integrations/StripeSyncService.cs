using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Integrations;

namespace ArgoBooks.Core.Services.Integrations;

public record StripeSyncPreview(
    IReadOnlyList<StripeChargeDetail> Charges,
    string? NewCursor,
    IReadOnlyList<StripePayoutSummary> NewPayouts,
    IReadOnlyList<StripeRefund> LaterRefunds,
    string? NewRefundCursor)
{
    /// <summary>True when there's anything to import: new revenue/fees, refunds on earlier sales, or new payouts to remember.</summary>
    public bool HasActivity => Charges.Count > 0 || NewPayouts.Count > 0 || LaterRefunds.Count > 0;

    /// <summary>Each charge's gross, in its own currency, as the import records it.</summary>
    public IReadOnlyList<IncomingAmount> Sales => Charges
        .Select(c =>
        {
            var currency = ImportLookup.NormalizeCurrency(c.Currency);
            return new IncomingAmount(ArgoMoney.ToDecimal(c.GrossCents, currency), currency, DateOf(c));
        })
        .ToList();

    /// <summary>Each charge's processing fee, in the fee's own currency, as the import records it.</summary>
    public IReadOnlyList<IncomingAmount> Fees => Charges
        .Where(c => c.FeeCents > 0)
        .Select(c =>
        {
            var currency = ImportLookup.NormalizeCurrency(c.FeeCurrency, fallback: ImportLookup.NormalizeCurrency(c.Currency));
            return new IncomingAmount(ArgoMoney.ToDecimal(c.FeeCents, currency), currency, DateOf(c));
        })
        .ToList();

    private static DateTime DateOf(StripeChargeDetail charge) =>
        DateTimeOffset.FromUnixTimeSeconds(charge.CreatedUnix).LocalDateTime;
}

/// <summary>
/// Orchestrates a Stripe sync. Revenue, fees and refunds come from a detailed per-charge
/// fetch (fetched newest-first until the last-synced watermark), each charge becoming its
/// own Revenue with product/customer/tax/discount. Payouts come from the payouts list and
/// are remembered only so a later bank import can auto-ignore the matching deposit.
/// Separating the two avoids Stripe's automatic-payout-only transaction grouping, so it
/// works for manual payouts too. Preview is read-only; import writes the records, remembers
/// the payouts, and advances the cursor.
/// </summary>
public class StripeSyncService(StripeApiClient client)
{
    public async Task<StripeSyncPreview> PreviewAsync(CompanyData data, CancellationToken ct = default)
    {
        var stripe = data.Settings.Integrations.Stripe;
        if (string.IsNullOrWhiteSpace(stripe.ApiKey))
            return Empty();

        var fetched = await client.FetchNewChargesAsync(stripe.ApiKey!, stripe.LastSyncCursor, ct);
        var newCursor = fetched.Cursor;

        // A charge already in the books is read again after a disconnect and reconnect, which
        // clears the cursor, and while an older charge is still pending. It is not imported twice.
        var inBooks = new HashSet<string>(
            data.Revenues.Select(r => r.ReferenceNumber).Where(r => !string.IsNullOrEmpty(r)), StringComparer.Ordinal);
        var rawCharges = fetched.Charges.Where(c => !inBooks.Contains(c.ChargeId)).ToList();

        // A charge's own expanded balance_transaction can silently yield a zero fee, so fill fees
        // from the balance-transactions list (the reliable source), keyed by charge id.
        var feeMap = rawCharges.Count > 0
            ? await client.FetchChargeFeesAsync(stripe.ApiKey!, ct)
            : new Dictionary<string, StripeFee>();
        var charges = rawCharges
            .Select(c => feeMap.TryGetValue(c.ChargeId, out var fee) && fee.Cents > c.FeeCents
                ? c with { FeeCents = fee.Cents, FeeCurrency = fee.Currency ?? c.FeeCurrency }
                : c)
            .ToList();

        var payouts = await client.FetchPayoutsAsync(stripe.ApiKey!, ct);
        var known = new HashSet<string>(stripe.ImportedPayouts.Select(p => p.StripePayoutId), StringComparer.Ordinal);
        var newPayouts = payouts
            .Where(p => !known.Contains(p.Id) && p.Status is not ("canceled" or "failed"))
            .ToList();

        var refunds = await FetchRefundsAsync(stripe, ct);
        var laterRefunds = refunds.Refunds
            .Where(r => StripeDetailImporter.SaleAwaiting(data, r) != null)
            .ToList();

        return new StripeSyncPreview(charges, newCursor, newPayouts, laterRefunds, refunds.Cursor);
    }

    /// <summary>
    /// The refunds made since the last sync. The charge list is only read back to the last sync,
    /// so a refund made afterwards on an older sale never appears there. A key without access to
    /// refunds does not stop the sync; it carries on without them.
    /// </summary>
    private async Task<StripeRefundFetch> FetchRefundsAsync(StripeIntegrationSettings stripe, CancellationToken ct)
    {
        try
        {
            return await client.FetchNewRefundsAsync(stripe.ApiKey!, stripe.LastRefundCursor, ct);
        }
        catch (HttpRequestException)
        {
            return new StripeRefundFetch([], stripe.LastRefundCursor);
        }
    }

    /// <summary>
    /// Imports the preview, first caching the exchange rates for the dates it is about to write.
    ///
    /// Without that step every row landing on a day the rate cache does not already hold shows
    /// "Pending" in place of its amount, and stays that way, because nothing refetches rates for
    /// rows already in the books. Fetching is best-effort; see <see cref="IntegrationRates"/>.
    /// </summary>
    public async Task<StripeImportCreation> ImportPreviewAsync(
        CompanyData data, StripeSyncPreview preview,
        IProgress<int>? rateProgress = null, CancellationToken ct = default)
    {
        await IntegrationRates.EnsureAsync(
            preview.Sales.Concat(preview.Fees),
            data.Settings.Localization.Currency,
            rateProgress,
            ct: ct);

        return ImportPreview(data, preview);
    }

    /// <summary>
    /// Imports the preview and returns a record of everything created, so the caller can register
    /// a single undo/redo for the whole sync. Import only appends, so the created items are the
    /// tail of each collection past the pre-import counts.
    ///
    /// Prefer <see cref="ImportPreviewAsync"/>: this overload writes rows without making sure the
    /// rates to display them exist.
    /// </summary>
    public StripeImportCreation ImportPreview(CompanyData data, StripeSyncPreview preview)
    {
        var stripe = data.Settings.Integrations.Stripe;
        var creation = new StripeImportCreation
        {
            PreviousCursor = stripe.LastSyncCursor,
            PreviousRefundCursor = stripe.LastRefundCursor,
            PreviousSyncTime = stripe.LastSyncTime,
            Pre = data.IdCounters.Clone()
        };

        int revBefore = data.Revenues.Count, expBefore = data.Expenses.Count,
            custBefore = data.Customers.Count, prodBefore = data.Products.Count,
            catBefore = data.Categories.Count, retBefore = data.Returns.Count,
            payBefore = stripe.ImportedPayouts.Count;

        var importer = new StripeDetailImporter();
        importer.ImportCharges(data, preview.Charges);
        importer.ApplyRefunds(data, preview.Charges);
        importer.ApplyLaterRefunds(data, preview.LaterRefunds);

        // Remember each new payout so a later bank import auto-ignores the matching deposit.
        foreach (var p in preview.NewPayouts)
        {
            stripe.ImportedPayouts.Add(new StripePayoutRecord
            {
                StripePayoutId = p.Id,
                AmountCents = Math.Abs(p.AmountCents),
                Currency = p.Currency?.ToUpperInvariant(),
                Date = DateTimeOffset.FromUnixTimeSeconds(p.DateUnix).LocalDateTime
            });
        }

        if (!string.IsNullOrEmpty(preview.NewCursor))
            stripe.LastSyncCursor = preview.NewCursor;
        if (!string.IsNullOrEmpty(preview.NewRefundCursor))
            stripe.LastRefundCursor = preview.NewRefundCursor;

        if (preview.HasActivity)
        {
            stripe.LastSyncTime = DateTime.Now;
            data.MarkAsModified();
        }

        // Capture what was created (the tail of each collection) for undo/redo.
        creation.Revenues.AddRange(data.Revenues.Skip(revBefore));
        creation.Expenses.AddRange(data.Expenses.Skip(expBefore));
        creation.ApplyStock(data);
        creation.Entities.AddRange(data.Customers.Skip(custBefore));
        creation.Entities.AddRange(data.Products.Skip(prodBefore));
        creation.Entities.AddRange(data.Categories.Skip(catBefore));
        creation.Returns.AddRange(data.Returns.Skip(retBefore));
        creation.Payouts.AddRange(stripe.ImportedPayouts.Skip(payBefore));
        creation.NewCursor = stripe.LastSyncCursor;
        creation.NewRefundCursor = stripe.LastRefundCursor;
        creation.NewSyncTime = stripe.LastSyncTime;
        creation.Post = data.IdCounters.Clone();
        return creation;
    }

    private static StripeSyncPreview Empty()
        => new([], null, [], [], null);
}
