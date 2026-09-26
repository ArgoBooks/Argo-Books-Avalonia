using ArgoBooks.Core.Services;

namespace ArgoBooks.Core.Models.Portal;

/// <summary>
/// Settings for the payment portal integration.
/// Stored in company data (persisted in .argo file).
/// The per-company API key is issued at portal registration and lives in that same
/// file; on open it is copied into a process-level slot, not read from a .env file.
/// </summary>
public class PortalSettings
{
    /// <summary>
    /// The payment portal API base URL.
    /// </summary>
    public static readonly string ApiBaseUrl = $"{ApiConfig.BaseUrl}/api/portal";

    /// <summary>
    /// Key under which the open company's portal API key is held in <see cref="DotEnv"/>.
    /// </summary>
    public const string ApiKeyEnvVar = "PAYMENT_PORTAL_API_KEY";

    /// <summary>
    /// Gets the open company's portal API key, as set by <see cref="ActivateApiKey"/>.
    /// </summary>
    [JsonIgnore]
    public static string ApiKey => DotEnv.Get(ApiKeyEnvVar);

    /// <summary>
    /// Whether the portal API is configured (API key is present).
    /// </summary>
    [JsonIgnore]
    public static bool IsConfigured => DotEnv.HasValue(ApiKeyEnvVar);

    /// <summary>
    /// Per-company API key persisted in the .argo file.
    /// On company open this is loaded into DotEnv so that the static ApiKey property works.
    /// </summary>
    [JsonPropertyName("apiKey")]
    public string? PersistedApiKey { get; set; }

    /// <summary>
    /// Loads this company's API key into the process-level DotEnv cache.
    /// Call on company open.
    /// </summary>
    public static void ActivateApiKey(PortalSettings? settings)
    {
        var key = settings?.PersistedApiKey;
        if (!string.IsNullOrEmpty(key))
            DotEnv.SetInMemory(ApiKeyEnvVar, key);
        else
            DotEnv.Unset(ApiKeyEnvVar);
    }

    /// <summary>
    /// Clears the API key from the process-level DotEnv cache.
    /// Call on company close.
    /// </summary>
    public static void DeactivateApiKey()
    {
        DotEnv.Unset(ApiKeyEnvVar);
    }

    /// <summary>
    /// Auto-sync interval in minutes. 0 = manual sync only.
    /// </summary>
    [JsonPropertyName("autoSyncIntervalMinutes")]
    public int AutoSyncIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// Whether to show an in-app notification when new online payments are received.
    /// Local only: this fires when the app polls, so it needs the app running.
    /// The email equivalent is <see cref="EmailOwnerOnPayment"/>.
    /// </summary>
    [JsonPropertyName("notifyOnPayment")]
    public bool NotifyOnPayment { get; set; } = true;

    /// <summary>
    /// Whether the server emails the business owner when a customer pays.
    /// </summary>
    /// <remarks>
    /// A cache of server state, not the source of truth: the payment webhooks
    /// read the server's copy while this app is closed. The server wins on load
    /// (see the status endpoint's preferences block).
    ///
    /// Deliberately separate from <see cref="NotifyOnPayment"/>. Muting an
    /// in-app popup is not consent to stop receiving email, and the two travel
    /// differently: the popup needs the app open, the email does not.
    ///
    /// Also requires a verified owner email server-side, so this being true is
    /// necessary but not sufficient.
    /// </remarks>
    [JsonPropertyName("emailOwnerOnPayment")]
    public bool EmailOwnerOnPayment { get; set; } = true;

    /// <summary>
    /// Whether the server sends automatic overdue reminders to customers at 3,
    /// 7 and 14 days past an invoice's due date.
    /// </summary>
    /// <remarks>
    /// Opt-in, so it defaults to false: turning this on emails real customers,
    /// which is never a safe default to inherit. Also a cache of server state.
    /// </remarks>
    [JsonPropertyName("sendPaymentReminders")]
    public bool SendPaymentReminders { get; set; }

    /// <summary>
    /// When reminders were last switched on, as reported by the server. Only
    /// invoices falling due after this are ever chased, so switching reminders
    /// on never releases a backlog of already-overdue invoices. Display only.
    /// </summary>
    [JsonPropertyName("remindersEnabledAt")]
    public DateTime? RemindersEnabledAt { get; set; }

    /// <summary>
    /// The business name shown to customers on the portal.
    /// </summary>
    /// <remarks>
    /// A cache of server state, like the notification flags: the server wins on load. Kept
    /// locally because the name has to be entered before a payment provider can be connected,
    /// and until that happens there is no server record to hold it.
    /// </remarks>
    [JsonPropertyName("companyName")]
    public string? CompanyName { get; set; }

    /// <summary>
    /// The customer-facing portal URL for this company (returned by the server during setup).
    /// </summary>
    [JsonPropertyName("portalUrl")]
    public string? PortalUrl { get; set; }

    /// <summary>
    /// Timestamp of the last successful sync.
    /// </summary>
    [JsonPropertyName("lastSyncTime")]
    public DateTime? LastSyncTime { get; set; }

    /// <summary>
    /// Connected payment account info (which providers the user has connected).
    /// </summary>
    [JsonPropertyName("connectedAccounts")]
    public ConnectedPaymentAccounts ConnectedAccounts { get; set; } = new();
}

/// <summary>
/// Tracks which payment provider accounts the user has connected via OAuth.
/// </summary>
public class ConnectedPaymentAccounts
{
    /// <summary>
    /// Whether Stripe is connected via Stripe Connect.
    /// </summary>
    [JsonPropertyName("stripeConnected")]
    public bool StripeConnected { get; set; }

    /// <summary>
    /// The email associated with the connected Stripe account.
    /// </summary>
    [JsonPropertyName("stripeEmail")]
    public string? StripeEmail { get; set; }

    /// <summary>
    /// Whether PayPal is connected.
    /// </summary>
    [JsonPropertyName("paypalConnected")]
    public bool PaypalConnected { get; set; }

    /// <summary>
    /// The email associated with the connected PayPal account.
    /// </summary>
    [JsonPropertyName("paypalEmail")]
    public string? PaypalEmail { get; set; }

    /// <summary>
    /// Whether Square is connected via Square OAuth.
    /// </summary>
    [JsonPropertyName("squareConnected")]
    public bool SquareConnected { get; set; }

    /// <summary>
    /// The email/name associated with the connected Square account.
    /// </summary>
    [JsonPropertyName("squareEmail")]
    public string? SquareEmail { get; set; }

}
