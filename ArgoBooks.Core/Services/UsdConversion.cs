using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// USD per unit of <paramref name="currency"/> on <paramref name="date"/>, or null when no rate is
/// held for that day. Lets a caller supply rates without the exchange rate singleton.
/// </summary>
public delegate decimal? UsdRateSource(string currency, DateTime date);

/// <summary>
/// Stores a record's USD amounts at its own date's rate, or marks it pending and queues it for
/// <see cref="PendingConversionService"/> (docs/Calculations.md Rule 3a). Every save, import and sync
/// goes through here, and the queue converts with the same field writes, so a record converted
/// straight away and one converted later store the same figures.
/// </summary>
public static class UsdConversion
{
    public static bool IsUsd(string? currency) =>
        string.IsNullOrEmpty(currency) || string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase);

    /// <summary>The rate held for the exact date, without going online. 1 for USD.</summary>
    public static decimal? CachedRate(string? currency, DateTime date, ExchangeRateService? rates = null)
    {
        if (IsUsd(currency)) return 1m;
        var rate = (rates ?? ExchangeRateService.Instance)?.GetExchangeRate(currency!, "USD", date) ?? -1m;
        return rate > 0 ? rate : null;
    }

    /// <inheritdoc cref="CachedRate(string?, DateTime, ExchangeRateService?)"/>
    public static decimal? CachedRate(string? currency, DateTime date, UsdRateSource? source) =>
        IsUsd(currency) ? 1m : source != null ? Positive(source(currency!, date)) : CachedRate(currency, date);

    /// <summary>The exact date's rate, fetched when it isn't held. Null when it can't be had.</summary>
    public static async Task<decimal?> FetchRateAsync(string? currency, DateTime date)
    {
        if (IsUsd(currency)) return 1m;
        if (ExchangeRateService.Instance is not { } rates) return null;
        try
        {
            return Positive(await rates.GetExchangeRateAsync(currency!, "USD", date, fetchIfMissing: true));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The rate money tied to an invoice converts at: the invoice's own, so a paid invoice nets to
    /// zero in USD, or its issue date's while the invoice has none yet (Rule 3a).
    /// </summary>
    public static decimal? InvoiceRate(Invoice invoice)
    {
        if (IsUsd(invoice.OriginalCurrency)) return 1m;
        if (!invoice.IsPendingConversion && invoice.Total > 0 && invoice.TotalUSD > 0)
            return invoice.TotalUSD / invoice.Total;
        return CachedRate(invoice.OriginalCurrency, invoice.IssueDate);
    }

    private static decimal? Positive(decimal? rate) => rate > 0 ? rate : null;

    #region Apply

    /// <summary>
    /// Stores every USD amount of a revenue or expense at <paramref name="rate"/>, or, when it is
    /// null, zeroes them, marks the record pending and queues it. <paramref name="rateDate"/> is the
    /// date whose rate it waits for, the record's own unless it converts at another record's rate.
    /// Returns false when the record is left pending.
    /// </summary>
    public static bool Apply(CompanyData data, Transaction txn, decimal? rate, DateTime? rateDate = null) =>
        Store(data, EntryFor(txn, rateDate), rate, e => Write(txn, e, rate));

    /// <inheritdoc cref="Apply(CompanyData, Transaction, decimal?, DateTime?)"/>
    public static bool Apply(CompanyData data, Payment payment, decimal? rate, DateTime? rateDate = null) =>
        Store(data, EntryFor(payment, rateDate), rate, e => Write(payment, e, rate));

    /// <inheritdoc cref="Apply(CompanyData, Transaction, decimal?, DateTime?)"/>
    public static bool Apply(CompanyData data, PurchaseOrder order, decimal? rate) =>
        Store(data, EntryFor(order), rate, e => Write(order, e, rate));

    /// <inheritdoc cref="Apply(CompanyData, Transaction, decimal?, DateTime?)"/>
    public static bool Apply(CompanyData data, Invoice invoice, decimal? rate) =>
        Store(data, EntryFor(invoice), rate, e => Write(invoice, e, rate));

    /// <summary>
    /// Stores a record's USD amounts without queueing it, for a record that isn't in the books,
    /// such as a recurring schedule's template.
    /// </summary>
    public static void SetAmounts(Transaction txn, decimal? rate) => Write(txn, EntryFor(txn, null), rate);

    private static bool Store(CompanyData data, PendingConversion entry, decimal? rate, Action<PendingConversion> write)
    {
        write(entry);
        Set(data, entry.Key, rate == null ? entry : null);
        return rate != null;
    }

    #endregion

    #region Requeue

    /// <summary>
    /// Puts the record's queue entry in step with it, queued while it is pending and gone once it
    /// isn't. For undo and redo, which put a record's fields back without converting it again.
    /// </summary>
    public static void Requeue(CompanyData data, Transaction txn, DateTime? rateDate = null) =>
        Set(data, KeyOf(txn), txn.IsPendingConversion ? EntryFor(txn, rateDate) : null);

    /// <inheritdoc cref="Requeue(CompanyData, Transaction, DateTime?)"/>
    public static void Requeue(CompanyData data, Payment payment, DateTime? rateDate = null) =>
        Set(data, KeyOf(payment), payment.IsPendingConversion ? EntryFor(payment, rateDate) : null);

    /// <inheritdoc cref="Requeue(CompanyData, Transaction, DateTime?)"/>
    public static void Requeue(CompanyData data, PurchaseOrder order) =>
        Set(data, KeyOf(order), order.IsPendingConversion ? EntryFor(order) : null);

    #endregion

    #region Queue

    public static PendingConversionKey KeyOf(Transaction txn) =>
        new(txn.Id, txn is Revenue ? PendingConversionType.Revenue : PendingConversionType.Expense);

    public static PendingConversionKey KeyOf(Payment payment) => new(payment.Id, PendingConversionType.Payment);

    public static PendingConversionKey KeyOf(PurchaseOrder order) => new(order.Id, PendingConversionType.PurchaseOrder);

    public static PendingConversionKey KeyOf(Invoice invoice) => new(invoice.Id, PendingConversionType.Invoice);

    public static PendingConversionKey KeyOf(InventoryItem item) => new(item.Id, PendingConversionType.InventoryItem);

    /// <summary>The record's queue entry, or null.</summary>
    public static PendingConversion? Queued(CompanyData data, PendingConversionKey key) =>
        data.PendingConversions.FirstOrDefault(p => p.Key == key);

    /// <summary>
    /// Replaces the record's queue entry with <paramref name="entry"/>, or drops it when null, in the
    /// company file and in the conversion service's copy. There is at most one entry per record, so
    /// the latest amounts always win.
    /// </summary>
    public static void Set(CompanyData data, PendingConversionKey key, PendingConversion? entry)
    {
        data.PendingConversions.RemoveAll(p => p.Key == key);
        if (entry != null)
            data.PendingConversions.Add(entry);
        Mirror(data, [key]);
    }

    /// <summary>
    /// Puts back entries saved before a change, for undo and redo: the records named by
    /// <paramref name="keys"/> are left with exactly <paramref name="entries"/>.
    /// </summary>
    public static void Restore(CompanyData data, IEnumerable<PendingConversionKey> keys, IReadOnlyCollection<PendingConversion> entries)
    {
        var all = keys.Concat(entries.Select(e => e.Key)).ToHashSet();
        data.PendingConversions.RemoveAll(p => all.Contains(p.Key));
        data.PendingConversions.AddRange(entries);
        Mirror(data, all);
    }

    /// <summary>The entries currently queued for these records, for <see cref="Restore"/>.</summary>
    public static List<PendingConversion> Snapshot(CompanyData data, IEnumerable<PendingConversionKey> keys)
    {
        var set = keys.ToHashSet();
        return data.PendingConversions.Where(p => set.Contains(p.Key)).ToList();
    }

    /// <summary>
    /// Brings the conversion service's queue in line with the company file for these records. The
    /// service converts from its own copy, so an entry changed only in the company file converts
    /// stale or not at all. Call on the thread that owns the company data.
    /// </summary>
    public static void Mirror(CompanyData data, IEnumerable<PendingConversionKey> keys)
    {
        if (PendingConversionService.Instance is { } service)
            _ = service.MirrorAsync(data, keys);
    }

    #endregion

    #region Entries and writes

    private static string CurrencyOf(string? currency) => string.IsNullOrEmpty(currency) ? "USD" : currency;

    public static PendingConversion EntryFor(Transaction txn, DateTime? rateDate = null) => new()
    {
        TransactionId = txn.Id,
        TransactionType = KeyOf(txn).TransactionType,
        OriginalCurrency = CurrencyOf(txn.OriginalCurrency),
        TransactionDate = rateDate ?? txn.Date,
        Total = txn.Total,
        TaxAmount = txn.TaxAmount,
        ShippingCost = txn.ShippingCost,
        Discount = txn.Discount,
        Fee = txn.Fee,
        UnitPrice = txn.UnitPrice
    };

    public static PendingConversion EntryFor(Payment payment, DateTime? rateDate = null) => new()
    {
        TransactionId = payment.Id,
        TransactionType = PendingConversionType.Payment,
        OriginalCurrency = CurrencyOf(payment.OriginalCurrency),
        TransactionDate = rateDate ?? payment.Date,
        Total = payment.Amount
    };

    public static PendingConversion EntryFor(PurchaseOrder order) => new()
    {
        TransactionId = order.Id,
        TransactionType = PendingConversionType.PurchaseOrder,
        OriginalCurrency = CurrencyOf(order.OriginalCurrency),
        TransactionDate = order.OrderDate,
        Total = order.Total
    };

    public static PendingConversion EntryFor(Invoice invoice) => new()
    {
        TransactionId = invoice.Id,
        TransactionType = PendingConversionType.Invoice,
        OriginalCurrency = CurrencyOf(invoice.OriginalCurrency),
        TransactionDate = invoice.IssueDate,
        Total = invoice.Total,
        Balance = invoice.Balance
    };

    // The USD base is stored at full precision, never rounded to cents (Rule 3). A null rate leaves
    // every USD field at 0 until the queue converts it.

    internal static void Write(Transaction txn, PendingConversion amounts, decimal? rate)
    {
        var r = rate ?? 0m;
        txn.TotalUSD = amounts.Total * r;
        txn.TaxAmountUSD = amounts.TaxAmount * r;
        txn.ShippingCostUSD = amounts.ShippingCost * r;
        txn.DiscountUSD = amounts.Discount * r;
        txn.FeeUSD = amounts.Fee * r;
        txn.UnitPriceUSD = amounts.UnitPrice * r;
        txn.IsPendingConversion = rate == null;
    }

    internal static void Write(Payment payment, PendingConversion amounts, decimal? rate)
    {
        payment.AmountUSD = amounts.Total * (rate ?? 0m);
        payment.IsPendingConversion = rate == null;
    }

    internal static void Write(PurchaseOrder order, PendingConversion amounts, decimal? rate)
    {
        order.TotalUSD = amounts.Total * (rate ?? 0m);
        order.IsPendingConversion = rate == null;
    }

    internal static void Write(Invoice invoice, PendingConversion amounts, decimal? rate)
    {
        var r = rate ?? 0m;
        invoice.TotalUSD = amounts.Total * r;
        invoice.BalanceUSD = amounts.Balance * r;
        invoice.IsPendingConversion = rate == null;
    }

    #endregion
}
