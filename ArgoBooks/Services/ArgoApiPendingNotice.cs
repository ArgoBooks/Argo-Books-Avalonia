using ArgoBooks.Core.Data;
using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.Integrations;
using ArgoBooks.Localization;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Services;

/// <summary>
/// Tells the user when a connected app has sent data that is still waiting to be imported.
///
/// A developer pushes on their own schedule, so without this nothing would tell the merchant
/// anything had arrived at all. It is checked when a company opens and on every visit to the
/// Revenue page, and both share one memory of what was last told, so the same count is never
/// announced twice.
/// </summary>
public static class ArgoApiPendingNotice
{
    private static (string? Key, int Count) _lastTold;

    /// <summary>
    /// How many items are waiting, or null when the company has no API key or the server can't be
    /// reached. GET /v1/account returns every type's count in one call, so this is a light check.
    /// </summary>
    /// <param name="notify">Shows the notice. Called only when the count is new for this company.</param>
    public static async Task<int?> CheckAsync(CompanyData? data, HttpClient? http, Action? notify = null)
    {
        var api = data?.Settings.Integrations.ArgoApi;
        if (api == null || !api.Enabled || string.IsNullOrWhiteSpace(api.DesktopKey) || http == null)
            return null;

        int count;
        try
        {
            var account = await new ArgoApiClient(http).GetAccountAsync(api.DesktopKey);
            // Pending is absent rather than empty if the server ever omits it.
            count = account?.Pending?.Values.Sum() ?? 0;
        }
        catch
        {
            // Unreachable or the key was revoked: stay quiet rather than raise connectivity errors
            // over a server the merchant does not control.
            return null;
        }

        if (count > 0 && _lastTold != (api.DesktopKey, count))
            (notify ?? NotifyUser)();
        _lastTold = (api.DesktopKey, count);
        return count;
    }

    /// <summary>
    /// Forgets what was last told, so the next check announces its count even if it is the same.
    /// After an undo the count returns to what it was before the import, which would otherwise be
    /// taken for a repeat.
    /// </summary>
    public static void Forget() => _lastTold = default;

    private static void NotifyUser() =>
        App.AddNotification(
            "Argo Books API".Translate(),
            "New data is waiting to be imported.".Translate(),
            NotificationType.Info,
            () => App.NavigationService?.NavigateTo(PageNames.Revenue));
}
