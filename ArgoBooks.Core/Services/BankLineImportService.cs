using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

public class BankImportCreation
{
    public List<Transaction> CreatedTransactions { get; } = [];
    public List<object> CreatedEntities { get; } = [];

    /// <summary>Conversions queued for rows whose date had no exchange rate yet.</summary>
    public List<PendingConversion> PendingConversions { get; } = [];

    private readonly List<ReleasedLine> _releasedLines = [];

    public void Undo(CompanyData data)
    {
        ReleaseMatchedLines(data);

        foreach (var tx in CreatedTransactions)
        {
            if (tx is Expense e) data.Expenses.Remove(e);
            else if (tx is Revenue r) data.Revenues.Remove(r);
        }

        foreach (var entity in CreatedEntities)
        {
            if (entity is Supplier s) data.Suppliers.Remove(s);
            else if (entity is Customer c) data.Customers.Remove(c);
            else if (entity is Product p) data.Products.Remove(p);
            else if (entity is Category cat) data.Categories.Remove(cat);
        }

        // The rows are gone, so their queued conversions have nothing left to convert, and
        // nothing else prunes an entry whose row no longer exists.
        var ids = PendingConversions.Select(p => p.TransactionId).ToHashSet(StringComparer.Ordinal);
        data.PendingConversions.RemoveAll(p => ids.Contains(p.TransactionId));
        MirrorPendingConversions(data);

        data.MarkAsModified();
    }

    public void Redo(CompanyData data)
    {
        foreach (var entity in CreatedEntities)
        {
            if (entity is Supplier s && !data.Suppliers.Contains(s)) data.Suppliers.Add(s);
            else if (entity is Customer c && !data.Customers.Contains(c)) data.Customers.Add(c);
            else if (entity is Product p && !data.Products.Contains(p)) data.Products.Add(p);
            else if (entity is Category cat && !data.Categories.Contains(cat)) data.Categories.Add(cat);
        }

        foreach (var tx in CreatedTransactions)
        {
            if (tx is Expense e && !data.Expenses.Contains(e)) data.Expenses.Add(e);
            else if (tx is Revenue r && !data.Revenues.Contains(r)) data.Revenues.Add(r);
        }

        // A row converted before the undo already has its USD figure, so only still-pending rows requeue.
        foreach (var entry in PendingConversions)
        {
            if (CreatedTransactions.Any(t => t.Id == entry.TransactionId && t.IsPendingConversion) &&
                !data.PendingConversions.Any(p => p.TransactionId == entry.TransactionId))
                data.PendingConversions.Add(entry);
        }
        MirrorPendingConversions(data);

        RelinkReleasedLines();
        data.MarkAsModified();
    }

    /// <summary>
    /// Brings the shared conversion queue in line with the company file for this import's rows; the
    /// retry timer drains that queue, which only takes in the company file's list when it opens.
    /// Fire and forget: it only writes a cache file, and failing to must never block the import or
    /// an undo the user has already seen happen.
    /// </summary>
    public void MirrorPendingConversions(CompanyData data)
    {
        if (PendingConversions.Count > 0 && PendingConversionService.Instance is { } svc)
            _ = svc.MirrorAsync(data, PendingConversions.Select(p => p.TransactionId));
    }

    /// <summary>
    /// Frees statement lines matched to this import's rows, whether linked at import or matched
    /// afterwards, so undo doesn't leave them Matched to rows that no longer exist.
    /// </summary>
    private void ReleaseMatchedLines(CompanyData data)
    {
        _releasedLines.Clear();
        foreach (var line in data.BankImportSessions.SelectMany(s => s.Lines))
        {
            if (line.MatchStatus != BankLineMatchStatus.Matched || line.MatchedRecordType is not { } type)
                continue;

            var record = CreatedTransactions.FirstOrDefault(t =>
                t.Id == line.MatchedRecordId && (t is Revenue ? BookRecordType.Revenue : BookRecordType.Expense) == type);
            if (record == null) continue;

            _releasedLines.Add(new ReleasedLine(line, record, type, line.MatchedDate, line.MatchConfidence));
            line.MatchStatus = BankLineMatchStatus.Unmatched;
            line.MatchedRecordType = null;
            line.MatchedRecordId = null;
            line.MatchedDate = null;
            line.MatchConfidence = 0;
        }
    }

