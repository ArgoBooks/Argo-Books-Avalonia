using ArgoBooks.Controls.ColumnWidths;
using ArgoBooks.Converters;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

    // Avalonia sets the pointer-over element (and thus the cursor) to null whenever the
    // pointer is captured but sits over something OTHER than the captured element, which
    // happens constantly during a resize because the gripper lags the pointer by a frame.
    // That null pointer-over shows the default cursor, hence the flicker. To keep the
    // resize cursor, we capture a transparent full-window overlay that carries the resize
    // cursor: the captured element is then exactly what the pointer is over everywhere, so
    // the cursor never changes. The drag itself is driven from the overlay's events.
    private OverlayLayer? _cursorOverlayLayer;
    private Border? _cursorOverlay;

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

            // Capture a transparent full-window overlay carrying the resize cursor so the
            // cursor stays put for the whole drag (see field comment). The overlay drives
            // the resize via its captured pointer events.
            _cursorOverlayLayer = OverlayLayer.GetOverlayLayer(this);
            if (_cursorOverlayLayer != null)
            {
                // The overlay must cover the window. On the very first drag the layer hasn't
                // been laid out yet, so its Bounds is still 0,0; fall back to the window's
                // ClientSize (valid from startup) so the overlay isn't zero-sized that one time.
                var size = _cursorOverlayLayer.Bounds.Size;
                if (size.Width <= 0 || size.Height <= 0)
                {
                    size = TopLevel.GetTopLevel(this)?.ClientSize ?? size;
                }

                _cursorOverlay = new Border
                {
                    Background = Brushes.Transparent,
                    Cursor = Cursor,
                    Width = size.Width,
                    Height = size.Height
                };
                _cursorOverlay.PointerMoved += OnOverlayPointerMoved;
                _cursorOverlay.PointerReleased += OnOverlayPointerReleased;
                _cursorOverlay.PointerCaptureLost += OnOverlayPointerCaptureLost;
                _cursorOverlayLayer.Children.Add(_cursorOverlay);
                e.Pointer.Capture(_cursorOverlay);
            }
            else
            {
                // Fallback: no overlay layer available, drive the drag from the gripper.
                e.Pointer.Capture(this);
            }

            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        // Only used in the fallback path (no overlay); normally the overlay drives the drag.
        if (_isDragging && _cursorOverlay == null)
        {
            ApplyResize(e.GetPosition(TopLevel.GetTopLevel(this)));
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isDragging && _cursorOverlay == null)
        {
            e.Pointer.Capture(null);
            EndDrag();
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_cursorOverlay == null)
        {
            EndDrag();
        }
    }

    private void OnOverlayPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging) return;
        ApplyResize(e.GetPosition(TopLevel.GetTopLevel(this)));
        e.Handled = true;
    }

    private void OnOverlayPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging) return;
        e.Pointer.Capture(null);
        EndDrag();
        e.Handled = true;
    }

    private void OnOverlayPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndDrag();
    }

    private void ApplyResize(Point currentPoint)
    {
        var delta = currentPoint.X - _lastDragPoint.X;

        if (Math.Abs(delta) >= 1)
        {
            var actualDelta = ColumnWidths?.ResizeColumn(ColumnName, delta) ?? 0;
            // Only move the drag anchor by the amount actually applied.
            // This ensures the mouse must "catch up" to the column position
            // when constraints prevent the full delta from being applied.
            _lastDragPoint = new Point(_lastDragPoint.X + actualDelta, currentPoint.Y);
        }
    }

    private void EndDrag()
    {
        if (!_isDragging) return;

        _isDragging = false;
        Background = Brushes.Transparent;

        if (_cursorOverlay != null)
        {
            _cursorOverlay.PointerMoved -= OnOverlayPointerMoved;
            _cursorOverlay.PointerReleased -= OnOverlayPointerReleased;
            _cursorOverlay.PointerCaptureLost -= OnOverlayPointerCaptureLost;
            _cursorOverlayLayer?.Children.Remove(_cursorOverlay);
            _cursorOverlay = null;
            _cursorOverlayLayer = null;
        }
    }

    private void AutoSizeColumn()
    {
        ColumnWidths?.AutoSizeColumn(ColumnName);
    }
}
