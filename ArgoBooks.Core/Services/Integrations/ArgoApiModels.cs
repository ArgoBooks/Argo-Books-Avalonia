namespace ArgoBooks.Core.Services.Integrations;

/// <summary>
/// Wire types for the Argo Books public API (/v1).
///
/// Money crosses the wire as an integer in the currency's smallest unit, so
/// nothing is lost to floating point on the way in. <see cref="ArgoMoney"/>
/// converts once, at the boundary.
/// </summary>
public static class ArgoMoney
{
    /// <summary>
    /// Currencies with no minor unit. The server stores the same list; both sides
    /// have to agree or a 1000 JPY sale imports as 10 JPY.
    /// </summary>
    private static readonly HashSet<string> ZeroDecimal = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA",
        "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF"
    };

    /// <summary>Convert minor units to the amount a person would write down.</summary>
    public static decimal ToDecimal(long minorUnits, string? currency)
        => ZeroDecimal.Contains(currency ?? "USD") ? minorUnits : minorUnits / 100m;

    /// <summary>Convert an amount to the currency's smallest unit, the form Stripe states amounts in.</summary>
    public static long ToMinorUnits(decimal amount, string? currency)
        => (long)Math.Round(ZeroDecimal.Contains(currency ?? "USD") ? amount : amount * 100m);
}

/// <summary>Import lifecycle reported alongside every object.</summary>
public record ArgoImportState(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("batch")] string? Batch,
    [property: JsonPropertyName("imported_at")] long? ImportedAt,
    [property: JsonPropertyName("local_ref")] string? LocalRef);

/// <summary>Envelope every list endpoint returns.</summary>
public record ArgoList<T>(
    [property: JsonPropertyName("data")] List<T> Data,
    [property: JsonPropertyName("has_more")] bool HasMore);

public record ArgoCustomer(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("company")] string? Company,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("import")] ArgoImportState Import);

public record ArgoSupplier(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("website")] string? Website,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("import")] ArgoImportState Import);

public record ArgoCategory(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("import")] ArgoImportState Import);

public record ArgoProduct(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("sku")] string? Sku,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("unit_amount")] long? UnitAmount,
    [property: JsonPropertyName("currency")] string? Currency,
    [property: JsonPropertyName("tax_rate")] decimal? TaxRate,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("import")] ArgoImportState Import);

public record ArgoLineItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("product")] string? Product,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("quantity")] decimal Quantity,
    [property: JsonPropertyName("unit_amount")] long UnitAmount,
    [property: JsonPropertyName("tax_amount")] long TaxAmount,
    [property: JsonPropertyName("discount_amount")] long DiscountAmount);

public record ArgoExpense(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("amount")] long Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("tax_amount")] long TaxAmount,
    [property: JsonPropertyName("occurred_on")] string OccurredOn,
    [property: JsonPropertyName("supplier")] string? Supplier,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("payment_method")] string? PaymentMethod,
    [property: JsonPropertyName("reference")] string? Reference,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("line_items")] List<ArgoLineItem>? LineItems,
    [property: JsonPropertyName("import")] ArgoImportState Import);

public record ArgoRevenue(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("amount")] long Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("tax_amount")] long TaxAmount,
    [property: JsonPropertyName("discount_amount")] long DiscountAmount,
    [property: JsonPropertyName("fee_amount")] long FeeAmount,
    [property: JsonPropertyName("occurred_on")] string OccurredOn,
    [property: JsonPropertyName("customer")] string? Customer,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("payment_method")] string? PaymentMethod,
    [property: JsonPropertyName("reference")] string? Reference,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("line_items")] List<ArgoLineItem>? LineItems,
    [property: JsonPropertyName("import")] ArgoImportState Import);

public record ArgoRefund(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("revenue")] string Revenue,
    [property: JsonPropertyName("amount")] long Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("occurred_on")] string OccurredOn,
    [property: JsonPropertyName("import")] ArgoImportState Import);

public record ArgoAccount(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("company_uid")] string? CompanyUid,
    [property: JsonPropertyName("pending")] Dictionary<string, int>? Pending);

public record ArgoBatch(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status);

/// <summary>The error envelope /v1 returns for every failure.</summary>
public record ArgoApiError(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("param")] string? Param);

public record ArgoErrorEnvelope([property: JsonPropertyName("error")] ArgoApiError Error);

/// <summary>Thrown when /v1 returns a structured error, so callers can show its message.</summary>
public class ArgoApiException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
