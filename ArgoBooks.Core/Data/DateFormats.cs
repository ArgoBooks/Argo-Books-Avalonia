using System.Globalization;

namespace ArgoBooks.Core.Data;

/// <summary>
/// The date formats a company can pick in Settings, and how a date is written in each.
/// </summary>
public static class DateFormats
{
    public const string Default = "MM/DD/YYYY";

    /// <summary>The setting values offered in Settings, in the order shown.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        "MM/DD/YYYY",
        "DD/MM/YYYY",
        "YYYY-MM-DD",
        "MMM D, YYYY"
    ];

    /// <summary>The .NET format string for a setting value, falling back to the default.</summary>
    public static string ToDotNet(string? setting) => setting switch
    {
        "DD/MM/YYYY" => "dd/MM/yyyy",
        "YYYY-MM-DD" => "yyyy-MM-dd",
        "MMM D, YYYY" => "MMM d, yyyy",
        _ => "MM/dd/yyyy"
    };

    /// <summary>Writes a date in the given setting's format.</summary>
    public static string Format(DateTime date, string? setting) =>
        // InvariantCulture, so the format's '/' is a literal slash rather than the locale's separator, which a German locale would draw as '.'.
        date.ToString(ToDotNet(setting), CultureInfo.InvariantCulture);
}
