namespace ArgoBooks.Core.Enums;

/// <summary>
/// Status of a rental record.
/// </summary>
public enum RentalStatus
{
    /// <summary>Booked for later dates. Its units stay in stock until it is checked out.</summary>
    Reserved,

    /// <summary>Rental is currently active.</summary>
    Active,

    /// <summary>Item has been returned.</summary>
    Returned,

    /// <summary>Rental is past due date.</summary>
    Overdue,

    /// <summary>Rental has been cancelled.</summary>
    Cancelled
}

/// <summary>
/// Extension methods for RentalStatus.
/// </summary>
public static class RentalStatusExtensions
{
    /// <summary>
    /// Gets filter options including "All" as the first entry.
    /// </summary>
    public static string[] GetFilterOptions()
    {
        return
        [
            "All",
            nameof(RentalStatus.Reserved),
            nameof(RentalStatus.Active),
            nameof(RentalStatus.Returned),
            nameof(RentalStatus.Overdue),
            nameof(RentalStatus.Cancelled)
        ];
    }
}
