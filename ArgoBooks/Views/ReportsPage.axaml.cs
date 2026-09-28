using ArgoBooks.Controls.Reports;
using ArgoBooks.Helpers;
using ArgoBooks.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace ArgoBooks.Views;

/// <summary>
/// Code-behind for the Reports page, handling report designer interactions.
/// </summary>
public partial class ReportsPage : UserControl
{
    private SkiaReportDesignCanvas? _designCanvas;
    private ScrollViewer? _previewScrollViewer;
    private LayoutTransformControl? _previewZoomTransformControl;
    private ScrollViewer? _toolbarScrollViewer;
    private StackPanel? _toolbarContent;
    private Grid? _saveButtonContainer;
    private TextBlock? _asterisk;
    private Border? _saveConfirmationBorder;
    private Border? _noChangesBorder;
    private bool _isAsteriskInitialized;
    private bool _isPanning;
    private Point _panStartPoint;
    private Vector _panStartOffset;
    private bool _toolbarScrollbarVisible;

    // Preview zoom level
    private double _previewZoomLevel = 1.0;

    // Rubberband overscroll effect for preview
    private OverscrollHelper? _previewOverscrollHelper;

    private const double SendButtonTextMinWidth = 480;
    private const double NarrowDesignerThreshold = 900;
    private bool _isNarrowDesigner;
    private bool _toolboxAutoCollapsed;

