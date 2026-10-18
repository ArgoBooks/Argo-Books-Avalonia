using System.Globalization;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Services;
using ArgoBooks.Localization;

namespace ArgoBooks.ViewModels;

/// <summary>Reads the opening bank balance typed into Create Company or Edit Company.</summary>
internal static class OpeningBalanceInput
{
    /// <summary>
    /// The balance typed in, dated today, or null when the box is left empty. False, with a
    /// message to show under the box, when it is not a number. Today is always the right day:
    /// the balance already includes everything before it, whatever history is entered later.
    /// </summary>
    public static bool TryRead(string? text, string currency, out OpeningBalance? balance, out string? error)
    {
        balance = null;
        error = null;

        var trimmed = text?.Trim() ?? "";
        if (trimmed.Length == 0)
            return true;

        if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            error = "Enter the balance as a number.".Translate();
            return false;
        }

        balance = new OpeningBalance { Amount = amount, Currency = currency, Date = DateTime.Today };
        return true;
    }

    /// <summary>A balance in a line, such as "$5,000.00 on Mar 1, 2026", or "Not set" for none.</summary>
    public static string Describe(OpeningBalance? balance) => balance == null
        ? "Not set".Translate()
        : "{0} on {1}".TranslateFormat(
            MoneyFormat.Format(balance.Amount, CurrencyInfo.GetByCode(balance.Currency).Symbol, 2),
            balance.Date.ToString(Core.Data.DateFormats.Short, CultureInfo.CurrentCulture));
}
