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
/// Minimal Stripe REST client. Validates a key by reading balance transactions
/// (the same data the sync uses) and fetches balance transactions for import.
/// </summary>
public class StripeApiClient
{
    private const string BalanceTxUrl = "https://api.stripe.com/v1/balance_transactions";
    private const string PayoutsUrl = "https://api.stripe.com/v1/payouts";
    private const string ChargesUrl = "https://api.stripe.com/v1/charges";
    private const int MaxPages = 20;
    private readonly HttpClient _http;

    public StripeApiClient(HttpClient http) => _http = http;

    public async Task<StripeValidationResult> ValidateKeyAsync(string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return new StripeValidationResult(false, null, "Enter your Stripe key first.");

        var key = apiKey.Trim();

        // Validate against the exact data the feature reads (balance transactions), so a
        // restricted key scoped only to Balance transactions / Charges / Payouts passes.
        // Validating against /v1/account would fail such a key with 403, since we do not
        // ask for account read access.
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
    /// succeeded, paid charges are included. Returns the new charges, newest first.
    /// </summary>
    public async Task<IReadOnlyList<StripeChargeDetail>> FetchChargesUntilAsync(
        string apiKey, string? watermarkChargeId, CancellationToken ct = default)
    {
        var results = new List<StripeChargeDetail>();
        string? after = null;
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
                        return results; // reached the last-synced watermark
                    lastId = id;
                    pageCount++;

                    var isPaid = el.TryGetProperty("paid", out var p) && p.ValueKind == JsonValueKind.True;
                    if (PropStr(el, "status") != "succeeded" || !isPaid) continue;
                    results.Add(ParseCharge(el));
                }
            }

            var hasMore = root.TryGetProperty("has_more", out var hm) && hm.ValueKind == JsonValueKind.True;
            if (!hasMore || pageCount == 0 || lastId == null) break;
            after = lastId;
        }

        return results;
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
