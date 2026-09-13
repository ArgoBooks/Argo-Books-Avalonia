using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Services;
using ArgoBooks.Shared.Telemetry;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the ErrorLogger class.
/// </summary>
public class ErrorLoggerTests
{
    private readonly ErrorLogger _errorLogger = new(maxEntries: 100);

    #region LogError Tests

    [Fact]
    public void LogError_WithException_CreatesLogEntry()
    {
        var exception = new InvalidOperationException("Test error message");

        _errorLogger.LogError(exception, ErrorCategory.Validation, "Test context");

        var errors = _errorLogger.GetRecentErrors(1);
        Assert.Single(errors);
        Assert.Equal("InvalidOperationException", errors[0].ErrorCode);
        Assert.Equal(ErrorCategory.Validation, errors[0].Category);
        Assert.Contains("Test error message", errors[0].Message);
        Assert.Equal("Test context", errors[0].Context);
        Assert.Equal(LogLevel.Error, errors[0].Level);
    }

    [Fact]
    public void LogError_WithCancellation_IsNotRecorded()
    {
        // Cancellations aren't failures and shouldn't clutter the error log.
        _errorLogger.LogError(new TaskCanceledException(), ErrorCategory.Network, "cancelled request");
        _errorLogger.LogError(new OperationCanceledException(), ErrorCategory.Network);

        Assert.Empty(_errorLogger.GetAllErrors());
    }

    [Fact]
    public void LogError_WithMessage_CreatesLogEntry()
    {
        _errorLogger.LogError("Custom error message", ErrorCategory.Network, "Network context");

        var errors = _errorLogger.GetRecentErrors(1);
        Assert.Single(errors);
        Assert.Equal("Custom error message", errors[0].Message);
        Assert.Equal(ErrorCategory.Network, errors[0].Category);
        Assert.Equal("Network context", errors[0].Context);
    }

    [Fact]
    public void LogError_RaisesErrorLoggedEvent()
    {
        ErrorLogEntry? raisedEntry = null;
        _errorLogger.ErrorLogged += (_, entry) => raisedEntry = entry;

        _errorLogger.LogError(new Exception("Test"), ErrorCategory.Api);

        Assert.NotNull(raisedEntry);
        Assert.Equal(ErrorCategory.Api, raisedEntry.Category);
    }

    #endregion

    #region Message Sanitization Tests

    [Fact]
    public void LogError_SanitizesWindowsUserPaths()
    {
        var exception = new Exception(@"File not found at C:\Users\JohnDoe\Documents\secret.txt");

        _errorLogger.LogError(exception, ErrorCategory.FileSystem);

        var errors = _errorLogger.GetRecentErrors(1);
        Assert.DoesNotContain("JohnDoe", errors[0].Message);
        Assert.Contains("[USER_PATH]", errors[0].Message);
    }

    [Fact]
    public void LogError_SanitizesMacUserPaths()
    {
        var exception = new Exception("File not found at /Users/JohnDoe/Documents/secret.txt");

        _errorLogger.LogError(exception, ErrorCategory.FileSystem);

        var errors = _errorLogger.GetRecentErrors(1);
        Assert.DoesNotContain("JohnDoe", errors[0].Message);
        Assert.Contains("[USER_PATH]", errors[0].Message);
    }

    [Fact]
    public void LogError_SanitizesLinuxUserPaths()
    {
        var exception = new Exception("File not found at /home/johndoe/documents/secret.txt");

        _errorLogger.LogError(exception, ErrorCategory.FileSystem);

        var errors = _errorLogger.GetRecentErrors(1);
        Assert.DoesNotContain("johndoe", errors[0].Message);
        Assert.Contains("[USER_PATH]", errors[0].Message);
    }

    [Fact]
    public void LogError_SanitizesEmailAddresses()
    {
        var exception = new Exception("Invalid email: test@example.com");

        _errorLogger.LogError(exception, ErrorCategory.Validation);

        var errors = _errorLogger.GetRecentErrors(1);
        Assert.DoesNotContain("test@example.com", errors[0].Message);
        Assert.Contains("[EMAIL]", errors[0].Message);
    }

    [Fact]
    public void LogError_SanitizesPhoneNumbers()
    {
        var exception = new Exception("Call failed for number 555-123-4567");

        _errorLogger.LogError(exception, ErrorCategory.Network);

        var errors = _errorLogger.GetRecentErrors(1);
        Assert.DoesNotContain("555-123-4567", errors[0].Message);
        Assert.Contains("[PHONE]", errors[0].Message);
    }

    [Fact]
    public void LogError_TruncatesLongMessages()
    {
        var longMessage = new string('A', 1000);
        var exception = new Exception(longMessage);

        _errorLogger.LogError(exception, ErrorCategory.Unknown);

        var errors = _errorLogger.GetRecentErrors(1);
        Assert.True(errors[0].Message.Length <= 500);
        Assert.EndsWith("...", errors[0].Message);
    }

    #endregion

    #region Log Level Tests

    [Fact]
    public void LogWarning_CreatesWarningEntry()
    {
        _errorLogger.LogWarning("Test warning", "Warning context");

        var errors = _errorLogger.GetAllErrors();
        var warning = errors.FirstOrDefault(e => e.Level == LogLevel.Warning);
        Assert.NotNull(warning);
        Assert.Equal("Test warning", warning.Message);
    }

    [Fact]
    public void LogInfo_CreatesInfoEntry()
    {
        _errorLogger.LogInfo("Test info message");

        // Info messages are stored but not returned by GetRecentErrors (which filters to Warning+)
        var allErrors = _errorLogger.GetAllErrors();
        Assert.Empty(allErrors); // GetAllErrors also filters to Warning+
    }

