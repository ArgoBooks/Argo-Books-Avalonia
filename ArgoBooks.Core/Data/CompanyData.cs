using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Insights;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Invoices;
using ArgoBooks.Core.Models.Rentals;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;

// FUTURE MULTI-ACCOUNTANT SUPPORT:
// When multi-accountant support is added, the EventLog will record which accountant
// performed each action via AuditEvent.AccountantId. The sync layer should merge
// event logs from multiple clients, using timestamps and accountant IDs to detect
// and resolve conflicts. Admin permissions can be enforced by checking the accountant's
// role before allowing undo of another accountant's actions.

namespace ArgoBooks.Core.Data;

/// <summary>
/// Main container for all company data.
/// This class holds all data collections that are stored in the .argo file.
/// </summary>
public class CompanyData
{
    /// <summary>
    /// Lock object for thread-safe access to collections.
    /// Callers performing multi-step operations should lock on this object.
    /// </summary>
    [JsonIgnore]
    public object SyncRoot { get; } = new();

    /// <summary>
    /// Company settings (stored as appSettings.json).
    /// </summary>
    [JsonPropertyName("settings")]
    public CompanySettings Settings { get; init; } = new();

    /// <summary>
    /// ID counters for generating sequential IDs.
    /// </summary>
    [JsonPropertyName("idCounters")]
    public IdCounters IdCounters { get; init; } = new();

    #region Entities

    /// <summary>
    /// All customers.
    /// </summary>
    [JsonPropertyName("customers")]
    public List<Customer> Customers { get; init; } = [];

    /// <summary>
    /// All products and services.
    /// </summary>
    [JsonPropertyName("products")]
    public List<Product> Products { get; init; } = [];

    /// <summary>
    /// All suppliers.
    /// </summary>
    [JsonPropertyName("suppliers")]
    public List<Supplier> Suppliers { get; init; } = [];

    /// <summary>
    /// Everyone on the payroll, including those who have left. Employees are archived rather
    /// than removed, because a T4 must still be produceable for someone who left mid-year.
    /// </summary>
    [JsonPropertyName("employees")]
    public List<Models.Payroll.Employee> Employees { get; init; } = [];

    /// <summary>
    /// Every payroll ever run, including voided ones and the reversals that cancelled them.
    /// Nothing is removed: an approved run's figures are what the employee's pay stub says.
    /// </summary>
    [JsonPropertyName("payRuns")]
    public List<Models.Payroll.PayRun> PayRuns { get; init; } = [];

    /// <summary>
    /// All categories (revenue, expense, rental).
    /// </summary>
    [JsonPropertyName("categories")]
    public List<Category> Categories { get; init; } = [];

    /// <summary>
    /// Bank categorization rules for automated bank statement matching. Stored in
    /// <see cref="CompanySettings.BankCategoryRules"/> so they save and cancel with the other
    /// company settings; this pass-through keeps the existing data.BankCategoryRules call sites
    /// working (read, Add, Remove all operate on the same underlying list).
    /// </summary>
    [JsonIgnore]
    public List<Models.BankMatching.BankCategoryRule> BankCategoryRules => Settings.BankCategoryRules;

    /// <summary>
    /// All accountants.
    /// </summary>
    [JsonPropertyName("accountants")]
    public List<Accountant> Accountants { get; init; } = [];

    /// <summary>
    /// All warehouse/storage locations.
    /// </summary>
    [JsonPropertyName("locations")]
    public List<Location> Locations { get; init; } = [];

    #endregion

    #region Transactions

    /// <summary>
    /// All revenue transactions.
    /// </summary>
    [JsonPropertyName("revenues")]
    public List<Revenue> Revenues { get; init; } = [];

    /// <summary>
    /// All expense transactions.
    /// </summary>
    [JsonPropertyName("expenses")]
    public List<Expense> Expenses { get; init; } = [];

    /// <summary>
    /// All invoices.
    /// </summary>
    [JsonPropertyName("invoices")]
    public List<Invoice> Invoices { get; init; } = [];

    /// <summary>
    /// All payments received.
    /// </summary>
    [JsonPropertyName("payments")]
    public List<Payment> Payments { get; init; } = [];

