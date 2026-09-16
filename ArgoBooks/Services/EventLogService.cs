using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models;

namespace ArgoBooks.Services;

/// <summary>
/// Service that manages the audit event log for version history. Records all entity changes as a
/// read-only history and persists events to the company file via CompanyData.EventLog. Toolbar
/// undo/redo (Ctrl+Z/Ctrl+Y) is recorded as its own history entries; there is no per-event undo.
/// </summary>
/// <remarks>
/// FUTURE MULTI-ACCOUNTANT SUPPORT:
/// When multi-accountant support is added:
/// 1. Set AccountantId/AccountantName on each event from the current session's accountant.
/// 2. Add filtering by accountant to GetEvents/GetGroupedEvents.
/// 3. Enforce permissions before allowing undo of another accountant's actions
///    (e.g., only admins can undo other accountants' changes).
/// 4. For sync, the event log becomes the unit of replication, merge event logs
///    from multiple clients using timestamp ordering and conflict detection.
/// </remarks>
public class EventLogService
{
    private readonly List<AuditEvent> _events = [];
    private readonly Dictionary<string, IUndoableAction> _undoableActions = new();
    private readonly Dictionary<IUndoableAction, string> _actionToEventId = new();
    private readonly int _maxEventCount;
    private UndoRedoManager? _undoRedoManager;
    private Dictionary<string, FieldChange>? _pendingChanges;

    /// <summary>
    /// Raised when an event is recorded or modified (for UI updates).
    /// </summary>
    public event EventHandler? EventsChanged;

    /// <summary>
    /// Creates a new EventLogService.
    /// </summary>
    /// <param name="maxEventCount">Maximum number of events to retain. Oldest events are trimmed.</param>
    public EventLogService(int maxEventCount = 10000)
    {
        _maxEventCount = maxEventCount;
    }

    /// <summary>
    /// Subscribes to the UndoRedoManager so a toolbar undo/redo (Ctrl+Z/Ctrl+Y) is recorded as an
    /// entry in the audit trail.
    /// </summary>
    public void SetUndoRedoManager(UndoRedoManager undoRedoManager)
    {
        if (_undoRedoManager != null)
        {
            _undoRedoManager.ActionUndone -= OnLinearUndo;
            _undoRedoManager.ActionRedone -= OnLinearRedo;
        }

        _undoRedoManager = undoRedoManager;
        _undoRedoManager.ActionUndone += OnLinearUndo;
        _undoRedoManager.ActionRedone += OnLinearRedo;
    }

