using System.ComponentModel;
using ArgoBooks.Helpers;
using ArgoBooks.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ArgoBooks.Modals;

/// <summary>
/// Code-behind for the Purchase Orders modals.
/// ESC key handling is managed by ModalOverlay.
/// </summary>
public partial class PurchaseOrdersModals : UserControl
{
    private readonly PreviewZoom _sendZoom;
    private OverscrollHelper? _sendOverscrollHelper;

    private bool _isPanning;
    private Point _panStartPoint;
    private Vector _panStartOffset;
    private ScrollViewer? _panScrollViewer;
    private OverscrollHelper? _activePanOverscroll;

    private PurchaseOrdersModalsViewModel? _subscribedVm;

    public PurchaseOrdersModals()
    {
        _sendZoom = new PreviewZoom(() => SendPdfPreviewScrollViewer, () => SendPdfPreviewZoomTransform, () => SendPdfPreviewCard,
            () => SendPdfPreviewZoomSlider, () => SendPdfPreviewZoomText);
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (SendPdfPreviewScrollViewer != null)
        {
            SendPdfPreviewScrollViewer.AddHandler(PointerWheelChangedEvent, OnSendPdfPreviewPointerWheelChanged, RoutingStrategies.Tunnel);
            SendPdfPreviewScrollViewer.AddHandler(PointerPressedEvent, OnSendPdfPreviewPointerPressed, RoutingStrategies.Tunnel);
            SendPdfPreviewScrollViewer.AddHandler(PointerMovedEvent, OnSendPdfPreviewPointerMoved, RoutingStrategies.Tunnel);
            SendPdfPreviewScrollViewer.AddHandler(PointerReleasedEvent, OnSendPdfPreviewPointerReleased, RoutingStrategies.Tunnel);
        }

        if (SendPdfPreviewZoomTransform != null && _sendOverscrollHelper == null)
        {
            _sendOverscrollHelper = new OverscrollHelper(SendPdfPreviewZoomTransform);
        }

        _sendZoom.ShowLevel();

        if (DataContext is PurchaseOrdersModalsViewModel vm)
        {
            _subscribedVm = vm;
            vm.PropertyChanged += OnSendViewModelPropertyChanged;
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        if (_subscribedVm != null)
        {
            _subscribedVm.PropertyChanged -= OnSendViewModelPropertyChanged;
            _subscribedVm = null;
        }
    }

    private void OnSendViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not PurchaseOrdersModalsViewModel vm) return;

        if (e.PropertyName == nameof(PurchaseOrdersModalsViewModel.SendPdfPreview) && vm.SendPdfPreview != null)
        {
            _ = _sendZoom.FitAfterLayoutAsync();
        }
        else if (e.PropertyName == nameof(PurchaseOrdersModalsViewModel.IsSendModalOpen) && vm.IsSendModalOpen)
        {
            _ = _sendZoom.FitAfterLayoutAsync();
        }
        else if (e.PropertyName == nameof(PurchaseOrdersModalsViewModel.IsSendFullscreen))
        {
            _ = _sendZoom.FitAfterLayoutAsync();
        }
    }

    #region Send PDF Preview Zoom

    private void SendPdfPreviewZoomIn_Click(object? sender, RoutedEventArgs e) => _sendZoom.ZoomTowardsCenter(true);
    private void SendPdfPreviewZoomOut_Click(object? sender, RoutedEventArgs e) => _sendZoom.ZoomTowardsCenter(false);
    private void SendPdfPreviewZoomSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e) => _sendZoom.OnSliderChanged(e.NewValue);
    private void SendPdfPreviewFitToWindow_Click(object? sender, RoutedEventArgs e) => _sendZoom.FitToWindow();

    private void OnSendPdfPreviewPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var delta = e.Delta.Y;
        if (delta != 0)
            _sendZoom.ZoomAtPoint(delta > 0, e.GetPosition(SendPdfPreviewScrollViewer));
        e.Handled = true;
    }

    #endregion

    #region Send Preview Panning

    private void OnSendPdfPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;
        if (PreviewZoom.IsOnScrollBar(e.Source)) return;

        var point = e.GetCurrentPoint(scrollViewer);
        if (point.Properties.IsLeftButtonPressed
            || point.Properties.IsRightButtonPressed
            || point.Properties.IsMiddleButtonPressed)
        {
            _isPanning = true;
            _panScrollViewer = scrollViewer;
            _activePanOverscroll = _sendOverscrollHelper;
            _panStartPoint = e.GetPosition(this);
            _panStartOffset = new Vector(scrollViewer.Offset.X, scrollViewer.Offset.Y);
            e.Pointer.Capture(scrollViewer);
            scrollViewer.Cursor = new Cursor(StandardCursorType.Hand);
            e.Handled = true;
        }
    }

    private void OnSendPdfPreviewPointerMoved(object? sender, PointerEventArgs e)
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

    private void OnSendPdfPreviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPanning) return;

        var scrollViewer = _panScrollViewer;
        _isPanning = false;
        _panScrollViewer = null;
        e.Pointer.Capture(null);

        if (scrollViewer != null)
            scrollViewer.Cursor = new Cursor(StandardCursorType.Hand);

        if (_activePanOverscroll?.HasOverscroll == true)
        {
            _ = _activePanOverscroll.AnimateSnapBackAsync();
        }

        _activePanOverscroll = null;
        e.Handled = true;
    }

    #endregion
}