    private void RelinkReleasedLines()
    {
        foreach (var released in _releasedLines)
        {
            // The line was matched or ignored some other way in between, so the row gives up its flag.
            if (released.Line.MatchStatus is BankLineMatchStatus.Matched or BankLineMatchStatus.Ignored)
            {
                released.Record.BankMatched = false;
                released.Record.BankMatchedDate = null;
                released.Record.BankMatchedLineId = null;
                continue;
            }

            released.Line.MatchStatus = BankLineMatchStatus.Matched;
            released.Line.MatchedRecordType = released.Type;
            released.Line.MatchedRecordId = released.Record.Id;
            released.Line.MatchedDate = released.MatchedDate;
            released.Line.MatchConfidence = released.Confidence;
        }

        _releasedLines.Clear();
    }

    private sealed record ReleasedLine(
        BankStatementLine Line, Transaction Record, BookRecordType Type, DateTime? MatchedDate, double Confidence);
}

/// <summary>
/// Turns resolved unmatched bank lines into Expense/Revenue transactions, auto-creating any
/// new supplier/customer/category, and marks each line matched to its new transaction.
/// </summary>
/// <param name="convert">
/// Converts to the USD base at a date. Injectable so tests needn't install the exchange rate
/// singleton; defaults to its cache-only exact-date conversion.
/// </param>
public class BankLineImportService(UsdConverter? convert = null)
{
    private readonly UsdConverter _convert = convert ?? DefaultConverter;

    public BankImportCreation CreateFromLines(CompanyData data, IReadOnlyList<BankLineResolution> resolutions, bool linkToBankLine = true)
    {
        var creation = new BankImportCreation();

        // Bank statements carry no currency, so amounts are in the company's currency.
        var companyCurrency = string.IsNullOrWhiteSpace(data.Settings.Localization.Currency)
            ? "USD"
            : data.Settings.Localization.Currency;

        // Dedup caches so repeated rows reuse one created entity instead of making duplicates.
        var supplierCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var customerCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var categoryCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var productCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var expenseIds = IdGenerator.TakenSet(data.Expenses.Select(e => e.Id));
        var revenueIds = IdGenerator.TakenSet(data.Revenues.Select(r => r.Id));

        foreach (var r in resolutions)
        {
            var isExpense = r.Type == BookRecordType.Expense;

            // Resolve / create counterparty.
            string? counterpartyId = r.CounterpartyId;
            if (counterpartyId == null && !string.IsNullOrWhiteSpace(r.NewCounterpartyName))
            {
                counterpartyId = isExpense
                    ? ResolveSupplier(data, creation, supplierCache, r.NewCounterpartyName!)
                    : ResolveCustomer(data, creation, customerCache, r.NewCounterpartyName!);
            }

            // Resolve / create the product that carries the category for this transaction.
            string? productId = r.ProductId;
            if (productId == null && !string.IsNullOrWhiteSpace(r.NewProductName))
            {
                var type = isExpense ? CategoryType.Expense : CategoryType.Revenue;
                var categoryId = r.ProductCategoryId;
                // Don't propagate a category id that doesn't resolve to a real category (a deleted one, or
                // a bad value the caller/AI supplied - the model sometimes echoes a hallucinated id or a
                // name in this field). Fall back to resolving by name, or leave it unset, so the product
                // and its learned rule never carry a dangling category reference (which the Bank import
                // rules screen then shows with an empty category).
                if (!string.IsNullOrEmpty(categoryId) && data.GetCategory(categoryId) == null)
                    categoryId = null;
                if (categoryId == null && !string.IsNullOrWhiteSpace(r.NewProductCategoryName))
                    categoryId = ResolveCategory(data, creation, categoryCache, r.NewProductCategoryName!, type);

                productId = ResolveProduct(data, creation, productCache, r.NewProductName!, categoryId, type);
            }

            var draft = new TransactionDraft(
                Date: r.Line.Date,
                Description: r.Line.Description,
                Total: Math.Abs(r.Line.Amount),
                CounterpartyId: counterpartyId,
                Notes: "Imported from bank statement",
                OriginalCurrency: companyCurrency,
                ProductId: productId);

            Transaction tx = isExpense
                ? TransactionFactory.CreateExpense(data, draft, expenseIds)
                : TransactionFactory.CreateRevenue(data, draft, revenueIds);

            ApplyUsdAmounts(data, creation, tx);

            if (linkToBankLine)
            {
                tx.BankMatched = true;
                tx.BankMatchedLineId = r.Line.Id;
                tx.BankMatchedDate = DateTime.UtcNow;
            }

            if (isExpense) data.Expenses.Add((Expense)tx);
            else data.Revenues.Add((Revenue)tx);

            if (linkToBankLine)
            {
                r.Line.MatchStatus = BankLineMatchStatus.Matched;
                r.Line.MatchedRecordType = r.Type;
                r.Line.MatchedRecordId = tx.Id;
                r.Line.MatchedDate = DateTime.UtcNow;
            }

            creation.CreatedTransactions.Add(tx);
        }

        data.MarkAsModified();
        return creation;
    }

