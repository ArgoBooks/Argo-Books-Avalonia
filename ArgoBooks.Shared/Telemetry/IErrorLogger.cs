using System.Runtime.CompilerServices;
using ArgoBooks.Core.Models.Telemetry;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Interface for centralized error logging throughout the application.
/// </summary>
public interface IErrorLogger
{
    /// <summary>
    /// Logs an exception with categorization.
    /// </summary>
    /// <param name="exception">The exception to log.</param>
    /// <param name="category">Category of the error.</param>
    /// <param name="context">Optional context about where/why the error occurred.</param>
    void LogError(
        Exception exception,
        ErrorCategory category,
        string? context = null,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0,
        [CallerMemberName] string callerMember = "");

    /// <summary>
    /// Logs an error message without an exception.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <param name="category">Category of the error.</param>
    /// <param name="context">Optional context.</param>
    void LogError(
        string message,
        ErrorCategory category,
        string? context = null,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0,
        [CallerMemberName] string callerMember = "");

    /// <summary>
    /// Logs a warning message.
    /// </summary>
    /// <remarks>
    /// Warnings are local-only by default. A warning is uploaded to telemetry only when
    /// it is given a <paramref name="code"/>, which is what makes it identifiable on the
    /// dashboard (it groups by code the same way errors do). Without one, every warning
    /// would arrive as an uncategorised row with nothing to group or act on, so an
    /// un-coded warning stays in the in-app log where it is still useful for support.
    /// Add a code to a warning when you actually want to watch it in production.
    /// </remarks>
    /// <param name="message">Warning message.</param>
    /// <param name="context">Optional context.</param>
    /// <param name="category">Category, used only when <paramref name="code"/> is set.</param>
    /// <param name="code">Stable identifier. Supplying it opts this warning into telemetry.</param>
    void LogWarning(
        string message,
        string? context = null,
        ErrorCategory category = ErrorCategory.Unknown,
        string? code = null,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0,
        [CallerMemberName] string callerMember = "");

    /// <summary>
    /// Logs an informational message.
    /// </summary>
    /// <param name="message">Info message.</param>
    void LogInfo(string message);

    /// <summary>
    /// Logs a debug message (only in debug builds).
    /// </summary>
    /// <param name="message">Debug message.</param>
    void LogDebug(string message);

    /// <summary>
    /// Gets recent error log entries.
    /// </summary>
    /// <param name="count">Maximum number of entries to return.</param>
    /// <returns>List of recent error entries.</returns>
    IReadOnlyList<ErrorLogEntry> GetRecentErrors(int count = 50);

    /// <summary>
    /// Gets all error log entries.
    /// </summary>
    /// <returns>All error entries.</returns>
    IReadOnlyList<ErrorLogEntry> GetAllErrors();

    /// <summary>
    /// Exports the error log to a string (JSON format).
    /// </summary>
    /// <returns>JSON string of all error logs.</returns>
    Task<string> ExportErrorLogAsync();

    /// <summary>
    /// Clears all error logs.
    /// </summary>
    void ClearLogs();

    /// <summary>
    /// Event raised when a new error is logged.
    /// </summary>
    event EventHandler<ErrorLogEntry>? ErrorLogged;
}

/// <summary>
/// Represents a single error log entry.
/// </summary>
public class ErrorLogEntry
{
    /// <summary>
    /// Unique identifier for the log entry.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..16];

    /// <summary>
    /// When the error occurred.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Severity level of the entry.
    /// </summary>
    public LogLevel Level { get; set; }

    /// <summary>
    /// Category of the error.
    /// </summary>
    public ErrorCategory Category { get; set; }

    /// <summary>
    /// Error code or exception type.
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Error message (sanitized).
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Additional context.
    /// </summary>
    public string? Context { get; set; }

    /// <summary>
    /// Source file (filename only).
    /// </summary>
    public string? SourceFile { get; set; }

    /// <summary>
    /// Line number.
    /// </summary>
    public int? LineNumber { get; set; }

    /// <summary>
    /// Method name.
    /// </summary>
    public string? MethodName { get; set; }

    /// <summary>
    /// Sanitized stack trace (first few frames only).
    /// </summary>
    public string? StackTrace { get; set; }
}

/// <summary>
/// Log severity levels.
/// </summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}
