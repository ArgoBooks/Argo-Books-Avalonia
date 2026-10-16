using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ArgoBooks.Helpers;
using ArgoBooks.Utilities;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Modals;

/// <summary>
/// Code-behind for the Receipts modals.
/// </summary>
public partial class ReceiptsModals : UserControl
{
    private readonly PreviewZoom _scanZoom;
    private readonly PreviewZoom _bulkZoom;
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
        _scanZoom = new PreviewZoom(() => ScanPreviewScrollViewer, () => ScanPreviewZoomTransform, () => ScanPreviewItems,
            () => ScanPreviewZoomSlider, () => ScanPreviewZoomText);
        _bulkZoom = new PreviewZoom(() => BulkPreviewScrollViewer, () => BulkPreviewZoomTransform, () => BulkPreviewItems,
            () => BulkPreviewZoomSlider, () => BulkPreviewZoomText);
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

        _scanZoom.ShowLevel();

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

        _bulkZoom.ShowLevel();

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
            // Fit when results first appear
            _ = _scanZoom.FitAfterLayoutAsync();
        }
        else if (e.PropertyName == nameof(ReceiptsModalsViewModel.IsScanReviewModalOpen) && vm.IsScanReviewModalOpen && vm.HasScanResult)
        {
            // Fit to window when modal re-opens with existing results
            _ = _scanZoom.FitAfterLayoutAsync();
        }
        else if (e.PropertyName == nameof(ReceiptsModalsViewModel.IsFullscreen))
        {
            // Re-fit after fullscreen toggle changes viewport size
            _ = _scanZoom.FitAfterLayoutAsync();
        }
        else if (e.PropertyName == nameof(ReceiptsModalsViewModel.CurrentBulkItem))
        {
            // Fit when navigating to a new receipt in carousel review
            _ = _bulkZoom.FitAfterLayoutAsync();
        }
    }

    #region Scan Preview Zoom

    private void ScanPreviewZoomIn_Click(object? sender, RoutedEventArgs e) => _scanZoom.ZoomTowardsCenter(true);
    private void ScanPreviewZoomOut_Click(object? sender, RoutedEventArgs e) => _scanZoom.ZoomTowardsCenter(false);
    private void ScanPreviewZoomSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e) => _scanZoom.OnSliderChanged(e.NewValue);
    private void ScanPreviewFitToWindow_Click(object? sender, RoutedEventArgs e) => _scanZoom.FitToWindow();

    private void OnScanPreviewPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var delta = e.Delta.Y;
        if (delta != 0)
            _scanZoom.ZoomAtPoint(delta > 0, e.GetPosition(ScanPreviewScrollViewer));
        e.Handled = true;
    }

    #endregion

    #region Preview Panning (Scan + Bulk)

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;

        // Don't start panning when the press lands on a scroll bar, let it scroll normally.
        if (PreviewZoom.IsOnScrollBar(e.Source)) return;

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

    private void BulkPreviewZoomIn_Click(object? sender, RoutedEventArgs e) => _bulkZoom.ZoomTowardsCenter(true);
    private void BulkPreviewZoomOut_Click(object? sender, RoutedEventArgs e) => _bulkZoom.ZoomTowardsCenter(false);
    private void BulkPreviewZoomSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e) => _bulkZoom.OnSliderChanged(e.NewValue);
    private void BulkPreviewFitToWindow_Click(object? sender, RoutedEventArgs e) => _bulkZoom.FitToWindow();

    private void OnBulkPreviewPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var delta = e.Delta.Y;
        if (delta != 0)
            _bulkZoom.ZoomTowardsCenter(delta > 0);
        e.Handled = true;
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
                FileTypeFilter = FilePickerTypes.ReceiptFilters
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
