using System.Net;
using System.Net.Http.Headers;

namespace ArgoBooks.Core.Services.Integrations;

/// <summary>Result of validating a pasted Stripe key.</summary>
public record StripeValidationResult(bool Ok, string? AccountLabel, string? ErrorMessage);

/// <summary>
/// A Stripe payout (the net deposit that lands in the bank). DateUnix is the bank arrival date.
/// AmountCents is in the currency's smallest unit, which for JPY and the like is the whole unit.
/// </summary>
public record StripePayoutSummary(string Id, long AmountCents, long DateUnix, string Status, string? Currency = null);

/// <summary>A charge's processing fee, in the balance transaction's (settlement) currency.</summary>
public record StripeFee(long Cents, string? Currency);

/// <summary>
/// The charges that are new since the last sync, the charge to stop at next time, and the ones
/// seen that have not gone through yet.
/// </summary>
public record StripeChargeFetch(
    IReadOnlyList<StripeChargeDetail> Charges, string? Cursor, IReadOnlyList<string> PendingIds);

/// <summary>A refund made on a charge. AmountCents is in the currency's smallest unit.</summary>
public record StripeRefund(string Id, string ChargeId, long AmountCents, string Currency, long CreatedUnix);

/// <summary>The refunds that are new since the last sync, and the refund to stop at next time.</summary>
public record StripeRefundFetch(IReadOnlyList<StripeRefund> Refunds, string? Cursor);

/// <summary>
/// Minimal Stripe REST client. Validates a key by reading balance transactions
/// (the same data the sync uses) and fetches balance transactions for import.
/// </summary>
public class StripeApiClient
{
    private const string BalanceTxUrl = "https://api.stripe.com/v1/balance_transactions";
    private const string PayoutsUrl = "https://api.stripe.com/v1/payouts";
    private const string ChargesUrl = "https://api.stripe.com/v1/charges";
    private const string RefundsUrl = "https://api.stripe.com/v1/refunds";
    private const int MaxPages = 20;
    private readonly HttpClient _http;

    public StripeApiClient(HttpClient http) => _http = http;

    public async Task<StripeValidationResult> ValidateKeyAsync(string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return new StripeValidationResult(false, null, "Enter your Stripe key first.");

        var key = apiKey.Trim();

        // Validate against the exact data the feature reads (balance transactions), so a restricted key scoped only to Balance transactions / Charges / Payouts passes.
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{BalanceTxUrl}?limit=1");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, ct);
        }
        catch (HttpRequestException)
        {
            return new StripeValidationResult(false, null,
                "Could not reach Stripe. Check your internet connection and try again.");
        }

