using System.Globalization;

namespace ArgoBooks.Core.Models.Inventory;

/// <summary>
/// The units a product can be stocked in, and how a stock quantity is written.
/// </summary>
public static class StockUnits
{
    public const string Each = "Each";

    public static IReadOnlyList<string> All { get; } =
        [Each, "kg", "g", "lb", "oz", "L", "mL", "gal", "m", "cm", "ft", "Box", "Pack"];

    /// <summary>Up to three decimal places, so 12 shows as "12" and 2.5 as "2.5".</summary>
    public static string Format(decimal quantity) =>
        quantity.ToString("#,0.###", CultureInfo.CurrentCulture);

    public static string Format(decimal quantity, string? unit) =>
        string.IsNullOrEmpty(unit) || unit == Each ? Format(quantity) : $"{Format(quantity)} {unit}";
}
