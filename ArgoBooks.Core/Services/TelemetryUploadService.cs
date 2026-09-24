using System.Net.Http.Headers;
using System.Text;

using ArgoBooks.Core.Models.Telemetry;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Service for uploading telemetry data to the server.
/// </summary>
public class TelemetryUploadService : ITelemetryUploadService
{
    private static readonly string UploadUrl = $"{ApiConfig.BaseUrl}/api/data/upload";
    private const string UserAgentPrefix = "ArgoSalesTracker";
    private const int MaxRetries = 3;
    private const int BatchSize = 500;

    private readonly HttpClient _httpClient;
    private readonly ITelemetryStorageService _storageService;
    private readonly IErrorLogger? _errorLogger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _appVersion;

    // Two uploads at once would both read the same pending events before either marked them
    // sent, and the server would receive the batch twice.
    private readonly SemaphoreSlim _uploadGate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the TelemetryUploadService.
    /// </summary>
    public TelemetryUploadService(
        ITelemetryStorageService storageService,
        HttpClient? httpClient = null,
        IErrorLogger? errorLogger = null,
        string? appVersion = null)
    {
        _storageService = storageService;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _errorLogger = errorLogger;
        _appVersion = appVersion ?? "1.0.0";

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    /// <inheritdoc />
    public async Task<TelemetryUploadResult> UploadPendingDataAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _uploadGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new TelemetryUploadResult { Success = false, ErrorMessage = "Upload cancelled" };
        }

