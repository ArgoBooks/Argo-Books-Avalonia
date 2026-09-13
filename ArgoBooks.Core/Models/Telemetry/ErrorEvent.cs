using ArgoBooks.Core.Services;

namespace ArgoBooks.Core.Models.Telemetry;

/// <summary>
/// Error event for tracking application errors.
/// </summary>
public class ErrorEvent : TelemetryEvent
{
    /// <inheritdoc />
    public override TelemetryDataType DataType => TelemetryDataType.Error;

    /// <summary>
    /// How serious this is. <see cref="LogLevel.Error"/> is a genuine failure;
    /// <see cref="LogLevel.Warning"/> is an expected, handled condition that is worth
    /// knowing about but is not a defect (a company file saved by a newer build, say).
    /// Only these two levels ever reach telemetry. The dashboard keeps warnings out of
    /// its error charts, and treats an event with no severity at all as an error.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LogLevel Severity { get; set; } = LogLevel.Error;

    /// <summary>
    /// Error code or exception type name.
    /// </summary>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>
    /// Category of the error.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ErrorCategory ErrorCategory { get; set; }

    /// <summary>
    /// Sanitized error message (PII removed).
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Source file where the error occurred (filename only, no path).
    /// </summary>
    public string? SourceFile { get; set; }

    /// <summary>
    /// Line number where the error occurred.
    /// </summary>
    public int? LineNumber { get; set; }

    /// <summary>
    /// Method name where the error occurred.
    /// </summary>
    public string? MethodName { get; set; }

    /// <summary>
    /// What the call site was attempting ("Invoice usage check failed"). Written by us
    /// rather than taken from an exception, so unlike Message it is safe on errors too.
    /// </summary>
    public string? Context { get; set; }
}
