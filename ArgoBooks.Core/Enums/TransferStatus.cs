namespace ArgoBooks.Core.Enums;

/// <summary>
/// Status of a stock transfer.
///
/// Transferring stock moves the units, adjusts both locations and writes the record in one
/// step, so Completed is the only value anything produces. The other three describe a
/// two-step transfer, raised at one location and received at the other, which the app does
/// not offer. They are kept because the Stock Transfers report renders this column and
/// saved report templates refer to it.
/// </summary>
public enum TransferStatus
{
    /// <summary>Transfer is pending approval or processing.</summary>
    Pending,

    /// <summary>Transfer is in transit between locations.</summary>
    InTransit,

    /// <summary>Transfer has been completed.</summary>
    Completed,

    /// <summary>Transfer has been cancelled.</summary>
    Cancelled
}