    public ReportsPage()
    {
        InitializeComponent();

        Step1TabHeader.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged)
                SendToAccountantText.IsVisible = e.NewSize.Width >= SendButtonTextMinWidth;
        };
        DesignerLayout.SizeChanged += OnDesignerLayoutSizeChanged;
    }

    /// <summary>
    /// Closes the element toolbox when the designer gets narrow and reopens it when it widens, but
    /// only when the width crosses the threshold, so a toggle made at the current size stands.
    /// </summary>
    private void OnDesignerLayoutSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || DataContext is not ReportsPageViewModel vm)
            return;

        var isNarrow = e.NewSize.Width < NarrowDesignerThreshold;
        if (isNarrow == _isNarrowDesigner)
            return;
        _isNarrowDesigner = isNarrow;

        if (isNarrow && vm.IsElementPanelExpanded)
        {
            _toolboxAutoCollapsed = true;
            vm.SetElementPanelExpandedForWidth(false);
            AnimateElementToolbox(false);
        }
        else if (!isNarrow && _toolboxAutoCollapsed)
        {
            _toolboxAutoCollapsed = false;
            if (!vm.IsElementPanelExpanded)
            {
                vm.SetElementPanelExpandedForWidth(true);
                AnimateElementToolbox(true);
            }
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _designCanvas = this.FindControl<SkiaReportDesignCanvas>("DesignCanvas");
        _previewScrollViewer = this.FindControl<ScrollViewer>("PreviewScrollViewer");
        _previewZoomTransformControl = this.FindControl<LayoutTransformControl>("PreviewZoomTransformControl");

        if (_previewZoomTransformControl != null)
        {
            _previewOverscrollHelper = new OverscrollHelper(_previewZoomTransformControl);
        }

        _toolbarScrollViewer = this.FindControl<ScrollViewer>("ToolbarScrollViewer");
        _toolbarContent = this.FindControl<StackPanel>("ToolbarContent");
        _saveButtonContainer = this.FindControl<Grid>("SaveButtonContainer");
        _saveConfirmationBorder = this.FindControl<Border>("SaveConfirmationBorder");
        _noChangesBorder = this.FindControl<Border>("NoChangesBorder");

        // Wire up toolbar scrollbar visibility detection
        if (_toolbarScrollViewer != null)
        {
            _toolbarScrollViewer.LayoutUpdated += OnToolbarLayoutUpdated;
        }

        // Wire up zoom, pan, and selection for the design canvas
        if (_designCanvas != null)
        {
            // Use tunnel routing to intercept scroll wheel before ScrollViewer handles it
            _designCanvas.AddHandler(PointerWheelChangedEvent, OnCanvasPointerWheelChanged, RoutingStrategies.Tunnel);
            _designCanvas.PointerPressed += OnCanvasPointerPressed;
            _designCanvas.PointerMoved += OnCanvasPointerMoved;
            _designCanvas.PointerReleased += OnCanvasPointerReleased;
            _designCanvas.SelectionChanged += OnCanvasSelectionChanged;
            _designCanvas.ContextMenuRequested += OnCanvasContextMenuRequested;

            // Provide viewport center callback to ViewModel for element placement
            if (DataContext is ReportsPageViewModel vm2)
                vm2.GetViewportCenter = _designCanvas.GetViewportCenterOnPage;
        }

        // Wire up keyboard shortcuts
        KeyDown += OnKeyDown;

        // Wire up zoom-to-cursor and right-click pan with rubberband for the preview
        if (_previewScrollViewer != null)
        {
            // Use tunnel routing to intercept before ScrollViewer handles it
            _previewScrollViewer.AddHandler(PointerWheelChangedEvent, OnPreviewPointerWheelChanged, RoutingStrategies.Tunnel);
            _previewScrollViewer.PointerPressed += OnPreviewPointerPressed;
            _previewScrollViewer.PointerMoved += OnPreviewPointerMoved;
            _previewScrollViewer.PointerReleased += OnPreviewPointerReleased;
        }

        // Apply initial zoom
        ApplyPreviewZoom();

        // Subscribe to ViewModel property changes to sync canvas elements
        if (DataContext is ReportsPageViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.ElementPropertyChanged += OnElementPropertyChanged;
            vm.PageSettingsRefreshRequested += OnPageSettingsRefreshRequested;
            vm.TemplateLoaded += OnTemplateLoaded;
            vm.PreviewFitToWindowRequested += OnPreviewFitToWindowRequested;
            vm.CanvasRefreshRequested += OnCanvasRefreshRequested;

            // Ensure viewport center callback is wired (may have missed it if DataContext wasn't ready earlier)
            if (_designCanvas != null)
                vm.GetViewportCenter = _designCanvas.GetViewportCenterOnPage;

            // Subscribe to UndoRedoManager state changes to update asterisk visibility
            vm.UndoRedoManager.StateChanged += OnUndoRedoStateChanged;

            // Initial sync in case elements were already added
            _designCanvas?.SyncElements();

            // Mark asterisk as initialized after a short delay to prevent flashing on load
            InitializeAsteriskAsync();

            // Trigger initial fit-to-window (template was already loaded in ViewModel constructor)
            TriggerInitialZoomToFit();
        }
    }

    private async void TriggerInitialZoomToFit()
    {
        try
        {
            if (_designCanvas == null) return;

            // Hide canvas during initial load to prevent flash of unzoomed content
            _designCanvas.Opacity = 0;

            // Wait for layout to stabilize - the ScrollViewer inside the canvas
            // needs time for its Viewport to be calculated
            var tcs = new TaskCompletionSource<bool>();

            void OnLayoutUpdated(object? sender, EventArgs args)
            {
                if (_designCanvas.Bounds is { Width: > 0, Height: > 0 })
                {
                    _designCanvas.LayoutUpdated -= OnLayoutUpdated;
                    tcs.TrySetResult(true);
                }
            }

            if (_designCanvas.Bounds is { Width: > 0, Height: > 0 })
            {
                tcs.TrySetResult(true);
            }
            else
            {
                _designCanvas.LayoutUpdated += OnLayoutUpdated;
            }

            // Wait for layout to complete
            await tcs.Task;

            // Wait for ScrollViewer's Viewport to be calculated
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);

            _designCanvas?.ZoomToFit();

            // Show canvas after zoom is applied
            _designCanvas?.Opacity = 1;
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.UI, "TriggerInitialZoomToFit");
        }
    }

    private async void InitializeAsteriskAsync()
    {
        try
        {
            // Wait for layout and rendering passes to complete before allowing asterisk to show
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            _isAsteriskInitialized = true;
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.UI, "InitializeAsteriskAsync");
        }
    }

    private void OnUndoRedoStateChanged(object? sender, EventArgs e)
    {
        if (!_isAsteriskInitialized) return;

        if (DataContext is ReportsPageViewModel vm)
        {
            UpdateAsteriskVisibility(vm.HasUnsavedChanges);
        }
    }

    private void UpdateAsteriskVisibility(bool show)
    {
        if (_saveButtonContainer == null || !_isAsteriskInitialized) return;

        if (show && _asterisk == null)
        {
            // Create asterisk as an overlay that doesn't affect layout
            // Positioned at top-right of the Grid container
            _asterisk = new TextBlock
            {
                Text = "*",
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                Margin = new Thickness(0, 0, -2, 0),
                IsHitTestVisible = false
            };
            // Add as overlay in the Grid (won't shift other elements)
            _saveButtonContainer.Children.Add(_asterisk);
        }
        else if (!show && _asterisk != null)
        {
            _saveButtonContainer.Children.Remove(_asterisk);
            _asterisk = null;
        }
    }

    private async void OnTemplateLoaded(object? sender, EventArgs e)
    {
        try
        {
            if (_designCanvas == null) return;

            // Hide canvas during template load to prevent flash of unzoomed content
            _designCanvas.Opacity = 0;

            // Wait for layout to complete before fitting to window
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            _designCanvas.ZoomToFit();

            // Show canvas after zoom is applied
            _designCanvas.Opacity = 1;
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.UI, "OnTemplateLoaded");
        }
    }

    private async void OnPreviewFitToWindowRequested(object? sender, EventArgs e)
    {
        try
        {
            // Wait for layout and rendering passes to complete
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            PreviewZoomToFit();
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.UI, "OnPreviewFitToWindowRequested");
        }
    }

    private void OnPageSettingsRefreshRequested(object? sender, EventArgs e)
    {
        _designCanvas?.RefreshPageSettings();
    }

    private void OnCanvasRefreshRequested(object? sender, EventArgs e)
    {
        // Invalidate the canvas to repaint with updated content
        _designCanvas?.InvalidateCanvas();
    }

    private void OnToolbarLayoutUpdated(object? sender, EventArgs e)
    {
        if (_toolbarScrollViewer == null || _toolbarContent == null) return;

        // Check if horizontal scrollbar is visible (extent > viewport)
        var isScrollbarVisible = _toolbarScrollViewer.Extent.Width > _toolbarScrollViewer.Viewport.Width;

        // Only update if visibility changed
        if (isScrollbarVisible != _toolbarScrollbarVisible)
        {
            _toolbarScrollbarVisible = isScrollbarVisible;
            // Add bottom margin to content when scrollbar is visible to make space for it
            _toolbarContent.Margin = isScrollbarVisible ? new Thickness(0, 0, 0, 12) : new Thickness(0);
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        // Unsubscribe from events
        if (_designCanvas != null)
        {
            _designCanvas.RemoveHandler(PointerWheelChangedEvent, OnCanvasPointerWheelChanged);
            _designCanvas.PointerPressed -= OnCanvasPointerPressed;
            _designCanvas.PointerMoved -= OnCanvasPointerMoved;
            _designCanvas.PointerReleased -= OnCanvasPointerReleased;
            _designCanvas.SelectionChanged -= OnCanvasSelectionChanged;
            _designCanvas.ContextMenuRequested -= OnCanvasContextMenuRequested;
        }

        if (_previewScrollViewer != null)
        {
            _previewScrollViewer.RemoveHandler(PointerWheelChangedEvent, OnPreviewPointerWheelChanged);
            _previewScrollViewer.PointerPressed -= OnPreviewPointerPressed;
            _previewScrollViewer.PointerMoved -= OnPreviewPointerMoved;
            _previewScrollViewer.PointerReleased -= OnPreviewPointerReleased;
        }

        if (_toolbarScrollViewer != null)
        {
            _toolbarScrollViewer.LayoutUpdated -= OnToolbarLayoutUpdated;
        }

        if (DataContext is ReportsPageViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.ElementPropertyChanged -= OnElementPropertyChanged;
            vm.PageSettingsRefreshRequested -= OnPageSettingsRefreshRequested;
            vm.TemplateLoaded -= OnTemplateLoaded;
            vm.PreviewFitToWindowRequested -= OnPreviewFitToWindowRequested;
            vm.CanvasRefreshRequested -= OnCanvasRefreshRequested;
            vm.UndoRedoManager.StateChanged -= OnUndoRedoStateChanged;
            vm.Cleanup();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // When Configuration changes, sync the canvas elements and selection
        if (e.PropertyName == nameof(ReportsPageViewModel.Configuration))
        {
            _designCanvas?.SyncElements();
            _designCanvas?.RefreshAllElements();
            SyncSelectionToCanvas();
        }
        // When SelectedElement changes, sync to canvas
        else if (e.PropertyName == nameof(ReportsPageViewModel.SelectedElement))
        {
            SyncSelectionToCanvas();
        }
        // When PreviewZoom changes from slider/buttons, sync to our local zoom
        else if (e.PropertyName == nameof(ReportsPageViewModel.PreviewZoom))
        {
            SyncPreviewZoomFromViewModel();
        }
        // Animate the save confirmation overlay
        else if (e.PropertyName == nameof(ReportsPageViewModel.ShowSaveConfirmation))
        {
            AnimateSaveConfirmation();
        }
        // Animate the no changes message overlay
        else if (e.PropertyName == nameof(ReportsPageViewModel.ShowNoChangesMessage))
        {
            AnimateNoChangesMessage();
        }
    }

    /// <summary>
    /// Syncs selection from ViewModel to canvas.
    /// </summary>
    private void SyncSelectionToCanvas()
    {
        if (_designCanvas == null || DataContext is not ReportsPageViewModel vm) return;

        // Only sync if canvas selection doesn't match ViewModel selection
        var canvasSelected = _designCanvas.GetSelectedElement();
        if (canvasSelected?.Id != vm.SelectedElement?.Id && vm.SelectedElement != null)
        {
            _designCanvas.SelectElement(vm.SelectedElement);
        }
        else if (vm.SelectedElement == null && canvasSelected != null)
        {
            _designCanvas.ClearSelection();
        }
    }

    private void AnimateSaveConfirmation()
    {
        if (_saveConfirmationBorder == null || DataContext is not ReportsPageViewModel vm) return;

        // Animate opacity based on visibility state
        _saveConfirmationBorder.Opacity = vm.ShowSaveConfirmation ? 1 : 0;
    }

    private void AnimateNoChangesMessage()
    {
        if (_noChangesBorder == null || DataContext is not ReportsPageViewModel vm) return;

        // Animate opacity based on visibility state
        _noChangesBorder.Opacity = vm.ShowNoChangesMessage ? 1 : 0;
    }

    private void OnElementPropertyChanged(object? sender, Core.Models.Reports.ReportElementBase element)
    {
        // Refresh the specific element's content when its properties change
        _designCanvas?.RefreshElementContent(element);
    }

    private void OnCanvasSelectionChanged(object? sender, Controls.Reports.SelectionChangedEventArgs e)
    {
        // Sync canvas selection to ViewModel
        if (DataContext is ReportsPageViewModel vm)
        {
            vm.SyncSelection(e.SelectedElements.ToList());
        }
    }

    private void OnCanvasContextMenuRequested(object? sender, ContextMenuRequestedEventArgs e)
    {
        // Show context menu at the requested position
        if (DataContext is ReportsPageViewModel vm)
        {
            vm.ShowContextMenu(e.X, e.Y);
        }
    }

    private void OnCanvasPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        // Only zoom when Ctrl is held; otherwise let ScrollViewer handle it for panning
        if (!e.KeyModifiers.HasZoomModifier())
            return;

        // Zoom at cursor position
        if (_designCanvas != null && DataContext is ReportsPageViewModel vm)
        {
            var scrollViewer = _designCanvas.FindControl<ScrollViewer>("CanvasScrollViewer");
            var zoomTransformControl = _designCanvas.FindControl<LayoutTransformControl>("ZoomTransformControl");

            if (scrollViewer != null && zoomTransformControl != null)
            {
                CanvasZoomAtPoint(e.Delta.Y > 0, e.GetPosition(scrollViewer), e.GetPosition(zoomTransformControl), scrollViewer, vm);
            }
            else
            {
                // Fallback to center zoom
                if (e.Delta.Y > 0)
                    vm.ZoomInCommand.Execute(null);
                else if (e.Delta.Y < 0)
                    vm.ZoomOutCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

    /// <summary>
    /// Zooms the design canvas at a specific point, keeping that point fixed on screen.
    /// </summary>
    private void CanvasZoomAtPoint(bool zoomIn, Point viewportPoint, Point scaledContentPoint, ScrollViewer scrollViewer, ReportsPageViewModel vm)
    {
        var oldZoom = vm.ZoomLevel;
        var newZoom = zoomIn
            ? Math.Min(oldZoom + SkiaReportDesignCanvas.ZoomStep, SkiaReportDesignCanvas.MaxZoom)
            : Math.Max(oldZoom - SkiaReportDesignCanvas.ZoomStep, SkiaReportDesignCanvas.MinZoom);

        if (Math.Abs(oldZoom - newZoom) < 0.001) return;

        // Convert scaled content point to unscaled coordinates
        var unscaledX = scaledContentPoint.X / oldZoom;
        var unscaledY = scaledContentPoint.Y / oldZoom;

        // Apply the zoom
        vm.ZoomLevel = newZoom;

        // Post the offset adjustment to run after layout has updated
        Dispatcher.UIThread.Post(() =>
        {
            // Calculate new offset to keep the same content point under cursor
            var newOffsetX = unscaledX * newZoom - viewportPoint.X;
            var newOffsetY = unscaledY * newZoom - viewportPoint.Y;

            // Clamp to valid scroll range
            var maxX = Math.Max(0, scrollViewer.Extent.Width - scrollViewer.Viewport.Width);
            var maxY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);

            scrollViewer.Offset = new Vector(
                Math.Clamp(newOffsetX, 0, maxX),
                Math.Clamp(newOffsetY, 0, maxY)
            );
        }, DispatcherPriority.Render);
    }

    private void OnPreviewPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        // Only zoom when Ctrl is held; otherwise let ScrollViewer handle it for panning
        if (!e.KeyModifiers.HasZoomModifier())
            return;

        // Zoom at cursor position
        if (_previewScrollViewer != null && _previewZoomTransformControl != null)
        {
            var viewportPoint = e.GetPosition(_previewScrollViewer);
            var contentPoint = e.GetPosition(_previewZoomTransformControl);
            PreviewZoomAtPoint(e.Delta.Y > 0, viewportPoint, contentPoint);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Zooms the preview at a specific point, keeping that point fixed on screen.
    /// </summary>
    private void PreviewZoomAtPoint(bool zoomIn, Point viewportPoint, Point scaledContentPoint)
    {
        if (_previewScrollViewer == null || _previewZoomTransformControl == null) return;

        var oldZoom = _previewZoomLevel;
        var newZoom = zoomIn
            ? Math.Min(oldZoom + SkiaReportDesignCanvas.ZoomStep, SkiaReportDesignCanvas.MaxZoom)
            : Math.Max(oldZoom - SkiaReportDesignCanvas.ZoomStep, SkiaReportDesignCanvas.MinZoom);

        if (Math.Abs(oldZoom - newZoom) < 0.001) return;

        // Convert scaled content point to unscaled coordinates
        var unscaledX = scaledContentPoint.X / oldZoom;
        var unscaledY = scaledContentPoint.Y / oldZoom;

        // Apply the zoom
        _previewZoomLevel = newZoom;
        ApplyPreviewZoom();

        // Force layout to update so we get accurate extent/viewport values
        _previewZoomTransformControl.UpdateLayout();

        // Now calculate offset with actual post-zoom values
        var newOffsetX = unscaledX * newZoom - viewportPoint.X;
        var newOffsetY = unscaledY * newZoom - viewportPoint.Y;

        // Use actual extent and viewport after layout update
        var maxX = Math.Max(0, _previewScrollViewer.Extent.Width - _previewScrollViewer.Viewport.Width);
        var maxY = Math.Max(0, _previewScrollViewer.Extent.Height - _previewScrollViewer.Viewport.Height);

        _previewScrollViewer.Offset = new Vector(
            Math.Clamp(newOffsetX, 0, maxX),
            Math.Clamp(newOffsetY, 0, maxY)
        );

        // Update the ViewModel's PreviewZoom to keep slider in sync
        if (DataContext is ReportsPageViewModel vm)
        {
            vm.PreviewZoom = newZoom;
        }
    }

    /// <summary>
    /// Zooms the preview towards the center of the viewport.
    /// </summary>
    private void PreviewZoomTowardsCenter(bool zoomIn)
    {
        var newZoom = zoomIn
            ? Math.Min(_previewZoomLevel + SkiaReportDesignCanvas.ZoomStep, SkiaReportDesignCanvas.MaxZoom)
            : Math.Max(_previewZoomLevel - SkiaReportDesignCanvas.ZoomStep, SkiaReportDesignCanvas.MinZoom);

        PreviewZoomToLevel(newZoom);
    }

    /// <summary>
    /// Zooms the preview to a specific level while maintaining center focus.
    /// </summary>
    private void PreviewZoomToLevel(double newZoom)
    {
        if (_previewScrollViewer == null || _previewZoomTransformControl == null) return;

        var oldZoom = _previewZoomLevel;
        newZoom = Math.Clamp(newZoom, SkiaReportDesignCanvas.MinZoom, SkiaReportDesignCanvas.MaxZoom);

        if (Math.Abs(oldZoom - newZoom) < 0.001) return;

        // Get the center point of the viewport
        var viewportCenterX = _previewScrollViewer.Viewport.Width / 2;
        var viewportCenterY = _previewScrollViewer.Viewport.Height / 2;

        // Calculate the content point at the center of the viewport
        var contentCenterX = (_previewScrollViewer.Offset.X + viewportCenterX) / oldZoom;
        var contentCenterY = (_previewScrollViewer.Offset.Y + viewportCenterY) / oldZoom;

        // Apply the zoom
        _previewZoomLevel = newZoom;
        ApplyPreviewZoom();

        // Force layout to update so we get accurate extent/viewport values
        _previewZoomTransformControl.UpdateLayout();

        // Calculate new offset to keep the same content point at center
        var newOffsetX = contentCenterX * newZoom - viewportCenterX;
        var newOffsetY = contentCenterY * newZoom - viewportCenterY;

        // Use actual extent and viewport after layout update
        var maxX = Math.Max(0, _previewScrollViewer.Extent.Width - _previewScrollViewer.Viewport.Width);
        var maxY = Math.Max(0, _previewScrollViewer.Extent.Height - _previewScrollViewer.Viewport.Height);

        _previewScrollViewer.Offset = new Vector(
            Math.Clamp(newOffsetX, 0, maxX),
            Math.Clamp(newOffsetY, 0, maxY)
        );

        // Update the ViewModel's PreviewZoom to keep slider in sync
        if (DataContext is ReportsPageViewModel vm)
        {
            vm.PreviewZoom = newZoom;
        }
    }

    /// <summary>
    /// Applies the current zoom level to the preview.
    /// </summary>
    private void ApplyPreviewZoom()
    {
        _previewZoomTransformControl?.LayoutTransform = new ScaleTransform(_previewZoomLevel, _previewZoomLevel);
    }

    // Right-click pan for design canvas
    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_designCanvas);
        if (point.Properties.IsRightButtonPressed && _designCanvas != null)
        {
            // Find the ScrollViewer inside the canvas
            var scrollViewer = _designCanvas.FindControl<ScrollViewer>("CanvasScrollViewer");
            if (scrollViewer != null)
            {
                _isPanning = true;
                _panStartPoint = e.GetPosition(scrollViewer);
                _panStartOffset = new Vector(scrollViewer.Offset.X, scrollViewer.Offset.Y);
                e.Pointer.Capture(_designCanvas);
                _designCanvas.Cursor = new Cursor(StandardCursorType.Hand);
                e.Handled = true;
            }
        }
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isPanning && _designCanvas != null)
        {
            var scrollViewer = _designCanvas.FindControl<ScrollViewer>("CanvasScrollViewer");
            if (scrollViewer != null)
            {
                var currentPoint = e.GetPosition(scrollViewer);
                var delta = _panStartPoint - currentPoint;
                scrollViewer.Offset = new Vector(
                    _panStartOffset.X + delta.X,
                    _panStartOffset.Y + delta.Y);
                e.Handled = true;
            }
        }
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isPanning && _designCanvas != null)
        {
            _isPanning = false;
            e.Pointer.Capture(null);
            _designCanvas.Cursor = new Cursor(StandardCursorType.Arrow);
            e.Handled = true;
        }
    }

    // Right-click pan for preview scroll viewer with rubber band effect
    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_previewScrollViewer);
        if (point.Properties.IsRightButtonPressed && _previewScrollViewer != null)
        {
            _isPanning = true;
            _panStartPoint = e.GetPosition(_previewScrollViewer);
            _panStartOffset = new Vector(_previewScrollViewer.Offset.X, _previewScrollViewer.Offset.Y);
            e.Pointer.Capture(_previewScrollViewer);
            _previewScrollViewer.Cursor = new Cursor(StandardCursorType.SizeAll);
            e.Handled = true;
        }
    }

    private void OnPreviewPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isPanning && _previewScrollViewer != null && _previewOverscrollHelper != null)
        {
            var currentPoint = e.GetPosition(_previewScrollViewer);
            var delta = _panStartPoint - currentPoint;

            // Calculate desired offset
            var desiredX = _panStartOffset.X + delta.X;
            var desiredY = _panStartOffset.Y + delta.Y;

            // Calculate bounds
            var maxX = Math.Max(0, _previewScrollViewer.Extent.Width - _previewScrollViewer.Viewport.Width);
            var maxY = Math.Max(0, _previewScrollViewer.Extent.Height - _previewScrollViewer.Viewport.Height);

            var (clampedX, clampedY, overscrollX, overscrollY) =
                _previewOverscrollHelper.CalculateOverscroll(desiredX, desiredY, maxX, maxY);

            _previewScrollViewer.Offset = new Vector(clampedX, clampedY);

            // Apply overscroll visual effect
            _previewOverscrollHelper.ApplyOverscroll(overscrollX, overscrollY);

            e.Handled = true;
        }
    }

    private void OnPreviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isPanning && _previewScrollViewer != null)
        {
            _isPanning = false;
            e.Pointer.Capture(null);
            _previewScrollViewer.Cursor = new Cursor(StandardCursorType.Arrow);

            // Animate overscroll back to zero (rubberband snap-back)
            if (_previewOverscrollHelper?.HasOverscroll == true)
            {
                _ = _previewOverscrollHelper.AnimateSnapBackAsync();
            }

            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles the Zoom In button click for the design canvas.
    /// </summary>
    public void OnZoomInClick(object? sender, RoutedEventArgs e)
    {
        _designCanvas?.ZoomIn();
    }

    /// <summary>
    /// Handles the Zoom Out button click for the design canvas.
    /// </summary>
    public void OnZoomOutClick(object? sender, RoutedEventArgs e)
    {
        _designCanvas?.ZoomOut();
    }

    /// <summary>
    /// Handles the Fit to Window button click for the design canvas.
    /// </summary>
    public void OnZoomFitClick(object? sender, RoutedEventArgs e)
    {
        _designCanvas?.ZoomToFit();
    }

    /// <summary>
    /// Handles the Zoom In button click for the preview.
    /// </summary>
    public void OnPreviewZoomInClick(object? sender, RoutedEventArgs e)
    {
        PreviewZoomTowardsCenter(true);
    }

    /// <summary>
    /// Handles the Zoom Out button click for the preview.
    /// </summary>
    public void OnPreviewZoomOutClick(object? sender, RoutedEventArgs e)
    {
        PreviewZoomTowardsCenter(false);
    }

    /// <summary>
    /// Handles the Fit to Window button click for the preview.
    /// </summary>
    public void OnPreviewZoomFitClick(object? sender, RoutedEventArgs e)
    {
        PreviewZoomToFit();
    }

    /// <summary>
    /// Zooms the preview to fit page width in the viewport.
    /// </summary>
    private void PreviewZoomToFit()
    {
        if (_previewScrollViewer == null || _previewZoomTransformControl == null) return;
        if (DataContext is not ReportsPageViewModel vm) return;

        var imageWidth = vm.PreviewDisplayWidth;
        if (imageWidth <= 0) return;

        var viewportWidth = _previewScrollViewer.Bounds.Width;
        if (viewportWidth <= 0) return;

        _previewZoomLevel = viewportWidth / imageWidth;
        _previewZoomLevel = Math.Clamp(_previewZoomLevel, SkiaReportDesignCanvas.MinZoom, SkiaReportDesignCanvas.MaxZoom);
        ApplyPreviewZoom();

        // Update ViewModel
        vm.PreviewZoom = _previewZoomLevel;
    }

    /// <summary>
    /// Syncs our local zoom level from the ViewModel (when slider changes).
    /// </summary>
    private void SyncPreviewZoomFromViewModel()
    {
        if (DataContext is ReportsPageViewModel vm)
        {
            if (Math.Abs(_previewZoomLevel - vm.PreviewZoom) > 0.001)
            {
                PreviewZoomToLevel(vm.PreviewZoom);
            }
        }
    }

    /// <summary>
    /// Handles keyboard shortcuts.
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ReportsPageViewModel vm) return;

        // Only handle shortcuts when no text input is focused
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
            return;

        switch (e.Key)
        {
            case Key.G when e.KeyModifiers.HasCommand():
                // Toggle grid
                vm.ShowGrid = !vm.ShowGrid;
                e.Handled = true;
                break;

            // Shift is left to the app-wide Save As, which runs only if this page has not
            // already taken the key.
            case Key.S when e.KeyModifiers.HasCommand() && !e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                vm.OpenSaveTemplateCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Z when e.KeyModifiers.HasCommand():
                // Undo
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    vm.RedoCommand.Execute(null);
                else
                    vm.UndoCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Y when e.KeyModifiers.HasCommand():
                // Redo
                vm.RedoCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.D when e.KeyModifiers.HasCommand():
                vm.DuplicateSelectedElementsCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Handles the element panel collapse/expand toggle.
    /// </summary>
    private void OnToggleElementPanelClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ReportsPageViewModel vm) return;

        _toolboxAutoCollapsed = false;
        vm.IsElementPanelExpanded = !vm.IsElementPanelExpanded;
        AnimateElementToolbox(vm.IsElementPanelExpanded);
    }

    private async void AnimateElementToolbox(bool expanded)
    {
        try
        {
            var toolbox = ElementToolbox;

            // Animate width
            var targetWidth = expanded ? 160.0 : 40.0;
            var startWidth = toolbox.Width;
            if (double.IsNaN(startWidth)) startWidth = expanded ? 40.0 : 160.0;

            const int steps = 10;
            const int delayMs = 16;

            for (int i = 1; i <= steps; i++)
            {
                double t = i / (double)steps;
                double easeOut = 1 - Math.Pow(1 - t, 3);
                toolbox.Width = startWidth + (targetWidth - startWidth) * easeOut;
                await Task.Delay(delayMs);
            }

            // Ensure final state
            toolbox.Width = targetWidth;
        }
        catch (Exception ex)
        {
            App.ErrorLogger?.LogError(ex, Core.Models.Telemetry.ErrorCategory.UI, "AnimateElementToolbox");
        }
    }
}
