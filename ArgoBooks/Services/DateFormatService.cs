namespace ArgoBooks.Services;

/// <summary>
/// Service for formatting dates based on user settings.
/// </summary>
public static class DateFormatService
{
    /// <summary>
    /// Event raised when the date format setting changes.
    /// </summary>
    public static event EventHandler? DateFormatChanged;

    /// <summary>
    /// Raises the DateFormatChanged event to notify subscribers that the date format has changed.
    /// </summary>
    public static void NotifyDateFormatChanged()
    {
        DateFormatChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Gets the current date format setting from company settings.
    /// </summary>
    private static string? CurrentFormat => App.CompanyManager?.CompanyData?.Settings.Localization.DateFormat;

    /// <summary>
    /// Formats a date using the current date format setting.
    /// </summary>
    /// <param name="date">The date to format.</param>
    /// <returns>The formatted date string.</returns>
    public static string Format(DateTime date) => Core.Data.DateFormats.Format(date, CurrentFormat);

    /// <summary>
    /// Formats a date using the current date format setting.
    /// </summary>
    /// <param name="date">The date to format.</param>
    /// <returns>The formatted date string, or empty if null.</returns>
    public static string Format(DateTime? date) => date is { } d ? Format(d) : string.Empty;

    /// <summary>
    /// Gets the .NET format string for the current date format setting.
    /// Useful for chart axis labelers.
    /// </summary>
    public static string GetCurrentDotNetFormat() => Core.Data.DateFormats.ToDotNet(CurrentFormat);
}
