namespace ArgoBooks.Data;

/// <summary>
/// Represents a timezone option for display in the UI.
/// </summary>
public class TimeZoneItem
{
    /// <summary>
    /// The system timezone ID (e.g., "America/New_York" or "Eastern Standard Time").
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The display name shown to users (e.g., "(UTC-05:00) Eastern Time (US &amp; Canada)").
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// The base UTC offset for sorting purposes.
    /// </summary>
    public TimeSpan BaseUtcOffset { get; }

    public TimeZoneItem(string id, string displayName, TimeSpan baseUtcOffset)
    {
        Id = id;
        DisplayName = displayName;
        BaseUtcOffset = baseUtcOffset;
    }

    public override string ToString() => DisplayName;

    public override bool Equals(object? obj)
    {
        if (obj is TimeZoneItem other)
            return Id == other.Id;
        return false;
    }

    public override int GetHashCode() => Id.GetHashCode();
}

/// <summary>
/// Provides timezone data from the system with priority timezones for common selections.
/// </summary>
public static class TimeZones
{
    /// <summary>
    /// Priority timezone IDs shown at the top of the dropdown.
    /// These are common timezones that users frequently select.
    /// </summary>
    private static readonly HashSet<string> PriorityIds = new(StringComparer.OrdinalIgnoreCase)
    {
        // UTC
        "UTC",
        "Etc/UTC",
        "Coordinated Universal Time",

        // US timezones
        "America/New_York",
        "Eastern Standard Time",
        "America/Chicago",
        "Central Standard Time",
        "America/Denver",
        "Mountain Standard Time",
        "America/Los_Angeles",
        "Pacific Standard Time",

        // European timezones
        "Europe/London",
        "GMT Standard Time",
        "Europe/Paris",
        "Romance Standard Time",
        "Europe/Berlin",
        "W. Europe Standard Time",

        // Asia-Pacific timezones
        "Asia/Tokyo",
        "Tokyo Standard Time",
        "Asia/Shanghai",
        "China Standard Time",
        "Australia/Sydney",
        "AUS Eastern Standard Time"
    };

    private static List<TimeZoneItem>? _allTimeZones;
    private static List<TimeZoneItem>? _priorityTimeZones;

    /// <summary>
    /// Gets all available system timezones, sorted by UTC offset then by name.
    /// </summary>
    public static IReadOnlyList<TimeZoneItem> All
    {
        get
        {
            if (_allTimeZones == null)
            {
                LoadTimeZones();
            }
            return _allTimeZones!;
        }
    }

    /// <summary>
    /// Gets priority timezones (common ones) shown at the top of the dropdown.
    /// </summary>
    public static IReadOnlyList<TimeZoneItem> Priority
    {
        get
        {
            if (_priorityTimeZones == null)
            {
                LoadTimeZones();
            }
            return _priorityTimeZones!;
        }
    }

    /// <summary>
    /// Loads timezone data from the system.
    /// </summary>
    private static void LoadTimeZones()
    {
        var allZones = new List<TimeZoneItem>();
        var priorityZones = new List<TimeZoneItem>();

        // Add UTC first as a special case
        var utcItem = new TimeZoneItem("UTC", "(UTC+00:00) UTC", TimeSpan.Zero);
        allZones.Add(utcItem);
        priorityZones.Add(utcItem);

        // Get all system timezones
        foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
        {
            // Skip UTC since we added it manually
            if (tz.Id == "UTC" || tz.Id == "Etc/UTC" || tz.Id == "Coordinated Universal Time")
                continue;

            var item = new TimeZoneItem(tz.Id, FormatDisplayName(tz), tz.BaseUtcOffset);
            allZones.Add(item);

            // Check if this is a priority timezone
            if (PriorityIds.Contains(tz.Id))
            {
                priorityZones.Add(item);
            }
        }

        // Sort all timezones by UTC offset, then by display name
        _allTimeZones = allZones
            .OrderBy(tz => tz.BaseUtcOffset)
            .ThenBy(tz => tz.DisplayName)
            .ToList();

        // Sort priority timezones by UTC offset, then by display name
        _priorityTimeZones = priorityZones
            .OrderBy(tz => tz.BaseUtcOffset)
            .ThenBy(tz => tz.DisplayName)
            .ToList();
    }

    /// <summary>
    /// Formats a timezone for display with UTC offset prefix.
    /// </summary>
    private static string FormatDisplayName(TimeZoneInfo tz)
    {
        var offset = tz.BaseUtcOffset;
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var absOffset = offset.Duration();
        var offsetStr = $"(UTC{sign}{absOffset.Hours:D2}:{absOffset.Minutes:D2})";

        // Use the display name if available, otherwise use the ID
        var name = !string.IsNullOrEmpty(tz.DisplayName) ? tz.DisplayName : tz.Id;

        // If the display name already contains the UTC offset, just return it
        if (name.StartsWith("(UTC"))
            return name;

        return $"{offsetStr} {name}";
    }

    /// <summary>
    /// Finds a TimeZoneItem by its ID.
    /// </summary>
    /// <param name="id">The timezone ID to find.</param>
    /// <returns>The TimeZoneItem if found, or UTC if not found.</returns>
    public static TimeZoneItem FindById(string? id)
    {
        if (string.IsNullOrEmpty(id) || id == "UTC")
            return All.First(tz => tz.Id == "UTC");

        return All.FirstOrDefault(tz => tz.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
               ?? All.First(tz => tz.Id == "UTC");
    }

    /// <summary>
    /// Checks if a timezone ID exists in the system.
    /// </summary>
    /// <param name="id">The timezone ID to check.</param>
    /// <returns>True if the timezone exists.</returns>
    public static bool Exists(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        if (id == "UTC")
            return true;

        return All.Any(tz => tz.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }
}
