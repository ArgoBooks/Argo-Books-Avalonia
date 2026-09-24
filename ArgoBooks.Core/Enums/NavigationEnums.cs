namespace ArgoBooks.Core.Enums;

/// <summary>
/// Quick action names triggered from the command palette / quick actions panel.
/// </summary>
public enum QuickActionName
{
    OpenAddModal,
    OpenSettings,
    OpenHelp,
    OpenExport,
    OpenImport,
    OpenScanModal,
    OpenBankImport,
    OpenEditCompany,
    OpenCheckForUpdates
}

/// <summary>
/// Extension methods for QuickActionName.
/// </summary>
public static class QuickActionNameExtensions
{
    /// <summary>
    /// Parses a quick action name string to a QuickActionName enum value.
    /// </summary>
    public static QuickActionName? ParseQuickActionName(string? name)
    {
        return name switch
        {
            "OpenAddModal" => QuickActionName.OpenAddModal,
            "OpenSettings" => QuickActionName.OpenSettings,
            "OpenHelp" => QuickActionName.OpenHelp,
            "OpenExport" => QuickActionName.OpenExport,
            "OpenImport" => QuickActionName.OpenImport,
            "OpenScanModal" => QuickActionName.OpenScanModal,
            "OpenBankImport" => QuickActionName.OpenBankImport,
            "OpenEditCompany" => QuickActionName.OpenEditCompany,
            "OpenCheckForUpdates" => QuickActionName.OpenCheckForUpdates,
            _ => null
        };
    }
}

/// <summary>
/// Navigation targets for quick action add-modal dispatching.
/// </summary>
public enum NavigationTarget
{
    RentalInventory,
    RentalRecords,
    Customers,
    Products,
    Suppliers,
    Invoices,
    Quotes,
    Expenses,
    Revenue,
    Payments,
    Categories,
    StockLevels,
    PurchaseOrders,
    Locations,
    Adjustments
}

/// <summary>
/// Extension methods for NavigationTarget.
/// </summary>
public static class NavigationTargetExtensions
{
    /// <summary>
    /// Parses a navigation target string to a NavigationTarget enum value.
    /// </summary>
    public static NavigationTarget? ParseNavigationTarget(string? name)
    {
        return name switch
        {
            "RentalInventory" => NavigationTarget.RentalInventory,
            "RentalRecords" => NavigationTarget.RentalRecords,
            "Customers" => NavigationTarget.Customers,
            "Products" => NavigationTarget.Products,
            "Suppliers" => NavigationTarget.Suppliers,
            "Invoices" => NavigationTarget.Invoices,
            "Quotes" => NavigationTarget.Quotes,
            "Expenses" => NavigationTarget.Expenses,
            "Revenue" => NavigationTarget.Revenue,
            "Payments" => NavigationTarget.Payments,
            "Categories" => NavigationTarget.Categories,
            "StockLevels" => NavigationTarget.StockLevels,
            "PurchaseOrders" => NavigationTarget.PurchaseOrders,
            "Locations" => NavigationTarget.Locations,
            "Adjustments" => NavigationTarget.Adjustments,
            _ => null
        };
    }
}
