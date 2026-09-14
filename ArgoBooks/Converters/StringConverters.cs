using Avalonia.Data.Converters;

namespace ArgoBooks.Converters;

/// <summary>
/// String converters for various UI elements.
/// </summary>
public static class StringConverters
{
    /// <summary>
    /// Converts item type ("Product" or "Service") to badge background color.
    /// </summary>
    public static readonly IValueConverter ToItemTypeBadgeBackground = StatusConverters.ItemTypeBadgeBackground;

    /// <summary>
    /// Converts item type ("Product" or "Service") to badge foreground color.
    /// </summary>
    public static readonly IValueConverter ToItemTypeBadgeForeground = StatusConverters.ItemTypeBadgeForeground;

    /// <summary>
    /// Converts history transaction type to badge background color.
    /// </summary>
    public static readonly IValueConverter ToHistoryTypeBadgeBackground = StatusConverters.HistoryTypeBadgeBackground;

    /// <summary>
    /// Converts history transaction type to badge foreground color.
    /// </summary>
    public static readonly IValueConverter ToHistoryTypeBadgeForeground = StatusConverters.HistoryTypeBadgeForeground;

    /// <summary>
    /// Converts history status to badge background color.
    /// </summary>
    public static readonly IValueConverter ToHistoryStatusBadgeBackground = StatusConverters.HistoryStatusBadgeBackground;

    /// <summary>
    /// Converts history status to badge foreground color.
    /// </summary>
    public static readonly IValueConverter ToHistoryStatusBadgeForeground = StatusConverters.HistoryStatusBadgeForeground;

    /// <summary>
    /// Converts rental record status to badge background color.
    /// </summary>
    public static readonly IValueConverter ToRentalStatusBackground = StatusConverters.RentalStatusBackground;

    /// <summary>
    /// Converts rental record status to badge foreground color.
    /// </summary>
    public static readonly IValueConverter ToRentalStatusForeground = StatusConverters.RentalStatusForeground;

    /// <summary>
    /// Converts rental item status to badge background color.
    /// </summary>
    public static readonly IValueConverter ToRentalItemStatusBackground = StatusConverters.RentalItemStatusBackground;

    /// <summary>
    /// Converts rental item status to badge foreground color.
    /// </summary>
    public static readonly IValueConverter ToRentalItemStatusForeground = StatusConverters.RentalItemStatusForeground;

    /// <summary>
    /// Converts invoice status to badge background color.
    /// </summary>
    public static readonly IValueConverter ToInvoiceStatusBackground = StatusConverters.InvoiceStatusBackground;

    /// <summary>
    /// Converts invoice status to badge foreground color.
    /// </summary>
    public static readonly IValueConverter ToInvoiceStatusForeground = StatusConverters.InvoiceStatusForeground;

    /// <summary>
    /// Converts expense status to badge background color.
    /// </summary>
    public static readonly IValueConverter ToExpenseStatusBackground = StatusConverters.TransactionStatusBackground;

    /// <summary>
    /// Converts expense status to badge foreground color.
    /// </summary>
    public static readonly IValueConverter ToExpenseStatusForeground = StatusConverters.TransactionStatusForeground;

    /// <summary>
    /// Converts revenue status to badge background color.
    /// </summary>
    public static readonly IValueConverter ToRevenueStatusBackground = StatusConverters.TransactionStatusBackground;

    /// <summary>
    /// Converts revenue status to badge foreground color.
    /// </summary>
    public static readonly IValueConverter ToRevenueStatusForeground = StatusConverters.TransactionStatusForeground;

    /// <summary>
    /// Converts rental availability day state ("Met"/"Partial"/"Full"/"Outside"/"Empty") to background brush.
    /// </summary>
    public static readonly IValueConverter ToAvailabilityDayBackground = StatusConverters.AvailabilityDayBackground;

    /// <summary>
    /// Converts rental availability day state to border brush.
    /// </summary>
    public static readonly IValueConverter ToAvailabilityDayBorder = StatusConverters.AvailabilityDayBorder;

    /// <summary>
    /// Converts rental availability day state to free-count text color.
    /// </summary>
    public static readonly IValueConverter ToAvailabilityDayForeground = StatusConverters.AvailabilityDayForeground;
}
