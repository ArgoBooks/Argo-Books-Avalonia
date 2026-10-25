using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Returning some or all of a sale or purchase: which lines are left to return, what a choice of them
/// refunds, and recording or removing a return with its refund and the stock it moves
/// (docs/Calculations.md §10, Returns and Losses).
/// </summary>
public static class TransactionReturns
{
    /// <summary>A line of the transaction, with how much of it has come back already.</summary>
    public sealed record Line(int Index, LineItem Item, decimal Sold, decimal AlreadyReturned)
    {
        public decimal Remaining => Math.Max(0m, Sold - AlreadyReturned);
    }

    /// <summary>What a return refunds, each part in the transaction's own currency.</summary>
    public sealed record Amounts(decimal Items, decimal Discount, decimal Tax, decimal Shipping, decimal Fee)
    {
        public decimal Total => Items - Discount + Tax + Shipping + Fee;
    }

    /// <summary>What the user chose to return.</summary>
    public sealed record Request(
        IReadOnlyDictionary<int, decimal> Quantities,
        bool Shipping,
        bool Fee,
        decimal RestockingFee,
        bool MoveStock,
        string Reason,
        string Notes,
        DateTime Date);

    /// <summary>A recorded or removed return, with everything it changed, so undo and redo can swap it in and out.</summary>
    public sealed class Change
    {
        internal Change(Return record, ReturnRefunds.Refund? refund)
        {
            Record = record;
            Refund = refund;
        }

        public Return Record { get; }
        public ReturnRefunds.Refund? Refund { get; }

        // The stock the return moved, found by its adjustments each time it is taken out, so a return
        // recorded in this session and one from an earlier session come out and go back the same way.
        private List<(InventoryItem, StockAdjustment)>? _removedStock;

        /// <summary>Takes the return, its refund and its stock movement out of the books.</summary>
        public void TakeOut(CompanyData data)
        {
            data.Returns.RemoveRecord(Record);
            if (Refund != null) ReturnRefunds.Remove(data, Refund);
            _removedStock = InventoryStockService.RemoveReturn(data, Record.Id);
        }

        /// <summary>Puts the return, its refund and its stock movement back.</summary>
        public void PutBack(CompanyData data)
        {
            data.Returns.RestoreRecord(Record);
            if (Refund != null) ReturnRefunds.Restore(data, Record, Refund);
            if (_removedStock != null)
                InventoryStockService.RestoreReturn(data, _removedStock);
            _removedStock = null;
        }
    }

    /// <summary>
    /// The transaction's lines with what has come back already. A transaction with no lines counts as
    /// one line of its whole amount. A return not made by <see cref="Record"/>, such as one recorded
    /// before returns could be partial or a refund imported from Stripe, took everything.
    /// </summary>
    public static List<Line> LinesOf(CompanyData data, Transaction transaction)
    {
        var items = ItemsOf(transaction);
        var returned = new decimal[items.Count];
        foreach (var record in data.Returns.Where(r => r.OriginalTransactionId == transaction.Id && r.Status != ReturnStatus.Rejected))
        {
            if (!IsByLine(record))
            {
                for (var i = 0; i < items.Count; i++)
                    returned[i] = items[i].Quantity;
                continue;
            }

            foreach (var item in record.Items)
            {
                if (item.LineIndex is { } index && index >= 0 && index < items.Count)
                    returned[index] += item.Quantity;
            }
        }

        return items.Select((item, i) => new Line(i, item, item.Quantity, Math.Min(returned[i], item.Quantity))).ToList();
    }

    /// <summary>Whether the return says which lines came back, as every return <see cref="Record"/> makes does.</summary>
    public static bool IsByLine(Return record) =>
        record.RefundTax != null && record.Items.All(i => i.LineIndex != null);

    /// <summary>Whether anything of the transaction has come back, and whether all of it has.</summary>
    public static (bool Any, bool All) ReturnedState(CompanyData data, Transaction transaction)
    {
        if (!data.Returns.Any(r => r.OriginalTransactionId == transaction.Id && r.Status != ReturnStatus.Rejected))
            return (false, false);
        var lines = LinesOf(data, transaction);
        return (true, lines.All(l => l.Remaining == 0));
    }

    /// <summary>For each of the transactions that has a return, whether all of it has come back.</summary>
    public static Dictionary<string, bool> ReturnedById(CompanyData data, IEnumerable<Transaction> transactions)
    {
        var withReturns = data.Returns
            .Where(r => r.Status != ReturnStatus.Rejected)
            .Select(r => r.OriginalTransactionId)
            .ToHashSet();
        return transactions
            .Where(t => withReturns.Contains(t.Id))
            .ToDictionary(t => t.Id, t => LinesOf(data, t).All(l => l.Remaining == 0));
    }

    /// <summary>The most recent return of the transaction, which the row's undo takes back.</summary>
    public static Return? LatestFor(CompanyData data, string transactionId) =>
        data.Returns
            .Where(r => r.OriginalTransactionId == transactionId)
            .OrderBy(r => r.CreatedAt)
            .LastOrDefault();

    /// <summary>Whether an earlier return of the transaction already refunded its shipping, or its fee.</summary>
    public static (bool Shipping, bool Fee) AlreadyRefunded(CompanyData data, string transactionId)
    {
        var earlier = data.Returns.Where(r => r.OriginalTransactionId == transactionId).ToList();
        return (earlier.Any(r => r.IncludesShipping), earlier.Any(r => r.IncludesFee));
    }

