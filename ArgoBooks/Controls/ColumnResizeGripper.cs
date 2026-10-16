using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Converters;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace ArgoBooks.Controls;

/// <summary>
/// A control for resizing table columns via drag.
/// </summary>
public class ColumnResizeGripper : Border
{
    private bool _isDragging;
    private Point _lastDragPoint;
    private DateTime _lastClickTime = DateTime.MinValue;
    private const int DoubleClickThresholdMs = 300;

    public static readonly StyledProperty<string> ColumnNameProperty =
        AvaloniaProperty.Register<ColumnResizeGripper, string>(nameof(ColumnName), string.Empty);

    public static readonly StyledProperty<ITableColumnWidths?> ColumnWidthsProperty =
        AvaloniaProperty.Register<ColumnResizeGripper, ITableColumnWidths?>(nameof(ColumnWidths));

    /// <summary>
    /// Gets or sets the name of the column this gripper controls.
    /// </summary>
    public string ColumnName
    {
        get => GetValue(ColumnNameProperty);
        set => SetValue(ColumnNameProperty, value);
    }

    /// <summary>
    /// Gets or sets the table column widths instance.
    /// </summary>
    public ITableColumnWidths? ColumnWidths
    {
        get => GetValue(ColumnWidthsProperty);
        set => SetValue(ColumnWidthsProperty, value);
    }

    public ColumnResizeGripper()
    {
        Width = 8;
        MinHeight = 20;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        Background = Brushes.Transparent;
        Margin = new Thickness(-4, 0, -4, 0);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (!_isDragging)
        {
            Background = ConverterUtils.AccentTint(80);
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!_isDragging)
        {
            Background = Brushes.Transparent;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            // Check for double-click
            var now = DateTime.Now;
            if ((now - _lastClickTime).TotalMilliseconds < DoubleClickThresholdMs)
            {
                // Double-click: auto-size the column
                AutoSizeColumn();
                _lastClickTime = DateTime.MinValue;
                e.Handled = true;
                return;
            }
            _lastClickTime = now;

            _isDragging = true;
            _lastDragPoint = e.GetPosition(TopLevel.GetTopLevel(this));
            Background = ConverterUtils.AccentTint(120);
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_isDragging)
        {
            ApplyResize(e.GetPosition(TopLevel.GetTopLevel(this)));
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isDragging)
        {
            e.Pointer.Capture(null);
            EndDrag();
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndDrag();
    }

    private void ApplyResize(Point currentPoint)
    {
        var delta = currentPoint.X - _lastDragPoint.X;

        if (Math.Abs(delta) >= 1)
        {
            var actualDelta = ColumnWidths?.ResizeColumn(ColumnName, delta) ?? 0;
            // Only move the drag anchor by the amount actually applied. This ensures the mouse must "catch up" to the column position when constraints prevent the full delta from being applied.
            _lastDragPoint = new Point(_lastDragPoint.X + actualDelta, currentPoint.Y);
        }
    }

    private void EndDrag()
    {
        if (!_isDragging) return;

        _isDragging = false;
        Background = Brushes.Transparent;
    }

    private void AutoSizeColumn()
    {
        ColumnWidths?.AutoSizeColumn(ColumnName);
    }
}
