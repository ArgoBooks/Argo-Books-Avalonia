using ArgoBooks.Core.Data;

namespace ArgoBooks.Core.Models;

/// <summary>
/// Decides which optional sidebar sections an open company shows. The sidebar and the Settings
/// toggles both read this, so the switch on screen can never disagree with the sidebar beside it.
/// </summary>
public static class FeatureVisibility
{
    public static (bool Inventory, bool Rentals, bool Payroll) Resolve(
        CompanySettings? settings,
        CompanyData? data)
    {
        var defaults = IndustryFeatureDefaults.For(settings?.Company.Industry);
        var chosen = settings?.Features;

        // A company that already has records in a section keeps it, whatever the industry would have defaulted to.
        var usesInventory = data is not null &&
            (data.Inventory.Count > 0 || data.StockAdjustments.Count > 0 ||
             data.PurchaseOrders.Count > 0 || data.StockTransfers.Count > 0);
        var usesRentals = data is not null &&
            (data.RentalInventory.Count > 0 || data.Rentals.Count > 0);
        var usesPayroll = data is not null && data.Employees.Count > 0;

        return (
            chosen?.ShowInventory ?? (defaults.Inventory || usesInventory),
            chosen?.ShowRentals ?? (defaults.Rentals || usesRentals),
            chosen?.ShowPayroll ?? (defaults.Payroll || usesPayroll));
    }
}
