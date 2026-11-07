using System.Text;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Platform;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Handles exporting chart data to Google Sheets via the argorobots.com server proxy.
/// </summary>
public class GoogleSheetsService
{
    private static readonly string ExportEndpoint = $"{ApiConfig.BaseUrl}/api/google/sheets/export.php";

    private readonly HttpClient _httpClient;
    private readonly IErrorLogger? _errorLogger;

    /// <summary>
    /// Creates a new instance of the GoogleSheetsService.
    /// </summary>
    public GoogleSheetsService(IErrorLogger? errorLogger = null)
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _errorLogger = errorLogger;
    }

    /// <summary>
    /// Chart type for Google Sheets visualization.
    /// </summary>
    public enum ChartType
    {
        Line,
        Spline,
        Column,
        Pie,
        Area,
        StepLine,
        Scatter
    }

    /// <summary>
    /// Exports pre-formatted chart data (from ChartLoaderService) to Google Sheets.
    /// </summary>
    public async Task<string?> ExportFormattedDataToGoogleSheetsAsync(
        List<List<object>> exportData,
        string chartTitle,
        ChartType chartType,
        string companyName,
        CancellationToken cancellationToken = default)
    {
        if (exportData.Count == 0)
            return null;

        // First row is headers
        var headers = exportData[0].Select(h => h.ToString() ?? "").ToArray();
        var rows = exportData.Skip(1).ToList();

        var sheet = new
        {
            name = "Chart Data",
            headers,
            rows,
            numberFormat = "#,##0.00"
        };

        return await SendExportRequestAsync(
            $"{companyName} - {chartTitle} - {DateTime.Today:yyyy-MM-dd}",
            new[] { sheet },
            new { type = MapChartType(chartType), title = chartTitle },
            true,
            cancellationToken);
    }

    private async Task<string?> SendExportRequestAsync(
        string title,
        object sheets,
        object chartConfig,
        bool shareAsReader,
        CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            title,
            sheets,
            chartConfig,
            shareAsReader
        };

        var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, ExportEndpoint);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        GoogleCredentialsManager.AddAuthHeaders(request);

        HttpResponseMessage response;
        string responseBody;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
            responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(await ConnectivityMessage.ResolveAsync(), ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(await ConnectivityMessage.ResolveAsync(), ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            // Extract server error message
            var errorMsg = "Google Sheets export failed.";
            string? errorCode = null;
            try
            {
                using var errorDoc = JsonDocument.Parse(responseBody);
                if (errorDoc.RootElement.TryGetProperty("message", out var msg))
                    errorMsg = msg.GetString() ?? errorMsg;
                if (errorDoc.RootElement.TryGetProperty("errorCode", out var code) && code.ValueKind == JsonValueKind.String)
                    errorCode = code.GetString();
            }
            catch { /* ignore parse errors */ }

            var report = $"Google Sheets export proxy error {response.StatusCode}: {errorMsg}";
            var reportCode = errorCode ?? $"Http{(int)response.StatusCode}";
            // The user's own Google connection missing or expired, or our rate limit, is something they fix by reconnecting or waiting.
            if (errorCode is "NOT_AUTHENTICATED" or "TOKEN_EXPIRED" or "RATE_LIMITED")
                _errorLogger?.LogWarning(report, "Google Sheets export", ErrorCategory.Api, reportCode);
            else
                _errorLogger?.LogError(report, ErrorCategory.Api, "Google Sheets export", reportCode);

            var failure = new InvalidOperationException(errorMsg);
            ErrorLogger.MarkReported(failure);
            throw failure;
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        if (root.TryGetProperty("success", out var success) && success.GetBoolean()
            && root.TryGetProperty("spreadsheetUrl", out var url))
        {
            return url.GetString();
        }

        return null;
    }

    private static string MapChartType(ChartType chartType)
    {
        return chartType switch
        {
            ChartType.Line => "line",
            ChartType.Spline => "line",
            ChartType.Column => "column",
            ChartType.Pie => "pie",
            ChartType.Area => "area",
            ChartType.StepLine => "stepped_area",
            ChartType.Scatter => "scatter",
            _ => "column"
        };
    }
}
