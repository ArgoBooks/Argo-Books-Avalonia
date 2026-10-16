using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArgoBooks.Helpers;

/// <summary>
/// Zoom for a document preview: content scaled inside a scroll viewer, with a zoom slider and a
/// percentage readout. The receipt viewer, the receipt scan previews and the purchase order send
/// preview each hold one.
/// </summary>
internal sealed class PreviewZoom(
    Func<ScrollViewer?> scrollViewer,
    Func<LayoutTransformControl?> transform,
    Func<Control?> content,
    Func<Slider?> slider,
    Func<TextBlock?> percentText,
    double fitPadding = 0)
{
    public const double MinZoom = 0.25;
    public const double MaxZoom = 4.0;
    public const double Step = 0.25;

    private bool _updatingSlider;

    public double Level { get; private set; } = 1.0;

    public void Apply()
    {
        if (transform() is not { } zoomTransform) return;
        zoomTransform.LayoutTransform = new ScaleTransform(Level, Level);
        ShowLevel();
    }

    /// <summary>Moves the slider and the percentage to the current level.</summary>
    public void ShowLevel()
    {
        if (slider() is { } zoomSlider && !_updatingSlider)
        {
            _updatingSlider = true;
            zoomSlider.Value = Level;
            _updatingSlider = false;
        }

        if (percentText() is { } text)
            text.Text = $"{Level:P0}";
    }

    public void OnSliderChanged(double value)
    {
        if (_updatingSlider) return;
        ZoomToLevel(value);
    }

    public void Reset()
    {
        Level = 1.0;
        Apply();
    }

    public void ZoomTowardsCenter(bool zoomIn) => ZoomToLevel(Stepped(zoomIn));

    /// <summary>Zooms to a level, keeping the content at the middle of the view where it is.</summary>
    public void ZoomToLevel(double newZoom)
    {
        if (scrollViewer() is not { } viewer || transform() is not { } zoomTransform) return;

        var oldZoom = Level;
        newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
        if (Math.Abs(oldZoom - newZoom) < 0.001) return;

        var viewportCenterX = viewer.Viewport.Width / 2;
        var viewportCenterY = viewer.Viewport.Height / 2;
        var contentCenterX = (viewer.Offset.X + viewportCenterX) / oldZoom;
        var contentCenterY = (viewer.Offset.Y + viewportCenterY) / oldZoom;

        Level = newZoom;
        Apply();
        zoomTransform.UpdateLayout();

        var maxX = Math.Max(0, viewer.Extent.Width - viewer.Viewport.Width);
        var maxY = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
        viewer.Offset = new Vector(
            Math.Clamp(contentCenterX * newZoom - viewportCenterX, 0, maxX),
            Math.Clamp(contentCenterY * newZoom - viewportCenterY, 0, maxY));
    }

    /// <summary>Zooms one step, keeping the content under the pointer where it is.</summary>
    public void ZoomAtPoint(bool zoomIn, Point viewportPoint)
    {
        if (scrollViewer() is not { } viewer || transform() is not { } zoomTransform) return;

        var oldZoom = Level;
        var newZoom = Stepped(zoomIn);
        if (Math.Abs(oldZoom - newZoom) < 0.001) return;

        var viewportWidth = viewer.Viewport.Width;
        var viewportHeight = viewer.Viewport.Height;
        var oldExtent = viewer.Extent;
        var oldOffset = viewer.Offset;

        // Content smaller than the view is centred in it, so the pointer is that much further from its edge.
        var unscaledX = (viewportPoint.X + oldOffset.X - CenteringOffset(oldExtent.Width, viewportWidth)) / oldZoom;
        var unscaledY = (viewportPoint.Y + oldOffset.Y - CenteringOffset(oldExtent.Height, viewportHeight)) / oldZoom;

        Level = newZoom;
        Apply();
        zoomTransform.UpdateLayout();

        var newExtent = viewer.Extent;
        var newOffsetX = unscaledX * newZoom - viewportPoint.X + CenteringOffset(newExtent.Width, viewportWidth);
        var newOffsetY = unscaledY * newZoom - viewportPoint.Y + CenteringOffset(newExtent.Height, viewportHeight);

        viewer.Offset = new Vector(
            Math.Clamp(newOffsetX, 0, Math.Max(0, newExtent.Width - viewportWidth)),
            Math.Clamp(newOffsetY, 0, Math.Max(0, newExtent.Height - viewportHeight)));
    }

    /// <summary>Zooms so the whole content fits in the view.</summary>
    /// <param name="measureAtFullSize">Lays the content out at 100% first, for content whose size is not settled yet.</param>
    public void FitToWindow(bool measureAtFullSize = false)
    {
        if (scrollViewer() is not { } viewer || content() is not { } fitted) return;

        if (measureAtFullSize)
        {
            if (transform() is not { } zoomTransform) return;
            Reset();
            zoomTransform.UpdateLayout();
        }

        var contentWidth = fitted.Bounds.Width;
        var contentHeight = fitted.Bounds.Height;
        if (contentWidth <= 0 || contentHeight <= 0) return;

        var viewportWidth = viewer.Bounds.Width - fitPadding;
        var viewportHeight = viewer.Bounds.Height - fitPadding;
        if (viewportWidth <= 0 || viewportHeight <= 0) return;

        // The lower limit is below MinZoom so that a very large page still fits.
        Level = Math.Clamp(Math.Min(viewportWidth / contentWidth, viewportHeight / contentHeight), 0.01, MaxZoom);
        Apply();
    }

    /// <summary>Fits once the current layout pass has finished, for content that has only just been shown.</summary>
    public async Task FitAfterLayoutAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        FitToWindow();
    }

    /// <summary>True if the event source is a scroll bar or part of one, such as its thumb.</summary>
    public static bool IsOnScrollBar(object? source)
    {
        var current = source as Visual;
        while (current != null)
        {
            if (current is ScrollBar) return true;
            current = current.GetVisualParent();
        }
        return false;
    }

    private double Stepped(bool zoomIn) => zoomIn
        ? Math.Min(Level + Step, MaxZoom)
        : Math.Max(Level - Step, MinZoom);

    private static double CenteringOffset(double extent, double viewport) =>
        extent < viewport ? (viewport - extent) / 2 : 0;
}
