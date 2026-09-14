using System.Globalization;
using ArgoBooks.Core;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ArgoBooks.Converters;

/// <summary>
/// A configurable converter that maps status strings to brush colors.
/// Use this instead of creating multiple individual status-to-brush converters.
/// </summary>
public class StatusToBrushConverter : IValueConverter
{
    private readonly Dictionary<string, string> _statusColors;
    private readonly string _defaultColor;

    /// <summary>
    /// Creates a new StatusToBrushConverter with the specified status-to-color mappings.
    /// </summary>
    /// <param name="statusColors">Dictionary mapping status strings to hex color codes.</param>
    /// <param name="defaultColor">Default hex color if status is not found.</param>
    public StatusToBrushConverter(Dictionary<string, string> statusColors, string defaultColor = AppColors.GrayLightest)
    {
        _statusColors = statusColors;
        _defaultColor = defaultColor;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string status)
            return new SolidColorBrush(Color.Parse(_defaultColor));

        var color = _statusColors.GetValueOrDefault(status, _defaultColor);
        return new SolidColorBrush(Color.Parse(color));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Pre-configured status converters for common status types.
/// </summary>
public static class StatusConverters
{
    #region Color Palettes

    // Background colors
    private const string GreenBg = AppColors.SuccessLight;
    private const string YellowBg = AppColors.WarningLight;
    private const string RedBg = AppColors.ErrorLight;
    private const string BlueBg = AppColors.PrimaryLight;
    private const string PurpleBg = AppColors.PurpleLight;
    private const string IndigoBg = AppColors.IndigoLight;
    private const string OrangeBg = AppColors.OrangeLight;
    private const string GrayBg = AppColors.GrayLightest;

    // Foreground colors
    private const string GreenFg = AppColors.SuccessText;
    private const string YellowFg = AppColors.WarningText;
    private const string RedFg = AppColors.Error;
    private const string BlueFg = AppColors.PrimaryText;
    private const string PurpleFg = AppColors.VioletHover;
    private const string IndigoFg = AppColors.IndigoText;
    private const string OrangeFg = AppColors.OrangeDark;
    private const string GrayFg = AppColors.GrayText;

    #endregion

    #region Transaction Status (Expense/Revenue)

    public static readonly IValueConverter TransactionStatusBackground = new StatusToBrushConverter(
        new Dictionary<string, string>
        {
            ["Completed"] = GreenBg,
            ["Pending"] = YellowBg,
            ["Unpaid"] = OrangeBg,
            ["Returned"] = BlueBg,
            ["Partial Return"] = PurpleBg,
            ["Lost / Damaged"] = RedBg,
            ["Cancelled"] = GrayBg
        });

    public static readonly IValueConverter TransactionStatusForeground = new StatusToBrushConverter(
        new Dictionary<string, string>
        {
            ["Completed"] = GreenFg,
            ["Pending"] = YellowFg,
            ["Unpaid"] = OrangeFg,
            ["Returned"] = BlueFg,
            ["Partial Return"] = PurpleFg,
            ["Lost / Damaged"] = RedFg,
            ["Cancelled"] = GrayFg
        }, GrayFg);

    #endregion

    #region Invoice Status

    public static readonly IValueConverter InvoiceStatusBackground = new StatusToBrushConverter(
        new Dictionary<string, string>
        {
            ["Paid"] = GreenBg,
            ["Pending"] = YellowBg,
            ["Overdue"] = RedBg,
            ["Draft"] = GrayBg,
            ["Sent"] = BlueBg,
            ["Viewed"] = BlueBg,
            ["Partial"] = PurpleBg,
            ["Cancelled"] = GrayBg
        });

    public static readonly IValueConverter InvoiceStatusForeground = new StatusToBrushConverter(
        new Dictionary<string, string>
        {
            ["Paid"] = GreenFg,
            ["Pending"] = YellowFg,
            ["Overdue"] = RedFg,
            ["Draft"] = GrayFg,
            ["Sent"] = BlueFg,
            ["Viewed"] = BlueFg,
            ["Partial"] = PurpleFg,
            ["Cancelled"] = GrayFg
        }, GrayFg);

    #endregion

    #region Rental Status

