using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Data;
using ArgoBooks.Localization;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Services;

/// <summary>
/// Determines whether a selected currency is consistent with a company's country
/// and, if not, asks the user to confirm. Used by the create and edit company flows.
///
/// Only countries whose official currency is one of the supported currencies
/// (<see cref="CurrencyInfo.All"/>) are mapped here. For any other country the
/// expected currency is treated as unknown and no warning is raised, so users in
/// countries whose currency isn't selectable (e.g. India/INR) are never nagged.
/// </summary>
public static class CurrencyCountryMatcher
{
    /// <summary>
    /// Maps ISO 3166-1 alpha-2 country codes to their official ISO 4217 currency code.
    /// Scoped to currencies Argo supports so we never warn about a currency the user
    /// could not have selected anyway.
    /// </summary>
    private static readonly Dictionary<string, string> CountryToCurrency = new(StringComparer.OrdinalIgnoreCase)
    {
        ["US"] = "USD",
        ["CA"] = "CAD",
        ["GB"] = "GBP",
        ["AU"] = "AUD",
        ["CH"] = "CHF", ["LI"] = "CHF",
        ["CN"] = "CNY",
        ["JP"] = "JPY",
        ["KR"] = "KRW",
        ["TW"] = "TWD",
        ["BR"] = "BRL",
        ["RU"] = "RUB",
        ["UA"] = "UAH",
        ["TR"] = "TRY",
        ["PL"] = "PLN",
        ["CZ"] = "CZK",
        ["HU"] = "HUF",
        ["RO"] = "RON",
        ["BG"] = "BGN",
        ["RS"] = "RSD",
        ["BA"] = "BAM",
        ["MK"] = "MKD",
        ["BY"] = "BYN",
        ["AL"] = "ALL",
        ["IS"] = "ISK",
        ["NO"] = "NOK",
        ["SE"] = "SEK",
        ["DK"] = "DKK",
        // Eurozone (official euro users plus the European microstates)
        ["AT"] = "EUR", ["BE"] = "EUR", ["HR"] = "EUR", ["CY"] = "EUR",
        ["EE"] = "EUR", ["FI"] = "EUR", ["FR"] = "EUR", ["DE"] = "EUR",
        ["GR"] = "EUR", ["IE"] = "EUR", ["IT"] = "EUR", ["LV"] = "EUR",
        ["LT"] = "EUR", ["LU"] = "EUR", ["MT"] = "EUR", ["NL"] = "EUR",
        ["PT"] = "EUR", ["SK"] = "EUR", ["SI"] = "EUR", ["ES"] = "EUR",
        ["AD"] = "EUR", ["MC"] = "EUR", ["SM"] = "EUR", ["ME"] = "EUR"
    };

    /// <summary>
    /// Gets the supported currency code a country is expected to use, or null when the
    /// country is unknown or its official currency is not one of the supported currencies.
    /// </summary>
    public static string? GetExpectedCurrency(string? countryName)
    {
        if (string.IsNullOrWhiteSpace(countryName))
            return null;

        // Resolve aliases (USA, UK, ...) to a canonical name, then to its alpha-2 code.
        var canonical = Countries.NormalizeCountry(countryName);
        if (canonical == null)
            return null;

        var info = Countries.All.FirstOrDefault(c =>
            c.Name.Equals(canonical, StringComparison.OrdinalIgnoreCase));
        if (info == null)
            return null;

        return CountryToCurrency.TryGetValue(info.Code, out var currency) ? currency : null;
    }

    /// <summary>
    /// Returns true when the country has a known supported currency that differs from
    /// <paramref name="currencyCode"/>. When no expected currency is known, returns false.
    /// </summary>
    public static bool IsMismatch(string? countryName, string? currencyCode, out string? expectedCurrencyCode)
    {
        expectedCurrencyCode = GetExpectedCurrency(countryName);

        if (expectedCurrencyCode == null || string.IsNullOrWhiteSpace(currencyCode))
            return false;

        return !expectedCurrencyCode.Equals(currencyCode, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// If the selected currency doesn't match the country, shows a confirmation dialog.
    /// Returns true to proceed (no mismatch, no dialog available, or the user confirmed)
    /// and false when the user chose to cancel.
    /// </summary>
    public static async Task<bool> ConfirmIfMismatchAsync(string? countryName, string currencyCode)
    {
        if (!IsMismatch(countryName, currencyCode, out var expectedCode) || expectedCode == null)
            return true;

        var dialog = App.ConfirmationDialog;
        if (dialog == null)
            return true;

        var currencyName = CurrencyInfo.GetByCode(currencyCode).Name;
        var expectedName = CurrencyInfo.GetByCode(expectedCode).Name;
        var displayCountry = Countries.NormalizeCountryOrKeep(countryName);

        var message = string.Format(
            "You selected {0} ({1}), but {2} normally uses {3} ({4}). Do you want to continue with {0}?".Translate(),
            currencyCode, currencyName, displayCountry, expectedCode, expectedName);

        var result = await dialog.ShowAsync(new ConfirmationDialogOptions
        {
            Title = "Currency may not match country".Translate(),
            Message = message,
            PrimaryButtonText = string.Format("Use {0}".Translate(), currencyCode),
            CancelButtonText = "Cancel".Translate(),
            IsPrimaryDestructive = false
        });

        return result == ConfirmationResult.Primary;
    }
}
