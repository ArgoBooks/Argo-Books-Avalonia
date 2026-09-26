namespace ArgoBooks.Core.Enums;

/// <summary>
/// Types of worksheets that can be imported from a spreadsheet.
/// </summary>
public enum SpreadsheetSheetType
{
    Customers,
    Suppliers,
    Products,
    Categories,
    Locations,
    Invoices,
    InvoiceLineItems,
    Expenses,
    Inventory,
    Payments,
    Revenue,
    RentalInventory,
    RentalRecords,
    RecurringInvoices,
    StockAdjustments,
    PurchaseOrders,
    PurchaseOrderLineItems,
    Returns,
    LostDamaged,

    /// <summary>The payroll list. Imports like any other entity sheet.</summary>
    Employees,

    /// <summary>
    /// The payroll register. Export only: an approved run's figures are frozen so that a stub
    /// reprinted next year still matches the one the employee was handed, and letting them be
    /// typed over in a spreadsheet would defeat that. Recognised so that the sheet is skipped
    /// deliberately rather than as an unnamed one.
    /// </summary>
    PayRuns,

    BankStatement,
    Unknown
}

/// <summary>
/// Extension methods for SpreadsheetSheetType.
/// </summary>
public static class SpreadsheetSheetTypeExtensions
{

    /// <summary>
    /// Parses a worksheet name string to a SpreadsheetSheetType enum value.
    /// Handles common aliases (e.g., "Sales" → Revenue, "Purchases" → Expenses).
    /// </summary>
    public static SpreadsheetSheetType ParseSheetName(string sheetName)
    {
        return sheetName.Trim().ToLowerInvariant() switch
        {
            "customers" => SpreadsheetSheetType.Customers,
            "suppliers" => SpreadsheetSheetType.Suppliers,
            "products" => SpreadsheetSheetType.Products,
            "categories" => SpreadsheetSheetType.Categories,
            "locations" => SpreadsheetSheetType.Locations,
            "invoices" => SpreadsheetSheetType.Invoices,
            "invoice line items" or "invoice items" => SpreadsheetSheetType.InvoiceLineItems,
            "expenses" or "purchases" => SpreadsheetSheetType.Expenses,
            "inventory" => SpreadsheetSheetType.Inventory,
            "payments" => SpreadsheetSheetType.Payments,
            "revenue" or "sales" => SpreadsheetSheetType.Revenue,
            "rental inventory" => SpreadsheetSheetType.RentalInventory,
            "rental records" => SpreadsheetSheetType.RentalRecords,
            "recurring invoices" => SpreadsheetSheetType.RecurringInvoices,
            "stock adjustments" => SpreadsheetSheetType.StockAdjustments,
            "purchase orders" => SpreadsheetSheetType.PurchaseOrders,
            "purchase order line items" => SpreadsheetSheetType.PurchaseOrderLineItems,
            "employees" or "staff" => SpreadsheetSheetType.Employees,
            "pay runs" or "payroll" => SpreadsheetSheetType.PayRuns,
            "returns" => SpreadsheetSheetType.Returns,
            "lost damaged" or "lost / damaged" or "lost/damaged" => SpreadsheetSheetType.LostDamaged,
            "bank statement" or "bank" or "bank transactions" => SpreadsheetSheetType.BankStatement,
            _ => SpreadsheetSheetType.Unknown
        };
    }
}
