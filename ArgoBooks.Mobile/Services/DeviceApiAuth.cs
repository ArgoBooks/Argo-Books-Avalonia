using ArgoBooks.Core.Services;
using ArgoBooks.Shared.Mobile;

namespace ArgoBooks.Mobile.Services;

/// <summary>
/// IApiAuth adapter for the phone's own AI-proxy device identity (Option A from the capture
/// plan): adds the header the AI proxy's existing free-tier device auth already reads,
/// <c>X-Device-Id</c>, using a stable id generated once and persisted in SecureStorage. No
/// license key is sent, so each scan counts against this device's own free 10/month quota rather
/// than a paired owner's Premium 500/month (a later fast-follow can tie phone scans to the paired
/// owner instead - see the capture plan's Global Constraints).
///
/// Not unit-tested: <see cref="CreateAsync"/> only wraps <see cref="ISecureStore"/> reads/writes
/// (ISecureStore itself is unit-tested with an in-memory fake elsewhere), and
/// <see cref="AddAuthHeaders"/> is a one-line header set. Exercised end to end by the device
/// verification pass in the capture-flow report.
/// </summary>
public class DeviceApiAuth : IApiAuth
{
    private const string DeviceIdKey = "ai_proxy_device_id";

    private readonly string _deviceId;

    private DeviceApiAuth(string deviceId)
    {
        _deviceId = deviceId;
    }

    /// <inheritdoc />
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_deviceId);

    /// <inheritdoc />
    public void AddAuthHeaders(HttpRequestMessage request)
    {
        request.Headers.Remove("X-Device-Id");
        request.Headers.Add("X-Device-Id", _deviceId);
    }

    /// <summary>
    /// Loads this device's stable id from secure storage, generating and persisting a fresh one
    /// the first time this runs on a given install. Call once (e.g. at shell startup) and reuse
    /// the returned instance across scans rather than re-reading storage per scan.
    /// </summary>
    public static async Task<DeviceApiAuth> CreateAsync(ISecureStore secureStore)
    {
        if (secureStore == null) throw new ArgumentNullException(nameof(secureStore));

        var existing = await secureStore.GetAsync(DeviceIdKey);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return new DeviceApiAuth(existing);
        }

        var generated = Guid.NewGuid().ToString("N");
        await secureStore.SetAsync(DeviceIdKey, generated);
        return new DeviceApiAuth(generated);
    }
}
