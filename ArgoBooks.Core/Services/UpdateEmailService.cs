using System.Text;
using System.Text.Json;
using ArgoBooks.Core.Validation;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Signs someone up for product update emails.
///
/// The app deliberately requires no account, which means there is otherwise no way to reach
/// anyone using it: to tell them what shipped, to ask why they stopped, or to answer a problem
/// they never reported. This is the one optional place an address can be offered, and nothing in
/// the app depends on it.
///
/// Posts to the same endpoint the website's free tools use, so there is one subscribe path and
/// one double opt-in flow. Nothing is stored locally but the fact that it was done, so the
/// address lives only on the list the person can unsubscribe from.
/// </summary>
public class UpdateEmailService(HttpClient httpClient, IErrorLogger? errorLogger = null) : IDisposable
{
    private static readonly string SubscribeUrl = $"{ApiConfig.BaseUrl}/api/tool-email.php";

    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public UpdateEmailService(IErrorLogger? errorLogger = null)
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }, errorLogger)
    {
        _ownsHttpClient = true;
    }

    /// <summary>
    /// Asks the server to send a confirmation link. Success here means the confirmation was
    /// sent, not that the person is on the list: they still have to click it.
    /// </summary>
    public async Task<UpdateEmailResult> SubscribeAsync(string email, CancellationToken cancellationToken = default)
    {
        email = (email ?? string.Empty).Trim();
        if (!DataValidator.IsValidEmail(email))
            return new UpdateEmailResult(false, "Please enter a valid email address.");

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                email,
                source = "desktop_app",
                subscribe = true
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await httpClient.PostAsync(SubscribeUrl, content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var ok = root.TryGetProperty("ok", out var okElement) && okElement.ValueKind == JsonValueKind.True;
            var message = root.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : null;

            return new UpdateEmailResult(ok, message ?? (ok
                ? "Almost there. Check your inbox for a link to confirm."
                : "That did not work. Please try again."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            errorLogger?.LogWarning($"Update email subscribe failed: {ex.Message}", "UpdateEmail");
            return new UpdateEmailResult(false, "Could not reach the server. Please check your connection and try again.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsHttpClient) httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}

public readonly record struct UpdateEmailResult(bool Success, string Message);
