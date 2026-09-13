using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>Why a receipt cannot be switched, or <see cref="None"/> when it can.</summary>
public enum ReceiptSwitchBlock
{
    None,
    NoTransaction,
    HasPayments,
    FromInvoice,
    HasReturns,
    UsedByPayRun
}

/// <summary>
/// What a switch replaced and everything it brought into existence, so it can be undone and redone
/// without leaving orphans behind.
/// </summary>
public sealed record ReceiptSwitchResult(
    Transaction Removed,
    Transaction Created,
    string PreviousType,
    Supplier? CreatedSupplier,
    Customer? CreatedCustomer,
    Category? CreatedCategory,
    IReadOnlyList<Product> CreatedProducts,
    PendingConversion? MovedConversion);

/// <summary>
/// Moves the transaction behind a receipt from one side of the books to the other. A receipt only
/// records which transaction it belongs to, so switching its type means replacing that transaction
/// and everything that hangs off it: the counterparty is a supplier on one side and a customer on
/// the other, and products are typed per side too, so both are resolved for the target side.
/// </summary>
public static class ReceiptTypeConverter
{
    public const string Expense = "Expense";
    public const string Revenue = "Revenue";

    /// <summary>
    /// Other records reference a transaction by id, and a switch mints a new one. Anything holding
    /// such a reference has to block the switch or it would be left pointing at nothing.
    /// </summary>
    public static ReceiptSwitchBlock GetBlockReason(CompanyData data, Receipt receipt)
    {
        var id = receipt.TransactionId;
        if (string.IsNullOrEmpty(id))
            return ReceiptSwitchBlock.NoTransaction;

        var transaction = Find(data, receipt);
        if (transaction == null)
            return ReceiptSwitchBlock.NoTransaction;

        if (transaction is Revenue revenue)
        {
            if (data.Payments.Any(p => p.RevenueId == id))
                return ReceiptSwitchBlock.HasPayments;
            if (!string.IsNullOrEmpty(revenue.InvoiceId))
                return ReceiptSwitchBlock.FromInvoice;
        }

        if (data.Returns.Any(r => r.OriginalTransactionId == id))
            return ReceiptSwitchBlock.HasReturns;

        if (transaction is Expense
            && data.PayRuns.Any(p => p.Lines.Any(l => l.ExpenseId == id)))
            return ReceiptSwitchBlock.UsedByPayRun;

        return ReceiptSwitchBlock.None;
    }

    /// <summary>
    /// Replaces the receipt's transaction with its counterpart. Call <see cref="GetBlockReason"/>
    /// first. The counterparty and the line items' products are resolved for the target side,
    /// creating them from the receipt when nothing matches, the way a receipt scan already handles
    /// a name it has not seen before.
    /// </summary>
    public static ReceiptSwitchResult Switch(CompanyData data, Receipt receipt)
    {
        var existing = Find(data, receipt) ?? throw new InvalidOperationException(
            $"Receipt {receipt.Id} has no transaction to switch.");

        var toRevenue = existing is Expense;
        var targetType = toRevenue ? CategoryType.Revenue : CategoryType.Expense;

        Category? createdCategory = null;
        var createdProducts = new List<Product>();
        var lineItems = ResolveLineItems(data, existing.LineItems, targetType, ref createdCategory, createdProducts);

        Transaction created;
        Supplier? createdSupplier = null;
        Customer? createdCustomer = null;

        if (toRevenue)
        {
            var expense = (Expense)existing;
            data.Expenses.Remove(expense);

            data.IdCounters.Revenue++;
            var revenue = new Revenue
            {
                Id = $"REV-{expense.Date:yyyy}-{data.IdCounters.Revenue:D5}",
                Subtotal = expense.Amount,
                CustomerId = ResolveCustomer(data, receipt, out createdCustomer)
            };
            CopyShared(expense, revenue, lineItems);
            data.Revenues.Add(revenue);
            created = revenue;
        }
        else
        {
            var revenue = (Revenue)existing;
            data.Revenues.Remove(revenue);

            data.IdCounters.Expense++;
            var expense = new Expense
            {
                Id = $"PUR-{revenue.Date:yyyy}-{data.IdCounters.Expense:D5}",
                SupplierId = ResolveSupplier(data, receipt, out createdSupplier)
            };
            CopyShared(revenue, expense, lineItems);
            data.Expenses.Add(expense);
            created = expense;
        }

        created.ReceiptId = receipt.Id;
        created.UpdatedAt = DateTime.UtcNow;
        receipt.TransactionId = created.Id;
        receipt.TransactionType = toRevenue ? Revenue : Expense;

        var moved = MoveConversion(data, existing.Id, created.Id, receipt.TransactionType);

        return new ReceiptSwitchResult(
            existing, created, toRevenue ? Expense : Revenue,
            createdSupplier, createdCustomer, createdCategory, createdProducts, moved);
    }

