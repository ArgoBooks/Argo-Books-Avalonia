using ArgoBooks.Core.Models.Common;

using SkiaSharp;

using Xunit;

namespace ArgoBooks.Tests.Models;

/// <summary>
/// Guards the currency list against the two ways it has broken: a symbol the PDF font cannot
/// draw, and the picker list drifting from the data behind it.
/// </summary>
public class CurrencyListTests
{
    // The family every PDF renderer asks for (pay stubs, T4, RL-1, ROE, purchase orders).
    private const string PdfFontFamily = "Helvetica";

    [Fact]
    public void SymbolsUseOnlyCharactersEveryPdfFontShips()
    {
        // A symbol is drawn by whatever font the PDF asks for, and "Helvetica" is a different
        // font on each platform: Segoe UI stands in for it on Windows, while macOS has the real
        // Helvetica, which carries Latin, Cyrillic and the currency signs and nothing else. So a
        // symbol borrowed from a script (฿ Thai, ৳ Bengali, ﷼ Arabic) prints as an empty box on a
        // Mac even where it looks fine here. Those currencies use letters instead: THB, Tk.
        //
        // U+20A0..U+20BF is the Currency Symbols block: ₹ ₩ ₽ ₺ ₴ ₦ ₱ ₨ ₪ ₵ ₫ and the rest.
        static bool IsWidelyShipped(char ch) =>
            ch <= 'ſ'                          // Latin, Latin-1, Latin Extended-A (č, ł)
            || (ch >= 'Ѐ' && ch <= 'ӿ')   // Cyrillic (лв, ден, дин)
            || (ch >= '₠' && ch <= '₿');  // Currency Symbols

        var offenders = CurrencyInfo.All
            .SelectMany(entry => entry.Value.Symbol
                .Where(ch => !IsWidelyShipped(ch))
                .Select(ch => $"{entry.Key} '{ch}' (U+{(int)ch:X4})"))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Symbols outside Latin, Cyrillic and the currency block will not render in every PDF font: {string.Join(", ", offenders)}. Use letters instead, as THB and BDT do.");
    }

    [Fact]
    public void EverySymbolHasAGlyphInThePdfFontOnThisMachine()
    {
        // The rule above is the real guard, since it holds on every platform. This checks the
        // font this machine actually resolves, which catches a symbol that even the local font
        // cannot draw. Glyph 0 is .notdef: the blank or box shown for a missing character.
        using var typeface = SKTypeface.FromFamilyName(PdfFontFamily);
        Assert.NotNull(typeface);

        var missing = CurrencyInfo.All
            .SelectMany(entry => entry.Value.Symbol
                .Where(ch => typeface.GetGlyph(ch) == 0)
                .Select(ch => $"{entry.Key} '{ch}' (U+{(int)ch:X4})"))
            .ToList();

        Assert.True(missing.Count == 0,
            $"{typeface.FamilyName} (resolved from {PdfFontFamily}) cannot draw: {string.Join(", ", missing)}.");
    }

    [Fact]
    public void PickerListMatchesTheCurrencyData()
    {
        // ArgoBooks/Data/Currencies.cs holds the dropdown labels and CurrencyInfo holds the data
        // behind them. They are separate lists, and they have drifted before: INR was in the
        // parser and in no dropdown.
        var picker = ArgoBooks.Data.Currencies.All
            .Select(label =>
            {
                var dash = label.IndexOf(" - ", StringComparison.Ordinal);
                var open = label.LastIndexOf(" (", StringComparison.Ordinal);
                return new
                {
                    Code = label[..dash],
                    Name = label[(dash + 3)..open],
                    Symbol = label[(open + 2)..^1],
                };
            })
            .ToList();

        Assert.Equal(
            CurrencyInfo.All.Keys.OrderBy(c => c, StringComparer.Ordinal),
            picker.Select(p => p.Code).OrderBy(c => c, StringComparer.Ordinal));

        foreach (var entry in picker)
        {
            var info = CurrencyInfo.All[entry.Code];
            Assert.Equal(info.Name, entry.Name);
            Assert.Equal(info.Symbol, entry.Symbol);
        }
    }

    [Fact]
    public void PriorityCurrenciesAreAllSupported()
    {
        foreach (var code in CurrencyInfo.PriorityCodes)
        {
            Assert.True(CurrencyInfo.All.ContainsKey(code),
                $"{code} is pinned to the top of pickers but is not a supported currency.");
        }
    }
}
