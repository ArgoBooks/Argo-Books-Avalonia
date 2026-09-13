using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services.Integrations;

public record StripeDetailResult(int RevenuesCreated, int ExpensesCreated, int Returns);

/// <summary>
/// Imports Stripe charges as detailed Revenue records (product, customer, tax,
/// discount), auto-creating the customer, product, and a "Stripe" category, and a
/// processing-fee expense. Constructs records directly because the flat
/// TransactionFactory can't carry tax/discount/line items.
/// </summary>
public class StripeDetailImporter
{
    private readonly Dictionary<string, string> _customerCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _productCache = new(StringComparer.OrdinalIgnoreCase);
    private string? _stripeCategoryId;

    public StripeDetailResult ImportCharges(CompanyData data, IReadOnlyList<StripeChargeDetail> charges)
    {
        int revs = 0, exps = 0;
        foreach (var ch in charges)
        {
            var customerId = ResolveCustomer(data, ch);
            var productId = ResolveProduct(data, ch.ProductName);

            var currency = string.IsNullOrWhiteSpace(ch.Currency) ? "USD" : ch.Currency.ToUpperInvariant();
            var gross = ArgoMoney.ToDecimal(ch.GrossCents, currency);
            var tax = ArgoMoney.ToDecimal(ch.TaxCents, currency);
            var discount = ArgoMoney.ToDecimal(ch.DiscountCents, currency);
            var subtotal = gross - tax;
            var taxRate = subtotal > 0 ? tax / subtotal : 0m;
            var date = DateTimeOffset.FromUnixTimeSeconds(ch.CreatedUnix).LocalDateTime;

            data.IdCounters.Revenue++;
            var rev = new Revenue
            {
                Id = $"REV-{date:yyyy}-{data.IdCounters.Revenue:D5}",
                Date = date,
                Description = ch.ProductName,
                CustomerId = customerId ?? string.Empty,
                Quantity = 1,
                UnitPrice = subtotal,
                Amount = subtotal,
                Subtotal = subtotal,
                TaxRate = taxRate,
                TaxAmount = tax,
                Discount = discount,
                Total = gross,
                ReferenceNumber = ch.ChargeId,
                Notes = "Imported from Stripe",
                OriginalCurrency = currency,
                PaymentStatus = RevenuePaymentStatus.Paid,
                LineItems =
                [
                    new LineItem
                    {
                        ProductId = productId,
                        Description = ch.ProductName,
                        Quantity = 1,
                        UnitPrice = subtotal,
                        TaxRate = taxRate,
                        Discount = discount
                    }
                ]
            };
            IntegrationRates.ApplyUsdAmounts(rev, currency, data);
            data.Revenues.Add(rev);
            revs++;

            if (ch.FeeCents > 0)
            {
                data.IdCounters.Expense++;
                var feeCurrency = string.IsNullOrWhiteSpace(ch.FeeCurrency) ? currency : ch.FeeCurrency.ToUpperInvariant();
                var feeAmount = ArgoMoney.ToDecimal(ch.FeeCents, feeCurrency);
                var fee = new Expense
                {
                    Id = $"PUR-{date:yyyy}-{data.IdCounters.Expense:D5}",
                    Date = date,
                    Description = "Stripe processing fee",
                    Quantity = 1,
                    UnitPrice = feeAmount,
                    Amount = feeAmount,
                    Total = feeAmount,
                    // Same reference as the sale's Revenue, so the fee is linked to the charge it came from.
                    ReferenceNumber = ch.ChargeId,
                    Notes = $"Processing fee for Stripe sale {ch.ChargeId}",
                    OriginalCurrency = feeCurrency
                };
                IntegrationRates.ApplyUsdAmounts(fee, feeCurrency, data);
                data.Expenses.Add(fee);
                exps++;
            }
        }

        if (revs > 0 || exps > 0) data.MarkAsModified();
        return new StripeDetailResult(revs, exps, 0);
    }

