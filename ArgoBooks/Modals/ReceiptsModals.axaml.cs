using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ArgoBooks.Helpers;
using ArgoBooks.Utilities;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Modals;

/// <summary>
/// Code-behind for the Receipts modals.
/// </summary>
public partial class ReceiptsModals : UserControl
{
    // Zoom settings for scan preview
    private double _scanZoomLevel = 1.0;
    private double _bulkZoomLevel = 1.0;
    private const double MinZoom = 0.25;
    private const double MaxZoom = 4.0;
    private const double ZoomStep = 0.25;
    private bool _updatingSlider;
    private bool _updatingBulkSlider;
    private ReceiptsModalsViewModel? _subscribedVm;

    // Panning (any mouse button drag on preview areas)
    private bool _isPanning;
    private Point _panStartPoint;
    private Vector _panStartOffset;
    private ScrollViewer? _panScrollViewer;
    private OverscrollHelper? _overscrollHelper;
    private OverscrollHelper? _bulkOverscrollHelper;
    private OverscrollHelper? _activePanOverscroll;

    public ReceiptsModals()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (ScanPreviewScrollViewer != null)
        {
            ScanPreviewScrollViewer.AddHandler(PointerWheelChangedEvent, OnScanPreviewPointerWheelChanged, RoutingStrategies.Tunnel);
            ScanPreviewScrollViewer.AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
            ScanPreviewScrollViewer.AddHandler(PointerMovedEvent, OnPreviewPointerMoved, RoutingStrategies.Tunnel);
            ScanPreviewScrollViewer.AddHandler(PointerReleasedEvent, OnPreviewPointerReleased, RoutingStrategies.Tunnel);
        }

        if (ScanPreviewZoomTransform != null && _overscrollHelper == null)
        {
            _overscrollHelper = new OverscrollHelper(ScanPreviewZoomTransform);
        }

        if (ScanPreviewZoomSlider != null)
        {
            _updatingSlider = true;
            ScanPreviewZoomSlider.Value = _scanZoomLevel;
            _updatingSlider = false;
        }

        // Bulk preview zoom setup
        if (BulkPreviewScrollViewer != null)
        {
            BulkPreviewScrollViewer.AddHandler(PointerWheelChangedEvent, OnBulkPreviewPointerWheelChanged, RoutingStrategies.Tunnel);
            BulkPreviewScrollViewer.AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
            BulkPreviewScrollViewer.AddHandler(PointerMovedEvent, OnPreviewPointerMoved, RoutingStrategies.Tunnel);
            BulkPreviewScrollViewer.AddHandler(PointerReleasedEvent, OnPreviewPointerReleased, RoutingStrategies.Tunnel);
        }

        if (BulkPreviewZoomTransform != null && _bulkOverscrollHelper == null)
        {
            _bulkOverscrollHelper = new OverscrollHelper(BulkPreviewZoomTransform);
        }

        if (BulkPreviewZoomSlider != null)
        {
            _updatingBulkSlider = true;
            BulkPreviewZoomSlider.Value = _bulkZoomLevel;
            _updatingBulkSlider = false;
        }

        // Wire up drag-drop on the bulk scan drop zone
        var dropZone = this.FindControl<Border>("DropZoneBorder");
        if (dropZone != null)
        {
            DragDrop.SetAllowDrop(dropZone, true);
            dropZone.AddHandler(DragDrop.DragOverEvent, OnDropZoneDragOver);
            dropZone.AddHandler(DragDrop.DropEvent, OnDropZoneDrop);
        }

        // Subscribe to ViewModel to fit image when scan results arrive
        if (DataContext is ReceiptsModalsViewModel vm)
        {
            _subscribedVm = vm;
            vm.PropertyChanged += OnScanViewModelPropertyChanged;
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        if (_subscribedVm != null)
        {
            _subscribedVm.PropertyChanged -= OnScanViewModelPropertyChanged;
            _subscribedVm = null;
        }
    }

