using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Localization;

namespace ArgoBooks.Services;

/// <summary>
/// Action for adding an element.
/// </summary>
public class AddElementAction(ReportConfiguration config, ReportElementBase element) : IUndoableAction
{
    public string Description => "Add {0}".TranslateFormat(element.DisplayName);

    public void Undo()
    {
        config.RemoveElement(element.Id);
    }

    public void Redo()
    {
        config.AddElement(element);
    }
}

/// <summary>
/// Action for removing an element.
/// </summary>
public class RemoveElementAction : IUndoableAction
{
    private readonly ReportConfiguration _config;
    private readonly ReportElementBase _element;
    private readonly int _originalZOrder;

    public RemoveElementAction(ReportConfiguration config, ReportElementBase element)
    {
        _config = config;
        _element = element.Clone();
        _element.Id = element.Id; // Preserve original ID so undo/redo pairs stay consistent
        _originalZOrder = element.ZOrder;
    }

    public string Description => "Remove {0}".TranslateFormat(_element.DisplayName);

    public void Undo()
    {
        // Straight into the list: AddElement would put it on top of everything.
        _element.ZOrder = _originalZOrder;
        _config.Elements.Add(_element);
    }

    public void Redo()
    {
        _config.RemoveElement(_element.Id);
    }
}

/// <summary>
/// Action for removing a whole selection of elements as one undo entry.
/// </summary>
public class RemoveElementsAction : IUndoableAction
{
    private readonly ReportConfiguration _config;
    private readonly List<(ReportElementBase Element, int ZOrder)> _removed;

    public RemoveElementsAction(ReportConfiguration config, IReadOnlyList<ReportElementBase> elements)
    {
        _config = config;
        _removed = elements.Select(e =>
        {
            var clone = e.Clone();
            clone.Id = e.Id; // Preserve original ID so undo/redo pairs stay consistent
            clone.PageNumber = e.PageNumber;
            return (clone, e.ZOrder);
        }).ToList();
    }

    public string Description => _removed.Count == 1
        ? "Remove {0}".TranslateFormat(_removed[0].Element.DisplayName)
        : $"Remove {_removed.Count} elements";

    /// <summary>
    /// Removes the elements and records the whole set as one undo entry, so a single
    /// undo brings the entire selection back instead of one element at a time.
    /// </summary>
    public static void RemoveAndRecord(
        ReportConfiguration config,
        IReadOnlyList<ReportElementBase> elements,
        UndoRedoManager? undoRedoManager)
    {
        if (elements.Count == 0) return;

        undoRedoManager?.RecordAction(new RemoveElementsAction(config, elements));

        foreach (var element in elements)
            config.RemoveElement(element.Id);
    }

    public void Undo()
    {
        foreach (var (element, zOrder) in _removed)
        {
            // Straight into the list: AddElement would put it on top of everything.
            element.ZOrder = zOrder;
            _config.Elements.Add(element);
        }
    }

    public void Redo()
    {
        foreach (var (element, _) in _removed)
            _config.RemoveElement(element.Id);
    }
}

/// <summary>
/// Action for moving/resizing an element. Supports coalescing so that rapid
/// sequential changes (e.g., from spinner controls) merge into a single undo entry.
/// </summary>
public class MoveResizeElementAction : ICoalescingUndoableAction
{
    private readonly ReportConfiguration _config;
    private readonly string _elementId;
    private readonly (double X, double Y, double Width, double Height) _oldBounds;
    private (double X, double Y, double Width, double Height) _newBounds;
    private bool _isResize;

    public MoveResizeElementAction(
        ReportConfiguration config,
        string elementId,
        (double X, double Y, double Width, double Height) oldBounds,
        (double X, double Y, double Width, double Height) newBounds,
        bool isResize = false)
    {
        _config = config;
        _elementId = elementId;
        _oldBounds = oldBounds;
        _newBounds = newBounds;
        _isResize = isResize;
    }

    public string Description => _isResize ? "Resize element".Translate() : "Move element".Translate();

    public string CoalescingKey => $"move-resize:{_elementId}";

    public void Undo()
    {
        var element = _config.GetElementById(_elementId);
        element?.Bounds = _oldBounds;
    }

    public void Redo()
    {
        var element = _config.GetElementById(_elementId);
        element?.Bounds = _newBounds;
    }

    public void UpdateToNewState(ICoalescingUndoableAction newerAction)
    {
        if (newerAction is MoveResizeElementAction newer)
        {
            _newBounds = newer._newBounds;
            _isResize = _isResize || newer._isResize;
        }
    }
}

