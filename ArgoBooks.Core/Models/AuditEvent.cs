using ArgoBooks.Core.Models.Common;

namespace ArgoBooks.Core.Models;

/// <summary>
/// Represents a recorded change in the company data for version history and audit trail.
/// Each event captures what changed, when, and by whom.
/// </summary>
/// <remarks>
/// FUTURE MULTI-ACCOUNTANT SUPPORT:
/// When multi-accountant support is added, set <see cref="AccountantId"/> and <see cref="AccountantName"/>
/// to identify which accountant performed the action. The version history modal can then filter by
/// accountant and show per-user activity. The server-side sync layer can use these fields to merge
/// event streams from multiple clients and detect conflicts (e.g., two accountants editing the same entity).
/// </remarks>
public class AuditEvent : IRecord
{
    /// <summary>
    /// Unique identifier for this event.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// When the event occurred (UTC).
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// The type of action performed.
    /// </summary>
    [JsonPropertyName("action")]
    public AuditAction Action { get; set; }

    /// <summary>
    /// The type of entity that was affected (e.g., "Customer", "Expense", "Invoice").
    /// </summary>
    [JsonPropertyName("entityType")]
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable display name of the affected entity.
    /// </summary>
    [JsonPropertyName("entityName")]
    public string EntityName { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description of the action (e.g., "Add customer 'Acme Corp'").
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Field-level changes for Modified actions. Key is the field name.
    /// </summary>
    [JsonPropertyName("changes")]
    public Dictionary<string, FieldChange>? Changes { get; set; }

    /// <summary>
    /// If this event is itself an undo/redo of another event, the ID of that original event.
    /// </summary>
    [JsonPropertyName("relatedEventId")]
    public string? RelatedEventId { get; set; }

    /// <summary>
    /// Runtime-only flag indicating whether this event has been persisted to disk.
    /// Events loaded from file are marked as saved; new in-session events start unsaved.
    /// Not serialized to JSON.
    /// </summary>
    [JsonIgnore]
    public bool IsSaved { get; set; }

}

/// <summary>
/// Represents a single field-level change within a Modified event.
/// </summary>
public class FieldChange
{
    /// <summary>
    /// The previous value (as a display string).
    /// </summary>
    [JsonPropertyName("oldValue")]
    public string? OldValue { get; set; }

    /// <summary>
    /// The new value (as a display string).
    /// </summary>
    [JsonPropertyName("newValue")]
    public string? NewValue { get; set; }
}

/// <summary>
/// The type of action recorded in an audit event.
/// </summary>
public enum AuditAction
{
    /// <summary>A new entity was created.</summary>
    Added,

    /// <summary>An existing entity was modified.</summary>
    Modified,

    /// <summary>An entity was deleted.</summary>
    Deleted,

    /// <summary>A previous action was undone.</summary>
    Undone,

    /// <summary>A previously undone action was redone.</summary>
    Redone
}