    /// <summary>
    /// What returning <paramref name="quantities"/> of each line refunds: the lines' share of the items,
    /// less the same share of the transaction's discount, plus the same share of its tax, plus the
    /// shipping and fee when chosen. Returning everything refunds the items after the discount, plus
    /// the tax, as marking a whole transaction returned always has.
    /// </summary>
    public static Amounts AmountsFor(Transaction transaction, IReadOnlyDictionary<int, decimal> quantities, bool shipping, bool fee)
    {
        var items = ItemsOf(transaction);
        var allItems = items.Sum(li => li.Subtotal);
        var returnedItems = items.Select((li, i) =>
                quantities.TryGetValue(i, out var q) && li.Quantity != 0 ? li.Subtotal * Math.Min(q, li.Quantity) / li.Quantity : 0m)
            .Sum();
        var share = allItems > 0 ? returnedItems / allItems : 0m;

        return new Amounts(
            Round(returnedItems),
            Round(transaction.Discount * share),
            Round(transaction.TaxAmount * share),
            shipping ? transaction.ShippingCost : 0m,
            fee ? transaction.Fee : 0m);
    }

    /// <summary>
    /// Records the return of what <paramref name="request"/> chose, with its refund and the stock it moves,
    /// and gives back what changed for undo. The refund follows <see cref="ReturnRefunds.Record"/>.
    /// </summary>
    public static Change Record(CompanyData data, Transaction transaction, Request request, string? processedBy = null)
    {
        var isPurchase = transaction is Expense;
        var lines = LinesOf(data, transaction);
        var chosen = request.Quantities
            .Where(kv => kv.Value > 0 && kv.Key >= 0 && kv.Key < lines.Count)
            .Select(kv => (Line: lines[kv.Key], Quantity: Math.Min(kv.Value, lines[kv.Key].Remaining)))
            .Where(c => c.Quantity > 0)
            .OrderBy(c => c.Line.Index)
            .ToList();

        var amounts = AmountsFor(transaction, chosen.ToDictionary(c => c.Line.Index, c => c.Quantity), request.Shipping, request.Fee);
        var record = new Return
        {
            Id = new IdGenerator(data).NextReturnId(),
            OriginalTransactionId = transaction.Id,
            ReturnType = isPurchase ? "Expense" : "Customer",
            SupplierId = (transaction as Expense)?.SupplierId ?? "",
            CustomerId = (transaction as Revenue)?.CustomerId ?? "",
            ReturnDate = request.Date,
            Items = chosen.Select(c => new ReturnItem
            {
                ProductId = c.Line.Item.ProductId ?? "",
                Quantity = c.Quantity,
                Reason = request.Reason,
                LineIndex = c.Line.Index
            }).ToList(),
            RefundAmount = amounts.Total,
            RefundTax = amounts.Tax,
            RestockingFee = Math.Min(Math.Max(0m, request.RestockingFee), amounts.Total),
            IncludesShipping = request.Shipping,
            IncludesFee = request.Fee,
            Status = ReturnStatus.Completed,
            Notes = request.Notes,
            ProcessedBy = processedBy ?? transaction.AccountantId ?? "",
            CreatedAt = DateTime.UtcNow
        };
        data.Returns.Add(record);

        var stockLines = chosen.Where(c => InventoryStockService.TracksStock(data, c.Line.Item)).ToList();
        if (request.MoveStock && stockLines.Count > 0)
        {
            InventoryStockService.ApplyReturn(data, stockLines.Select(c => (c.Line.Item, c.Quantity)), isPurchase, record.Id);
            record.Restocked = true;
            // Goods back on the shelf were not sold after all, so what they cost comes off cost of goods sold.
            if (!isPurchase)
                record.CostOfGoodsReturnedUSD = stockLines.Sum(c =>
                    c.Line.Item.Quantity != 0 ? (c.Line.Item.CostOfGoodsUSD ?? 0m) * c.Quantity / c.Line.Item.Quantity : 0m);
        }

        var refund = ReturnRefunds.Record(data, record);
        return new Change(record, refund);
    }

    /// <summary>Takes a return recorded earlier out of the books with its refund and stock, and gives back what changed for redo.</summary>
    public static Change Remove(CompanyData data, Return record)
    {
        var change = new Change(record, ReturnRefunds.Find(data, record));
        change.TakeOut(data);
        return change;
    }

    /// <summary>
    /// Takes out every return of a transaction being deleted, newest first, so its refunds and the
    /// stock they moved go with it. <see cref="PutBackAll"/> puts them back when the delete is undone.
    /// </summary>
    public static List<Change> RemoveAll(CompanyData data, string transactionId) =>
        data.Returns
            .Where(r => r.OriginalTransactionId == transactionId)
            .OrderByDescending(r => r.CreatedAt)
            .ToList()
            .Select(r => Remove(data, r))
            .ToList();

    /// <summary>Puts back what <see cref="RemoveAll"/> took out, oldest first.</summary>
    public static void PutBackAll(CompanyData data, List<Change> changes)
    {
        for (var i = changes.Count - 1; i >= 0; i--)
            changes[i].PutBack(data);
    }

    /// <summary>The transaction's lines, or one line of its whole amount when it has none.</summary>
    internal static List<LineItem> ItemsOf(Transaction transaction)
    {
        if (transaction.LineItems.Count > 0)
            return transaction.LineItems;

        var amount = transaction is Revenue sale ? ReturnRefunds.SaleItems(sale) : transaction.Amount;
        var quantity = transaction.Quantity > 0 ? transaction.Quantity : 1m;
        return [new LineItem { Description = transaction.Description, Quantity = quantity, UnitPrice = amount / quantity }];
    }

    private static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