/// <summary>
/// Action for resizing accounting table columns via drag in the designer.
/// </summary>
public class ColumnResizeAction(
    ReportConfiguration config,
    string elementId,
    List<double> oldRatios,
    List<double> newRatios)
    : IUndoableAction
{
    private readonly List<double> _oldRatios = oldRatios.ToList();
    private readonly List<double> _newRatios = newRatios.ToList();

    public string Description => "Resize column".Translate();

    public void Undo()
    {
        if (config.GetElementById(elementId) is AccountingTableReportElement element)
            element.ColumnWidthRatios = _oldRatios.ToList();
    }

    public void Redo()
    {
        if (config.GetElementById(elementId) is AccountingTableReportElement element)
            element.ColumnWidthRatios = _newRatios.ToList();
    }
}

/// <summary>
/// Action for changing element Z-order.
/// </summary>
public class ZOrderChangeAction(
    ReportConfiguration config,
    Dictionary<string, int> oldZOrders,
    Dictionary<string, int> newZOrders,
    string description)
    : IUndoableAction
{
    private readonly Dictionary<string, int> _oldZOrders = new(oldZOrders);
    private readonly Dictionary<string, int> _newZOrders = new(newZOrders);

    public string Description { get; } = description;

    public void Undo()
    {
        foreach (var kvp in _oldZOrders)
        {
            var element = config.GetElementById(kvp.Key);
            element?.ZOrder = kvp.Value;
        }
    }

    public void Redo()
    {
        foreach (var kvp in _newZOrders)
        {
            var element = config.GetElementById(kvp.Key);
            element?.ZOrder = kvp.Value;
        }
    }
}

/// <summary>
/// Action for changing an element property value.
/// </summary>
public class ElementPropertyChangeAction(
    ReportConfiguration config,
    string elementId,
    string elementDisplayName,
    string propertyName,
    object? oldValue,
    object? newValue)
    : ICoalescingUndoableAction
{
    private object? _newValue = newValue;

    public string Description => "Change {0} {1}".TranslateFormat(elementDisplayName, FormatPropertyName(propertyName));

    public string CoalescingKey => $"prop-change:{elementId}:{propertyName}";

    public void UpdateToNewState(ICoalescingUndoableAction newerAction)
    {
        if (newerAction is ElementPropertyChangeAction newer)
        {
            _newValue = newer._newValue;
        }
    }

    public void Undo()
    {
        var element = config.GetElementById(elementId);
        if (element != null)
        {
            SetPropertyValue(element, propertyName, oldValue);
        }
    }

    public void Redo()
    {
        var element = config.GetElementById(elementId);
        if (element != null)
        {
            SetPropertyValue(element, propertyName, _newValue);
        }
    }

    private static void SetPropertyValue(object target, string propertyName, object? value)
    {
        var property = target.GetType().GetProperty(propertyName);
        if (property != null && property.CanWrite)
        {
            // Handle type conversion for enums and other types
            var convertedValue = ConvertValue(value, property.PropertyType);
            property.SetValue(target, convertedValue);
        }
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value == null)
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

        if (targetType.IsInstanceOfType(value))
            return value;

        if (targetType.IsEnum && value is string stringValue)
            return Enum.Parse(targetType, stringValue);

        if (targetType.IsEnum && value.GetType().IsEnum)
            return value;

        try
        {
            return Convert.ChangeType(value, targetType);
        }
        catch
        {
            return value;
        }
    }

    private static string FormatPropertyName(string propertyName)
    {
        // Convert PascalCase to space-separated words
        var result = new System.Text.StringBuilder();
        foreach (var c in propertyName)
        {
            if (char.IsUpper(c) && result.Length > 0)
                result.Append(' ');
            result.Append(char.ToLower(c));
        }
        return result.ToString();
    }
}

/// <summary>
/// Action for batch operations (alignment, distribution, sizing, cross-page moves).
/// </summary>
public class BatchMoveResizeAction(
    ReportConfiguration config,
    Dictionary<string, (double X, double Y, double Width, double Height, int PageNumber)> oldBounds,
    Dictionary<string, (double X, double Y, double Width, double Height, int PageNumber)> newBounds,
    string description)
    : IUndoableAction
{
    private readonly Dictionary<string, (double X, double Y, double Width, double Height, int PageNumber)> _oldBounds = new(oldBounds);
    private readonly Dictionary<string, (double X, double Y, double Width, double Height, int PageNumber)> _newBounds = new(newBounds);

    public string Description { get; } = description;

    public void Undo()
    {
        foreach (var kvp in _oldBounds)
        {
            var element = config.GetElementById(kvp.Key);
            if (element == null) continue;
            element.BoundsWithPage = kvp.Value;
        }
    }

    public void Redo()
    {
        foreach (var kvp in _newBounds)
        {
            var element = config.GetElementById(kvp.Key);
            if (element == null) continue;
            element.BoundsWithPage = kvp.Value;
        }
    }
}

/// <summary>
/// Action for adding a new page to the report.
/// </summary>
public class AddPageAction(ReportConfiguration config) : IUndoableAction
{
    private readonly int _newPageNumber = config.PageCount + 1;

