using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services.Integrations;

/// <summary>
/// Records everything one Argo Books API import created, plus the id-counter
/// state before and after, so the UI can offer a single undo/redo for the whole
/// import. Mirrors <see cref="StripeImportCreation"/>.
///
/// One difference from the Stripe version matters: this import also claimed
/// objects on the server. <see cref="BatchId"/> carries that claim so the caller
/// can release it when the merchant undoes, otherwise the developer's queue would
/// keep reporting data as imported that is no longer in anyone's books.
/// </summary>
public class ArgoApiImportCreation
{
    public List<Revenue> Revenues { get; } = [];
    public List<Expense> Expenses { get; } = [];
    public List<object> Entities { get; } = []; // Customer / Supplier / Product / Category
    public List<Return> Returns { get; } = [];

    /// <summary>The server-side batch this import claimed, once the claim succeeded.</summary>
    public string? BatchId { get; set; }

    /// <summary>Every API object id this import took, in claim order.</summary>
    public List<string> ClaimedObjectIds { get; } = [];

    /// <summary>API id to the local id it became, sent to the server so developers can trace it.</summary>
    public Dictionary<string, string> LocalRefs { get; } = new(StringComparer.Ordinal);

    public DateTime? PreviousSyncTime { get; set; }
    public DateTime? NewSyncTime { get; set; }

    public CounterSnapshot Pre { get; set; }
    public CounterSnapshot Post { get; set; }

    public int RevenuesCreated => Revenues.Count;
    public int ExpensesCreated => Expenses.Count;

    /// <summary>True when the import actually created something, so an undo is worth recording.</summary>
    public bool AnyCreated =>
        Revenues.Count > 0 || Expenses.Count > 0 || Entities.Count > 0 || Returns.Count > 0;

    public void Undo(CompanyData data)
    {
        foreach (var r in Revenues) data.Revenues.Remove(r);
        foreach (var e in Expenses) data.Expenses.Remove(e);
        foreach (var ent in Entities)
        {
            if (ent is Customer c) data.Customers.Remove(c);
            else if (ent is Supplier s) data.Suppliers.Remove(s);
            else if (ent is Product p) data.Products.Remove(p);
            else if (ent is Category cat) data.Categories.Remove(cat);
        }
        foreach (var ret in Returns) data.Returns.Remove(ret);

        // The rows are gone, so their queued currency conversions have nothing left
        // to convert. Nothing else prunes those: the reconcile pass only drops an
        // entry whose record exists and is already converted, so one whose record has
        // been removed would be retried on every pass forever.
        ForgetPendingConversions(data);
        var api = data.Settings.Integrations.ArgoApi;
        if (BatchId != null) api.ImportedBatches.Remove(BatchId);
        api.LastSyncTime = PreviousSyncTime;

        Pre.RewindTo(data.IdCounters, Post);
        data.MarkAsModified();
    }

    public void Redo(CompanyData data)
    {
        // Entities first: revenues and expenses reference them, and re-adding in
        // the other order would briefly leave dangling ids for anything watching.
        foreach (var ent in Entities)
        {
            if (ent is Customer c && !data.Customers.Contains(c)) data.Customers.Add(c);
            else if (ent is Supplier s && !data.Suppliers.Contains(s)) data.Suppliers.Add(s);
            else if (ent is Product p && !data.Products.Contains(p)) data.Products.Add(p);
            else if (ent is Category cat && !data.Categories.Contains(cat)) data.Categories.Add(cat);
        }
        foreach (var r in Revenues) if (!data.Revenues.Contains(r)) data.Revenues.Add(r);
        foreach (var e in Expenses) if (!data.Expenses.Contains(e)) data.Expenses.Add(e);
        foreach (var ret in Returns) if (!data.Returns.Contains(ret)) data.Returns.Add(ret);

        var api = data.Settings.Integrations.ArgoApi;
        if (BatchId != null && !api.ImportedBatches.Contains(BatchId)) api.ImportedBatches.Add(BatchId);
        api.LastSyncTime = NewSyncTime;

        Post.RaiseTo(data.IdCounters);
        data.MarkAsModified();
    }

    /// <summary>Snapshot of the id counters this import can bump, so undo/redo can put them back.</summary>
    public readonly record struct CounterSnapshot(
        int Revenue, int Expense, int Customer, int Supplier, int Product, int Category, int Return)
    {
        public static CounterSnapshot From(IdCounters c) =>
            new(c.Revenue, c.Expense, c.Customer, c.Supplier, c.Product, c.Category, c.Return);

        /// <summary>
        /// Back to this snapshot, but only for a counter still where the import left it. One that
        /// has moved on issued an id to a record the undo does not remove, and lowering it would
        /// issue that id again.
        /// </summary>
        public void RewindTo(IdCounters c, CounterSnapshot post)
        {
            if (c.Revenue == post.Revenue) c.Revenue = Revenue;
            if (c.Expense == post.Expense) c.Expense = Expense;
            if (c.Customer == post.Customer) c.Customer = Customer;
            if (c.Supplier == post.Supplier) c.Supplier = Supplier;
            if (c.Product == post.Product) c.Product = Product;
            if (c.Category == post.Category) c.Category = Category;
            if (c.Return == post.Return) c.Return = Return;
        }

        /// <summary>Up to this snapshot, never down: an id issued while the import was undone stays issued.</summary>
        public void RaiseTo(IdCounters c)
        {
            c.Revenue = Math.Max(c.Revenue, Revenue);
            c.Expense = Math.Max(c.Expense, Expense);
            c.Customer = Math.Max(c.Customer, Customer);
            c.Supplier = Math.Max(c.Supplier, Supplier);
            c.Product = Math.Max(c.Product, Product);
            c.Category = Math.Max(c.Category, Category);
            c.Return = Math.Max(c.Return, Return);
        }
    }

    /// <summary>
    /// Withdraw the currency-conversion entries this import queued, from the company
    /// file and from the shared queue behind it. Both, because the service merges its
    /// own copy back into whichever company is open, so clearing one alone lets the
    /// other put it straight back.
    /// </summary>
    private void ForgetPendingConversions(CompanyData data)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in Revenues) ids.Add(r.Id);
        foreach (var e in Expenses) ids.Add(e.Id);
        if (ids.Count == 0) return;

        data.PendingConversions.RemoveAll(p => ids.Contains(p.TransactionId));

        // Fire and forget: it only writes a cache file, and failing to prune it must
        // never block an undo the user has already seen happen.
        if (PendingConversionService.Instance is { } svc)
            _ = svc.ForgetAsync(ids);
    }
}