    /// <summary>
    /// Re-points a queued currency conversion at the transaction that replaced the one it was
    /// raised for.
    ///
    /// <para>
    /// A transaction entered without its date's rate on hand carries IsPendingConversion and a
    /// row in the queue, and the switch copies that flag onto the replacement. The row still
    /// named the transaction the switch deleted, so when the rate finally arrived it found
    /// nothing, dropped the row as done, and left the replacement flagged forever. A pending
    /// transaction reports zero in USD, so it counted as nothing in every report rather than
    /// merely looking unfinished.
    /// </para>
    /// </summary>
    private static PendingConversion? MoveConversion(
        CompanyData data, string fromId, string toId, string toType)
    {
        var entry = data.PendingConversions.FirstOrDefault(p => p.TransactionId == fromId);
        if (entry == null) return null;

        entry.TransactionId = toId;
        entry.TransactionType = toType;
        return entry;
    }

    /// <summary>Puts back exactly what <see cref="Switch"/> replaced.</summary>
    public static void Revert(CompanyData data, Receipt receipt, ReceiptSwitchResult result)
    {
        Remove(data, result.Created);

        foreach (var product in result.CreatedProducts)
            data.Products.Remove(product);
        if (result.CreatedCategory != null) data.Categories.Remove(result.CreatedCategory);
        if (result.CreatedSupplier != null) data.Suppliers.Remove(result.CreatedSupplier);
        if (result.CreatedCustomer != null) data.Customers.Remove(result.CreatedCustomer);

        Add(data, result.Removed);
        result.Removed.ReceiptId = receipt.Id;
        receipt.TransactionId = result.Removed.Id;
        receipt.TransactionType = result.PreviousType;

        if (result.MovedConversion != null)
        {
            result.MovedConversion.TransactionId = result.Removed.Id;
            result.MovedConversion.TransactionType = result.PreviousType;
        }
    }

    /// <summary>Re-applies a switch that <see cref="Revert"/> undid, reusing the same records.</summary>
    public static void Reapply(CompanyData data, Receipt receipt, ReceiptSwitchResult result)
    {
        Remove(data, result.Removed);

        if (result.CreatedCategory != null && !data.Categories.Contains(result.CreatedCategory))
            data.Categories.Add(result.CreatedCategory);
        foreach (var product in result.CreatedProducts.Where(p => !data.Products.Contains(p)))
            data.Products.Add(product);
        if (result.CreatedSupplier != null && !data.Suppliers.Contains(result.CreatedSupplier))
            data.Suppliers.Add(result.CreatedSupplier);
        if (result.CreatedCustomer != null && !data.Customers.Contains(result.CreatedCustomer))
            data.Customers.Add(result.CreatedCustomer);

        Add(data, result.Created);
        result.Created.ReceiptId = receipt.Id;
        receipt.TransactionId = result.Created.Id;
        receipt.TransactionType = result.PreviousType == Expense ? Revenue : Expense;

        if (result.MovedConversion != null)
        {
            result.MovedConversion.TransactionId = result.Created.Id;
            result.MovedConversion.TransactionType = receipt.TransactionType;
        }
    }