    public string Description => "Add Page".Translate();

    public void Undo()
    {
        // Remove any elements that were added to the new page
        var elementsOnPage = config.Elements.Where(e => e.PageNumber == _newPageNumber).ToList();
        foreach (var element in elementsOnPage)
        {
            config.Elements.Remove(element);
        }
        config.PageCount--;
    }

    public void Redo()
    {
        config.PageCount++;
    }
}

/// <summary>
/// Action for deleting a page from the report.
/// </summary>
public class DeletePageAction : IUndoableAction
{
    private readonly ReportConfiguration _config;
    private readonly int _deletedPageNumber;
    private readonly List<ReportElementBase> _removedElements;

    public DeletePageAction(ReportConfiguration config, int pageNumber, List<ReportElementBase> removedElements)
    {
        _config = config;
        _deletedPageNumber = pageNumber;
        _removedElements = removedElements.Select(e => e.Clone()).ToList();
        // Preserve original IDs and page numbers in clones, so other undo/redo actions that name
        // these elements still find them once the page is back (as RemoveElementAction does)
        for (int i = 0; i < _removedElements.Count; i++)
        {
            _removedElements[i].Id = removedElements[i].Id;
            _removedElements[i].PageNumber = removedElements[i].PageNumber;
        }
    }

    public string Description => "Delete Page".Translate();

    public void Undo()
    {
        // Renumber pages above the deleted page back up
        foreach (var element in _config.Elements.Where(e => e.PageNumber >= _deletedPageNumber))
        {
            element.PageNumber++;
        }
        _config.PageCount++;

        foreach (var element in _removedElements)
        {
            var restored = element.Clone();
            restored.Id = element.Id;
            _config.Elements.Add(restored);
        }
    }

    public void Redo()
    {
        var elementsOnPage = _config.Elements.Where(e => e.PageNumber == _deletedPageNumber).ToList();
        foreach (var element in elementsOnPage)
        {
            _config.Elements.Remove(element);
        }

        // Renumber pages above the deleted page down
        foreach (var element in _config.Elements.Where(e => e.PageNumber > _deletedPageNumber))
        {
            element.PageNumber--;
        }
        _config.PageCount--;
    }
}

/// <summary>
/// Snapshot of all page settings for undo/redo.
/// </summary>
public record PageSettingsSnapshot(
    PageSize PageSize,
    PageOrientation PageOrientation,
    double MarginTop,
    double MarginRight,
    double MarginBottom,
    double MarginLeft,
    bool ShowHeader,
    bool ShowFooter,
    bool ShowPageNumbers,
    bool ShowCompanyDetails,
    string BackgroundColor,
    double TitleFontSize,
    string DatePreset);

/// <summary>
/// Action for changing page settings. Captures a full snapshot of all settings
/// so that undo/redo restores the complete state. Supports coalescing so rapid
/// sequential changes (e.g., margin spinners) merge into a single undo entry.
/// </summary>
public class PageSettingsChangeAction : ICoalescingUndoableAction
{
    private readonly ReportConfiguration _config;
    private readonly PageSettingsSnapshot _oldSettings;
    private PageSettingsSnapshot _newSettings;
    private readonly Action<PageSettingsSnapshot> _applyToViewModel;

    public PageSettingsChangeAction(
        ReportConfiguration config,
        PageSettingsSnapshot oldSettings,
        PageSettingsSnapshot newSettings,
        Action<PageSettingsSnapshot> applyToViewModel)
    {
        _config = config;
        _oldSettings = oldSettings;
        _newSettings = newSettings;
        _applyToViewModel = applyToViewModel;
    }

    public string Description => "Change page settings".Translate();

    public string CoalescingKey => "page-settings";

    public void Undo()
    {
        ApplyToConfig(_oldSettings);
        _applyToViewModel(_oldSettings);
    }

    public void Redo()
    {
        ApplyToConfig(_newSettings);
        _applyToViewModel(_newSettings);
    }

    public void UpdateToNewState(ICoalescingUndoableAction newerAction)
    {
        if (newerAction is PageSettingsChangeAction newer)
        {
            _newSettings = newer._newSettings;
        }
    }

    private void ApplyToConfig(PageSettingsSnapshot s)
    {
        _config.PageSize = s.PageSize;
        _config.PageOrientation = s.PageOrientation;
        _config.PageMargins = new ReportMargins(s.MarginLeft, s.MarginTop, s.MarginRight, s.MarginBottom);
        _config.ShowHeader = s.ShowHeader;
        _config.ShowFooter = s.ShowFooter;
        _config.ShowPageNumbers = s.ShowPageNumbers;
        _config.ShowCompanyDetails = s.ShowCompanyDetails;
        _config.BackgroundColor = s.BackgroundColor;
        _config.TitleFontSize = s.TitleFontSize;
        _config.Filters.DatePresetName = s.DatePreset;
    }
}
