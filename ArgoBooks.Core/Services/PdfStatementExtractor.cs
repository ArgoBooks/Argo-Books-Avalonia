using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Models.Telemetry;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Sends a PDF bank statement to the AI proxy and returns parsed rows. Mirrors the receipt
/// proxy's auth/multipart contract. Usage-counted by the caller.
/// </summary>
public class PdfStatementExtractor(IErrorLogger? errorLogger = null) : IPdfStatementExtractor
{
    private static readonly string ExtractEndpoint = $"{ApiConfig.BaseUrl}/api/bank/extract.php";
    // A long statement can take two reads on the server, and the server is told this so it knows whether a second read will finish in time.
    private const int WaitSeconds = 240;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(WaitSeconds + 10) };

    public bool IsConfigured => LicenseAuthHelper.IsConfigured;

    public async Task<StatementExtraction> ExtractAsync(byte[] pdfData, string fileName, CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(pdfData);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            content.Add(fileContent, "statement", fileName);
            content.Add(new StringContent(WaitSeconds.ToString(CultureInfo.InvariantCulture)), "waitSeconds");

            using var request = new HttpRequestMessage(HttpMethod.Post, ExtractEndpoint) { Content = content };
            LicenseAuthHelper.AddAuthHeaders(request);

            var wallClock = Stopwatch.StartNew();
            using var response = await Http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            wallClock.Stop();
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                throw ServerRateLimitedException.FromBody(body);
            // Anything else the server refused is not an empty statement.
            if (!response.IsSuccessStatusCode)
                throw StatementExtractionException.FromBody(body);
            RecordTiming(body, wallClock.Elapsed.TotalMilliseconds, pdfData.Length);
            return new StatementExtraction(ParseRows(body), ParseCheck(body));
        }
        catch (ServerRateLimitedException)
        {
            // Not a failure to read the file: the caller tells the user to wait instead.
            throw;
        }
        catch (StatementExtractionException)
        {
            // Same reason: the caller says what the server said, rather than blaming the file.
            throw;
        }
        catch (Exception ex)
        {
            errorLogger?.LogError(ex, ErrorCategory.Api, "PDF statement extraction failed");
            return new StatementExtraction([], null);
        }
    }

    public static List<BankStatementLine> ParseRows(string json)
    {
        var rows = new List<BankStatementLine>();
        JsonElement root;
        try
        {
            root = JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (JsonException)
        {
            return rows;
        }
        if (!root.TryGetProperty("success", out var ok) || !ok.GetBoolean()) return rows;
        if (!root.TryGetProperty("lines", out var lines) || lines.ValueKind != JsonValueKind.Array) return rows;

        foreach (var el in lines.EnumerateArray())
        {
            var line = new BankStatementLine
            {
                Id = Guid.NewGuid().ToString("N"),
                Description = el.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                Amount = el.TryGetProperty("amount", out var a) && a.TryGetDecimal(out var amt) ? amt : 0m
            };
            // InvariantCulture, as every other date parse here, or an ambiguous date such as 02/03/2023 reads as March 2 on a day-first machine.
            if (el.TryGetProperty("date", out var dt)
                && DateTime.TryParse(dt.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                line.Date = parsed;
            rows.Add(line);
        }
        return rows;
    }

    public static StatementCheck? ParseCheck(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("check", out var check) || check.ValueKind != JsonValueKind.Object)
                return null;
            if (!check.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String)
                return null;
            decimal? gap = check.TryGetProperty("gap", out var g) && g.ValueKind == JsonValueKind.Number && g.TryGetDecimal(out var value) ? value : null;
            return new StatementCheck(status.GetString()!, gap);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Feeds the server-measured extraction time (and load factor) from the response into the
    /// shared estimator so the bank-PDF progress bar self-calibrates. Best-effort.
    /// </summary>
    private static void RecordTiming(string body, double wallClockMs, long uploadBytes)
    {
        var service = OperationTimingService.Instance;
        if (service == null)
            return;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("timing", out var timing))
                return;
            double serverMs = timing.TryGetProperty("elapsed_ms", out var e) && e.TryGetDouble(out var ev) ? ev : 0;
            double? loadFactor = timing.TryGetProperty("load_factor", out var lf) && lf.TryGetDouble(out var lv) ? lv : null;
            service.RecordResult(OperationKind.BankPdfExtract, serverMs, wallClockMs, uploadBytes, loadFactor);
        }
        catch (JsonException)
        {
            // Best-effort: a malformed/older response just means no timing sample this call.
        }
    }
}