    private string? ResolveCustomer(CompanyData data, StripeChargeDetail ch)
    {
        var key = string.IsNullOrWhiteSpace(ch.CustomerEmail) ? ch.CustomerName : ch.CustomerEmail;
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (_customerCache.TryGetValue(key, out var cached)) return cached;

        var existing = !string.IsNullOrWhiteSpace(ch.CustomerEmail)
            ? data.Customers.FirstOrDefault(c => string.Equals(c.Email, ch.CustomerEmail, StringComparison.OrdinalIgnoreCase))
            : data.Customers.FirstOrDefault(c => string.Equals(c.Name, ch.CustomerName, StringComparison.OrdinalIgnoreCase));
        if (existing != null) { _customerCache[key] = existing.Id; return existing.Id; }

        var customer = new Customer
        {
            Id = new IdGenerator(data).NextCustomerId(),
            Name = string.IsNullOrWhiteSpace(ch.CustomerName) ? (ch.CustomerEmail ?? "Stripe customer") : ch.CustomerName!,
            Email = ch.CustomerEmail ?? string.Empty
        };
        data.Customers.Add(customer);
        _customerCache[key] = customer.Id;
        return customer.Id;
    }

    private string ResolveProduct(CompanyData data, string name)
    {
        if (_productCache.TryGetValue(name, out var cached)) return cached;
        var existing = data.Products.FirstOrDefault(p =>
            p.Type == CategoryType.Revenue && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null) { _productCache[name] = existing.Id; return existing.Id; }

        var categoryId = ResolveStripeCategory(data);
        var product = new Product
        {
            Id = new IdGenerator(data).NextProductId(),
            Name = name,
            CategoryId = categoryId,
            Type = CategoryType.Revenue,
            ItemType = "Service"
        };
        data.Products.Add(product);
        _productCache[name] = product.Id;
        return product.Id;
    }

    /// <summary>
    /// Marks refunded charges as returns against the original imported sale. Falls back
    /// to a "Stripe refund" expense when the charge predates the integration and has no
    /// matching Revenue.
    /// </summary>
    public int ApplyRefunds(CompanyData data, IReadOnlyList<StripeChargeDetail> charges)
    {
        var made = 0;
        foreach (var ch in charges)
        {
            if (ch.AmountRefundedCents <= 0) continue;
            var currency = string.IsNullOrWhiteSpace(ch.Currency) ? "USD" : ch.Currency.ToUpperInvariant();
            var amount = ArgoMoney.ToDecimal(ch.AmountRefundedCents, currency);

            var rev = data.Revenues.FirstOrDefault(r => r.ReferenceNumber == ch.ChargeId);
            if (rev != null)
            {
                if (data.Returns.Any(rt => rt.OriginalTransactionId == rev.Id)) continue; // already recorded

                data.IdCounters.Return++;
                data.Returns.Add(new Return
                {
                    Id = $"RET-{data.IdCounters.Return:D3}",
                    OriginalTransactionId = rev.Id,
                    ReturnType = "Customer",
                    CustomerId = rev.CustomerId ?? string.Empty,
                    ReturnDate = DateTime.Now,
                    RefundAmount = amount,
                    Status = ReturnStatus.Completed
                });
                made++;
            }
            else
            {
                // Fallback: charge predates the integration, no matching sale to return against.
                if (data.Expenses.Any(e => e.ReferenceNumber == ch.ChargeId && e.Description == "Stripe refund"))
                    continue;

                data.IdCounters.Expense++;
                var exp = new Expense
                {
                    Id = $"PUR-{DateTime.Now:yyyy}-{data.IdCounters.Expense:D5}",
                    Date = DateTime.Now,
                    Description = "Stripe refund",
                    Quantity = 1,
                    UnitPrice = amount,
                    Amount = amount,
                    Total = amount,
                    ReferenceNumber = ch.ChargeId,
                    Notes = "Imported from Stripe",
                    OriginalCurrency = currency
                };
                IntegrationRates.ApplyUsdAmounts(exp, currency, data);
                data.Expenses.Add(exp);
                made++;
            }
        }

        if (made > 0) data.MarkAsModified();
        return made;
    }

    private string ResolveStripeCategory(CompanyData data)
    {
        if (_stripeCategoryId != null) return _stripeCategoryId;
        var existing = data.Categories.FirstOrDefault(c =>
            c.Type == CategoryType.Revenue && string.Equals(c.Name, "Stripe", StringComparison.OrdinalIgnoreCase));
        if (existing != null) { _stripeCategoryId = existing.Id; return existing.Id; }

        data.IdCounters.Category++;
        var cat = new Category { Id = $"CAT-SAL-{data.IdCounters.Category:D3}", Name = "Stripe", Type = CategoryType.Revenue };
        data.Categories.Add(cat);
        _stripeCategoryId = cat.Id;
        return cat.Id;
    }
}