    /// <summary>
    /// Imported bank statements kept for the Bank Matching feature, so match progress
    /// survives reload. Reference data only; never aggregated into financials.
    /// </summary>
    [JsonPropertyName("bankImportSessions")]
    public List<Models.BankMatching.BankImportSession> BankImportSessions { get; init; } = [];

    /// <summary>
    /// All recurring invoice schedules.
    /// </summary>
    [JsonPropertyName("recurringInvoices")]
    public List<RecurringInvoice> RecurringInvoices { get; init; } = [];

    /// <summary>
    /// Schedules that generate expenses or revenue on a cadence.
    /// </summary>
    [JsonPropertyName("recurringTransactions")]
    public List<RecurringTransaction> RecurringTransactions { get; init; } = [];

    #endregion

    #region Inventory

    /// <summary>
    /// All inventory items (stock levels).
    /// </summary>
    [JsonPropertyName("inventory")]
    public List<InventoryItem> Inventory { get; init; } = [];

    /// <summary>
    /// All stock adjustments.
    /// </summary>
    [JsonPropertyName("stockAdjustments")]
    public List<StockAdjustment> StockAdjustments { get; init; } = [];

    /// <summary>
    /// All stock transfers.
    /// </summary>
    [JsonPropertyName("stockTransfers")]
    public List<StockTransfer> StockTransfers { get; init; } = [];

    /// <summary>
    /// All purchase orders.
    /// </summary>
    [JsonPropertyName("purchaseOrders")]
    public List<PurchaseOrder> PurchaseOrders { get; init; } = [];

    #endregion

    #region Rentals

    /// <summary>
    /// All rental inventory items.
    /// </summary>
    [JsonPropertyName("rentalInventory")]
    public List<RentalItem> RentalInventory { get; init; } = [];

    /// <summary>
    /// All rental records.
    /// </summary>
    [JsonPropertyName("rentals")]
    public List<RentalRecord> Rentals { get; init; } = [];

    #endregion

    #region Tracking

    /// <summary>
    /// All return records.
    /// </summary>
    [JsonPropertyName("returns")]
    public List<Return> Returns { get; init; } = [];

    /// <summary>
    /// All lost/damaged records.
    /// </summary>
    [JsonPropertyName("lostDamaged")]
    public List<LostDamaged> LostDamaged { get; init; } = [];

    /// <summary>
    /// All receipts.
    /// </summary>
    [JsonPropertyName("receipts")]
    public List<Receipt> Receipts { get; init; } = [];

    /// <summary>
    /// Phones paired to this company for mobile sync.
    /// </summary>
    [JsonPropertyName("pairedDevices")]
    public List<PairedDevice> PairedDevices { get; init; } = [];

    /// <summary>
    /// ScanUids of phone-captured transactions already ingested via <c>CaptureIngestService</c>. Lets
    /// mobile-sync de-dupe a re-delivered capture restart-safe (not just for the current app session).
    /// Bounded to the most recently ingested 1000 entries.
    /// </summary>
    [JsonPropertyName("ingestedScanUids")]
    public List<string> IngestedScanUids { get; set; } = [];

    #endregion

    #region Invoice Templates

    /// <summary>
    /// All custom invoice templates for email sending.
    /// </summary>
    [JsonPropertyName("invoiceTemplates")]
    public List<InvoiceTemplate> InvoiceTemplates { get; init; } = [];

    #endregion

    #region Pending Conversions

    /// <summary>
    /// Transactions saved offline that are awaiting USD conversion.
    /// Persisted in the .argo file as a secondary backup (primary backup is in app data directory).
    /// </summary>
    [JsonPropertyName("pendingConversions")]
    public List<PendingConversion> PendingConversions { get; init; } = [];

    #endregion

    #region Insights

    /// <summary>
    /// Historical forecast records for accuracy tracking.
    /// </summary>
    [JsonPropertyName("forecastRecords")]
    public List<ForecastAccuracyRecord> ForecastRecords { get; init; } = [];

    #endregion

    #region Version History

    /// <summary>
    /// Audit event log for version history tracking.
    /// Records all entity changes for audit trail and undo support.
    /// </summary>
    [JsonPropertyName("eventLog")]
    public List<AuditEvent> EventLog { get; init; } = [];

    #endregion

    #region State Tracking

    /// <summary>
    /// Gets or sets whether there are unsaved changes.
    /// </summary>
    [JsonIgnore]
    public bool ChangesMade
    {
        get => Settings.ChangesMade;
        set => Settings.ChangesMade = value;
    }