    /// <summary>
    /// Called when UndoRedoManager performs a linear undo (Ctrl+Z).
    /// Finds the matching audit event and updates its state.
    /// </summary>
    private void OnLinearUndo(object? sender, ActionRecordedEventArgs e)
    {
        var eventId = _actionToEventId.GetValueOrDefault(e.Action);
        if (eventId == null) return;

        var evt = _events.FirstOrDefault(ev => ev.Id == eventId);
        if (evt == null) return;

        // Record the undo in the audit trail
        var undoEvent = new AuditEvent
        {
            Id = GenerateEventId(),
            Timestamp = DateTime.UtcNow,
            Action = AuditAction.Undone,
            EntityType = evt.EntityType,
            EntityName = evt.EntityName,
            Description = $"Undo: {evt.Description}",
            RelatedEventId = evt.Id
        };
        _events.Add(undoEvent);

        TrimIfNeeded();
        EventsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Called when UndoRedoManager performs a linear redo (Ctrl+Y).
    /// Finds the matching audit event and updates its state.
    /// </summary>
    private void OnLinearRedo(object? sender, ActionRecordedEventArgs e)
    {
        var eventId = _actionToEventId.GetValueOrDefault(e.Action);
        if (eventId == null) return;

        var evt = _events.FirstOrDefault(ev => ev.Id == eventId);
        if (evt == null) return;

        // Record the redo in the audit trail
        var redoEvent = new AuditEvent
        {
            Id = GenerateEventId(),
            Timestamp = DateTime.UtcNow,
            Action = AuditAction.Redone,
            EntityType = evt.EntityType,
            EntityName = evt.EntityName,
            Description = $"Redo: {evt.Description}",
            RelatedEventId = evt.Id
        };
        _events.Add(redoEvent);

        TrimIfNeeded();
        EventsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Initializes the service with persisted events from a loaded company file.
    /// </summary>
    public void Initialize(List<AuditEvent> persistedEvents)
    {
        _events.Clear();
        _undoableActions.Clear();
        _actionToEventId.Clear();

        if (persistedEvents.Count > _maxEventCount)
        {
            _events.AddRange(persistedEvents.Skip(persistedEvents.Count - _maxEventCount));
        }
        else
        {
            _events.AddRange(persistedEvents);
        }

        // Mark all loaded events as saved (they came from disk)
        foreach (var evt in _events)
        {
            evt.IsSaved = true;
        }

        EventsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Clears all events and action mappings (called when closing a company).
    /// </summary>
    public void Clear()
    {
        _events.Clear();
        _undoableActions.Clear();
        _actionToEventId.Clear();
        EventsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Sets pending field-level changes to be attached to the next recorded Modified event.
    /// Call this before RecordAction for an edit operation.
    /// </summary>
    public void SetPendingChanges(Dictionary<string, FieldChange> changes)
    {
        _pendingChanges = changes;
    }

    /// <summary>
    /// Records a new audit event linked to an undoable action.
    /// </summary>
    /// <param name="action">The undoable action (for selective undo support).</param>
    /// <param name="description">Human-readable description of the change.</param>
    /// <param name="auditAction">The type of action (Added, Modified, Deleted).</param>
    /// <param name="entityType">The entity type (e.g., "Customer", "Expense").</param>
    /// <param name="entityName">The entity's display name.</param>
    /// <param name="changes">Optional field-level changes for edit operations.</param>
    /// <returns>The created audit event.</returns>
    public AuditEvent RecordEvent(
        IUndoableAction action,
        string description,
        AuditAction auditAction,
        string entityType = "",
        string entityName = "",
        Dictionary<string, FieldChange>? changes = null)
    {
        var evt = new AuditEvent
        {
            Id = GenerateEventId(),
            Timestamp = DateTime.UtcNow,
            Action = auditAction,
            EntityType = entityType,
            EntityName = entityName,
            Description = description,
            Changes = changes
        };

        _events.Add(evt);
        _undoableActions[evt.Id] = action;
        _actionToEventId[action] = evt.Id;

        TrimIfNeeded();
        EventsChanged?.Invoke(this, EventArgs.Empty);

        return evt;
    }

    /// <summary>
    /// Records an audit event automatically from an IUndoableAction's description.
    /// Parses the description to extract action type, entity type, and entity name.
    /// </summary>
    public AuditEvent RecordFromAction(IUndoableAction action)
    {
        var (auditAction, entityType, entityName) = ParseActionDescription(action.Description);

        // Consume pending field-level changes for Modified events
        Dictionary<string, FieldChange>? changes = null;
        if (auditAction == AuditAction.Modified && _pendingChanges != null)
            changes = _pendingChanges;

        _pendingChanges = null;

        return RecordEvent(
            action,
            action.Description,
            auditAction,
            entityType,
            entityName: entityName,
            changes: changes);
    }

    /// <summary>
    /// Gets all events, newest first.
    /// </summary>
    public IReadOnlyList<AuditEvent> GetEvents()
    {
        return _events.AsReadOnly();
    }

    /// <summary>
    /// Gets events filtered by criteria, newest first.
    /// </summary>
    public IEnumerable<AuditEvent> GetFilteredEvents(
        AuditAction? actionFilter = null,
        string? entityTypeFilter = null,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        IEnumerable<AuditEvent> results = _events;

        if (actionFilter.HasValue)
            results = results.Where(e => e.Action == actionFilter.Value);

        if (!string.IsNullOrWhiteSpace(entityTypeFilter))
            results = results.Where(e => string.Equals(e.EntityType, entityTypeFilter, StringComparison.OrdinalIgnoreCase));

        if (fromDate.HasValue)
            results = results.Where(e => e.Timestamp >= fromDate.Value);

        if (toDate.HasValue)
            results = results.Where(e => e.Timestamp <= toDate.Value);

        return results;
    }

    /// <summary>
    /// Gets all distinct entity types present in the event log (for filter dropdown).
    /// </summary>
    public IEnumerable<string> GetEntityTypes()
    {
        return _events
            .Where(e => e.IsSaved && !string.IsNullOrEmpty(e.EntityType))
            .Select(e => e.EntityType)
            .Distinct()
            .OrderBy(t => t);
    }

    /// <summary>
    /// Commits pending (unsaved) events by checking which ones correspond to actions
    /// still in the undo stack. Pending events whose actions have been undone (no longer
    /// in the undo stack) are removed. Remaining pending events are marked as saved.
    /// Call this before SyncToCompanyData during save.
    /// </summary>
    /// <param name="activeUndoActions">The current undo stack actions from UndoRedoManager.</param>
    public void CommitPendingEvents(IReadOnlyList<IUndoableAction> activeUndoActions)
    {
        var activeSet = new HashSet<IUndoableAction>(activeUndoActions);

        // Find unsaved events whose actions are no longer active (undone via Ctrl+Z)
        var eventsToRemove = new List<AuditEvent>();
        foreach (var evt in _events)
        {
            if (evt.IsSaved)
                continue;

            // Undo/redo records follow their parent event
            if (evt.Action is AuditAction.Undone or AuditAction.Redone)
            {
                // Keep if the related original event is still present
                if (!string.IsNullOrEmpty(evt.RelatedEventId) &&
                    _events.Any(e => e.Id == evt.RelatedEventId && !eventsToRemove.Contains(e)))
                    continue;

                eventsToRemove.Add(evt);
                continue;
            }

            // Check if this event's action is still in the undo stack
            if (_undoableActions.TryGetValue(evt.Id, out var action) && activeSet.Contains(action))
                continue;

            // Action was undone (not in undo stack), if the event doesn't have a mapped action,
            // it might have been created without one, so keep it
            if (!_undoableActions.ContainsKey(evt.Id))
                continue;

            eventsToRemove.Add(evt);
        }

        // Remove stale events
        foreach (var evt in eventsToRemove)
        {
            _events.Remove(evt);
            if (_undoableActions.TryGetValue(evt.Id, out var action))
            {
                _actionToEventId.Remove(action);
                _undoableActions.Remove(evt.Id);
            }
        }

        // Mark all remaining unsaved events as saved
        foreach (var evt in _events)
        {
            evt.IsSaved = true;
        }

        if (eventsToRemove.Count > 0)
            EventsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Syncs the in-memory event list back to the CompanyData for persistence.
    /// Call this before saving the company file.
    /// </summary>
    public void SyncToCompanyData(CompanyData companyData)
    {
        companyData.EventLog.Clear();
        companyData.EventLog.AddRange(_events);
    }

    private void TrimIfNeeded()
    {
        if (_events.Count > _maxEventCount)
        {
            var excess = _events.Count - _maxEventCount;
            var removedIds = _events.Take(excess).Select(e => e.Id).ToHashSet();
            _events.RemoveRange(0, excess);

            // Clean up action references for trimmed events
            foreach (var id in removedIds)
            {
                if (_undoableActions.TryGetValue(id, out var action))
                {
                    _actionToEventId.Remove(action);
                    _undoableActions.Remove(id);
                }
            }
        }
    }

    private static string GenerateEventId()
    {
        return $"evt_{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Parses an IUndoableAction.Description string to extract audit metadata.
    /// Handles patterns like "Add customer 'Acme Corp'", "Edit product 'Widget A'",
    /// "Delete expense PUR-2026-00042".
    /// </summary>
    private static (AuditAction action, string entityType, string entityName) ParseActionDescription(string description)
    {
        var auditAction = AuditAction.Modified;
        var entityType = "";
        var entityName = "";

        if (string.IsNullOrWhiteSpace(description))
            return (auditAction, entityType, entityName);

        var desc = description.Trim();

        // Determine action type from prefix
        if (desc.StartsWith("Add ", StringComparison.OrdinalIgnoreCase))
        {
            auditAction = AuditAction.Added;
            desc = desc[4..];
        }
        else if (desc.StartsWith("Edit ", StringComparison.OrdinalIgnoreCase))
        {
            auditAction = AuditAction.Modified;
            desc = desc[5..];
        }
        else if (desc.StartsWith("Delete ", StringComparison.OrdinalIgnoreCase))
        {
            auditAction = AuditAction.Deleted;
            desc = desc[7..];
        }
        else if (desc.StartsWith("Update ", StringComparison.OrdinalIgnoreCase))
        {
            auditAction = AuditAction.Modified;
            desc = desc[7..];
        }

        // Extract entity type (first word after action)
        var spaceIndex = desc.IndexOf(' ');
        if (spaceIndex > 0)
        {
            entityType = CapitalizeFirst(desc[..spaceIndex]);
            var remainder = desc[(spaceIndex + 1)..].Trim();

            // Extract entity name from quotes or remainder
            var singleQuoteStart = remainder.IndexOf('\'');
            if (singleQuoteStart >= 0)
            {
                var singleQuoteEnd = remainder.IndexOf('\'', singleQuoteStart + 1);
                if (singleQuoteEnd > singleQuoteStart)
                {
                    entityName = remainder[(singleQuoteStart + 1)..singleQuoteEnd];
                }
            }
            else
            {
                entityName = remainder;
            }
        }
        else
        {
            entityType = CapitalizeFirst(desc);
        }

        return (auditAction, entityType, entityName);
    }

    private static string CapitalizeFirst(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpper(s[0]) + s[1..];
    }
}
