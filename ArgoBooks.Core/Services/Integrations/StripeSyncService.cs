using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Integrations;

namespace ArgoBooks.Core.Services.Integrations;

public record StripeSyncPreview(
    IReadOnlyList<StripeChargeDetail> Charges,
    decimal TotalRevenue,
    decimal TotalFees,
    string? NewCursor,
    IReadOnlyList<StripePayoutSummary> NewPayouts)
{
    /// <summary>True when there's anything to import: new revenue/fees, or new payouts to remember.</summary>
    public bool HasActivity => Charges.Count > 0 || NewPayouts.Count > 0;
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

        var rawCharges = await client.FetchChargesUntilAsync(stripe.ApiKey!, stripe.LastSyncCursor, ct);
        var newCursor = rawCharges.Count > 0 ? rawCharges[0].ChargeId : stripe.LastSyncCursor;

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

        var totalRevenue = charges.Sum(c => ArgoMoney.ToDecimal(c.GrossCents, c.Currency));
        var totalFees = charges.Sum(c => ArgoMoney.ToDecimal(c.FeeCents, c.FeeCurrency ?? c.Currency));

        return new StripeSyncPreview(charges, totalRevenue, totalFees, newCursor, newPayouts);
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
            preview.Charges.SelectMany(c => new[] { c.Currency, c.FeeCurrency ?? c.Currency }.Select(currency =>
                (DateTimeOffset.FromUnixTimeSeconds(c.CreatedUnix).LocalDateTime, currency))),
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
            PreviousSyncTime = stripe.LastSyncTime,
            Pre = StripeImportCreation.CounterSnapshot.From(data.IdCounters)
        };

        int revBefore = data.Revenues.Count, expBefore = data.Expenses.Count,
            custBefore = data.Customers.Count, prodBefore = data.Products.Count,
            catBefore = data.Categories.Count, retBefore = data.Returns.Count,
            payBefore = stripe.ImportedPayouts.Count;

        var importer = new StripeDetailImporter();
        importer.ImportCharges(data, preview.Charges);
        importer.ApplyRefunds(data, preview.Charges);

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

        if (preview.HasActivity)
        {
            stripe.LastSyncTime = DateTime.Now;
            data.MarkAsModified();
        }

        // Capture what was created (the tail of each collection) for undo/redo.
        creation.Revenues.AddRange(data.Revenues.Skip(revBefore));
        creation.Expenses.AddRange(data.Expenses.Skip(expBefore));
        creation.Entities.AddRange(data.Customers.Skip(custBefore));
        creation.Entities.AddRange(data.Products.Skip(prodBefore));
        creation.Entities.AddRange(data.Categories.Skip(catBefore));
        creation.Returns.AddRange(data.Returns.Skip(retBefore));
        creation.Payouts.AddRange(stripe.ImportedPayouts.Skip(payBefore));
        creation.NewCursor = stripe.LastSyncCursor;
        creation.NewSyncTime = stripe.LastSyncTime;
        creation.Post = StripeImportCreation.CounterSnapshot.From(data.IdCounters);
        return creation;
    }

    private static StripeSyncPreview Empty()
        => new(Array.Empty<StripeChargeDetail>(), 0m, 0m, null, Array.Empty<StripePayoutSummary>());
}
