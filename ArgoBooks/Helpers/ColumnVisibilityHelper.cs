using ArgoBooks.Controls.ColumnWidths;

namespace ArgoBooks.Helpers;

/// <summary>
/// Helper for persisting column visibility settings across app restarts.
/// </summary>
public static class ColumnVisibilityHelper
{
    /// <summary>
    /// Pushes a page view model's current column visibility into its column-width manager.
    /// Reflects over every <c>Show{Column}Column</c> boolean property on the view model and
    /// reports its value to the manager found on the view model's <c>ColumnWidths</c> property.
    /// </summary>
    /// <remarks>
    /// This is required because the <c>[ObservableProperty]</c> field initializers that load
    /// persisted visibility (e.g. <c>_showFooColumn = Load("Page", "Foo", false)</c>) do NOT
    /// fire the generated <c>On...Changed</c> partials. Without this, a column that is hidden by
    /// default - or that the user has previously hidden - is never reported to the manager, so the
    /// manager keeps reserving proportional width for it and renders an empty gap. Call once from
    /// the page view model's constructor.
    /// </remarks>
    public static void SyncToManager(object viewModel)
    {
        if (GetManager(viewModel) is not { } widths)
            return;

        foreach (var prop in viewModel.GetType().GetProperties())
        {
            if (prop.PropertyType != typeof(bool) || !prop.CanRead)
                continue;

            if (GetColumnKey(prop.Name) is { } column && prop.GetValue(viewModel) is bool isVisible)
                widths.SetColumnVisibility(column, isVisible);
        }
    }

    /// <summary>
    /// Gets the column-width manager exposed by a view model's <c>ColumnWidths</c> property, if any.
    /// </summary>
    public static ITableColumnWidths? GetManager(object viewModel) =>
        viewModel.GetType().GetProperty("ColumnWidths")?.GetValue(viewModel) as ITableColumnWidths;

    /// <summary>
    /// Returns the column key for a <c>Show{Column}Column</c> property name, or null for any other name.
    /// </summary>
    public static string? GetColumnKey(string? propertyName) =>
        propertyName is { Length: > 10 } name &&
        name.StartsWith("Show", StringComparison.Ordinal) &&
        name.EndsWith("Column", StringComparison.Ordinal)
            ? name[4..^6]
            : null;

    /// <summary>
    /// Sets every <c>Show{Column}Column</c> property on the view model back to its default.
    /// </summary>
    public static void ApplyDefaults(object viewModel, ColumnVisibilityDefaults columns)
    {
        var type = viewModel.GetType();
        foreach (var (column, isVisible) in columns.Defaults)
            type.GetProperty($"Show{column}Column")?.SetValue(viewModel, isVisible);
    }

    /// <summary>
    /// Loads a saved column visibility value, or returns the default if not found.
    /// </summary>
    public static bool Load(string pageName, string columnName, bool defaultValue)
    {
        var settings = App.SettingsService?.GlobalSettings.Ui;
        if (settings == null)
            return defaultValue;

        if (settings.ColumnVisibility.TryGetValue(pageName, out var pageColumns) &&
            pageColumns.TryGetValue(columnName, out var isVisible))
        {
            return isVisible;
        }

        return defaultValue;
    }

    /// <summary>
    /// Saves a column visibility value and persists to disk.
    /// </summary>
    public static void Save(string pageName, string columnName, bool isVisible)
    {
        var settings = App.SettingsService?.GlobalSettings.Ui;
        if (settings == null)
            return;

        if (!settings.ColumnVisibility.TryGetValue(pageName, out var pageColumns))
        {
            pageColumns = new Dictionary<string, bool>();
            settings.ColumnVisibility[pageName] = pageColumns;
        }

        // Only save to disk if value actually changed
        if (pageColumns.TryGetValue(columnName, out var existing) && existing == isVisible)
            return;

        pageColumns[columnName] = isVisible;
        _ = App.SettingsService!.SaveGlobalSettingsAsync();
    }

    /// <summary>
    /// Clears all saved column visibility overrides for a page, so columns revert to defaults.
    /// </summary>
    public static void ResetPage(string pageName)
    {
        var settings = App.SettingsService?.GlobalSettings.Ui;
        if (settings == null)
            return;

        if (settings.ColumnVisibility.Remove(pageName))
        {
            _ = App.SettingsService!.SaveGlobalSettingsAsync();
        }
    }
}

/// <summary>
/// A page's column visibility settings key and each column's default visibility, shared by the
/// page's <c>Show{Column}Column</c> field initializers and the column reset.
/// </summary>
public sealed class ColumnVisibilityDefaults(string pageName, IReadOnlyDictionary<string, bool> defaults)
{
    public string PageName { get; } = pageName;

    public IReadOnlyDictionary<string, bool> Defaults { get; } = defaults;

    /// <summary>
    /// Loads the saved visibility for a column, or its default.
    /// </summary>
    public bool Load(string columnName) =>
        ColumnVisibilityHelper.Load(PageName, columnName, Defaults[columnName]);
}
