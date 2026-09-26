using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services.Integrations;

/// <summary>
/// Records everything one integration import created, plus the id-counter state before and after,
/// so the UI can offer a single undo/redo for the whole import. Each integration puts back the
/// state it keeps about its own syncs in <see cref="UndoIntegrationState"/> and
/// <see cref="RedoIntegrationState"/>.
/// </summary>
public abstract class IntegrationImportCreation
{
    public List<Revenue> Revenues { get; } = [];
    public List<Expense> Expenses { get; } = [];
    public List<object> Entities { get; } = []; // Customer / Supplier / Product / Category
    public List<Return> Returns { get; } = [];
    public List<StockChange> StockChanges { get; private set; } = [];

    public DateTime? PreviousSyncTime { get; set; }
    public DateTime? NewSyncTime { get; set; }

    public IdCounters Pre { get; set; } = new();
    public IdCounters Post { get; set; } = new();

    public int RevenuesCreated => Revenues.Count;
    public int ExpensesCreated => Expenses.Count;

    /// <summary>True when the import actually created something, so an undo is worth recording.</summary>
    public virtual bool AnyCreated =>
        Revenues.Count > 0 || Expenses.Count > 0 || Entities.Count > 0 || Returns.Count > 0;

    /// <summary>
    /// Moves stock for the imported purchases and sales and records what sold stock cost, as saving
    /// them in the app would. Purchases go first, so a sale pushed with the stock it sold finds it.
    /// An expense with no line items, such as a Stripe fee or refund, moves nothing.
    /// </summary>
    public void ApplyStock(CompanyData data)
    {
        StockChanges = [];
        foreach (var e in Expenses)
            StockChanges.AddRange(InventoryStockService.Apply(data, e.LineItems, e, isPurchase: true));
        foreach (var r in Revenues)
            StockChanges.AddRange(InventoryStockService.Apply(data, r.LineItems, r, isPurchase: false));
    }

    public void Undo(CompanyData data)
    {
        InventoryStockService.Revert(data, StockChanges);
        foreach (var r in Revenues) data.Revenues.RemoveRecord(r);
        foreach (var e in Expenses) data.Expenses.RemoveRecord(e);
        foreach (var ent in Entities)
        {
            if (ent is Customer c) data.Customers.RemoveRecord(c);
            else if (ent is Supplier s) data.Suppliers.RemoveRecord(s);
            else if (ent is Product p) data.Products.RemoveRecord(p);
            else if (ent is Category cat) data.Categories.RemoveRecord(cat);
        }
        foreach (var ret in Returns) data.Returns.RemoveRecord(ret);

        // The rows are gone, so their queued currency conversions have nothing left
        // to convert. Nothing else prunes those: the reconcile pass only drops an
        // entry whose record exists and is already converted, so one whose record has
        // been removed would be retried on every pass forever.
        ForgetPendingConversions(data);
        UndoIntegrationState(data);

        data.IdCounters.RewindTo(Pre, Post);
        data.MarkAsModified();
    }

    public void Redo(CompanyData data)
    {
        // Entities first: revenues and expenses reference them, and re-adding in
        // the other order would briefly leave dangling ids for anything watching.
        foreach (var ent in Entities)
        {
            if (ent is Customer c) data.Customers.RestoreRecord(c);
            else if (ent is Supplier s) data.Suppliers.RestoreRecord(s);
            else if (ent is Product p) data.Products.RestoreRecord(p);
            else if (ent is Category cat) data.Categories.RestoreRecord(cat);
        }
        foreach (var r in Revenues) data.Revenues.RestoreRecord(r);
        foreach (var e in Expenses) data.Expenses.RestoreRecord(e);

        // A row converted before the undo already has its USD figure, so only still-pending rows requeue.
        foreach (var r in Revenues) UsdConversion.Requeue(data, r);
        foreach (var e in Expenses) UsdConversion.Requeue(data, e);
        ApplyStock(data);
        foreach (var ret in Returns) data.Returns.RestoreRecord(ret);

        RedoIntegrationState(data);

        data.IdCounters.RaiseTo(Post);
        data.MarkAsModified();
    }

    /// <summary>Puts the integration's own sync state back to how it was before the import.</summary>
    protected abstract void UndoIntegrationState(CompanyData data);

    /// <summary>Puts the integration's own sync state back to how the import left it.</summary>
    protected abstract void RedoIntegrationState(CompanyData data);

    /// <summary>
    /// Withdraw the currency-conversion entries this import queued, from the company
    /// file and from the shared queue behind it. Both, because the service merges its
    /// own copy back into whichever company is open, so clearing one alone lets the
    /// other put it straight back.
    /// </summary>
    private void ForgetPendingConversions(CompanyData data)
    {
        var keys = Revenues.Select(UsdConversion.KeyOf).Concat(Expenses.Select(UsdConversion.KeyOf)).ToList();
        if (keys.Count == 0) return;

        UsdConversion.Restore(data, keys, []);
    }
}