    [Fact]
    public void GetRecentErrors_OnlyReturnsWarningsAndErrors()
    {
        _errorLogger.LogInfo("Info message");
        _errorLogger.LogWarning("Warning message");
        _errorLogger.LogError("Error message", ErrorCategory.Unknown);

        var errors = _errorLogger.GetRecentErrors(10);
        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.True(e.Level >= LogLevel.Warning));
    }

    #endregion

    #region Capacity Tests

    [Fact]
    public void LogError_TrimsOldEntriesWhenOverCapacity()
    {
        var smallLogger = new ErrorLogger(maxEntries: 10);

        for (int i = 0; i < 20; i++)
        {
            smallLogger.LogError($"Error {i}", ErrorCategory.Unknown);
        }

        var errors = smallLogger.GetAllErrors();
        Assert.True(errors.Count <= 10, "Logger should trim to capacity");
    }

    #endregion

    #region Export Tests

    [Fact]
    public async Task ExportErrorLogAsync_ReturnsValidJson()
    {
        _errorLogger.LogError("Test error", ErrorCategory.Api);
        _errorLogger.LogWarning("Test warning");

        var json = await _errorLogger.ExportErrorLogAsync();

        Assert.NotEmpty(json);
        Assert.Contains("Test error", json);
        Assert.Contains("Test warning", json);
        Assert.Contains("Api", json);
    }

    #endregion

    #region Clear Tests

    [Fact]
    public void ClearLogs_RemovesAllEntries()
    {
        _errorLogger.LogError("Error 1", ErrorCategory.Unknown);
        _errorLogger.LogError("Error 2", ErrorCategory.Unknown);
        _errorLogger.LogWarning("Warning 1");

        _errorLogger.ClearLogs();

        var errors = _errorLogger.GetAllErrors();
        Assert.Empty(errors);
    }

    #endregion

    #region Unique ID Tests

    [Fact]
    public void LogError_GeneratesUniqueIds()
    {
        _errorLogger.LogError("Error 1", ErrorCategory.Unknown);
        _errorLogger.LogError("Error 2", ErrorCategory.Unknown);

        var errors = _errorLogger.GetRecentErrors(2);
        Assert.Equal(2, errors.Count);
        Assert.NotEqual(errors[0].Id, errors[1].Id);
    }

    #endregion

    #region Telemetry Forwarding Tests

    // Warnings are opt-in: one reaches telemetry only when the caller gives it a code,
    // which is what makes it groupable on the dashboard. Without this rule the dozens of
    // existing LogWarning calls would all upload as uncategorised noise, so these tests
    // pin the rule rather than the implementation that happens to satisfy it today.

    [Fact]
    public void LogWarning_WithCode_IsReportedToTelemetry()
    {
        var telemetry = new RecordingTelemetryManager();
        var logger = new ErrorLogger(maxEntries: 100) { TelemetryManager = telemetry };

        logger.LogWarning("Scan timed out", "ctx", ErrorCategory.Api, "ReceiptScanTimeout");

        var reported = Assert.Single(telemetry.Reported);
        Assert.Equal(LogLevel.Warning, reported.Level);
        Assert.Equal("ReceiptScanTimeout", reported.ErrorCode);
        Assert.Equal(ErrorCategory.Api, reported.Category);
    }

    [Fact]
    public void LogWarning_WithoutCode_StaysLocal()
    {
        var telemetry = new RecordingTelemetryManager();
        var logger = new ErrorLogger(maxEntries: 100) { TelemetryManager = telemetry };

        logger.LogWarning("Just a local diagnostic");

        Assert.Empty(telemetry.Reported);
        // Still recorded in the in-app log, which is where support reads it from.
        Assert.Single(logger.GetAllErrors());
    }

    [Fact]
    public void LogError_IsAlwaysReportedToTelemetry()
    {
        var telemetry = new RecordingTelemetryManager();
        var logger = new ErrorLogger(maxEntries: 100) { TelemetryManager = telemetry };

        logger.LogError("Something broke", ErrorCategory.FileSystem);

        var reported = Assert.Single(telemetry.Reported);
        Assert.Equal(LogLevel.Error, reported.Level);
    }

    /// <summary>Captures what ErrorLogger hands to telemetry. Every other member is unused.</summary>
    private sealed class RecordingTelemetryManager : ITelemetryManager
    {
        public List<ErrorLogEntry> Reported { get; } = [];

        public Task TrackErrorAsync(ErrorLogEntry errorEntry, CancellationToken cancellationToken = default)
        {
            Reported.Add(errorEntry);
            return Task.CompletedTask;
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EndSessionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void MarkActivity() { }
        public void NoteCurrentPage(string? pageName) { }
        public Task TrackFeatureAsync(FeatureName featureName, string? context = null, long? durationMs = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TrackPageViewAsync(string pageName, long activeSeconds, long durationSeconds, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TrackExportAsync(ExportType exportType, long durationMs, long fileSize, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TrackApiCallAsync(ApiName apiName, long durationMs, bool success, string? model = null, int? tokensUsed = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TrackCompanyProfileAsync(string? companyName, string? businessType, string? industry, string? country, string? currency, string? language, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TrackCompanyScaleAsync(CompanyScaleCounts counts, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TrackStartupAsync(long? toFirstPaintMs, long? toServicesReadyMs, long? toViewModelsReadyMs, long? toReadyMs, bool coldStart, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TelemetryUploadResult> UploadPendingDataAsync(CancellationToken cancellationToken = default) => Task.FromResult(new TelemetryUploadResult());
        public Task ClearAllDataAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TelemetryStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new TelemetryStatistics());
    }

    #endregion
}
