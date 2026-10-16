using System.Xml.Linq;

namespace ArgoBooks.Core.Services.Payroll;

/// <summary>Field helpers shared by the T4 and ROE XML writers.</summary>
internal static class PayrollXml
{
    /// <summary>Adds the element only when there is something to put in it.</summary>
    public static void Add(XElement parent, string element, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            parent.Add(new XElement(element, value));
        }
    }

    /// <summary>The value trimmed and cut to the field width, or null when it is blank.</summary>
    public static string? Text(string? value, int max)
    {
        string trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed[..Math.Min(trimmed.Length, max)];
    }

    /// <summary>Only the value's digits, cut to the field width, or null when it has none.</summary>
    public static string? Digits(string? value, int max)
    {
        string digits = new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : digits[..Math.Min(digits.Length, max)];
    }
}
