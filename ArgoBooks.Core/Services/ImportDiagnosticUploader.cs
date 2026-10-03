using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Shared.Telemetry;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Sends a file that failed to import, so the reason can be looked at.
///
/// Only ever called after the user has said yes to a dialog naming the file. There
/// is deliberately no automatic path and no retry queue: if the send fails, it is
/// not attempted again, because a second silent attempt is not what they agreed to.
///
/// The server holds the file encrypted for a few days and deletes it whether or not
/// anyone reads it. Nothing is sent for a successful import.
/// </summary>
public static class ImportDiagnosticUploader
{
    /// <summary>The same ceiling the server enforces, checked here so a large file fails locally.</summary>
    public const long MaxBytes = 12 * 1024 * 1024;

    private sealed class UploadBody
    {
        [JsonPropertyName("kind")]       public string Kind { get; set; } = "";
        [JsonPropertyName("reason")]     public string Reason { get; set; } = "";
        [JsonPropertyName("content")]    public string Content { get; set; } = "";
        [JsonPropertyName("appVersion")] public string? AppVersion { get; set; }
        [JsonPropertyName("pageCount")]  public int? PageCount { get; set; }
    }

    /// <summary>
    /// Posts the file. Returns true when the server accepted it. Never throws: a
    /// diagnostic send failing must not turn into a second thing going wrong in
    /// front of someone whose import already did not work.
    /// </summary>
    public static async Task<bool> SendAsync(
        string filePath,
        string reason,
        IErrorLogger? errorLogger = null,
        int? pageCount = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists || info.Length == 0 || info.Length > MaxBytes)
            {
                return false;
            }

            var kind = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
            if (kind is not ("PDF" or "CSV" or "XLSX" or "XLS"))
            {
                return false;
            }

            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"{ApiConfig.BaseUrl}/api/import-diagnostic/upload.php");
            LicenseAuthHelper.AddAuthHeaders(request);

            request.Content = JsonContent.Create(new UploadBody
            {
                Kind       = kind,
                Reason     = reason,
                Content    = Convert.ToBase64String(bytes),
                AppVersion = AppInfo.VersionNumber,
                PageCount  = pageCount,
            });

            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                errorLogger?.LogWarning(
                    $"Import diagnostic upload refused: {(int)response.StatusCode}",
                    "ImportDiagnostic",
                    ErrorCategory.Network,
                    ((int)response.StatusCode).ToString());
                return false;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            errorLogger?.LogWarning(
                $"Import diagnostic upload failed: {ex.GetType().Name}",
                "ImportDiagnostic",
                ErrorCategory.Network,
                ex.GetType().Name);
            return false;
        }
    }
}
