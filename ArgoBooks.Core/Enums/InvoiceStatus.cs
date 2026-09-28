namespace ArgoBooks.Core.Enums;

/// <summary>
/// Status of an invoice.
/// </summary>
public enum InvoiceStatus
{
    /// <summary>Invoice is being prepared.</summary>
    Draft,

    /// <summary>Invoice is ready but not yet sent.</summary>
    Pending,

    /// <summary>Invoice has been sent to customer.</summary>
    Sent,

    /// <summary>Invoice has been viewed by recipient.</summary>
    Viewed,

    /// <summary>Partial payment has been received.</summary>
    Partial,

    /// <summary>Invoice has been fully paid.</summary>
    Paid,

    /// <summary>Invoice is past due date.</summary>
    Overdue,

    /// <summary>Invoice has been cancelled.</summary>
    Cancelled,

    /// <summary>Invoice was paid and has since been fully refunded.</summary>
    Refunded,

    /// <summary>Invoice was paid and has been refunded in part.</summary>
    PartiallyRefunded
}

/// <summary>
/// Extension methods for InvoiceStatus.
/// </summary>
public static class InvoiceStatusExtensions
{
    /// <summary>
    /// The status as screens write it out, before translation. Each is written out here so the
    /// translation tool collects it.
    /// </summary>
    public static string ToDisplayText(this InvoiceStatus status) => status switch
    {
        InvoiceStatus.Draft => "Draft",
        InvoiceStatus.Pending => "Pending",
        InvoiceStatus.Sent => "Sent",
        InvoiceStatus.Viewed => "Viewed",
        InvoiceStatus.Partial => "Partial",
        InvoiceStatus.Paid => "Paid",
        InvoiceStatus.Overdue => "Overdue",
        InvoiceStatus.Cancelled => "Cancelled",
        InvoiceStatus.Refunded => "Refunded",
        InvoiceStatus.PartiallyRefunded => "Partially Refunded",
        _ => status.ToString()
    };

    /// <summary>
    /// Gets the modal status options (statuses selectable when creating/editing).
    /// </summary>
    public static string[] GetModalOptions()
    {
        return
        [
            nameof(InvoiceStatus.Draft),
            nameof(InvoiceStatus.Pending),
            nameof(InvoiceStatus.Sent),
            nameof(InvoiceStatus.Partial),
            nameof(InvoiceStatus.Paid),
            nameof(InvoiceStatus.Cancelled)
        ];
    }

    /// <summary>
    /// Gets filter options including "All" as the first entry.
    /// </summary>
    public static string[] GetFilterOptions()
    {
        return
        [
            "All",
            nameof(InvoiceStatus.Draft),
            nameof(InvoiceStatus.Pending),
            nameof(InvoiceStatus.Sent),
            nameof(InvoiceStatus.Partial),
            nameof(InvoiceStatus.Paid),
            nameof(InvoiceStatus.Overdue),
            nameof(InvoiceStatus.Cancelled),
            nameof(InvoiceStatus.PartiallyRefunded),
            nameof(InvoiceStatus.Refunded)
        ];
    }
}