    /// <summary>
    /// Counts edits, see <see cref="CompanySettings.ChangeCount"/>.
    /// </summary>
    [JsonIgnore]
    public long ChangeCount => Settings.ChangeCount;

    #endregion

    #region Helper Methods

    /// <summary>
    /// Gets the earliest date across all dated collections (revenues, expenses, payments,
    /// invoices, stock adjustments, purchase orders, and rental records).
    /// Returns DateTime.Today if no dated records exist.
    /// </summary>
    public DateTime GetEarliestDate()
    {
        // Use year 1900 as minimum to avoid DateTime arithmetic overflow from default/unset dates
        var minValid = new DateTime(1900, 1, 1);
        DateTime MinDate(IEnumerable<DateTime> source) => source.Where(d => d >= minValid).DefaultIfEmpty(DateTime.Today).Min();

        var dates = new List<DateTime>();
        if (Revenues.Count > 0) dates.Add(MinDate(Revenues.Select(r => r.Date)));
        if (Expenses.Count > 0) dates.Add(MinDate(Expenses.Select(e => e.Date)));
        if (Payments.Count > 0) dates.Add(MinDate(Payments.Select(p => p.Date)));
        if (Invoices.Count > 0) dates.Add(MinDate(Invoices.Select(i => i.IssueDate)));
        if (StockAdjustments.Count > 0) dates.Add(MinDate(StockAdjustments.Select(s => s.Timestamp)));
        if (PurchaseOrders.Count > 0) dates.Add(MinDate(PurchaseOrders.Select(p => p.OrderDate)));
        if (Rentals.Count > 0) dates.Add(MinDate(Rentals.Select(r => r.StartDate)));

        return dates.Count > 0 ? dates.Min() : DateTime.Today;
    }

    #region Cached Lookups

    [JsonIgnore] private readonly LookupCache<Customer> _customerLookup = new(c => c.Id);
    [JsonIgnore] private readonly LookupCache<Product> _productLookup = new(p => p.Id);
    [JsonIgnore] private readonly LookupCache<Supplier> _supplierLookup = new(s => s.Id);
    [JsonIgnore] private readonly LookupCache<Category> _categoryLookup = new(c => c.Id);
    [JsonIgnore] private readonly LookupCache<Accountant> _accountantLookup = new(a => a.Id);
    [JsonIgnore] private readonly LookupCache<Location> _locationLookup = new(l => l.Id);
    [JsonIgnore] private readonly LookupCache<Invoice> _invoiceLookup = new(i => i.Id);
    [JsonIgnore] private readonly LookupCache<Receipt> _receiptLookup = new(r => r.Id);

    /// <summary>
    /// An Id lookup over one collection, rebuilt when the collection changes. Records are only
    /// added at the end or removed, so any change moves the count or the last record; checking
    /// the count alone missed a delete followed by an add.
    /// </summary>
    private sealed class LookupCache<T>(Func<T, string> keySelector) where T : class
    {
        private Dictionary<string, T>? _lookup;
        private int _count;
        private T? _last;

        public T? Get(List<T> list, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var last = list.Count > 0 ? list[^1] : null;
            if (_lookup == null || _count != list.Count || !ReferenceEquals(_last, last))
            {
                _lookup = BuildLookup(list, keySelector);
                _count = list.Count;
                _last = last;
            }
            return _lookup.GetValueOrDefault(id);
        }

        public void Invalidate() => _lookup = null;
    }

    private static Dictionary<string, T> BuildLookup<T>(List<T> list, Func<T, string> keySelector)
    {
        var dict = new Dictionary<string, T>(list.Count, StringComparer.Ordinal);
        foreach (var item in list)
        {
            var key = keySelector(item);
            if (!string.IsNullOrEmpty(key))
                dict[key] = item;
        }
        return dict;
    }

    /// <summary>
    /// Invalidates all cached lookup dictionaries. Call after changing a record's Id, which
    /// leaves the collection itself as it was.
    /// </summary>
    public void InvalidateLookupCaches()
    {
        _customerLookup.Invalidate();
        _productLookup.Invalidate();
        _supplierLookup.Invalidate();
        _categoryLookup.Invalidate();
        _accountantLookup.Invalidate();
        _locationLookup.Invalidate();
        _invoiceLookup.Invalidate();
        _receiptLookup.Invalidate();
    }