        try
        {
            return await UploadPendingCoreAsync(cancellationToken);
        }
        finally
        {
            _uploadGate.Release();
        }
    }

    private async Task<TelemetryUploadResult> UploadPendingCoreAsync(CancellationToken cancellationToken)
    {
        var result = new TelemetryUploadResult();

        try
        {
            if (!LicenseAuthHelper.IsConfigured)
            {
                result.Success = false;
                result.ErrorMessage = "No authentication available (no license key or device ID)";
                result.BackupFilePath = await _storageService.SaveBackupFileAsync(cancellationToken);
                return result;
            }

            var pendingEvents = await _storageService.GetPendingEventsAsync(cancellationToken);
            if (pendingEvents.Count == 0)
            {
                result.Success = true;
                result.EventsUploaded = 0;
                return result;
            }

            // Upload in batches
            var batches = pendingEvents
                .Select((e, i) => new { Event = e, Index = i })
                .GroupBy(x => x.Index / BatchSize)
                .Select(g => g.Select(x => x.Event).ToList())
                .ToList();

            var uploadedIds = new List<string>();

            foreach (var batch in batches)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batchResult = await UploadBatchWithRetryAsync(batch, cancellationToken);

                if (batchResult.Success)
                {
                    uploadedIds.AddRange(batch.Select(e => e.DataId));
                }
                else
                {
                    // Stop on first failure
                    result.ErrorMessage = batchResult.ErrorMessage;
                    break;
                }
            }

            // Mark uploaded events
            if (uploadedIds.Count > 0)
            {
                await _storageService.MarkEventsUploadedAsync(uploadedIds, cancellationToken);
            }

            result.Success = uploadedIds.Count == pendingEvents.Count;
            result.EventsUploaded = uploadedIds.Count;
            result.TotalPending = pendingEvents.Count;

            // If upload failed or was partial, save a backup file locally
            if (!result.Success)
            {
                result.BackupFilePath = await _storageService.SaveBackupFileAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.ErrorMessage = "Upload cancelled";
            result.BackupFilePath = await _storageService.SaveBackupFileAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Silent background work: a user whose connection dropped must not read as a
            // defect, and probing to find out would cost five seconds on a retry nobody
            // is waiting for. The events stay pending and go up on the next flush.
            NetworkFailure.Report(_errorLogger, ex, "Failed to upload telemetry data");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            // Save backup on failure
            result.BackupFilePath = await _storageService.SaveBackupFileAsync(CancellationToken.None);
        }

        return result;
    }

    private async Task<TelemetryUploadResult> UploadBatchWithRetryAsync(
        List<TelemetryEvent> batch,
        CancellationToken cancellationToken)
    {
        var result = new TelemetryUploadResult();

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    // Exponential backoff: 2s, 4s, 8s
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    await Task.Delay(delay, cancellationToken);
                }

                var uploadResult = await UploadBatchAsync(batch, cancellationToken);
                if (uploadResult.Success)
                {
                    return uploadResult;
                }

                result.ErrorMessage = uploadResult.ErrorMessage;

                // Don't retry on client errors (4xx) since they won't succeed on retry
                if (uploadResult.HttpStatusCode is >= 400 and < 500)
                {
                    break;
                }
            }
            catch (HttpRequestException ex)
            {
                result.ErrorMessage = ex.Message;
                _errorLogger?.LogDebug($"Upload attempt {attempt + 1} failed: {ex.Message}");
            }
        }

        return result;
    }

    private async Task<TelemetryUploadResult> UploadBatchAsync(
        List<TelemetryEvent> batch,
        CancellationToken cancellationToken)
    {
        var result = new TelemetryUploadResult();

        var payload = BuildPayload(batch);
        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        var jsonBytes = Encoding.UTF8.GetBytes(json);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(jsonBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Add(fileContent, "file", $"telemetry_{DateTime.UtcNow:yyyyMMddHHmmss}.json");

        using var request = new HttpRequestMessage(HttpMethod.Post, UploadUrl);
        request.Content = content;
        // AddAuthHeaders sets X-License-Key (Premium) and/or X-Device-Id (Free). Server picks tier based on which is valid.
        LicenseAuthHelper.AddAuthHeaders(request);
        request.Headers.UserAgent.ParseAdd($"{UserAgentPrefix}/{_appVersion}");

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        result.HttpStatusCode = (int)response.StatusCode;

        if (response.IsSuccessStatusCode)
        {
            result.Success = true;
            result.EventsUploaded = batch.Count;
        }
        else
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            result.Success = false;
            result.ErrorMessage = $"HTTP {(int)response.StatusCode}: {responseBody}";
        }

        return result;
    }

    /// <summary>
    /// Build the upload payload using an explicit allowlist of safe fields.
    /// Same shape for both free and premium tiers. Fields not present here cannot
    /// leak to the wire by construction.
    /// Forbidden fields: userAgent, geoLocation.city, geoLocation.hashedIp,
    /// ApiUsageEvent.model, ApiUsageEvent.tokensUsed.
    /// FeatureUsageEvent.context is sent: every value is a literal we wrote ("mapping",
    /// "timeout") or a page name, never anything read out of a company file, and it is
    /// what makes an event say which step failed rather than only that one did.
    /// ErrorEvent.message is sent scrubbed rather than withheld, since a warning's code
    /// alone rarely says what happened; see the note at the field itself for what that
    /// scrubbing does and does not cover.
    /// </summary>
    private object BuildPayload(List<TelemetryEvent> batch)
    {
        var first = batch[0];
        var events = batch.Select(BuildEventDto).Where(e => e != null).ToList();

        var payload = new Dictionary<string, object?>
        {
            ["uploadTime"] = DateTime.UtcNow,
            ["appVersion"] = _appVersion,
            ["platform"] = first.Platform,
            ["eventCount"] = events.Count,
            ["events"] = events,
        };

        if (first.GeoLocation != null)
        {
            payload["geoLocation"] = new
            {
                country = first.GeoLocation.Country,
                countryCode = first.GeoLocation.CountryCode,
                region = first.GeoLocation.Region,
                timezone = first.GeoLocation.Timezone,
            };
        }

        return payload;
    }

    private static object? BuildEventDto(TelemetryEvent e) => e switch
    {
        SessionEvent s => new
        {
            dataId = s.DataId,
            timestamp = s.Timestamp,
            dataType = "Session",
            action = s.Action.ToString(),
            durationSeconds = s.DurationSeconds,
            activeSeconds = s.ActiveSeconds,
            lastPage = s.LastPage,
            // Without this a recovered unclean end is indistinguishable from the clean one
            // for the same run, and the pair reads as a duplicate.
            clean = s.Clean,
        },
        FeatureUsageEvent f => new
        {
            dataId = f.DataId,
            timestamp = f.Timestamp,
            dataType = "FeatureUsage",
            featureName = f.FeatureName.ToString(),
            durationMs = f.DurationMs,
            context = f.Context,
        },
        PageViewEvent page => new
        {
            dataId = page.DataId,
            timestamp = page.Timestamp,
            dataType = "PageView",
            pageName = page.PageName,
            activeSeconds = page.ActiveSeconds,
            durationSeconds = page.DurationSeconds,
        },
        CompanyScaleEvent scale => new
        {
            dataId = scale.DataId,
            timestamp = scale.Timestamp,
            dataType = "CompanyScale",
            expenses = scale.Expenses,
            revenues = scale.Revenues,
            invoices = scale.Invoices,
            payments = scale.Payments,
            customers = scale.Customers,
            suppliers = scale.Suppliers,
            products = scale.Products,
            categories = scale.Categories,
            receipts = scale.Receipts,
            employees = scale.Employees,
            bankLines = scale.BankLines,
        },
        ErrorEvent err => new
        {
            dataId = err.DataId,
            timestamp = err.Timestamp,
            dataType = "Error",
            // The server keys errors vs warnings off this. Older builds omit it, and the
            // dashboard reads a missing severity as "Error".
            severity = err.Severity.ToString(),
            errorCategory = err.ErrorCategory.ToString(),
            errorCode = err.ErrorCode,
            // Scrubbed and length-capped by ErrorLogger.SanitizeMessage, which strips
            // emails, phone numbers, and the user segment of a home directory path.
            // It does not strip file names: a message naming D:\Books\Acme.argo, or
            // C:\Users\<user>\Books\Acme.argo, arrives with Acme.argo intact, and
            // customers name company files after the business. Every uploaded Error
            // carries a message.
            // Warnings need it: their code alone rarely says what actually happened.
            message = err.Message,
            sourceFile = err.SourceFile,
            lineNumber = err.LineNumber,
            methodName = err.MethodName,
            context = err.Context,
        },
        ExportEvent ex => new
        {
            dataId = ex.DataId,
            timestamp = ex.Timestamp,
            dataType = "Export",
            exportType = ex.ExportType.ToString(),
            durationMs = ex.DurationMs,
            fileSize = ex.FileSize,
        },
        ApiUsageEvent api => new
        {
            dataId = api.DataId,
            timestamp = api.Timestamp,
            dataType = "ApiUsage",
            apiName = api.ApiName.ToString(),
            durationMs = api.DurationMs,
            success = api.Success,
        },
        CompanyProfileEvent profile => new
        {
            dataId = profile.DataId,
            timestamp = profile.Timestamp,
            dataType = "CompanyProfile",
            companyName = profile.CompanyName,
            businessType = profile.BusinessType,
            industry = profile.Industry,
            country = profile.Country,
            currency = profile.Currency,
            language = profile.Language,
        },
        StartupEvent startup => new
        {
            dataId = startup.DataId,
            timestamp = startup.Timestamp,
            dataType = "Startup",
            toFirstPaintMs = startup.ToFirstPaintMs,
            toServicesReadyMs = startup.ToServicesReadyMs,
            toViewModelsReadyMs = startup.ToViewModelsReadyMs,
            toReadyMs = startup.ToReadyMs,
            coldStart = startup.ColdStart,
        },

        // Anything with no arm is dropped by the caller's null filter, but the whole batch is
        // still marked uploaded, so it is discarded permanently rather than retried. Both types
        // above shipped without one and were never sent at all.
        _ => null,
    };
}

/// <summary>
/// Interface for telemetry upload operations.
/// </summary>
public interface ITelemetryUploadService
{
    /// <summary>
    /// Uploads all pending telemetry data to the server.
    /// </summary>
    Task<TelemetryUploadResult> UploadPendingDataAsync(CancellationToken cancellationToken = default);
}
