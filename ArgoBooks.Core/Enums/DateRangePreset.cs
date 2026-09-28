using ArgoBooks.Core.Models.Reports;

namespace ArgoBooks.Core.Enums;

/// <summary>
/// Standard date range presets used in chart settings and date filters.
/// </summary>
public enum DateRangePreset
{
    ThisMonth,
    LastMonth,
    Last30Days,
    Last100Days,
    Last365Days,
    ThisQuarter,
    LastQuarter,
    ThisYear,
    LastYear,
    AllTime,
    CustomRange
}

/// <summary>
/// Extension methods for DateRangePreset.
/// </summary>
public static class DateRangePresetExtensions
{
    private static readonly (DateRangePreset Preset, string Name)[] Names =
    [
        (DateRangePreset.ThisMonth, DatePresetNames.ThisMonth),
        (DateRangePreset.LastMonth, DatePresetNames.LastMonth),
        (DateRangePreset.Last30Days, DatePresetNames.Last30Days),
        (DateRangePreset.Last100Days, DatePresetNames.Last100Days),
        (DateRangePreset.Last365Days, DatePresetNames.Last365Days),
        (DateRangePreset.ThisQuarter, DatePresetNames.ThisQuarter),
        (DateRangePreset.LastQuarter, DatePresetNames.LastQuarter),
        (DateRangePreset.ThisYear, DatePresetNames.YearToDate),
        (DateRangePreset.LastYear, DatePresetNames.LastYear),
        (DateRangePreset.AllTime, DatePresetNames.AllTime),
        (DateRangePreset.CustomRange, DatePresetNames.CustomRange)
    ];

    /// <summary>
    /// The preset's name in <see cref="DatePresetNames"/>, the one vocabulary shown in the UI and
    /// stored in settings and report templates.
    /// </summary>
    public static string GetDisplayName(this DateRangePreset preset) =>
        Names.FirstOrDefault(n => n.Preset == preset).Name ?? preset.ToString();

    /// <summary>
    /// The preset a stored or displayed name stands for, ignoring case. Both custom spellings
    /// (<see cref="DatePresetNames.CustomRange"/> from chart settings, <see cref="DatePresetNames.Custom"/>
    /// from report templates) read as <see cref="DateRangePreset.CustomRange"/>.
    /// </summary>
    public static DateRangePreset? ParseDateRange(string? displayName)
    {
        if (string.Equals(displayName, DatePresetNames.Custom, StringComparison.OrdinalIgnoreCase))
            return DateRangePreset.CustomRange;
        foreach (var (preset, name) in Names)
            if (string.Equals(displayName, name, StringComparison.OrdinalIgnoreCase))
                return preset;
        return null;
    }

    public static string GetComparisonPeriodLabel(this DateRangePreset preset)
    {
        return preset switch
        {
            DateRangePreset.ThisMonth => "from last month",
            DateRangePreset.LastMonth => "from prior month",
            DateRangePreset.Last30Days => "from prior 30 days",
            DateRangePreset.Last100Days => "from prior 100 days",
            DateRangePreset.Last365Days => "from prior 365 days",
            DateRangePreset.ThisQuarter => "from last quarter",
            DateRangePreset.LastQuarter => "from prior quarter",
            DateRangePreset.ThisYear => "from last year",
            DateRangePreset.LastYear => "from prior year",
            DateRangePreset.AllTime => "",
            DateRangePreset.CustomRange => "from prior period",
            _ => "from last period"
        };
    }

    /// <summary>
    /// Gets all standard date range preset display names for UI dropdowns.
    /// </summary>
    public static string[] GetStandardOptions()
    {
        return
        [
            DateRangePreset.ThisMonth.GetDisplayName(),
            DateRangePreset.LastMonth.GetDisplayName(),
            DateRangePreset.Last30Days.GetDisplayName(),
            DateRangePreset.Last100Days.GetDisplayName(),
            DateRangePreset.Last365Days.GetDisplayName(),
            DateRangePreset.ThisQuarter.GetDisplayName(),
            DateRangePreset.LastQuarter.GetDisplayName(),
            DateRangePreset.ThisYear.GetDisplayName(),
            DateRangePreset.LastYear.GetDisplayName(),
            DateRangePreset.AllTime.GetDisplayName(),
            DateRangePreset.CustomRange.GetDisplayName()
        ];
    }
}