    #endregion

    /// <summary>
    /// Gets a customer by ID.
    /// </summary>
    public Customer? GetCustomer(string id) => _customerLookup.Get(Customers, id);

    /// <summary>
    /// Gets a product by ID.
    /// </summary>
    public Product? GetProduct(string id) => _productLookup.Get(Products, id);

    /// <summary>
    /// Gets a supplier by ID.
    /// </summary>
    public Supplier? GetSupplier(string id) => _supplierLookup.Get(Suppliers, id);

    /// <summary>
    /// Gets a category by ID.
    /// </summary>
    public Category? GetCategory(string id) => _categoryLookup.Get(Categories, id);

    /// <summary>
    /// Gets an accountant by ID.
    /// </summary>
    public Accountant? GetAccountant(string id) => _accountantLookup.Get(Accountants, id);

    /// <summary>
    /// Gets a location by ID.
    /// </summary>
    public Location? GetLocation(string id) => _locationLookup.Get(Locations, id);

    /// <summary>
    /// Gets an invoice by ID.
    /// </summary>
    public Invoice? GetInvoice(string id) => _invoiceLookup.Get(Invoices, id);

    /// <summary>
    /// Gets a receipt by ID.
    /// </summary>
    public Receipt? GetReceipt(string id) => _receiptLookup.Get(Receipts, id);

    /// <summary>
    /// Gets an invoice template by ID.
    /// </summary>
    public InvoiceTemplate? GetInvoiceTemplate(string id) =>
        InvoiceTemplates.FirstOrDefault(t => t.Id == id);

    /// <summary>
    /// Marks the data as modified.
    /// </summary>
    public void MarkAsModified()
    {
        Settings.ChangesMade = true;
    }

    /// <summary>
    /// Marks the data as saved (not modified).
    /// </summary>
    public void MarkAsSaved()
    {
        Settings.ChangesMade = false;
    }

    /// <summary>
    /// Marks the data as saved, unless something changed after <paramref name="changeCount"/>
    /// was read from <see cref="ChangeCount"/>, since that edit may not be in the file.
    /// </summary>
    public void MarkAsSaved(long changeCount)
    {
        if (Settings.ChangeCount == changeCount)
            Settings.ChangesMade = false;
    }

    #endregion
}

/// <summary>
/// Counters for generating sequential IDs.
/// </summary>
public class IdCounters
{
    [JsonPropertyName("customer")]
    public int Customer { get; set; }

    [JsonPropertyName("product")]
    public int Product { get; set; }

    [JsonPropertyName("supplier")]
    public int Supplier { get; set; }

    [JsonPropertyName("category")]
    public int Category { get; set; }

    [JsonPropertyName("accountant")]
    public int Accountant { get; set; }

    [JsonPropertyName("location")]
    public int Location { get; set; }

    [JsonPropertyName("revenue")]
    public int Revenue { get; set; }

    [JsonPropertyName("expense")]
    public int Expense { get; set; }

    [JsonPropertyName("invoice")]
    public int Invoice { get; set; }

    [JsonPropertyName("payment")]
    public int Payment { get; set; }

    [JsonPropertyName("recurringInvoice")]
    public int RecurringInvoice { get; set; }

    [JsonPropertyName("recurringTransaction")]
    public int RecurringTransaction { get; set; }

    [JsonPropertyName("inventoryItem")]
    public int InventoryItem { get; set; }

    [JsonPropertyName("stockAdjustment")]
    public int StockAdjustment { get; set; }

    [JsonPropertyName("stockTransfer")]
    public int StockTransfer { get; set; }

    [JsonPropertyName("purchaseOrder")]
    public int PurchaseOrder { get; set; }

    [JsonPropertyName("rentalItem")]
    public int RentalItem { get; set; }

    [JsonPropertyName("rental")]
    public int Rental { get; set; }

    [JsonPropertyName("return")]
    public int Return { get; set; }

    [JsonPropertyName("lostDamaged")]
    public int LostDamaged { get; set; }

    [JsonPropertyName("receipt")]
    public int Receipt { get; set; }

    [JsonPropertyName("invoiceTemplate")]
    public int InvoiceTemplate { get; set; }

    [JsonPropertyName("pairedDevice")]
    public int PairedDevice { get; set; }
}
