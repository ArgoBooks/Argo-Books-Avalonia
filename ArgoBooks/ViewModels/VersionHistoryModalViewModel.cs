using System.Collections.ObjectModel;
using ArgoBooks.Core.Models;
using ArgoBooks.Localization;
using ArgoBooks.Services;
using ArgoBooks.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.ViewModels;

/// <summary>
/// Display item for a single audit event in the version history timeline.
/// </summary>
public partial class VersionHistoryItem : ObservableObject
{
    /// <summary>
    /// The underlying audit event.
    /// </summary>
    public AuditEvent Event { get; }

    /// <summary>
    /// The event description.
    /// </summary>
    public string Description => Event.Description;

    /// <summary>
    /// Formatted time string (e.g., "2:41 PM").
    /// </summary>
    public string TimeText => Event.Timestamp.ToLocalTime().ToString("h:mm tt");

    /// <summary>
    /// The entity type label.
    /// </summary>
    public string EntityTypeText => !string.IsNullOrEmpty(Event.EntityType) ? Event.EntityType : "";

    /// <summary>
    /// The action type for visual indicators.
    /// </summary>
    public AuditAction Action => Event.Action;

    /// <summary>
    /// Formatted summary of field-level changes for Modified events (e.g., "Name: 'test' → '123test'").
    /// </summary>
    public string? ChangesSummary
    {
        get
        {
            if (Event.Changes == null || Event.Changes.Count == 0)
                return null;
            return string.Join("\n", Event.Changes.Select(c =>
                $"{c.Key}: '{c.Value.OldValue}' → '{c.Value.NewValue}'"));
        }
    }

    /// <summary>
    /// Whether there are field-level changes to display.
    /// </summary>
    public bool HasChanges => Event.Changes is { Count: > 0 };

    // Action-kind flags that pick the timeline icon for this entry. Each entry matches exactly one.
    public bool IsAddedEntry => Action == AuditAction.Added;
    public bool IsDeletedEntry => Action == AuditAction.Deleted;

    /// <summary>True when this entry records a toolbar undo or redo (shown with a neutral icon).</summary>
    public bool IsUndoRedo => Action is AuditAction.Undone or AuditAction.Redone;

    /// <summary>An import (logged as a generic Modified) gets its own import icon, not the edit pencil.</summary>
    public bool IsImportEntry => Action == AuditAction.Modified
        && Description.StartsWith("Import", StringComparison.OrdinalIgnoreCase);

    /// <summary>A genuine field edit: Modified, but not an import.</summary>
    public bool IsModifiedEntry => Action == AuditAction.Modified && !IsImportEntry;

    public VersionHistoryItem(AuditEvent evt)
    {
        Event = evt;
    }
}

/// <summary>
/// Groups version history items by date for the timeline view.
/// </summary>
public class VersionHistoryGroup
{
    /// <summary>
    /// The date label (e.g., "Today, Feb 9", "Yesterday, Feb 8", "Feb 7, 2026").
    /// </summary>
    public string DateLabel { get; set; } = string.Empty;

    /// <summary>
    /// The events for this date, newest first.
    /// </summary>
    public ObservableCollection<VersionHistoryItem> Items { get; } = [];
}

/// <summary>
/// ViewModel for the version history modal.
/// Displays a chronological timeline of all changes with search, filtering, and selective undo/redo.
/// </summary>
public partial class VersionHistoryModalViewModel : ViewModelBase
{
    private EventLogService? _eventLogService;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string? _selectedEntityTypeFilter;

    [ObservableProperty]
    private string? _selectedActionFilter;

    [ObservableProperty]
    private int _totalEventCount;

    [ObservableProperty]
    private int _filteredEventCount;

    [ObservableProperty]
    private bool _hasEvents;

    [ObservableProperty]
    private bool _isFiltered;

    [ObservableProperty]
    private bool _showNoResults;

    /// <summary>
    /// Grouped timeline items (by date).
    /// </summary>
    public ObservableCollection<VersionHistoryGroup> Groups { get; } = [];

    /// <summary>
    /// Available entity type filters.
    /// </summary>
    public ObservableCollection<string> EntityTypeFilters { get; } = [];

    /// <summary>
    /// Available action type filters.
    /// </summary>
    public ObservableCollection<string> ActionFilters { get; } =
    [
        "All",
        "Added",
        "Modified",
        "Deleted"
    ];