    public static readonly IValueConverter RentalStatusBackground = new StatusToBrushConverter(
        new Dictionary<string, string>
        {
            ["Active"] = GreenBg,
            ["Returned"] = BlueBg,
            ["Overdue"] = RedBg,
            ["Cancelled"] = GrayBg
        });

    public static readonly IValueConverter RentalStatusForeground = new StatusToBrushConverter(
        new Dictionary<string, string>
        {
            ["Active"] = GreenFg,
            ["Returned"] = BlueFg,
            ["Overdue"] = RedFg,
            ["Cancelled"] = GrayFg
        }, GrayFg);

    #endregion

    #region Rental Item Status

    public static readonly IValueConverter RentalItemStatusBackground = new StatusToBrushConverter(
        new Dictionary<string, string>
        {
            ["Available"] = GreenBg,
            ["In Maintenance"] = YellowBg,
            ["All Rented"] = PurpleBg
        });

    public static readonly IValueConverter RentalItemStatusForeground = new StatusToBrushConverter(
        new Dictionary<string, string>
        {
            ["Available"] = GreenFg,
            ["In Maintenance"] = YellowFg,
            ["All Rented"] = PurpleFg
        }, GrayFg);

    #endregion

    #region Availability Day (rental availability calendar)

    public static readonly IValueConverter AvailabilityDayBackground = new StatusToBrushConverter(
        new Dictionary<string, string>
        {
            ["Met"] = GreenBg,
            ["Partial"] = YellowBg,
            ["Full"] = AppColors.ErrorLightest,
            ["Outside"] = "Transparent",
            ["Empty"] = "Transparent"
        }, "Transparent");

    public static readonly IValueConverter AvailabilityDayBorder = new StatusToBrushConverter(
        new Dictionary<string, string>
        {
            ["Met"] = AppColors.Success,
            ["Partial"] = AppColors.Warning,
            ["Full"] = AppColors.ErrorLight,
            ["Outside"] = "Transparent",
            ["Empty"] = AppColors.GrayLightest
        }, AppColors.GrayLightest);

    public static readonly IValueConverter AvailabilityDayForeground = new StatusToBrushConverter(
        new Dictionary<string, string>
        {
            ["Met"] = GreenFg,
            ["Partial"] = YellowFg,
            ["Full"] = RedFg,
            ["Outside"] = GrayFg,
            ["Empty"] = GrayFg
        }, GrayFg);

    #endregion

    #region History Type

    public static readonly IValueConverter HistoryTypeBadgeBackground = new StatusToBrushConverter(
        new Dictionary<string, string>
        {
            ["Rental"] = BlueBg,
            ["Purchase"] = PurpleBg,
            ["Return"] = OrangeBg,
            ["Payment"] = GreenBg
        });

    public static readonly IValueConverter HistoryTypeBadgeForeground = new StatusToBrushConverter(
        new Dictionary<string, string>
        {
            ["Rental"] = BlueFg,
            ["Purchase"] = PurpleFg,
            ["Return"] = OrangeFg,
            ["Payment"] = GreenFg
        }, GrayFg);

    #endregion

    #region History Status

    public static readonly IValueConverter HistoryStatusBadgeBackground = new StatusToBrushConverter(
        new Dictionary<string, string>
        {
            ["Completed"] = GreenBg,
            ["Pending"] = YellowBg,
            ["Overdue"] = RedBg,
            ["Refunded"] = IndigoBg
        });

    public static readonly IValueConverter HistoryStatusBadgeForeground = new StatusToBrushConverter(
        new Dictionary<string, string>
        {
            ["Completed"] = GreenFg,
            ["Pending"] = YellowFg,
            ["Overdue"] = RedFg,
            ["Refunded"] = IndigoFg
        }, GrayFg);

    #endregion

    #region Item Type

    public static readonly IValueConverter ItemTypeBadgeBackground = new StatusToBrushConverter(
        new Dictionary<string, string>
        {
            ["Product"] = BlueBg,
            ["Service"] = PurpleBg
        });

    public static readonly IValueConverter ItemTypeBadgeForeground = new StatusToBrushConverter(
        new Dictionary<string, string>
        {
            ["Product"] = BlueFg,
            ["Service"] = PurpleFg
        }, GrayFg);

    #endregion
}
