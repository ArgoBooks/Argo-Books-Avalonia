using System.Text.RegularExpressions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Cleans product descriptions extracted by the receipt scanner. Receipts often print an
/// internal SKU/barcode/item code in front of the product name (e.g.
/// "6010-0272-0259-0062 Co Palm Refill"); users only care about the name, so we strip the
/// leading code.
/// </summary>
public static partial class ReceiptDescriptionCleaner
{
    // Matches a leading code token followed by whitespace and at least one more (non-space) character of the actual name.
    [GeneratedRegex(@"^\s*(?:\d+(?:-\d+)+|\d{5,})\s+(?=\S)")]
    private static partial Regex LeadingCodeRegex();

    /// <summary>
    /// Returns the description with any leading SKU/barcode/item code removed. If stripping the
    /// code would leave nothing, the original (trimmed) description is returned unchanged.
    /// </summary>
    public static string Clean(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return string.Empty;

        var trimmed = description.Trim();
        var cleaned = LeadingCodeRegex().Replace(trimmed, string.Empty).Trim();
        return cleaned.Length > 0 ? cleaned : trimmed;
    }
}