    /// <summary>
    /// Sets the event log service reference. Called during initialization.
    /// </summary>
    public void SetEventLogService(EventLogService eventLogService)
    {
        if (_eventLogService != null)
            _eventLogService.EventsChanged -= OnEventsChanged;

        _eventLogService = eventLogService;
        _eventLogService.EventsChanged += OnEventsChanged;
    }

    /// <summary>
    /// Opens the modal and refreshes the event list.
    /// </summary>
    [RelayCommand]
    private void Open()
    {
        RefreshEvents();
        IsOpen = true;
    }

    /// <summary>
    /// Closes the modal.
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
    }

    /// <summary>
    /// Clears all filters.
    /// </summary>
    [RelayCommand]
    private void ClearFilters()
    {
        SearchQuery = string.Empty;
        SelectedEntityTypeFilter = null;
        SelectedActionFilter = null;
    }

    partial void OnSearchQueryChanged(string value) => RefreshEvents();
    partial void OnSelectedEntityTypeFilterChanged(string? value) => RefreshEvents();
    partial void OnSelectedActionFilterChanged(string? value) => RefreshEvents();

    private void OnEventsChanged(object? sender, EventArgs e)
    {
        // Re-render the timeline when events change (new event recorded, undo/redo)
        if (IsOpen)
        {
            RefreshEvents();
        }
    }

    /// <summary>
    /// Rebuilds the grouped, read-only timeline from the event log. Every saved event (including
    /// undo/redo records) is shown as its own flat entry, newest first.
    /// </summary>
    private void RefreshEvents()
    {
        if (_eventLogService == null)
            return;

        AuditAction? actionFilter = SelectedActionFilter switch
        {
            "Added" => AuditAction.Added,
            "Modified" => AuditAction.Modified,
            "Deleted" => AuditAction.Deleted,
            _ => null
        };

        var entityTypeFilter = SelectedEntityTypeFilter is "All" or null
            ? null
            : SelectedEntityTypeFilter;

        // Saved events only; unsaved events appear once the file is saved.
        var events = _eventLogService.GetFilteredEvents(
            actionFilter: null,
            entityTypeFilter: entityTypeFilter)
            .Where(e => e.IsSaved)
            .ToList();

        if (actionFilter.HasValue)
            events = events.Where(e => e.Action == actionFilter.Value).ToList();

        // Apply fuzzy search using Levenshtein scoring
        List<AuditEvent> filteredEvents;
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filteredEvents = events
                .OrderByDescending(e => e.Timestamp)
                .RankBySearch(SearchQuery, e => [e.Description, e.EntityName, e.EntityType])
                .ToList();
        }
        else
        {
            filteredEvents = events
                .OrderByDescending(e => e.Timestamp)
                .ToList();
        }

        TotalEventCount = _eventLogService.GetFilteredEvents().Count(e => e.IsSaved);
        FilteredEventCount = filteredEvents.Count;
        HasEvents = TotalEventCount > 0;
        IsFiltered = actionFilter.HasValue
                     || !string.IsNullOrWhiteSpace(entityTypeFilter)
                     || !string.IsNullOrWhiteSpace(SearchQuery);
        ShowNoResults = HasEvents && IsFiltered && FilteredEventCount == 0;

        // Group by date
        Groups.Clear();
        var today = DateTime.Now.Date;
        var yesterday = today.AddDays(-1);

        var grouped = filteredEvents.GroupBy(e => e.Timestamp.ToLocalTime().Date);
        foreach (var group in grouped)
        {
            var dateLabel = group.Key == today
                ? "Today".Translate() + ", " + group.Key.ToString("MMM d")
                : group.Key == yesterday
                    ? "Yesterday".Translate() + ", " + group.Key.ToString("MMM d")
                    : group.Key.ToString("MMM d, yyyy");

            var historyGroup = new VersionHistoryGroup { DateLabel = dateLabel };
            foreach (var evt in group)
                historyGroup.Items.Add(new VersionHistoryItem(evt));
            Groups.Add(historyGroup);
        }

        // Refresh entity type filters
        var currentTypes = _eventLogService.GetEntityTypes().ToList();
        EntityTypeFilters.Clear();
        EntityTypeFilters.Add("All");
        foreach (var type in currentTypes)
        {
            EntityTypeFilters.Add(type);
        }
    }

}
