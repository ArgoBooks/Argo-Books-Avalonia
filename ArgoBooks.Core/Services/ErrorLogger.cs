using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ArgoBooks.Core.Models.Telemetry;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Centralized error logging service with telemetry integration.
/// </summary>
public partial class ErrorLogger : IErrorLogger
{
    private readonly ConcurrentQueue<ErrorLogEntry> _logEntries = new();
    private readonly int _maxEntries;
    private readonly Lock _trimLock = new();
    private readonly JsonSerializerOptions _jsonOptions;

    // Patterns for sanitizing PII from error messages and stack traces
    [GeneratedRegex(@"[A-Za-z]:\\Users\\[^\\]+", RegexOptions.IgnoreCase)]
    private static partial Regex WindowsUserPathRegex();

    [GeneratedRegex(@"/Users/[^/]+", RegexOptions.IgnoreCase)]
    private static partial Regex MacUserPathRegex();

    [GeneratedRegex(@"/home/[^/]+", RegexOptions.IgnoreCase)]
    private static partial Regex LinuxUserPathRegex();

    [GeneratedRegex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b\d{3}[-.]?\d{3}[-.]?\d{4}\b")]
    private static partial Regex PhoneRegex();

    /// <summary>
    /// Gets or sets the telemetry manager for reporting errors to telemetry.
    /// This is set after construction to avoid circular dependencies.
    /// </summary>
    public ITelemetryManager? TelemetryManager { get; set; }

    /// <inheritdoc />
    public event EventHandler<ErrorLogEntry>? ErrorLogged;

    /// <summary>
    /// Initializes a new instance of the ErrorLogger.
    /// </summary>
    /// <param name="maxEntries">Maximum number of log entries to keep in memory.</param>
    public ErrorLogger(int maxEntries = 1000)
    {
        _maxEntries = maxEntries;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    /// <inheritdoc />
    public void LogError(
        Exception exception,
        ErrorCategory category,
        string? context = null,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0,
        [CallerMemberName] string callerMember = "")
    {
        // A cancellation isn't a failure: a request cancelled on app close / navigation, or one that
        // timed out, is expected. Don't record it as an error (TaskCanceledException/OperationCanceled
        // were cluttering the error log). TaskCanceledException derives from OperationCanceledException.
        if (exception is OperationCanceledException)
            return;

        var entry = CreateLogEntry(LogLevel.Error, category, context, callerFile, callerLine, callerMember);
        entry.ErrorCode = exception.GetType().Name;
        entry.Message = SanitizeMessage(exception.Message);

        // Frame 0 of an async failure is the compiler's state machine, which knows nothing.
        var stackTrace = new StackTrace(exception, true);
        var frame = FirstLocatableFrame(stackTrace);
        if (frame != null)
        {
            var fileName = frame.GetFileName();
            entry.SourceFile = fileName != null ? Path.GetFileName(fileName) : null;
            entry.LineNumber = frame.GetFileLineNumber();
            entry.MethodName = DescribeMethod(frame);
        }

        // Sanitize and truncate stack trace
        entry.StackTrace = SanitizeStackTrace(exception.StackTrace, maxFrames: 5);

        // Append inner exception before enqueueing so subscribers see the full message
        if (exception.InnerException != null)
        {
            entry.Message += $" | Inner: {SanitizeMessage(exception.InnerException.Message)}";
        }

        AddEntry(entry);

        Debug.WriteLine($"[ERROR] [{category}] {entry.Message}");
    }

    /// <inheritdoc />
    public void LogError(
        string message,
        ErrorCategory category,
        string? context = null,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0,
        [CallerMemberName] string callerMember = "")
    {
        var entry = CreateLogEntry(LogLevel.Error, category, context, callerFile, callerLine, callerMember);
        entry.Message = SanitizeMessage(message);

        AddEntry(entry);
        Debug.WriteLine($"[ERROR] [{category}] {message}");
    }

    /// <inheritdoc />
    public void LogWarning(
        string message,
        string? context = null,
        ErrorCategory category = ErrorCategory.Unknown,
        string? code = null,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0,
        [CallerMemberName] string callerMember = "")
    {
        var entry = CreateLogEntry(LogLevel.Warning, category, context, callerFile, callerLine, callerMember);
        entry.Message = SanitizeMessage(message);
        entry.ErrorCode = code;

        AddEntry(entry);
        Debug.WriteLine($"[WARNING] [{category}] {message}");
    }

    /// <inheritdoc />
    public void LogInfo(string message)
    {
        var entry = CreateLogEntry(LogLevel.Info, ErrorCategory.Unknown, null);
        entry.Message = message;

        AddEntry(entry);
        Debug.WriteLine($"[INFO] {message}");
    }

    /// <inheritdoc />
    public void LogDebug(string message)
    {
#if DEBUG
        var entry = CreateLogEntry(LogLevel.Debug, ErrorCategory.Unknown, null);
        entry.Message = message;

        AddEntry(entry);
        Debug.WriteLine($"[DEBUG] {message}");
#endif
    }

    /// <inheritdoc />
    public IReadOnlyList<ErrorLogEntry> GetRecentErrors(int count = 50)
    {
        return _logEntries
            .Where(e => e.Level >= LogLevel.Warning)
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<ErrorLogEntry> GetAllErrors()
    {
        return _logEntries
            .Where(e => e.Level >= LogLevel.Warning)
            .OrderByDescending(e => e.Timestamp)
            .ToList();
    }

    /// <inheritdoc />
    public Task<string> ExportErrorLogAsync()
    {
        var entries = _logEntries.OrderByDescending(e => e.Timestamp).ToList();
        var json = JsonSerializer.Serialize(entries, _jsonOptions);
        return Task.FromResult(json);
    }

    /// <inheritdoc />
    public void ClearLogs()
    {
        while (_logEntries.TryDequeue(out _))
        {
            // Clear all entries
        }
    }

    private ErrorLogEntry CreateLogEntry(
        LogLevel level,
        ErrorCategory category,
        string? context,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0,
        [CallerMemberName] string callerMember = "")
    {
        return new ErrorLogEntry
        {
            Level = level,
            Category = category,
            Context = context,
            SourceFile = Path.GetFileName(callerFile),
            LineNumber = callerLine,
            MethodName = callerMember
        };
    }

    private void AddEntry(ErrorLogEntry entry)
    {
        _logEntries.Enqueue(entry);

        // Trim if over capacity
        if (_logEntries.Count > _maxEntries)
        {
            lock (_trimLock)
            {
                while (_logEntries.Count > _maxEntries * 0.9) // Trim to 90%
                {
                    _logEntries.TryDequeue(out _);
                }
            }
        }

        // Raise event
        ErrorLogged?.Invoke(this, entry);

        // Report to telemetry. Errors always go up. Warnings only go up when the caller
        // gave them a code, which is the opt-in: there are dozens of LogWarning calls
        // across the app and most are local diagnostics with no grouping key, so
        // uploading them wholesale would bury the ones worth watching.
        var reportable = entry.Level == LogLevel.Error
            || (entry.Level == LogLevel.Warning && !string.IsNullOrEmpty(entry.ErrorCode));
        if (reportable && TelemetryManager != null)
        {
            _ = TelemetryManager.TrackErrorAsync(entry);
        }
    }

    /// <summary>
    /// First frame that knows its file and line, or frame 0 when none do, which is the
    /// usual case: released builds ship without PDBs.
    /// </summary>
    private static StackFrame? FirstLocatableFrame(StackTrace stackTrace)
    {
        for (int i = 0; i < stackTrace.FrameCount; i++)
        {
            StackFrame? candidate = stackTrace.GetFrame(i);
            if (candidate?.GetFileName() != null && candidate.GetFileLineNumber() > 0)
            {
                return candidate;
            }
        }

        return stackTrace.FrameCount > 0 ? stackTrace.GetFrame(0) : null;
    }

    /// <summary>
    /// Names a frame's method, unwrapping the generated &lt;SendInvoiceAsync&gt;d__12 type
    /// an async method compiles to. Without this every async failure reports "MoveNext".
    /// </summary>
    private static string? DescribeMethod(StackFrame frame)
    {
        var method = frame.GetMethod();
        if (method == null)
        {
            return null;
        }

        var declaring = method.DeclaringType;
        if (declaring == null)
        {
            return method.Name;
        }

        if (declaring.Name.StartsWith('<'))
        {
            int close = declaring.Name.IndexOf('>');
            string inner = close > 1 ? declaring.Name[1..close] : declaring.Name;
            string? owner = declaring.DeclaringType?.Name;
            return owner != null ? $"{owner}.{inner}" : inner;
        }

        return $"{declaring.Name}.{method.Name}";
    }

    private static string SanitizeMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return string.Empty;

        var sanitized = message;

        // Remove user paths
        sanitized = WindowsUserPathRegex().Replace(sanitized, "[USER_PATH]");
        sanitized = MacUserPathRegex().Replace(sanitized, "[USER_PATH]");
        sanitized = LinuxUserPathRegex().Replace(sanitized, "[USER_PATH]");

        // Remove emails
        sanitized = EmailRegex().Replace(sanitized, "[EMAIL]");

        // Remove phone numbers
        sanitized = PhoneRegex().Replace(sanitized, "[PHONE]");

        // Truncate very long messages
        if (sanitized.Length > 500)
        {
            sanitized = sanitized[..497] + "...";
        }

        return sanitized;
    }

    private static string? SanitizeStackTrace(string? stackTrace, int maxFrames = 5)
    {
        if (string.IsNullOrEmpty(stackTrace))
            return null;

        var lines = stackTrace.Split('\n')
            .Take(maxFrames)
            .Select(line =>
            {
                var sanitized = WindowsUserPathRegex().Replace(line, "[USER_PATH]");
                sanitized = MacUserPathRegex().Replace(sanitized, "[USER_PATH]");
                sanitized = LinuxUserPathRegex().Replace(sanitized, "[USER_PATH]");
                return sanitized.Trim();
            })
            .Where(line => !string.IsNullOrWhiteSpace(line));

        return string.Join(" -> ", lines);
    }
}
