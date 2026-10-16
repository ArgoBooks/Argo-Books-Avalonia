using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
    private ReceiptViewerModalViewModel? _subscribedVm;

    private readonly PreviewZoom _zoom;

    // Panning (any mouse button drag)
    private bool _isPanning;
    private Point _panStartPoint;
    private Vector _panStartOffset;

    #endregion

    public ReceiptViewerModal()
    {
        // The scroll viewer has 16 px of padding on each side.
        _zoom = new PreviewZoom(() => _imageScrollViewer, () => _zoomTransformControl, () => _pagesContainer,
            () => _zoomSlider, () => _zoomPercentText, fitPadding: 32);
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

        _zoom.ShowLevel();
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

    private void ZoomIn() => _zoom.ZoomTowardsCenter(true);
    private void ZoomOut() => _zoom.ZoomTowardsCenter(false);
    private void ResetZoom() => _zoom.Reset();
    private void ZoomToFit() => _zoom.FitToWindow(measureAtFullSize: true);

    private void ZoomIn_Click(object? sender, RoutedEventArgs e) => ZoomIn();
    private void ZoomOut_Click(object? sender, RoutedEventArgs e) => ZoomOut();
    private void ZoomSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e) => _zoom.OnSliderChanged(e.NewValue);
    private void FitToWindow_Click(object? sender, RoutedEventArgs e) => ZoomToFit();

    private void OnScrollViewerPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var delta = e.Delta.Y;
        if (delta != 0)
            _zoom.ZoomAtPoint(delta > 0, e.GetPosition(_imageScrollViewer));
        e.Handled = true;
    }

    #endregion

    #region Panning and Overscroll

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_imageScrollViewer == null) return;

        // Don't start panning when the press lands on a scroll bar, let it scroll normally.
        if (PreviewZoom.IsOnScrollBar(e.Source)) return;

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
