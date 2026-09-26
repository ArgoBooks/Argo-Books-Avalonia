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
        var ids = new IdGenerator(data);
        var revenueIds = IdGenerator.TakenSet(data.Revenues.Select(r => r.Id));
        var expenseIds = IdGenerator.TakenSet(data.Expenses.Select(e => e.Id));
        var customerIds = IdGenerator.TakenSet(data.Customers.Select(c => c.Id));
        var productIds = IdGenerator.TakenSet(data.Products.Select(p => p.Id));
        foreach (var ch in charges)
        {
            var customerId = ResolveCustomer(data, ch, customerIds);
            var productId = ResolveProduct(data, ch.ProductName, productIds);

            var currency = ImportLookup.NormalizeCurrency(ch.Currency);
            var gross = ArgoMoney.ToDecimal(ch.GrossCents, currency);
            var tax = ArgoMoney.ToDecimal(ch.TaxCents, currency);
            var discount = ArgoMoney.ToDecimal(ch.DiscountCents, currency);
            // Gross is what was charged after the discount. Subtotal is before it, as on every
            // transaction (Total = Subtotal − Discount + Tax), and the discount stays at the
            // transaction level so it isn't taken off the line a second time.
            var subtotal = gross - tax + discount;
            var taxableBase = subtotal - discount;
            var taxRate = taxableBase > 0 ? tax / taxableBase : 0m;
            var date = DateTimeOffset.FromUnixTimeSeconds(ch.CreatedUnix).LocalDateTime;

            var rev = new Revenue
            {
                Id = ids.NextRevenueId(date, revenueIds),
                Date = date,
                Description = ch.ProductName,
                CustomerId = customerId ?? string.Empty,
                Quantity = 1,
                UnitPrice = subtotal,
                Amount = subtotal,
                Subtotal = subtotal,
                // Transaction.TaxRate is a percentage; LineItem.TaxRate below is a fraction.
                TaxRate = taxRate * 100m,
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
                        TaxRate = taxRate
                    }
                ]
            };
            rev.OriginalCurrency = currency;
            UsdConversion.Apply(data, rev, UsdConversion.CachedRate(currency, rev.Date));
            data.Revenues.Add(rev);
            revs++;

            if (ch.FeeCents > 0)
            {
                var feeCurrency = ImportLookup.NormalizeCurrency(ch.FeeCurrency, fallback: currency);
                var feeAmount = ArgoMoney.ToDecimal(ch.FeeCents, feeCurrency);
                var fee = new Expense
                {
                    Id = ids.NextExpenseId(date, expenseIds),
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
                fee.OriginalCurrency = feeCurrency;
                UsdConversion.Apply(data, fee, UsdConversion.CachedRate(feeCurrency, fee.Date));
                data.Expenses.Add(fee);
                exps++;
            }
        }

        if (revs > 0 || exps > 0) data.MarkAsModified();
        return new StripeDetailResult(revs, exps, 0);
    }

    private string? ResolveCustomer(CompanyData data, StripeChargeDetail ch, HashSet<string> customerIds)
    {
        var key = string.IsNullOrWhiteSpace(ch.CustomerEmail) ? ch.CustomerName : ch.CustomerEmail;
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (_customerCache.TryGetValue(key, out var cached)) return cached;

        var existing = ImportLookup.FindCustomer(data, ch.CustomerEmail, ch.CustomerName);
        if (existing != null) { _customerCache[key] = existing.Id; return existing.Id; }

        var customer = new Customer
        {
            Id = new IdGenerator(data).NextCustomerId(customerIds),
            Name = string.IsNullOrWhiteSpace(ch.CustomerName) ? (ch.CustomerEmail ?? "Stripe customer") : ch.CustomerName!,
            Email = ch.CustomerEmail ?? string.Empty
        };
        data.Customers.Add(customer);
        _customerCache[key] = customer.Id;
        return customer.Id;
    }

    private string ResolveProduct(CompanyData data, string name, HashSet<string> productIds)
    {
        if (_productCache.TryGetValue(name, out var cached)) return cached;
        var existing = ImportLookup.FindProduct(data, name, CategoryType.Revenue);
        if (existing != null) { _productCache[name] = existing.Id; return existing.Id; }

        var categoryId = ResolveStripeCategory(data);
        var product = new Product
        {
            Id = new IdGenerator(data).NextProductId(productIds),
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
        var ids = new IdGenerator(data);
        var returnIds = IdGenerator.TakenSet(data.Returns.Select(r => r.Id));
        var expenseIds = IdGenerator.TakenSet(data.Expenses.Select(e => e.Id));
        foreach (var ch in charges)
        {
            if (ch.AmountRefundedCents <= 0) continue;
            var currency = ImportLookup.NormalizeCurrency(ch.Currency);
            var amount = ArgoMoney.ToDecimal(ch.AmountRefundedCents, currency);

            var rev = data.Revenues.FirstOrDefault(r => r.ReferenceNumber == ch.ChargeId);
            if (rev != null)
            {
                if (data.Returns.Any(rt => rt.OriginalTransactionId == rev.Id)) continue; // already recorded

                data.Returns.Add(new Return
                {
                    Id = ids.NextReturnId(returnIds),
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

                var refundDate = DateTime.Now;
                var exp = new Expense
                {
                    Id = ids.NextExpenseId(refundDate, expenseIds),
                    Date = refundDate,
                    Description = "Stripe refund",
                    Quantity = 1,
                    UnitPrice = amount,
                    Amount = amount,
                    Total = amount,
                    ReferenceNumber = ch.ChargeId,
                    Notes = "Imported from Stripe",
                    OriginalCurrency = currency
                };
                exp.OriginalCurrency = currency;
                UsdConversion.Apply(data, exp, UsdConversion.CachedRate(currency, exp.Date));
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
        _stripeCategoryId = ImportLookup.FindOrCreateCategory(data, CategoryType.Revenue, "Stripe", out _).Id;
        return _stripeCategoryId;
    }
}
