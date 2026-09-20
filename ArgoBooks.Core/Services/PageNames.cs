namespace ArgoBooks.Core.Services;

/// <summary>
/// Constants for page names used in navigation.
/// </summary>
public static class PageNames
{
    public const string Dashboard = "Dashboard";
    public const string Analytics = "Analytics";
    public const string Revenue = "Revenue";
    public const string Expenses = "Expenses";
    public const string Quotes = "Quotes";
    public const string Invoices = "Invoices";
    public const string BankMatching = "BankMatching";
    public const string Products = "Products";
    public const string StockLevels = "StockLevels";
    public const string Locations = "Locations";
    public const string StockAdjustments = "StockAdjustments";
    public const string PurchaseOrders = "PurchaseOrders";
    public const string Categories = "Categories";

    // The sidebar opens the categories and products pages on a chosen tab, and registers those
    // under their own names. A page that only matches the bare name above never sees the
    // navigation the user actually performs.
    public const string ExpenseCategories = "ExpenseCategories";
    public const string RevenueCategories = "RevenueCategories";
    public const string ExpenseProducts = "ExpenseProducts";
    public const string RevenueProducts = "RevenueProducts";
    public const string Customers = "Customers";
    public const string Suppliers = "Suppliers";
    public const string RentalInventory = "RentalInventory";
    public const string RentalRecords = "RentalRecords";
    public const string Returns = "Returns";
    public const string LostDamaged = "LostDamaged";
    public const string Receipts = "Receipts";
}
