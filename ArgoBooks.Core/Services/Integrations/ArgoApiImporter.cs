using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services.Integrations;

/// <summary>
/// Maps objects pushed through the Argo Books API into the company's books.
///
/// Order is not a style choice: categories come before products, products and
/// people before transactions, and revenue before refunds, because each layer
/// resolves ids the next one needs.
///
/// Matching is by natural key (email, then name) rather than by API id, so a
/// developer pushing "Acme Ltd" lands on the Acme Ltd the merchant already has
/// instead of creating a duplicate. The API id is recorded on the transaction's
/// reference so a second push of the same object is recognisable.
/// </summary>
public class ArgoApiImporter
{
    private readonly Dictionary<string, string> _customers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _suppliers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _categories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _products = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _revenues = new(StringComparer.Ordinal);

    /// <summary>Fallback category for anything a developer pushed without one.</summary>
    private string? _apiRevenueCategoryId;
    private string? _apiExpenseCategoryId;

    /// <summary>
    /// References to objects imported on an earlier occasion, resolved by the
    /// sync service. Without these, anything pointing at a customer, supplier or
    /// product from a previous batch would import with that link missing.
    /// </summary>
    private IReadOnlyDictionary<string, ArgoExternalRef> _external =
        new Dictionary<string, ArgoExternalRef>();

