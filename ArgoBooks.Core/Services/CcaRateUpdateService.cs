namespace ArgoBooks.Core.Services;

/// <summary>
/// Fetches CRA's Capital Cost Allowance rates from the website, so a budget's change to them reaches everyone as a file upload rather than an app release.
/// </summary>
public class CcaRateUpdateService
{
    private static readonly string Url = ApiConfig.BaseUrl + "/resources/downloads/assets/" + CcaRateService.FileName;

    private readonly CcaRateService _rates;
    private readonly HttpClient _http;

    /// <param name="handler">Given by tests so the refusals can be checked without a server, and left null in the app.</param>
    public CcaRateUpdateService(CcaRateService rates, HttpMessageHandler? handler = null)
    {
        _rates = rates ?? throw new ArgumentNullException(nameof(rates));
        _http = handler == null
            ? new HttpClient { Timeout = TimeSpan.FromSeconds(30) }
            : new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>
    /// Downloads the rates and keeps them when they parse, pass <see cref="CcaRateValidator"/> and are a newer revision than the ones in use.
    /// </summary>
    /// <returns>True only when this call replaced the rates in use.</returns>
    public async Task<bool> TryUpdateAsync(CancellationToken cancellationToken = default)
    {
        string json;
        try
        {
            using HttpResponseMessage response = await _http.GetAsync(Url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return false;
            json = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Offline, a timeout or a cancelled check all leave the rates in use as they are.
            return false;
        }

        var downloaded = CcaRateService.ParseValid(json);
        if (downloaded == null || downloaded.Revision <= (_rates.Current?.Revision ?? 0))
            return false;

        try
        {
            Directory.CreateDirectory(_rates.CacheDirectory);
            var destination = Path.Combine(_rates.CacheDirectory, CcaRateService.FileName);
            var temporary = destination + ".tmp";

            // Written aside and moved into place, because a reader that found a half-written file would treat it as corrupt.
            File.WriteAllText(temporary, json);
            File.Move(temporary, destination, overwrite: true);

            _rates.Invalidate();
            return true;
        }
        catch (Exception)
        {
            // A read-only or full disk leaves the built-in rates working.
            return false;
        }
    }
}
