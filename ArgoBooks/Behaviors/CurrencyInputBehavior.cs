using System.Globalization;
using ArgoBooks.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ArgoBooks.Behaviors;

/// <summary>
/// Attached behavior that formats a money box as it is typed in: symbol, thousands separators
/// and all.
///
/// The caret is the whole difficulty. Rewriting the text moves it, so typing into the middle of
/// an existing figure would drop the digits somewhere else. It is preserved by counting the
/// SIGNIFICANT characters before it, the digits and the decimal point, and putting it back after
/// the same count in the rewritten text. Separators are not significant, so inserting one never
/// shifts the caret away from what the typist was aiming at.
///
/// Formatting follows CurrencyInfo.Format exactly: the symbol leads, and the separators are
/// invariant rather than the machine's. That is deliberate there,
/// so a German machine does not render a hybrid like "$1.234,56", and this has to match or the
/// field would reformat itself the moment it lost focus.
///
/// Do NOT also set <see cref="NumericInputBehavior"/>.IsDecimalOnly on the same box. It looks
/// complementary and is not: its TextInput handler builds the text the keystroke WOULD produce
/// and rejects anything that is not bare digits with at most one point. As soon as this behavior
/// writes a symbol, every following keystroke fails that test and the box accepts exactly one
/// digit. Nothing is needed alongside this anyway, because rewriting the text keeps only digits
/// and the first decimal point, so letters and stray separators are dropped as they arrive.
/// </summary>
public static class CurrencyInputBehavior
{
    public static readonly AttachedProperty<bool> FormatAsCurrencyProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("FormatAsCurrency", typeof(CurrencyInputBehavior));

    /// <summary>Guards the re-entrant TextChanged that setting Text raises. UI thread only.</summary>
    private static bool _formatting;

    static CurrencyInputBehavior()
    {
        FormatAsCurrencyProperty.Changed.AddClassHandler<TextBox>(OnFormatAsCurrencyChanged);
    }

    public static bool GetFormatAsCurrency(TextBox element) => element.GetValue(FormatAsCurrencyProperty);

    public static void SetFormatAsCurrency(TextBox element, bool value) =>
        element.SetValue(FormatAsCurrencyProperty, value);

    private static void OnFormatAsCurrencyChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs e)
    {
        // Detaching first keeps this idempotent if the property is ever set twice on one box.
        textBox.TextChanged -= OnTextChanged;
        textBox.LostFocus -= OnLostFocus;

        if (e.NewValue is true)
        {
            textBox.TextChanged += OnTextChanged;
            textBox.LostFocus += OnLostFocus;
        }
    }

    private static void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_formatting || sender is not TextBox box)
        {
            return;
        }

        string current = box.Text ?? string.Empty;
        string formatted = Format(current, padFraction: false);

        if (formatted == current)
        {
            return;
        }

        int caret = Math.Clamp(box.CaretIndex, 0, current.Length);
        int significant = CountSignificant(current[..caret]);

        _formatting = true;
        try
        {
            box.Text = formatted;
            box.CaretIndex = CaretAfter(formatted, significant);
        }
        finally
        {
            _formatting = false;
        }
    }

    /// <summary>Finishes the figure off with its full decimals once the field is done with.</summary>
    private static void OnLostFocus(object? sender, FocusChangedEventArgs e)
    {
        if (_formatting || sender is not TextBox box || string.IsNullOrWhiteSpace(box.Text))
        {
            return;
        }

        string padded = Format(box.Text, padFraction: true);
        if (padded == box.Text)
        {
            return;
        }

        _formatting = true;
        try
        {
            box.Text = padded;
            box.CaretIndex = padded.Length;
        }
        finally
        {
            _formatting = false;
        }
    }

    /// <summary>
    /// Rewrites whatever is in the box as currency.
    ///
    /// <paramref name="padFraction"/> is off while typing, so someone part way through "5.0"
    /// does not have it completed to "5.00" under them and then have to delete a digit they did
    /// not type. It is on once focus leaves.
    /// </summary>
    private static string Format(string text, bool padFraction)
    {
        var currency = CurrencyService.CurrentCurrency;
        int places = currency.DecimalPlaces;

        string digits = new(text.Where(char.IsDigit).ToArray());

        // The decimal separator, which may be the one the machine uses rather than the one the
        // box was written with.
        //
        // The first separator with no more trailing digits than the currency has decimals. A
        // grouping separator always has exactly three after it, so this tells them apart without
        // consulting a culture: "1,234" is a thousand, "1234,56" is a decimal.
        int dot = -1;

        if (places > 0)
        {
            char cultureSeparator = CultureInfo.CurrentCulture.NumberFormat
                .NumberDecimalSeparator.FirstOrDefault('.');

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '.' && text[i] != cultureSeparator)
                {
                    continue;
                }

                if (text[(i + 1)..].Count(char.IsDigit) <= places)
                {
                    dot = i;
                    break;
                }
            }
        }

        string wholeText = dot < 0 ? text : text[..dot];
        string whole = new(wholeText.Where(char.IsDigit).ToArray());
        string fraction = dot < 0 ? string.Empty : new(text[(dot + 1)..].Where(char.IsDigit).ToArray());

        if (fraction.Length > places)
        {
            fraction = fraction[..places];
        }

        if (digits.Length == 0 && dot < 0)
        {
            return string.Empty;
        }

        // Leading zeros go, but a lone zero stays so "0." and "0.5" can be typed.
        whole = whole.TrimStart('0');
        if (whole.Length == 0)
        {
            whole = "0";
        }

        if (padFraction && places > 0)
        {
            fraction = fraction.PadRight(places, '0');
            dot = 0; // force the separator to be written
        }

        string grouped = decimal.TryParse(whole, NumberStyles.None, CultureInfo.InvariantCulture, out decimal value)
            ? value.ToString("N0", CultureInfo.InvariantCulture)
            : whole;

        return dot < 0
            ? currency.Symbol + grouped
            : currency.Symbol + grouped + "." + fraction;
    }

    /// <summary>Digits and the decimal point. Separators and the symbol are not.</summary>
    private static int CountSignificant(string text) =>
        text.Count(c => char.IsDigit(c) || c == '.');

    /// <summary>The offset just past the given number of significant characters.</summary>
    private static int CaretAfter(string text, int significant)
    {
        int seen = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsDigit(text[i]) || text[i] == '.')
            {
                seen++;
                if (seen == significant)
                {
                    return i + 1;
                }
            }
        }

        // Nothing significant yet, so sit after the symbol rather than before it.
        return significant == 0 ? Math.Min(CurrencyService.CurrentSymbol.Length, text.Length) : text.Length;
    }

    /// <summary>
    /// Reads a figure back from a formatted box.
    ///
    /// The symbol is stripped rather than relying on NumberStyles.AllowCurrencySymbol, which only
    /// accepts the CULTURE's symbol: a company keeping books in euros on an English Canadian
    /// machine would otherwise fail to parse its own output and silently read as zero.
    ///
    /// The separator is resolved before parsing rather than by trying one culture then another.
    /// Falling back does not work here: NumberStyles.Number allows thousands separators, so
    /// "16129,00" does not fail under InvariantCulture, it succeeds as 1612900.
    /// </summary>
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0m;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string cleaned = new(text.Where(c => char.IsDigit(c) || c == '-' || c == '.' || c == ',').ToArray());

        if (cleaned.Length == 0)
        {
            return false;
        }

        int lastSeparator = cleaned.LastIndexOfAny(['.', ',']);

        if (lastSeparator >= 0)
        {
            string tail = cleaned[(lastSeparator + 1)..];
            bool bothKinds = cleaned.Contains('.') && cleaned.Contains(',');

            // Grouping only when it cannot be a decimal point: both kinds present means the last
            // one is decimal, and a repeated separator can only be grouping. That leaves a single
            // separator with exactly three digits after it, which is read as grouping.
            //
            // Not the machine's separator, which is the tempting answer and the wrong one. These
            // boxes are written by CurrencyInfo.Format and Format below, both deliberately
            // InvariantCulture so a customer-facing invoice never renders a hybrid like
            // "$1.234,56". A zero-decimal currency therefore sits in the box as "Ft52,000" with
            // no decimal point at all, and asking a comma-decimal machine would read that as 52.
            // Money is never quoted to three decimals here either, since Format only ever writes
            // N0 or N2.
            bool isGrouping = !bothKinds && tail.Length == 3;

            string whole = cleaned[..lastSeparator].Replace(".", string.Empty).Replace(",", string.Empty);

            cleaned = isGrouping || tail.Length == 0
                ? whole + tail
                : whole + "." + tail;
        }

        // Float, not Number: grouping is already gone, and allowing it again would let a stray
        // separator through as a silent factor of a thousand.
        return decimal.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
