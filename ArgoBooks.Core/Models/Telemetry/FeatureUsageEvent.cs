using ArgoBooks.Shared.Telemetry;

namespace ArgoBooks.Core.Models.Telemetry;

/// <summary>
/// Feature usage event for tracking which features are used.
/// </summary>
public class FeatureUsageEvent : TelemetryEvent
{
    /// <inheritdoc />
    public override TelemetryDataType DataType => TelemetryDataType.FeatureUsage;

    /// <summary>
    /// Name of the feature used.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FeatureName FeatureName { get; set; }

    /// <summary>
    /// Additional context about the feature usage (e.g., chart type, report type).
    /// </summary>
    public string? Context { get; set; }

    /// <summary>
    /// Duration of the operation in milliseconds (when applicable, e.g., data imports).
    /// </summary>
    public long? DurationMs { get; set; }
}