        using (resp)
        {
            if (resp.StatusCode == HttpStatusCode.Unauthorized)
                return new StripeValidationResult(false, null,
                    "That key was rejected by Stripe. Check you pasted a valid key.");

            if (resp.StatusCode == HttpStatusCode.Forbidden)
                return new StripeValidationResult(false, null,
                    "That key is missing read access. In Stripe, give it Read access to Balance transactions, Charges, and Payouts.");

            if (!resp.IsSuccessStatusCode)
                return new StripeValidationResult(false, null,
                    $"Stripe returned an unexpected error (HTTP {(int)resp.StatusCode}).");

            return new StripeValidationResult(true, ModeLabel(key), null);
        }
    }

    /// <summary>A friendly label from the key's mode, since a scoped key can't read the account name.</summary>
    private static string? ModeLabel(string key) =>
        key.Contains("_test_") ? "Test mode" : key.Contains("_live_") ? "Live account" : null;

    /// <summary>
    /// Lists the account's payouts (newest first). A payout is the net deposit that lands in the
    /// bank; remembered so a later bank import can auto-ignore the matching deposit (works for
    /// manual and automatic payouts, unlike filtering balance transactions by payout).
    /// </summary>
    public async Task<IReadOnlyList<StripePayoutSummary>> FetchPayoutsAsync(string apiKey, CancellationToken ct = default)
    {
        var results = new List<StripePayoutSummary>();
        string? after = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var url = $"{PayoutsUrl}?limit=100";
            if (!string.IsNullOrEmpty(after))
                url += $"&starting_after={Uri.EscapeDataString(after)}";

            using var doc = await GetJsonAsync(apiKey, url, ct);
            var root = doc.RootElement;

            var count = 0;
            string? lastId = null;
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in data.EnumerateArray())
                {
                    var id = PropStr(el, "id");
                    if (string.IsNullOrEmpty(id)) continue;
                    // arrival_date is when the deposit lands in the bank (best for matching); fall back to created.
                    var dateUnix = PropNum(el, "arrival_date");
                    if (dateUnix == 0) dateUnix = PropNum(el, "created");
                    results.Add(new StripePayoutSummary(
                        id, PropNum(el, "amount"), dateUnix, PropStr(el, "status"), NullableStr(el, "currency")));
                    lastId = id;
                    count++;
                }
            }

            var hasMore = root.TryGetProperty("has_more", out var hm) && hm.ValueKind == JsonValueKind.True;
            if (!hasMore || count == 0 || lastId == null) break;
            after = lastId;
        }

        return results;
    }

    /// <summary>
    /// Fetches charges newest-first, expanded with customer/invoice/balance_transaction, stopping
    /// when it reaches the watermark charge id (the newest charge seen at the last sync). Only
    /// succeeded, paid charges are included, newest first. Also returns the charge the next sync
    /// should stop at and the charges that are still pending.
    ///
    /// A bank debit takes days to succeed, and the charges made meanwhile are imported without
    /// it. The cursor then sits above it, so the list is never read that far back again. The
    /// caller keeps the pending ids and asks about each one by id until it settles.
    /// </summary>
    public async Task<StripeChargeFetch> FetchNewChargesAsync(
        string apiKey, string? watermarkChargeId, CancellationToken ct = default)
    {
        var results = new List<StripeChargeDetail>();
        var pending = new List<string>();
        string? after = null;
        string? cursor = null;
        const string expand = "&expand[]=data.customer&expand[]=data.invoice&expand[]=data.balance_transaction";

        for (var page = 0; page < MaxPages; page++)
        {
            var url = $"{ChargesUrl}?limit=100{expand}";
            if (!string.IsNullOrEmpty(after))
                url += $"&starting_after={Uri.EscapeDataString(after)}";

            using var doc = await GetJsonAsync(apiKey, url, ct);
            var root = doc.RootElement;

            var pageCount = 0;
            string? lastId = null;
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in data.EnumerateArray())
                {
                    var id = PropStr(el, "id");
                    if (!string.IsNullOrEmpty(watermarkChargeId) && id == watermarkChargeId)
                        return new StripeChargeFetch(results, cursor ?? watermarkChargeId, pending); // reached the last-synced watermark
                    lastId = id;
                    pageCount++;

                    if (cursor == null && !string.IsNullOrEmpty(id))
                        cursor = id;

                    var status = PropStr(el, "status");
                    if (status == "pending" && !string.IsNullOrEmpty(id))
                        pending.Add(id);

                    var isPaid = el.TryGetProperty("paid", out var p) && p.ValueKind == JsonValueKind.True;
                    if (status != "succeeded" || !isPaid) continue;
                    results.Add(ParseCharge(el));
                }
            }

            var hasMore = root.TryGetProperty("has_more", out var hm) && hm.ValueKind == JsonValueKind.True;
            if (!hasMore || pageCount == 0 || lastId == null) break;
            after = lastId;
        }

        return new StripeChargeFetch(results, cursor ?? watermarkChargeId, pending);
    }

    /// <summary>
    /// Reads one charge by id, for a charge that was pending at an earlier sync. Returns its
    /// status, and the charge itself once it has succeeded and been paid.
    /// </summary>
    public async Task<(string Status, StripeChargeDetail? Charge)> FetchChargeAsync(
        string apiKey, string chargeId, CancellationToken ct = default)
    {
        var url = $"{ChargesUrl}/{Uri.EscapeDataString(chargeId)}" +
                  "?expand[]=customer&expand[]=invoice&expand[]=balance_transaction";
        using var doc = await GetJsonAsync(apiKey, url, ct);
        var el = doc.RootElement;

        var status = PropStr(el, "status");
        var isPaid = el.TryGetProperty("paid", out var p) && p.ValueKind == JsonValueKind.True;
        return (status, status == "succeeded" && isPaid ? ParseCharge(el) : null);
    }

    /// <summary>
    /// Fetches refunds newest-first, stopping at the watermark refund id. Only refunds that went
    /// through are included. The cursor waits beneath one still in progress, as for charges.
    /// </summary>
    public async Task<StripeRefundFetch> FetchNewRefundsAsync(
        string apiKey, string? watermarkRefundId, CancellationToken ct = default)
    {
        var results = new List<StripeRefund>();
        string? after = null;
        string? cursor = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var url = $"{RefundsUrl}?limit=100";
            if (!string.IsNullOrEmpty(after))
                url += $"&starting_after={Uri.EscapeDataString(after)}";

            using var doc = await GetJsonAsync(apiKey, url, ct);
            var root = doc.RootElement;

            var pageCount = 0;
            string? lastId = null;
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in data.EnumerateArray())
                {
                    var id = PropStr(el, "id");
                    if (!string.IsNullOrEmpty(watermarkRefundId) && id == watermarkRefundId)
                        return new StripeRefundFetch(results, cursor ?? watermarkRefundId);
                    lastId = id;
                    pageCount++;

                    var status = PropStr(el, "status");
                    if (status is "pending" or "requires_action")
                        cursor = null;
                    else if (cursor == null && !string.IsNullOrEmpty(id))
                        cursor = id;

                    var chargeId = PropStr(el, "charge");
                    if (status != "succeeded" || string.IsNullOrEmpty(chargeId)) continue;
                    results.Add(new StripeRefund(
                        id, chargeId, PropNum(el, "amount"), PropStr(el, "currency"), PropNum(el, "created")));
                }
            }

            var hasMore = root.TryGetProperty("has_more", out var hm) && hm.ValueKind == JsonValueKind.True;
            if (!hasMore || pageCount == 0 || lastId == null) break;
            after = lastId;
        }

        return new StripeRefundFetch(results, cursor ?? watermarkRefundId);
    }

    private static StripeChargeDetail ParseCharge(JsonElement el)
    {
        long fee = 0;
        string? feeCurrency = null;
        if (el.TryGetProperty("balance_transaction", out var bt) && bt.ValueKind == JsonValueKind.Object)
        {
            fee = PropNum(bt, "fee");
            feeCurrency = NullableStr(bt, "currency");
        }

        string? custName = null, custEmail = null;
        if (el.TryGetProperty("customer", out var cust) && cust.ValueKind == JsonValueKind.Object)
        {
            custName = NullableStr(cust, "name");
            custEmail = NullableStr(cust, "email");
        }

        string product = "Stripe sale";
        long tax = 0, discount = 0;
        if (el.TryGetProperty("invoice", out var inv) && inv.ValueKind == JsonValueKind.Object)
        {
            tax = PropNum(inv, "tax");
            if (inv.TryGetProperty("total_discount_amounts", out var da) && da.ValueKind == JsonValueKind.Array)
                foreach (var d in da.EnumerateArray()) discount += PropNum(d, "amount");
            if (inv.TryGetProperty("lines", out var lines) && lines.TryGetProperty("data", out var ld)
                && ld.ValueKind == JsonValueKind.Array && ld.GetArrayLength() > 0)
            {
                var first = ld[0];
                var desc = NullableStr(first, "description");
                if (!string.IsNullOrWhiteSpace(desc)) product = desc;
            }
        }
        if (product == "Stripe sale")
        {
            var chargeDesc = NullableStr(el, "description");
            if (!string.IsNullOrWhiteSpace(chargeDesc)) product = chargeDesc;
        }

        return new StripeChargeDetail(
            ChargeId: PropStr(el, "id"),
            CreatedUnix: PropNum(el, "created"),
            GrossCents: PropNum(el, "amount"),
            FeeCents: fee,
            Currency: PropStr(el, "currency"),
            CustomerName: custName,
            CustomerEmail: custEmail,
            ProductName: product,
            TaxCents: tax,
            DiscountCents: discount,
            AmountRefundedCents: PropNum(el, "amount_refunded"),
            FeeCurrency: feeCurrency);
    }

    private static string? NullableStr(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>
    /// Maps charge id -> processing fee from the balance-transactions list. A charge's own
    /// expanded balance_transaction can come back unexpanded (a bare id string) depending on the
    /// key/settlement, silently yielding a zero fee; the balance-transactions list carries the fee
    /// reliably (its 'source' is the charge id), so this is the authoritative fee source.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, StripeFee>> FetchChargeFeesAsync(string apiKey, CancellationToken ct = default)
    {
        var map = new Dictionary<string, StripeFee>(StringComparer.Ordinal);
        string? after = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var url = $"{BalanceTxUrl}?limit=100&type=charge";
            if (!string.IsNullOrEmpty(after))
                url += $"&starting_after={Uri.EscapeDataString(after)}";

            using var doc = await GetJsonAsync(apiKey, url, ct);
            var root = doc.RootElement;

            var count = 0;
            string? lastId = null;
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in data.EnumerateArray())
                {
                    lastId = PropStr(el, "id");
                    count++;
                    var source = PropStr(el, "source"); // the charge id this balance transaction is for
                    if (!string.IsNullOrEmpty(source))
                        map[source] = new StripeFee(PropNum(el, "fee"), NullableStr(el, "currency"));
                }
            }

            var hasMore = root.TryGetProperty("has_more", out var hm) && hm.ValueKind == JsonValueKind.True;
            if (!hasMore || count == 0 || lastId == null) break;
            after = lastId;
        }

        return map;
    }

    private async Task<JsonDocument> GetJsonAsync(string apiKey, string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            // Surface Stripe's own error message instead of a bare status code.
            var detail = ExtractStripeError(body) ?? resp.ReasonPhrase;
            throw new HttpRequestException($"Stripe {(int)resp.StatusCode}: {detail}");
        }
        return JsonDocument.Parse(body);
    }

    private static string? ExtractStripeError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object
                && err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                return m.GetString();
        }
        catch (JsonException) { /* not JSON */ }
        return null;
    }

    private static string PropStr(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    private static long PropNum(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0L;
}
