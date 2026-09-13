namespace ArgoBooks.Core.Models.Dashboard;

/// <summary>
/// How much a saved dashboard edit changed, for the DashboardCustomized telemetry event.
/// Counts only, never widget contents: the server keeps 64 characters of context.
/// </summary>
public static class DashboardCustomization
{
    /// <summary>
    /// A short description of the change from <paramref name="before"/> to <paramref name="after"/>,
    /// or null when nothing changed, so saving an untouched dashboard isn't reported as an edit.
    /// </summary>
    public static string? Describe(DashboardLayout before, DashboardLayout after)
    {
        var beforeIds = before.Rows.SelectMany(r => r.Widgets).Select(w => w.Id).ToHashSet();
        var afterIds = after.Rows.SelectMany(r => r.Widgets).Select(w => w.Id).ToHashSet();
        var added = afterIds.Count(id => !beforeIds.Contains(id));
        var removed = beforeIds.Count(id => !afterIds.Contains(id));

        if (added == 0 && removed == 0 && SameShape(before, after))
            return null;

        var isDefault = SameShape(after, DashboardLayout.CreateDefault()) ? "yes" : "no";
        return $"widgets:{afterIds.Count},rows:{after.Rows.Count},added:{added},removed:{removed},default:{isDefault}";
    }

    /// <summary>
    /// The same rows holding the same kinds of widget, in the same order and at the same sizes.
    /// Ids are ignored, so a fresh default layout matches one saved earlier.
    /// </summary>
    public static bool SameShape(DashboardLayout a, DashboardLayout b) =>
        a.Rows.Count == b.Rows.Count
        && a.Rows.Zip(b.Rows).All(rows =>
            rows.First.Widgets.Count == rows.Second.Widgets.Count
            && rows.First.Widgets.Zip(rows.Second.Widgets).All(w => Kind(w.First) == Kind(w.Second)));

    private static string Kind(DashboardWidgetEntry widget) =>
        $"{widget.WidgetType}/{widget.Size}/{widget.Config.GetValueOrDefault("ChartDataType")}";
}
