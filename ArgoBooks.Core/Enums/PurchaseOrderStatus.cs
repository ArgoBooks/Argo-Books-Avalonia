namespace ArgoBooks.Core.Enums;

/// <summary>
/// Status of a purchase order.
/// </summary>
public enum PurchaseOrderStatus
{
    /// <summary>Purchase order is being prepared.</summary>
    Draft,

    /// <summary>Purchase order is awaiting approval.</summary>
    Pending,

    /// <summary>Purchase order has been approved.</summary>
    Approved,

    /// <summary>Purchase order has been sent to supplier.</summary>
    Sent,

    /// <summary>Items are on order, awaiting delivery.</summary>
    OnOrder,

    /// <summary>Some items have been received.</summary>
    PartiallyReceived,

    /// <summary>All items have been received.</summary>
    Received,

    /// <summary>Purchase order has been cancelled.</summary>
    Cancelled
}

/// <summary>
/// Extension methods for PurchaseOrderStatus.
/// </summary>
public static class PurchaseOrderStatusExtensions
{
    public static string GetDisplayName(this PurchaseOrderStatus status)
    {
        return status switch
        {
            PurchaseOrderStatus.OnOrder => "On Order",
            PurchaseOrderStatus.PartiallyReceived => "Partially Received",
            _ => status.ToString()
        };
    }

    /// <summary>
    /// Parses a display name string to a PurchaseOrderStatus enum value.
    /// </summary>
    public static PurchaseOrderStatus? ParsePurchaseOrderStatus(string? name)
    {
        return name switch
        {
            "Draft" => PurchaseOrderStatus.Draft,
            "Pending" => PurchaseOrderStatus.Pending,
            "Approved" => PurchaseOrderStatus.Approved,
            "Sent" => PurchaseOrderStatus.Sent,
            "On Order" => PurchaseOrderStatus.OnOrder,
            "Partially Received" => PurchaseOrderStatus.PartiallyReceived,
            "Received" => PurchaseOrderStatus.Received,
            "Cancelled" => PurchaseOrderStatus.Cancelled,
            _ => null
        };
    }

    /// <summary>
    /// Gets filter options including "All" as the first entry.
    /// </summary>
    public static string[] GetFilterOptions()
    {
        return
        [
            "All",
            PurchaseOrderStatus.Draft.GetDisplayName(),
            PurchaseOrderStatus.Pending.GetDisplayName(),
            PurchaseOrderStatus.Approved.GetDisplayName(),
            PurchaseOrderStatus.Sent.GetDisplayName(),
            PurchaseOrderStatus.OnOrder.GetDisplayName(),
            PurchaseOrderStatus.PartiallyReceived.GetDisplayName(),
            PurchaseOrderStatus.Received.GetDisplayName(),
            PurchaseOrderStatus.Cancelled.GetDisplayName()
        ];
    }
}