    private void OnScanViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ReceiptsModalsViewModel vm) return;

        if (e.PropertyName == nameof(ReceiptsModalsViewModel.HasScanResult) && vm.HasScanResult)
        {
            // Reset zoom and fit when results first appear
            _scanZoomLevel = 1.0;
            _ = FitScanPreviewAfterLayoutAsync();
        }
        else if (e.PropertyName == nameof(ReceiptsModalsViewModel.IsScanReviewModalOpen) && vm.IsScanReviewModalOpen && vm.HasScanResult)
        {
            // Fit to window when modal re-opens with existing results
            _scanZoomLevel = 1.0;
            _ = FitScanPreviewAfterLayoutAsync();
        }
        else if (e.PropertyName == nameof(ReceiptsModalsViewModel.IsFullscreen))
        {
            // Re-fit after fullscreen toggle changes viewport size
            _ = FitScanPreviewAfterLayoutAsync();
        }
        else if (e.PropertyName == nameof(ReceiptsModalsViewModel.CurrentBulkItem))
        {
            // Reset zoom and fit when navigating to a new receipt in carousel review
            _bulkZoomLevel = 1.0;
            _ = FitBulkPreviewAfterLayoutAsync();
        }
    }

    private async Task FitScanPreviewAfterLayoutAsync()
    {
        // Wait for layout and rendering passes to complete
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        ScanPreviewFitToWindow();
    }

    #region Scan Preview Zoom

    private void ApplyScanZoom()
    {
        if (ScanPreviewZoomTransform == null) return;
        ScanPreviewZoomTransform.LayoutTransform = new ScaleTransform(_scanZoomLevel, _scanZoomLevel);
        UpdateScanZoomDisplay();
    }

    private void UpdateScanZoomDisplay()
    {
        if (ScanPreviewZoomSlider != null && !_updatingSlider)
        {
            _updatingSlider = true;
            ScanPreviewZoomSlider.Value = _scanZoomLevel;
            _updatingSlider = false;
        }

        if (ScanPreviewZoomText != null)
        {
            ScanPreviewZoomText.Text = $"{_scanZoomLevel:P0}";
        }
    }

    private void ScanPreviewZoomIn_Click(object? sender, RoutedEventArgs e) => ScanPreviewZoomTowardsCenter(true);
    private void ScanPreviewZoomOut_Click(object? sender, RoutedEventArgs e) => ScanPreviewZoomTowardsCenter(false);

    private void ScanPreviewZoomSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSlider) return;
        ScanPreviewZoomToLevel(e.NewValue);
    }

    private void ScanPreviewZoomToLevel(double newZoom)
    {
        if (ScanPreviewScrollViewer == null || ScanPreviewZoomTransform == null) return;

        var oldZoom = _scanZoomLevel;
        newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
        if (Math.Abs(oldZoom - newZoom) < 0.001) return;

        var viewportCenterX = ScanPreviewScrollViewer.Viewport.Width / 2;
        var viewportCenterY = ScanPreviewScrollViewer.Viewport.Height / 2;
        var contentCenterX = (ScanPreviewScrollViewer.Offset.X + viewportCenterX) / oldZoom;
        var contentCenterY = (ScanPreviewScrollViewer.Offset.Y + viewportCenterY) / oldZoom;

        _scanZoomLevel = newZoom;
        ApplyScanZoom();
        ScanPreviewZoomTransform.UpdateLayout();

        var newOffsetX = contentCenterX * newZoom - viewportCenterX;
        var newOffsetY = contentCenterY * newZoom - viewportCenterY;
        var maxX = Math.Max(0, ScanPreviewScrollViewer.Extent.Width - ScanPreviewScrollViewer.Viewport.Width);
        var maxY = Math.Max(0, ScanPreviewScrollViewer.Extent.Height - ScanPreviewScrollViewer.Viewport.Height);

        ScanPreviewScrollViewer.Offset = new Vector(
            Math.Clamp(newOffsetX, 0, maxX),
            Math.Clamp(newOffsetY, 0, maxY)
        );
    }

    private void ScanPreviewFitToWindow_Click(object? sender, RoutedEventArgs e) => ScanPreviewFitToWindow();

    private void ScanPreviewFitToWindow()
    {
        if (ScanPreviewScrollViewer == null || ScanPreviewItems == null) return;

        // Fit the whole stacked-pages container (correct on HiDPI via measured DIP bounds).
        var imageWidth = ScanPreviewItems.Bounds.Width;
        var imageHeight = ScanPreviewItems.Bounds.Height;

        if (imageWidth <= 0 || imageHeight <= 0) return;

        var viewportWidth = ScanPreviewScrollViewer.Bounds.Width;
        var viewportHeight = ScanPreviewScrollViewer.Bounds.Height;
        if (viewportWidth <= 0 || viewportHeight <= 0) return;

        var scaleX = viewportWidth / imageWidth;
        var scaleY = viewportHeight / imageHeight;

        _scanZoomLevel = Math.Clamp(Math.Min(scaleX, scaleY), 0.01, MaxZoom);
        ApplyScanZoom();
    }

    private void ScanPreviewZoomTowardsCenter(bool zoomIn)
    {
        var newZoom = zoomIn
            ? Math.Min(_scanZoomLevel + ZoomStep, MaxZoom)
            : Math.Max(_scanZoomLevel - ZoomStep, MinZoom);
        ScanPreviewZoomToLevel(newZoom);
    }

    private void OnScanPreviewPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var delta = e.Delta.Y;
        if (delta != 0 && ScanPreviewZoomTransform != null)
        {
            var viewportPoint = e.GetPosition(ScanPreviewScrollViewer);
            var contentPoint = e.GetPosition(ScanPreviewZoomTransform);
            ScanPreviewZoomAtPoint(delta > 0, viewportPoint, contentPoint);
        }
        e.Handled = true;
    }

    private void ScanPreviewZoomAtPoint(bool zoomIn, Point viewportPoint, Point scaledContentPoint)
    {
        if (ScanPreviewScrollViewer == null || ScanPreviewZoomTransform == null) return;

        var oldZoom = _scanZoomLevel;
        var newZoom = zoomIn
            ? Math.Min(oldZoom + ZoomStep, MaxZoom)
            : Math.Max(oldZoom - ZoomStep, MinZoom);
        if (Math.Abs(oldZoom - newZoom) < 0.001) return;

        var oldExtent = ScanPreviewScrollViewer.Extent;
        var viewportWidth = ScanPreviewScrollViewer.Viewport.Width;
        var viewportHeight = ScanPreviewScrollViewer.Viewport.Height;
        var oldOffset = ScanPreviewScrollViewer.Offset;

        var centeringOffsetX = oldExtent.Width < viewportWidth ? (viewportWidth - oldExtent.Width) / 2 : 0;
        var centeringOffsetY = oldExtent.Height < viewportHeight ? (viewportHeight - oldExtent.Height) / 2 : 0;

        var contentX = viewportPoint.X + oldOffset.X - centeringOffsetX;
        var contentY = viewportPoint.Y + oldOffset.Y - centeringOffsetY;
        var unscaledX = contentX / oldZoom;
        var unscaledY = contentY / oldZoom;

        _scanZoomLevel = newZoom;
        ApplyScanZoom();
        ScanPreviewZoomTransform.UpdateLayout();

        var newExtent = ScanPreviewScrollViewer.Extent;
        var newCenteringOffsetX = newExtent.Width < viewportWidth ? (viewportWidth - newExtent.Width) / 2 : 0;
        var newCenteringOffsetY = newExtent.Height < viewportHeight ? (viewportHeight - newExtent.Height) / 2 : 0;

        var newOffsetX = unscaledX * newZoom - viewportPoint.X + newCenteringOffsetX;
        var newOffsetY = unscaledY * newZoom - viewportPoint.Y + newCenteringOffsetY;

        var maxX = Math.Max(0, newExtent.Width - viewportWidth);
        var maxY = Math.Max(0, newExtent.Height - viewportHeight);

        ScanPreviewScrollViewer.Offset = new Vector(
            Math.Clamp(newOffsetX, 0, maxX),
            Math.Clamp(newOffsetY, 0, maxY)
        );
    }

    #endregion

    #region Preview Panning (Scan + Bulk)

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;

        // Don't start panning when the press lands on a scroll bar, let it scroll normally.
        if (IsOnScrollBar(e.Source)) return;

        var point = e.GetCurrentPoint(scrollViewer);
        if (point.Properties.IsLeftButtonPressed
            || point.Properties.IsRightButtonPressed
            || point.Properties.IsMiddleButtonPressed)
        {
            _isPanning = true;
            _panScrollViewer = scrollViewer;
            _activePanOverscroll = scrollViewer == BulkPreviewScrollViewer
                ? _bulkOverscrollHelper
                : _overscrollHelper;
            _panStartPoint = e.GetPosition(this);
            _panStartOffset = new Vector(scrollViewer.Offset.X, scrollViewer.Offset.Y);
            e.Pointer.Capture(scrollViewer);
            scrollViewer.Cursor = new Cursor(StandardCursorType.Hand);
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
        if (!_isPanning || _panScrollViewer == null) return;

        var currentPoint = e.GetPosition(this);
        var delta = _panStartPoint - currentPoint;

        var desiredX = _panStartOffset.X + delta.X;
        var desiredY = _panStartOffset.Y + delta.Y;

        var maxX = Math.Max(0, _panScrollViewer.Extent.Width - _panScrollViewer.Viewport.Width);
        var maxY = Math.Max(0, _panScrollViewer.Extent.Height - _panScrollViewer.Viewport.Height);

        if (_activePanOverscroll != null)
        {
            var (clampedX, clampedY, overscrollX, overscrollY) =
                _activePanOverscroll.CalculateOverscroll(desiredX, desiredY, maxX, maxY);

            _panScrollViewer.Offset = new Vector(clampedX, clampedY);
            _activePanOverscroll.ApplyOverscroll(overscrollX, overscrollY);
        }
        else
        {
            _panScrollViewer.Offset = new Vector(
                Math.Clamp(desiredX, 0, maxX),
                Math.Clamp(desiredY, 0, maxY));
        }

        e.Handled = true;
    }

    private void OnPreviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPanning) return;

        var scrollViewer = _panScrollViewer;
        _isPanning = false;
        _panScrollViewer = null;
        e.Pointer.Capture(null);

        if (scrollViewer != null)
            scrollViewer.Cursor = new Cursor(StandardCursorType.Hand); // restore hand

        if (_activePanOverscroll?.HasOverscroll == true)
        {
            _ = _activePanOverscroll.AnimateSnapBackAsync();
        }

        _activePanOverscroll = null;
        e.Handled = true;
    }

    #endregion

    #region Bulk Preview Zoom

    private void BulkPreviewZoomIn_Click(object? sender, RoutedEventArgs e) => BulkPreviewZoomTowardsCenter(true);
    private void BulkPreviewZoomOut_Click(object? sender, RoutedEventArgs e) => BulkPreviewZoomTowardsCenter(false);

    private void BulkPreviewZoomSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingBulkSlider) return;
        BulkPreviewZoomToLevel(e.NewValue);
    }

    private void BulkPreviewZoomToLevel(double newZoom)
    {
        if (BulkPreviewScrollViewer == null || BulkPreviewZoomTransform == null) return;

        var oldZoom = _bulkZoomLevel;
        newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
        if (Math.Abs(oldZoom - newZoom) < 0.001) return;

        var viewportCenterX = BulkPreviewScrollViewer.Viewport.Width / 2;
        var viewportCenterY = BulkPreviewScrollViewer.Viewport.Height / 2;
        var contentCenterX = (BulkPreviewScrollViewer.Offset.X + viewportCenterX) / oldZoom;
        var contentCenterY = (BulkPreviewScrollViewer.Offset.Y + viewportCenterY) / oldZoom;

        _bulkZoomLevel = newZoom;
        ApplyBulkZoom();
        BulkPreviewZoomTransform.UpdateLayout();

        var newOffsetX = contentCenterX * newZoom - viewportCenterX;
        var newOffsetY = contentCenterY * newZoom - viewportCenterY;
        var maxX = Math.Max(0, BulkPreviewScrollViewer.Extent.Width - BulkPreviewScrollViewer.Viewport.Width);
        var maxY = Math.Max(0, BulkPreviewScrollViewer.Extent.Height - BulkPreviewScrollViewer.Viewport.Height);

        BulkPreviewScrollViewer.Offset = new Vector(
            Math.Clamp(newOffsetX, 0, maxX),
            Math.Clamp(newOffsetY, 0, maxY)
        );
    }

    private void BulkPreviewFitToWindow_Click(object? sender, RoutedEventArgs e) => BulkPreviewFitToWindow();

    private void BulkPreviewFitToWindow()
    {
        if (BulkPreviewScrollViewer == null || BulkPreviewItems == null) return;

        // Fit the whole stacked-pages container.
        var imageWidth = BulkPreviewItems.Bounds.Width;
        var imageHeight = BulkPreviewItems.Bounds.Height;

        if (imageWidth <= 0 || imageHeight <= 0) return;

        var viewportWidth = BulkPreviewScrollViewer.Bounds.Width;
        var viewportHeight = BulkPreviewScrollViewer.Bounds.Height;
        if (viewportWidth <= 0 || viewportHeight <= 0) return;

        var scaleX = viewportWidth / imageWidth;
        var scaleY = viewportHeight / imageHeight;

        _bulkZoomLevel = Math.Clamp(Math.Min(scaleX, scaleY), 0.01, MaxZoom);
        ApplyBulkZoom();
    }

    private void BulkPreviewZoomTowardsCenter(bool zoomIn)
    {
        var newZoom = zoomIn
            ? Math.Min(_bulkZoomLevel + ZoomStep, MaxZoom)
            : Math.Max(_bulkZoomLevel - ZoomStep, MinZoom);
        BulkPreviewZoomToLevel(newZoom);
    }

    private void OnBulkPreviewPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var delta = e.Delta.Y;
        if (delta != 0 && BulkPreviewZoomTransform != null)
        {
            BulkPreviewZoomTowardsCenter(delta > 0);
        }
        e.Handled = true;
    }

    private void ApplyBulkZoom()
    {
        if (BulkPreviewZoomTransform == null) return;
        BulkPreviewZoomTransform.LayoutTransform = new ScaleTransform(_bulkZoomLevel, _bulkZoomLevel);

        if (BulkPreviewZoomSlider != null && !_updatingBulkSlider)
        {
            _updatingBulkSlider = true;
            BulkPreviewZoomSlider.Value = _bulkZoomLevel;
            _updatingBulkSlider = false;
        }

        if (BulkPreviewZoomText != null)
        {
            BulkPreviewZoomText.Text = $"{_bulkZoomLevel:P0}";
        }
    }

    private async Task FitBulkPreviewAfterLayoutAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        BulkPreviewFitToWindow();
    }

    #endregion

    #region Bulk Scan Drop Zone

    private void OnDropZoneDragOver(object? sender, DragEventArgs e)
    {
        // The copy cursor shows only when a dragged file is a format the scanner takes, so a drop that would be ignored says so first.
        var files = e.DataTransfer.TryGetFiles();
        if (files != null && files.Any(f => FilePickerTypes.IsSupportedReceiptFile(f.TryGetLocalPath())))
        {
            e.DragEffects = DragDropEffects.Copy;
            return;
        }
        e.DragEffects = DragDropEffects.None;
    }

    private void OnDropZoneDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files == null) return;

        var paths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        if (DataContext is ReceiptsModalsViewModel vm)
        {
            vm.AddFilesToQueue(paths!);
        }
    }

    private async void OnBrowseFilesClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Receipts to Scan",
                AllowMultiple = true,
                FileTypeFilter = [FilePickerTypes.AllSupportedTypes, FilePickerTypes.ImageFileType, FilePickerTypes.PdfFileType]
            });

            if (files.Count > 0 && DataContext is ReceiptsModalsViewModel vm)
            {
                var paths = files
                    .Select(f => f.TryGetLocalPath())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();
                vm.AddFilesToQueue(paths!);
            }
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.FileSystem, "OnBrowseFilesClick");
        }
    }

    #endregion
}