    /// <summary>
    /// Clones the line items for the new transaction and points each at a product of the target
    /// side. The originals keep their own products, so a revert needs nothing undone on them.
    /// </summary>
    private static List<LineItem> ResolveLineItems(
        CompanyData data,
        List<LineItem> source,
        CategoryType targetType,
        ref Category? createdCategory,
        List<Product> createdProducts)
    {
        var clones = new List<LineItem>(source.Count);

        // The category the new products are filed under, which is usually one the company already
        // has. Kept apart from createdCategory: that one is what a revert deletes, so putting a
        // category we merely looked up into it would delete a category the user has had all along.
        Category? category = null;

        foreach (var item in source)
        {
            var clone = new LineItem
            {
                Description = item.Description,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                TaxRate = item.TaxRate,
                Discount = item.Discount,
                RentalRecordId = item.RentalRecordId,
                RevenueRecordId = item.RevenueRecordId
            };

            var name = ProductName(data, item);
            if (!string.IsNullOrEmpty(name))
            {
                var product = data.Products.FirstOrDefault(
                    p => p.Type == targetType && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

                if (product == null)
                {
                    if (category == null)
                    {
                        category = ResolveCategory(data, targetType, out var newCategory);
                        if (newCategory) createdCategory = category;
                    }

                    product = new Product
                    {
                        Id = new IdGenerator(data).NextProductId(),
                        Name = name,
                        CategoryId = category.Id,
                        Type = targetType,
                        UnitPrice = targetType == CategoryType.Revenue ? item.UnitPrice : 0,
                        CostPrice = targetType == CategoryType.Expense ? item.UnitPrice : 0
                    };
                    data.Products.Add(product);
                    createdProducts.Add(product);
                }

                clone.ProductId = product.Id;
            }

            clones.Add(clone);
        }

        return clones;
    }

    private static string? ProductName(CompanyData data, LineItem item)
    {
        if (!string.IsNullOrEmpty(item.ProductId))
        {
            var existing = data.Products.FirstOrDefault(p => p.Id == item.ProductId);
            if (existing != null) return existing.Name;
        }

        return string.IsNullOrWhiteSpace(item.Description) ? null : item.Description.Trim();
    }

    /// <summary>
    /// The category new products of the target side are filed under, reusing one the company
    /// already has where possible. <paramref name="created"/> reports which of the two happened,
    /// the way <see cref="ResolveSupplier"/> and <see cref="ResolveCustomer"/> do, because a
    /// revert may only delete what this actually created.
    /// </summary>
    private static Category ResolveCategory(CompanyData data, CategoryType type, out bool created)
    {
        created = false;

        var existing = data.Categories.FirstOrDefault(c => c.Type == type);
        if (existing != null) return existing;

        created = true;
        data.IdCounters.Category++;
        var category = new Category
        {
            Id = $"CAT-{(type == CategoryType.Revenue ? "SAL" : "PUR")}-{data.IdCounters.Category:D3}",
            Name = type == CategoryType.Revenue ? "General Sales" : "General Expenses",
            Type = type
        };
        data.Categories.Add(category);
        return category;
    }

    private static string? ResolveSupplier(CompanyData data, Receipt receipt, out Supplier? created)
    {
        created = null;
        var name = receipt.Supplier.Trim();
        if (string.IsNullOrEmpty(name)) return null;

        var match = data.Suppliers.FirstOrDefault(
            s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match != null) return match.Id;

        created = new Supplier
        {
            Id = new IdGenerator(data).NextSupplierId(),
            Name = name,
            Notes = "Created from receipt scan"
        };
        data.Suppliers.Add(created);
        return created.Id;
    }

    private static string? ResolveCustomer(CompanyData data, Receipt receipt, out Customer? created)
    {
        created = null;
        var name = receipt.Supplier.Trim();
        if (string.IsNullOrEmpty(name)) return null;

        var match = data.Customers.FirstOrDefault(
            c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match != null) return match.Id;

        created = new Customer
        {
            Id = new IdGenerator(data).NextCustomerId(),
            Name = name,
            Notes = "Created from receipt scan"
        };
        data.Customers.Add(created);
        return created.Id;
    }

    private static Transaction? Find(CompanyData data, Receipt receipt)
    {
        var id = receipt.TransactionId;
        if (string.IsNullOrEmpty(id)) return null;

        return receipt.TransactionType == Revenue
            ? data.Revenues.FirstOrDefault(r => r.Id == id)
            : data.Expenses.FirstOrDefault(e => e.Id == id);
    }

    private static void Add(CompanyData data, Transaction transaction)
    {
        if (transaction is Expense expense) data.Expenses.Add(expense);
        else if (transaction is Revenue revenue) data.Revenues.Add(revenue);
    }

    private static void Remove(CompanyData data, Transaction transaction)
    {
        if (transaction is Expense expense) data.Expenses.Remove(expense);
        else if (transaction is Revenue revenue) data.Revenues.Remove(revenue);
    }

    private static void CopyShared(Transaction from, Transaction to, List<LineItem> lineItems)
    {
        to.Date = from.Date;
        to.AccountantId = from.AccountantId;
        to.Description = from.Description;
        to.LineItems = lineItems;
        to.Quantity = from.Quantity;
        to.UnitPrice = from.UnitPrice;
        to.Amount = from.Amount;
        to.TaxRate = from.TaxRate;
        to.TaxAmount = from.TaxAmount;
        to.ShippingCost = from.ShippingCost;
        to.Discount = from.Discount;
        to.Fee = from.Fee;
        to.Total = from.Total;
        to.ReferenceNumber = from.ReferenceNumber;
        to.PaymentMethod = from.PaymentMethod;
        to.Notes = from.Notes;
        to.CreatedAt = from.CreatedAt;
        to.BankMatched = from.BankMatched;
        to.BankMatchedDate = from.BankMatchedDate;
        to.BankMatchedLineId = from.BankMatchedLineId;
        to.OriginalCurrency = from.OriginalCurrency;
        to.TotalUSD = from.TotalUSD;
        to.UnitPriceUSD = from.UnitPriceUSD;
        to.ShippingCostUSD = from.ShippingCostUSD;
        to.TaxAmountUSD = from.TaxAmountUSD;
        to.DiscountUSD = from.DiscountUSD;
        to.FeeUSD = from.FeeUSD;
        to.IsPendingConversion = from.IsPendingConversion;
    }
}