    public void Import(CompanyData data, ArgoApiSyncPreview preview, ArgoApiImportCreation creation)
    {
        _external = preview.ExternalRefs;

        foreach (var c in preview.Categories) ImportCategory(data, c, creation);
        foreach (var c in preview.Customers) ImportCustomer(data, c, creation);
        foreach (var s in preview.Suppliers) ImportSupplier(data, s, creation);
        foreach (var p in preview.Products) ImportProduct(data, p, creation);
        foreach (var e in preview.Expenses) ImportExpense(data, e, creation);
        foreach (var r in preview.Revenue) ImportRevenue(data, r, creation);
        foreach (var r in preview.Refunds) ImportRefund(data, r, creation);

        if (creation.AnyCreated) data.MarkAsModified();
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// Resolve an API id to a local record id.
    ///
    /// Three chances, in order: something imported in this batch, something
    /// imported earlier (via the id the server remembered for it), and finally a
    /// match on the natural key in case the local record was created by hand.
    /// Null means genuinely unknown, and the caller leaves the link empty.
    /// </summary>
    private string? ResolveRef(
        CompanyData data,
        Dictionary<string, string> inBatch,
        string? apiId,
        Func<CompanyData, ArgoExternalRef, string?> byNaturalKey)
    {
        if (string.IsNullOrEmpty(apiId)) return null;
        if (inBatch.TryGetValue(apiId, out var local)) return local;
        if (!_external.TryGetValue(apiId, out var ext)) return null;
        if (!string.IsNullOrEmpty(ext.LocalRef)) return ext.LocalRef;
        return byNaturalKey(data, ext);
    }

    private static string? MatchCustomer(CompanyData data, ArgoExternalRef r) =>
        (!string.IsNullOrWhiteSpace(r.Email)
            ? data.Customers.FirstOrDefault(c => string.Equals(c.Email, r.Email, StringComparison.OrdinalIgnoreCase))
            : data.Customers.FirstOrDefault(c => string.Equals(c.Name, r.Name, StringComparison.OrdinalIgnoreCase)))?.Id;

    private static string? MatchSupplier(CompanyData data, ArgoExternalRef r) =>
        (!string.IsNullOrWhiteSpace(r.Email)
            ? data.Suppliers.FirstOrDefault(x => string.Equals(x.Email, r.Email, StringComparison.OrdinalIgnoreCase))
            : data.Suppliers.FirstOrDefault(x => string.Equals(x.Name, r.Name, StringComparison.OrdinalIgnoreCase)))?.Id;

    private static string? MatchProduct(CompanyData data, ArgoExternalRef r) =>
        data.Products.FirstOrDefault(p => string.Equals(p.Name, r.Name, StringComparison.OrdinalIgnoreCase))?.Id;

    private static string? MatchCategory(CompanyData data, ArgoExternalRef r) =>
        data.Categories.FirstOrDefault(c => string.Equals(c.Name, r.Name, StringComparison.OrdinalIgnoreCase))?.Id;

    private static string? MatchRevenue(CompanyData data, ArgoExternalRef r) => null;

    private void ImportCategory(CompanyData data, ArgoCategory api, ArgoApiImportCreation creation)
    {
        var type = api.Kind == "expense" ? CategoryType.Expense : CategoryType.Revenue;

        var existing = data.Categories.FirstOrDefault(c =>
            c.Type == type && string.Equals(c.Name, api.Name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            Claim(creation, api.Id, existing.Id);
            _categories[api.Id] = existing.Id;
            return;
        }

        var category = new Category
        {
            Id = NextCategoryId(data, type),
            Name = api.Name,
            Type = type
        };
        data.Categories.Add(category);
        creation.Entities.Add(category);
        Claim(creation, api.Id, category.Id);
        _categories[api.Id] = category.Id;
    }

    private void ImportCustomer(CompanyData data, ArgoCustomer api, ArgoApiImportCreation creation)
    {
        var existing = !string.IsNullOrWhiteSpace(api.Email)
            ? data.Customers.FirstOrDefault(c => string.Equals(c.Email, api.Email, StringComparison.OrdinalIgnoreCase))
            : data.Customers.FirstOrDefault(c => string.Equals(c.Name, api.Name, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            Claim(creation, api.Id, existing.Id);
            _customers[api.Id] = existing.Id;
            return;
        }

        var customer = new Customer
        {
            Id = new IdGenerator(data).NextCustomerId(),
            Name = api.Name,
            Email = api.Email ?? string.Empty,
            Phone = api.Phone ?? string.Empty
        };
        data.Customers.Add(customer);
        creation.Entities.Add(customer);
        Claim(creation, api.Id, customer.Id);
        _customers[api.Id] = customer.Id;
    }

    private void ImportSupplier(CompanyData data, ArgoSupplier api, ArgoApiImportCreation creation)
    {
        var existing = !string.IsNullOrWhiteSpace(api.Email)
            ? data.Suppliers.FirstOrDefault(s => string.Equals(s.Email, api.Email, StringComparison.OrdinalIgnoreCase))
            : data.Suppliers.FirstOrDefault(s => string.Equals(s.Name, api.Name, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            Claim(creation, api.Id, existing.Id);
            _suppliers[api.Id] = existing.Id;
            return;
        }

        var supplier = new Supplier
        {
            Id = new IdGenerator(data).NextSupplierId(),
            Name = api.Name,
            Email = api.Email ?? string.Empty,
            Phone = api.Phone ?? string.Empty,
            Website = api.Website ?? string.Empty,
            Notes = api.Notes ?? string.Empty
        };
        data.Suppliers.Add(supplier);
        creation.Entities.Add(supplier);
        Claim(creation, api.Id, supplier.Id);
        _suppliers[api.Id] = supplier.Id;
    }

    private void ImportProduct(CompanyData data, ArgoProduct api, ArgoApiImportCreation creation)
    {
        var existing = data.Products.FirstOrDefault(p =>
            string.Equals(p.Name, api.Name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            Claim(creation, api.Id, existing.Id);
            _products[api.Id] = existing.Id;
            return;
        }

        var categoryId = ResolveRef(data, _categories, api.Category, MatchCategory)
            ?? ResolveFallbackCategory(data, CategoryType.Revenue, creation);

        // Take the product's type from the category it landed in rather than
        // assuming revenue: a product under an expense category is a thing you
        // buy, and typing it as revenue would file it on the wrong side.
        var productType = data.Categories.FirstOrDefault(c => c.Id == categoryId)?.Type ?? CategoryType.Revenue;

        var product = new Product
        {
            Id = new IdGenerator(data).NextProductId(),
            Name = api.Name,
            CategoryId = categoryId,
            Type = productType,
            ItemType = "Service"
        };
        data.Products.Add(product);
        creation.Entities.Add(product);
        Claim(creation, api.Id, product.Id);
        _products[api.Id] = product.Id;
    }

    private void ImportExpense(CompanyData data, ArgoExpense api, ArgoApiImportCreation creation)
    {
        var currency = Currency(api.Currency);
        var total = ArgoMoney.ToDecimal(api.Amount, currency);
        var tax = ArgoMoney.ToDecimal(api.TaxAmount, currency);
        var subtotal = total - tax;
        var date = ParseDate(api.OccurredOn);

        data.IdCounters.Expense++;
        var expense = new Expense
        {
            Id = $"PUR-{date:yyyy}-{data.IdCounters.Expense:D5}",
            Date = date,
            Description = api.Description,
            SupplierId = ResolveRef(data, _suppliers, api.Supplier, MatchSupplier),
            Quantity = 1,
            UnitPrice = subtotal,
            Amount = subtotal,
            TaxRate = subtotal > 0 ? tax / subtotal : 0m,
            TaxAmount = tax,
            Total = total,
            // The API id, so a repeat push of the same object is recognisable in
            // the books and a developer can match their record to this one.
            ReferenceNumber = string.IsNullOrWhiteSpace(api.Reference) ? api.Id : api.Reference!,
            Notes = BuildNotes(api.Notes, api.Id),
            OriginalCurrency = currency,
            LineItems = BuildLineItems(data, api.LineItems, currency, api.Description, subtotal, tax)
        };
        IntegrationRates.ApplyUsdAmounts(expense, currency, data);

        data.Expenses.Add(expense);
        creation.Expenses.Add(expense);
        Claim(creation, api.Id, expense.Id);
    }

    private void ImportRevenue(CompanyData data, ArgoRevenue api, ArgoApiImportCreation creation)
    {
        var currency = Currency(api.Currency);
        var total = ArgoMoney.ToDecimal(api.Amount, currency);
        var tax = ArgoMoney.ToDecimal(api.TaxAmount, currency);
        var discount = ArgoMoney.ToDecimal(api.DiscountAmount, currency);
        var fee = ArgoMoney.ToDecimal(api.FeeAmount, currency);
        var subtotal = total - tax;
        var date = ParseDate(api.OccurredOn);

        data.IdCounters.Revenue++;
        var revenue = new Revenue
        {
            Id = $"REV-{date:yyyy}-{data.IdCounters.Revenue:D5}",
            Date = date,
            Description = api.Description,
            CustomerId = ResolveRef(data, _customers, api.Customer, MatchCustomer) ?? string.Empty,
            Quantity = 1,
            UnitPrice = subtotal,
            Amount = subtotal,
            Subtotal = subtotal,
            TaxRate = subtotal > 0 ? tax / subtotal : 0m,
            TaxAmount = tax,
            Discount = discount,
            Total = total,
            ReferenceNumber = string.IsNullOrWhiteSpace(api.Reference) ? api.Id : api.Reference!,
            Notes = BuildNotes(api.Notes, api.Id),
            OriginalCurrency = currency,
            PaymentStatus = RevenuePaymentStatus.Paid,
            LineItems = BuildLineItems(data, api.LineItems, currency, api.Description, subtotal, tax)
        };
        IntegrationRates.ApplyUsdAmounts(revenue, currency, data);

        data.Revenues.Add(revenue);
        creation.Revenues.Add(revenue);
        Claim(creation, api.Id, revenue.Id);
        _revenues[api.Id] = revenue.Id;

        // A platform that withheld its cut reports it as fee_amount. Booking it as
        // its own expense keeps the sale at its gross value, which is what the
        // customer actually paid and what the tax is calculated on.
        if (fee > 0)
        {
            data.IdCounters.Expense++;
            var feeExpense = new Expense
            {
                Id = $"PUR-{date:yyyy}-{data.IdCounters.Expense:D5}",
                Date = date,
                Description = "Processing fee",
                Quantity = 1,
                UnitPrice = fee,
                Amount = fee,
                Total = fee,
                ReferenceNumber = revenue.ReferenceNumber,
                Notes = $"Processing fee for {revenue.Id} (Argo Books API {api.Id})",
                OriginalCurrency = currency
            };
            IntegrationRates.ApplyUsdAmounts(feeExpense, currency, data);
            data.Expenses.Add(feeExpense);
            creation.Expenses.Add(feeExpense);
        }
    }

    private void ImportRefund(CompanyData data, ArgoRefund api, ArgoApiImportCreation creation)
    {
        var currency = Currency(api.Currency);
        var amount = ArgoMoney.ToDecimal(api.Amount, currency);

        // The parent sale is normally in this same import, but it may have been
        // imported weeks ago, so fall back to searching the books by reference.
        // ReferenceNumber holds the developer's own reference when they set one,
        // and only falls back to the API id when they did not, so searching it
        // for an API id finds nothing in the common case. The resolver above is
        // what actually links a refund to a sale imported weeks ago.
        var localRevenueId = ResolveRef(data, _revenues, api.Revenue, MatchRevenue)
            ?? data.Revenues.FirstOrDefault(r => r.ReferenceNumber == api.Revenue)?.Id;

        // The id the server remembers for an earlier import can name a sale the merchant has
        // since deleted, which is as good as having no sale.
        var revenue = localRevenueId == null ? null : data.Revenues.FirstOrDefault(r => r.Id == localRevenueId);

        if (revenue == null)
        {
            // No sale to return against: book it as a standalone expense rather
            // than dropping it, so the money movement is still in the books.
            data.IdCounters.Expense++;
            var date = ParseDate(api.OccurredOn);
            var expense = new Expense
            {
                Id = $"PUR-{date:yyyy}-{data.IdCounters.Expense:D5}",
                Date = date,
                Description = string.IsNullOrWhiteSpace(api.Reason) ? "Refund" : $"Refund: {api.Reason}",
                Quantity = 1,
                UnitPrice = amount,
                Amount = amount,
                Total = amount,
                ReferenceNumber = api.Id,
                Notes = $"Refund imported from the Argo Books API for {api.Revenue}, with no matching sale in this company.",
                OriginalCurrency = currency
            };
            IntegrationRates.ApplyUsdAmounts(expense, currency, data);
            data.Expenses.Add(expense);
            creation.Expenses.Add(expense);
            Claim(creation, api.Id, expense.Id);
            return;
        }

        data.IdCounters.Return++;
        var ret = new Return
        {
            Id = $"RET-{data.IdCounters.Return:D3}",
            OriginalTransactionId = revenue.Id,
            ReturnType = "Customer",
            CustomerId = revenue.CustomerId ?? string.Empty,
            ReturnDate = ParseDate(api.OccurredOn),
            RefundAmount = amount,
            Status = ReturnStatus.Completed,
            Notes = api.Reason ?? string.Empty
        };
        data.Returns.Add(ret);
        creation.Returns.Add(ret);
        Claim(creation, api.Id, ret.Id);
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// Build line items from the pushed detail, or one synthetic item when the
    /// developer sent none, so every transaction has at least one line and the
    /// books look the same however the data arrived.
    /// </summary>
    private List<LineItem> BuildLineItems(
        CompanyData data, List<ArgoLineItem>? items, string currency, string fallbackDescription,
        decimal subtotal, decimal tax)
    {
        if (items == null || items.Count == 0)
        {
            return
            [
                new LineItem
                {
                    Description = fallbackDescription,
                    Quantity = 1,
                    UnitPrice = subtotal,
                    TaxRate = subtotal > 0 ? tax / subtotal : 0m
                }
            ];
        }

        return items.Select(i =>
        {
            var unit = ArgoMoney.ToDecimal(i.UnitAmount, currency);
            var lineTax = ArgoMoney.ToDecimal(i.TaxAmount, currency);
            var lineSubtotal = unit * i.Quantity;
            return new LineItem
            {
                ProductId = ResolveRef(data, _products, i.Product, MatchProduct),
                Description = i.Description,
                Quantity = i.Quantity,
                UnitPrice = unit,
                TaxRate = lineSubtotal > 0 ? lineTax / lineSubtotal : 0m,
                Discount = ArgoMoney.ToDecimal(i.DiscountAmount, currency)
            };
        }).ToList();
    }

    private string ResolveFallbackCategory(CompanyData data, CategoryType type, ArgoApiImportCreation creation)
    {
        var isExpense = type == CategoryType.Expense;
        var cached = isExpense ? _apiExpenseCategoryId : _apiRevenueCategoryId;
        if (cached != null) return cached;

        var existing = data.Categories.FirstOrDefault(c =>
            c.Type == type && string.Equals(c.Name, "Argo Books API", StringComparison.OrdinalIgnoreCase));

        var id = existing?.Id;
        if (id == null)
        {
            var category = new Category
            {
                Id = NextCategoryId(data, type),
                Name = "Argo Books API",
                Type = type
            };
            data.Categories.Add(category);
            creation.Entities.Add(category);
            id = category.Id;
        }

        if (isExpense) _apiExpenseCategoryId = id;
        else _apiRevenueCategoryId = id;

        return id;
    }

    private static string NextCategoryId(CompanyData data, CategoryType type)
    {
        data.IdCounters.Category++;
        var prefix = type switch
        {
            CategoryType.Revenue => "REV",
            CategoryType.Expense => "EXP",
            CategoryType.Rental => "RNT",
            _ => "GEN"
        };
        return $"CAT-{prefix}-{data.IdCounters.Category:D3}";
    }

    private static void Claim(ArgoApiImportCreation creation, string apiId, string localId)
    {
        creation.ClaimedObjectIds.Add(apiId);
        creation.LocalRefs[apiId] = localId;
    }

    private static string Currency(string? code)
        => string.IsNullOrWhiteSpace(code) ? "USD" : code.ToUpperInvariant();

    /// <summary>
    /// Parse the API's YYYY-MM-DD. Falls back to today rather than throwing: the
    /// server validated the format, so a failure here means something unexpected,
    /// and losing the whole import over one date would be the worse outcome.
    /// </summary>
    /// <summary>
    /// Internal so the sync service can preload exchange rates for exactly the
    /// dates this will store. Two separate parsers would eventually disagree,
    /// and the rows that fell through the gap would be the ones showing
    /// "Pending" instead of an amount.
    /// </summary>
    internal static DateTime ParseDate(string value)
        => DateTime.TryParseExact(value, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d)
            ? d
            : DateTime.Today;

    private static string BuildNotes(string? notes, string apiId)
        => string.IsNullOrWhiteSpace(notes)
            ? $"Imported from the Argo Books API ({apiId})"
            : $"{notes}\n\nImported from the Argo Books API ({apiId})";
}
