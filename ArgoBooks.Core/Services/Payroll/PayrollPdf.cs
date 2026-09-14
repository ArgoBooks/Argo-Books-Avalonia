using ArgoBooks.Core.Models.Common;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace ArgoBooks.Core.Services.Payroll;

/// <summary>Layout and formatting shared by the T4, RL-1 and ROE PDF renderers.</summary>
internal static class PayrollPdf
{
    public static IEnumerable<string> AddressLines(Address address)
    {
        if (!string.IsNullOrWhiteSpace(address.Street))
        {
            yield return address.Street;
        }

        string cityLine = string.Join(", ",
            new[] { address.City, address.State, address.ZipCode }.Where(s => !string.IsNullOrWhiteSpace(s)));

        if (!string.IsNullOrWhiteSpace(cityLine))
        {
            yield return cityLine;
        }
    }

    /// <summary>Grouped the way a SIN is normally written, so it can be checked at a glance.</summary>
    public static string FormatSin(string sin)
    {
        string digits = new(sin.Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 9 ? $"{digits[..3]} {digits[3..6]} {digits[6..]}" : "not provided";
    }

    public static string Money(decimal value) => $"${value:N2}";

    /// <summary>A row of small grey reference label ("Box 14", "Block 15A"), description, and right-aligned value.</summary>
    public static void LabelledRow(
        ColumnDescriptor col, string label, string description, string value,
        float labelWidth, float valueWidth, bool bold = false) =>
        col.Item().PaddingTop(2).Row(r =>
        {
            r.ConstantItem(labelWidth).Text(label).FontSize(9).FontColor(Colors.Grey.Darken2);

            var left = r.RelativeItem().Text(description);
            var right = r.ConstantItem(valueWidth).AlignRight().Text(value);

            if (bold)
            {
                left.SemiBold();
                right.SemiBold();
            }
        });
}