    /// <summary>
    /// Stores the USD base at the line's own date (Calculations.md Rule 3a). With no rate held for
    /// that date the row is marked pending and queued, so it converts later instead of carrying
    /// the company-currency figure as though it were USD.
    /// </summary>
    private void ApplyUsdAmounts(CompanyData data, BankImportCreation creation, Transaction tx)
    {
        var currency = tx.OriginalCurrency;
        if (string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase))
        {
            tx.TotalUSD = tx.Total;
            tx.UnitPriceUSD = tx.UnitPrice;
            return;
        }

        if (_convert(tx.Total, currency, tx.Date, out var totalUsd) &&
            _convert(tx.UnitPrice, currency, tx.Date, out var unitPriceUsd))
        {
            tx.TotalUSD = totalUsd;
            tx.UnitPriceUSD = unitPriceUsd;
            return;
        }

        tx.TotalUSD = 0m;
        tx.UnitPriceUSD = 0m;
        tx.IsPendingConversion = true;

        var entry = new PendingConversion
        {
            TransactionId = tx.Id,
            TransactionType = tx is Revenue ? "Revenue" : "Expense",
            OriginalCurrency = currency,
            TransactionDate = tx.Date,
            Total = tx.Total,
            UnitPrice = tx.UnitPrice
        };
        data.PendingConversions.RemoveAll(p => p.TransactionId == tx.Id);
        data.PendingConversions.Add(entry);
        creation.PendingConversions.Add(entry);
    }

    private static bool DefaultConverter(decimal amount, string currency, DateTime date, out decimal usd)
    {
        if (ExchangeRateService.Instance is { } rates)
            return rates.TryConvertToUsdBase(amount, currency, date, out usd);

        usd = 0m;
        return false;
    }

    private static string ResolveSupplier(CompanyData data, BankImportCreation creation,
        Dictionary<string, string> cache, string name)
    {
        var key = name.Trim();
        if (cache.TryGetValue(key, out var cached)) return cached;

        var existing = ImportLookup.FindSupplier(data, null, key);
        if (existing != null) { cache[key] = existing.Id; return existing.Id; }

        var supplier = new Supplier { Id = new IdGenerator(data).NextSupplierId(), Name = key };
        data.Suppliers.Add(supplier);
        creation.CreatedEntities.Add(supplier);
        cache[key] = supplier.Id;
        return supplier.Id;
    }

    private static string ResolveCustomer(CompanyData data, BankImportCreation creation,
        Dictionary<string, string> cache, string name)
    {
        var key = name.Trim();
        if (cache.TryGetValue(key, out var cached)) return cached;

        var existing = ImportLookup.FindCustomer(data, null, key);
        if (existing != null) { cache[key] = existing.Id; return existing.Id; }

        var customer = new Customer { Id = new IdGenerator(data).NextCustomerId(), Name = key };
        data.Customers.Add(customer);
        creation.CreatedEntities.Add(customer);
        cache[key] = customer.Id;
        return customer.Id;
    }

    private static string ResolveCategory(CompanyData data, BankImportCreation creation,
        Dictionary<string, string> cache, string name, CategoryType type)
    {
        var key = $"{type}|{name.Trim()}";
        if (cache.TryGetValue(key, out var cached)) return cached;

        var category = ImportLookup.FindOrCreateCategory(data, type, name.Trim(), out var created);
        if (created) creation.CreatedEntities.Add(category);
        cache[key] = category.Id;
        return category.Id;
    }

    private static string ResolveProduct(CompanyData data, BankImportCreation creation,
        Dictionary<string, string> cache, string name, string? categoryId, CategoryType type)
    {
        var key = $"{type}|{name.Trim()}";
        if (cache.TryGetValue(key, out var cached)) return cached;

        var existing = ImportLookup.FindProduct(data, name.Trim(), type);
        if (existing != null) { cache[key] = existing.Id; return existing.Id; }

        var product = new Product
        {
            Id = new IdGenerator(data).NextProductId(),
            Name = name.Trim(),
            CategoryId = categoryId,
            Type = type,
            ItemType = "Product"
        };
        data.Products.Add(product);
        creation.CreatedEntities.Add(product);
        cache[key] = product.Id;
        return product.Id;
    }
}
