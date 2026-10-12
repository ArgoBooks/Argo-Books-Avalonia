using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ArgoBooks.Helpers;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Modals;

/// <summary>
/// Modal for viewing receipt images with pan, zoom, and rubber band overscroll.
/// Animation is handled automatically by ModalOverlay control.
/// </summary>
public partial class ReceiptViewerModal : UserControl
{
    #region Private Fields

    private ScrollViewer? _imageScrollViewer;
    private LayoutTransformControl? _zoomTransformControl;
    private Control? _pagesContainer;
    private OverscrollHelper? _overscrollHelper;
    private Slider? _zoomSlider;
    private TextBlock? _zoomPercentText;
    private bool _eventsSubscribed;
    private bool _updatingSlider;
    private ReceiptViewerModalViewModel? _subscribedVm;

    // Zoom settings
    private double _zoomLevel = 1.0;
    private const double MinZoom = 0.25;
    private const double MaxZoom = 4.0;
    private const double ZoomStep = 0.25;

    // Panning (any mouse button drag)
    private bool _isPanning;
    private Point _panStartPoint;
    private Vector _panStartOffset;

    #endregion

    public ReceiptViewerModal()
    {
        InitializeComponent();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        FindControls();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        FindControls();

        if (_imageScrollViewer != null)
        {
            _imageScrollViewer.AddHandler(PointerWheelChangedEvent, OnScrollViewerPointerWheelChanged, RoutingStrategies.Tunnel);
            _imageScrollViewer.AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
            _imageScrollViewer.AddHandler(PointerMovedEvent, OnPreviewPointerMoved, RoutingStrategies.Tunnel);
            _imageScrollViewer.AddHandler(PointerReleasedEvent, OnPreviewPointerReleased, RoutingStrategies.Tunnel);
        }

        // Subscribe to ViewModel property changes
        if (DataContext is ReceiptViewerModalViewModel vm && !_eventsSubscribed)
        {
            _eventsSubscribed = true;
            _subscribedVm = vm;
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        if (_subscribedVm != null)
        {
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedVm = null;
            _eventsSubscribed = false;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ReceiptViewerModalViewModel vm) return;

        if (e.PropertyName == nameof(ReceiptViewerModalViewModel.IsFullscreen))
        {
            // Modal is already visible, layout is valid, fit directly
            ZoomToFit();
        }
        else if (e.PropertyName == nameof(ReceiptViewerModalViewModel.IsOpen) && vm.IsOpen)
        {
            // Modal just opened, layout hasn't completed yet, need to defer
            _ = FitToWindowOnOpenAsync();
        }
        else if (e.PropertyName == nameof(ReceiptViewerModalViewModel.IsLoadingPages) && !vm.IsLoadingPages && vm.IsOpen)
        {
            // Pages are rendered asynchronously after open, re-fit once they finish loading.
            _ = FitToWindowOnOpenAsync();
        }
    }

    private async Task FitToWindowOnOpenAsync()
    {
        // Hide content while calculating fit to prevent flash of unzoomed image
        if (_zoomTransformControl != null)
            _zoomTransformControl.Opacity = 0;

        // Wait for layout and rendering passes to complete
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        ZoomToFit();

        if (_zoomTransformControl != null)
            _zoomTransformControl.Opacity = 1;
    }

    private void FindControls()
    {
        _imageScrollViewer ??= this.FindControl<ScrollViewer>("ImageScrollViewer");
        _zoomTransformControl ??= this.FindControl<LayoutTransformControl>("ZoomTransformControl");
        _pagesContainer ??= this.FindControl<ItemsControl>("ReceiptPagesItems");
        _zoomSlider ??= this.FindControl<Slider>("ZoomSlider");
        _zoomPercentText ??= this.FindControl<TextBlock>("ZoomPercentText");

        if (_zoomTransformControl != null && _overscrollHelper == null)
        {
            _overscrollHelper = new OverscrollHelper(_zoomTransformControl);
        }

        // Initialize slider value
        if (_zoomSlider != null)
        {
            _updatingSlider = true;
            _zoomSlider.Value = _zoomLevel;
            _updatingSlider = false;
        }

        UpdateZoomDisplay();
    }

    #region Event Handlers

    private void Modal_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is ReceiptViewerModalViewModel vm)
        {
            switch (e.Key)
            {
                case Key.F:
                    vm.ToggleFullscreenCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.Add:
                case Key.OemPlus:
                    ZoomIn();
                    e.Handled = true;
                    break;
                case Key.Subtract:
                case Key.OemMinus:
                    ZoomOut();
                    e.Handled = true;
                    break;
                case Key.D0:
                case Key.NumPad0:
                    ResetZoom();
                    e.Handled = true;
                    break;
                case Key.W:
                    ZoomToFit();
                    e.Handled = true;
                    break;
            }
        }
    }

    #endregion

    #region Zoom

    private void ApplyZoom()
    {
        if (_zoomTransformControl == null) return;
        _zoomTransformControl.LayoutTransform = new ScaleTransform(_zoomLevel, _zoomLevel);
        UpdateZoomDisplay();
    }

    private void UpdateZoomDisplay()
    {
        // Update slider (without triggering the event)
        if (_zoomSlider != null && !_updatingSlider)
        {
            _updatingSlider = true;
            _zoomSlider.Value = _zoomLevel;
            _updatingSlider = false;
        }

        // Update percentage text
        if (_zoomPercentText != null)
        {
            _zoomPercentText.Text = $"{_zoomLevel:P0}";
        }
    }

    private void ZoomIn() => ZoomTowardsCenter(true);
    private void ZoomOut() => ZoomTowardsCenter(false);

    private void ZoomIn_Click(object? sender, RoutedEventArgs e) => ZoomIn();
    private void ZoomOut_Click(object? sender, RoutedEventArgs e) => ZoomOut();

    private void ZoomSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSlider) return;

        ZoomToLevel(e.NewValue);
    }

    /// <summary>
    /// Zooms to a specific level while maintaining center focus.
    /// </summary>
    private void ZoomToLevel(double newZoom)
    {
        if (_imageScrollViewer == null || _zoomTransformControl == null) return;

        var oldZoom = _zoomLevel;
        newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);

        if (Math.Abs(oldZoom - newZoom) < 0.001) return;

        // Get the center of the viewport in content coordinates
        var viewportCenterX = _imageScrollViewer.Viewport.Width / 2;
        var viewportCenterY = _imageScrollViewer.Viewport.Height / 2;

        var contentCenterX = (_imageScrollViewer.Offset.X + viewportCenterX) / oldZoom;
        var contentCenterY = (_imageScrollViewer.Offset.Y + viewportCenterY) / oldZoom;

        _zoomLevel = newZoom;
        ApplyZoom();
        _zoomTransformControl.UpdateLayout();

        // Calculate new offset to keep the same content point at center
        var newOffsetX = contentCenterX * newZoom - viewportCenterX;
        var newOffsetY = contentCenterY * newZoom - viewportCenterY;

        var maxX = Math.Max(0, _imageScrollViewer.Extent.Width - _imageScrollViewer.Viewport.Width);
        var maxY = Math.Max(0, _imageScrollViewer.Extent.Height - _imageScrollViewer.Viewport.Height);

        _imageScrollViewer.Offset = new Vector(
            Math.Clamp(newOffsetX, 0, maxX),
            Math.Clamp(newOffsetY, 0, maxY)
        );
    }

    private void ResetZoom()
    {
        _zoomLevel = 1.0;
        ApplyZoom();
    }

    private void FitToWindow_Click(object? sender, RoutedEventArgs e)
    {
        ZoomToFit();
    }

    /// <summary>
    /// Zooms to fit the entire image in the viewport.
    /// </summary>
    private void ZoomToFit()
    {
        if (_imageScrollViewer == null || _pagesContainer == null || _zoomTransformControl == null) return;

        // Measure the stacked pages container at zoom 1.0 to get its natural DIP size
        // (using pixel size would be wrong on HiDPI displays)
        _zoomLevel = 1.0;
        ApplyZoom();
        _zoomTransformControl.UpdateLayout();

        var imageWidth = _pagesContainer.Bounds.Width;
        var imageHeight = _pagesContainer.Bounds.Height;

        if (imageWidth <= 0 || imageHeight <= 0) return;

        // Get viewport size (accounting for padding)
        var viewportWidth = _imageScrollViewer.Bounds.Width - 32; // 16px padding on each side
        var viewportHeight = _imageScrollViewer.Bounds.Height - 32;

        if (viewportWidth <= 0 || viewportHeight <= 0) return;

        // Calculate zoom to fit both width and height
        var scaleX = viewportWidth / imageWidth;
        var scaleY = viewportHeight / imageHeight;

        // Use a very low minimum so fit-to-window works for any image size/orientation
        _zoomLevel = Math.Clamp(Math.Min(scaleX, scaleY), 0.01, MaxZoom);
        ApplyZoom();
    }

    private void ZoomTowardsCenter(bool zoomIn)
    {
        var newZoom = zoomIn
            ? Math.Min(_zoomLevel + ZoomStep, MaxZoom)
            : Math.Max(_zoomLevel - ZoomStep, MinZoom);

        ZoomToLevel(newZoom);
    }

    private void OnScrollViewerPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var delta = e.Delta.Y;
        if (delta != 0)
        {
            var viewportPoint = e.GetPosition(_imageScrollViewer);
            ZoomAtPoint(delta > 0, viewportPoint);
        }
        e.Handled = true;
    }

    private void ZoomAtPoint(bool zoomIn, Point viewportPoint)
    {
        if (_imageScrollViewer == null || _zoomTransformControl == null) return;

        var oldZoom = _zoomLevel;
        var newZoom = zoomIn
            ? Math.Min(oldZoom + ZoomStep, MaxZoom)
            : Math.Max(oldZoom - ZoomStep, MinZoom);

        if (Math.Abs(oldZoom - newZoom) < 0.001) return;

        // Account for centering when content is smaller than viewport
        var oldExtent = _imageScrollViewer.Extent;
        var viewportWidth = _imageScrollViewer.Viewport.Width;
        var viewportHeight = _imageScrollViewer.Viewport.Height;
        var oldOffset = _imageScrollViewer.Offset;

        var centeringOffsetX = oldExtent.Width < viewportWidth
            ? (viewportWidth - oldExtent.Width) / 2
            : 0;
        var centeringOffsetY = oldExtent.Height < viewportHeight
            ? (viewportHeight - oldExtent.Height) / 2
            : 0;

        // Calculate mouse position in content space
        var contentX = viewportPoint.X + oldOffset.X - centeringOffsetX;
        var contentY = viewportPoint.Y + oldOffset.Y - centeringOffsetY;

        // Convert to unscaled coordinates
        var unscaledX = contentX / oldZoom;
        var unscaledY = contentY / oldZoom;

        _zoomLevel = newZoom;
        ApplyZoom();
        _zoomTransformControl.UpdateLayout();

        // Calculate new offset to keep mouse position at same content point
        var newExtent = _imageScrollViewer.Extent;
        var newCenteringOffsetX = newExtent.Width < viewportWidth
            ? (viewportWidth - newExtent.Width) / 2
            : 0;
        var newCenteringOffsetY = newExtent.Height < viewportHeight
            ? (viewportHeight - newExtent.Height) / 2
            : 0;

        var newContentX = unscaledX * newZoom;
        var newContentY = unscaledY * newZoom;

        var newOffsetX = newContentX - viewportPoint.X + newCenteringOffsetX;
        var newOffsetY = newContentY - viewportPoint.Y + newCenteringOffsetY;

        var maxX = Math.Max(0, newExtent.Width - viewportWidth);
        var maxY = Math.Max(0, newExtent.Height - viewportHeight);

        _imageScrollViewer.Offset = new Vector(
            Math.Clamp(newOffsetX, 0, maxX),
            Math.Clamp(newOffsetY, 0, maxY)
        );
    }

    #endregion

    #region Panning and Overscroll

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_imageScrollViewer == null) return;

        // Don't start panning when the press lands on a scroll bar, let it scroll normally.
        if (IsOnScrollBar(e.Source)) return;

        var point = e.GetCurrentPoint(_imageScrollViewer);
        if (point.Properties.IsLeftButtonPressed
            || point.Properties.IsRightButtonPressed
            || point.Properties.IsMiddleButtonPressed)
        {
            _isPanning = true;
            _panStartPoint = e.GetPosition(this);
            _panStartOffset = new Vector(_imageScrollViewer.Offset.X, _imageScrollViewer.Offset.Y);
            e.Pointer.Capture(_imageScrollViewer);
            _imageScrollViewer.Cursor = new Cursor(StandardCursorType.Hand);
            e.Handled = true;
        }
    }

    /// <summary>
    /// True if the event source is a scroll bar (or a part of one, e.g. the drag thumb).
    /// </summary>
    private static bool IsOnScrollBar(object? source)
    {
        var current = source as Visual;
        while (current != null)
        {
            if (current is ScrollBar) return true;
            current = current.GetVisualParent();
        }
        return false;
    }

    private void OnPreviewPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPanning || _imageScrollViewer == null || _overscrollHelper == null) return;

        var currentPoint = e.GetPosition(this);
        var delta = _panStartPoint - currentPoint;

        var desiredX = _panStartOffset.X + delta.X;
        var desiredY = _panStartOffset.Y + delta.Y;

        var maxX = Math.Max(0, _imageScrollViewer.Extent.Width - _imageScrollViewer.Viewport.Width);
        var maxY = Math.Max(0, _imageScrollViewer.Extent.Height - _imageScrollViewer.Viewport.Height);

        var (clampedX, clampedY, overscrollX, overscrollY) =
            _overscrollHelper.CalculateOverscroll(desiredX, desiredY, maxX, maxY);

        _imageScrollViewer.Offset = new Vector(clampedX, clampedY);
        _overscrollHelper.ApplyOverscroll(overscrollX, overscrollY);

        e.Handled = true;
    }

    private void OnPreviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPanning) return;

        _isPanning = false;
        e.Pointer.Capture(null);

        if (_imageScrollViewer != null)
            _imageScrollViewer.Cursor = new Cursor(StandardCursorType.Hand); // restore hand

        if (_overscrollHelper?.HasOverscroll == true)
        {
            _ = _overscrollHelper.AnimateSnapBackAsync();
        }

        e.Handled = true;
    }

    #endregion
}
