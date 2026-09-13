namespace ArgoBooks.Services;

/// <summary>
/// Static service for notifying subscribers when payment provider connection state changes.
/// Follows the same pattern as CurrencyService and DateFormatService.
/// </summary>
public static class PaymentProviderService
{
    /// <summary>
    /// Event raised when payment provider connections change (connect or disconnect).
    /// </summary>
    public static event EventHandler? ProvidersChanged;

    /// <summary>
    /// Raises the ProvidersChanged event to notify subscribers.
    /// </summary>
    public static void NotifyProvidersChanged()
    {
        ProvidersChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Updates the local PortalSettings.ConnectedAccounts from a payment_methods list
    /// (e.g. ["stripe", "square"]) and fires the ProvidersChanged event.
    /// Providers not in the list are marked as disconnected.
    /// </summary>
    public static void UpdateFromPaymentMethods(List<string> paymentMethods)
    {
        var settings = App.CompanyManager?.CompanyData?.Settings.PaymentPortal;
        if (settings == null) return;

        var methods = new HashSet<string>(paymentMethods.Select(m => m.ToLowerInvariant()));

        // Only update the connected flags; preserve emails for providers still connected
        if (!methods.Contains("stripe"))
        {
            settings.ConnectedAccounts.StripeConnected = false;
            settings.ConnectedAccounts.StripeEmail = null;
        }
        else
        {
            settings.ConnectedAccounts.StripeConnected = true;
        }

        if (!methods.Contains("paypal"))
        {
            settings.ConnectedAccounts.PaypalConnected = false;
            settings.ConnectedAccounts.PaypalEmail = null;
        }
        else
        {
            settings.ConnectedAccounts.PaypalConnected = true;
        }

        if (!methods.Contains("square"))
        {
            settings.ConnectedAccounts.SquareConnected = false;
            settings.ConnectedAccounts.SquareEmail = null;
        }
        else
        {
            settings.ConnectedAccounts.SquareConnected = true;
        }

        NotifyProvidersChanged();
    }

    /// <summary>
    /// Gets the list of currently connected payment method names (e.g. ["stripe", "paypal"]).
    /// Reads from the current PortalSettings.ConnectedAccounts.
    /// </summary>
    public static List<string> GetConnectedMethods()
    {
        var settings = App.CompanyManager?.CompanyData?.Settings.PaymentPortal;
        if (settings == null) return [];

        var methods = new List<string>();
        if (settings.ConnectedAccounts.StripeConnected)
            methods.Add("stripe");
        if (settings.ConnectedAccounts.PaypalConnected)
            methods.Add("paypal");
        if (settings.ConnectedAccounts.SquareConnected)
            methods.Add("square");
        return methods;
    }
}
