using System.Text.Json.Serialization;

namespace ArgoBooks.Core.Services.Sync;

/// <summary>
/// Small, read-only projection of <see cref="Data.CompanyData"/> for the paired mobile app.
/// This is NOT the raw company file: it is a purpose-built read model containing just the
/// dashboard totals and lightweight row lists for each section the mobile prototype shows.
/// A later task encrypts and uploads this via <see cref="SyncCrypto"/> / <see cref="SyncService"/>.
/// Property names are pinned with <see cref="JsonPropertyNameAttribute"/> so the desktop/phone
/// wire format doesn't drift with C# member-casing changes.
/// </summary>
public sealed class MobileSnapshot
{
    /// <summary>Dashboard summary totals (money in/out, profit, margin).</summary>
    [JsonPropertyName("dashboard")]
    public DashboardDto Dashboard { get; init; } = new();

    /// <summary>Expense rows: vendor as Title, date as Subtitle, formatted amount.</summary>
    [JsonPropertyName("expenses")]
    public List<RowDto> Expenses { get; init; } = [];

    /// <summary>Revenue rows: customer as Title, date as Subtitle, formatted amount.</summary>
    [JsonPropertyName("revenue")]
    public List<RowDto> Revenue { get; init; } = [];

    /// <summary>Invoice rows: invoice number as Title, status as Subtitle, formatted total.</summary>
    [JsonPropertyName("invoices")]
    public List<RowDto> Invoices { get; init; } = [];

    /// <summary>Customer rows: name as Title, company as Subtitle, formatted outstanding balance.</summary>
    [JsonPropertyName("customers")]
    public List<RowDto> Customers { get; init; } = [];

    /// <summary>Supplier rows: name as Title, contact person as Subtitle, formatted total spent.</summary>
    [JsonPropertyName("suppliers")]
    public List<RowDto> Suppliers { get; init; } = [];

    /// <summary>Product rows: name as Title, SKU as Subtitle, stock-on-hand as Amount.</summary>
    [JsonPropertyName("products")]
    public List<RowDto> Products { get; init; } = [];

    /// <summary>When this snapshot was built (UTC).</summary>
    [JsonPropertyName("generatedAt")]
    public DateTime GeneratedAt { get; init; }

    /// <summary>The currency every amount in this snapshot is in.</summary>
    [JsonPropertyName("currency")]
    public CurrencyDto Currency { get; init; } = new();
}

/// <summary>
/// The snapshot's currency, with what the phone needs to write an amount in it.
/// </summary>
public sealed class CurrencyDto
{
    /// <summary>ISO 4217 code, e.g. "CAD".</summary>
    [JsonPropertyName("code")]
    public string Code { get; init; } = "USD";

    [JsonPropertyName("symbol")]
    public string Symbol { get; init; } = "$";

    /// <summary>2 for most currencies, 0 for ones without minor units such as JPY.</summary>
    [JsonPropertyName("decimalPlaces")]
    public int DecimalPlaces { get; init; } = 2;

    public string Format(decimal amount) => MoneyFormat.Format(amount, Symbol, DecimalPlaces);
}

/// <summary>
/// Dashboard summary totals shown at the top of the mobile app.
/// </summary>
public sealed class DashboardDto
{
    /// <summary>Collected revenue less refunds, as the desktop's Total Revenue card counts it.</summary>
    [JsonPropertyName("moneyIn")]
    public decimal MoneyIn { get; init; }

    /// <summary>Total expenses (gross).</summary>
    [JsonPropertyName("moneyOut")]
    public decimal MoneyOut { get; init; }

    /// <summary>MoneyIn - MoneyOut.</summary>
    [JsonPropertyName("profit")]
    public decimal Profit { get; init; }

    /// <summary>Profit as a fraction of MoneyIn (0 when MoneyIn is 0).</summary>
    [JsonPropertyName("profitMargin")]
    public decimal ProfitMargin { get; init; }
}

/// <summary>
/// A single lightweight row for a mobile section list. Values are pre-formatted display
/// strings (not raw numbers) so the phone doesn't need to duplicate currency/locale
/// formatting logic; it just renders Title/Subtitle/Amount as-is.
/// </summary>
public sealed class RowDto
{
    /// <summary>Primary label (e.g. vendor/customer name, invoice number, product name).</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>Secondary label (e.g. date, status, contact person, SKU).</summary>
    [JsonPropertyName("subtitle")]
    public string Subtitle { get; init; } = string.Empty;

    /// <summary>Pre-formatted amount/value (e.g. "-$40.00", "42 in stock").</summary>
    [JsonPropertyName("amount")]
    public string Amount { get; init; } = string.Empty;

    /// <summary>The money behind <see cref="Amount"/>, signed as shown; null for a row that isn't money.</summary>
    [JsonPropertyName("value")]
    public decimal? Value { get; init; }
}
