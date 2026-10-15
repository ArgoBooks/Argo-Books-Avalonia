using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ArgoBooks.Helpers;
using ArgoBooks.Utilities;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Modals;

public partial class AppTourOverlay : UserControl
{
    private AppTourViewModel? _viewModel;
    private Avalonia.Controls.Shapes.Path? _backdropPath;

    public AppTourOverlay()
    {
        InitializeComponent();

        _backdropPath = this.FindControl<Avalonia.Controls.Shapes.Path>("BackdropPath");

        DataContextChanged += OnDataContextChanged;
        PropertyChanged += OnPropertyChanged;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        if (_viewModel != null)
        {
            _viewModel.TargetAreaChanged -= OnTargetAreaChanged;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }

        DataContextChanged -= OnDataContextChanged;
        PropertyChanged -= OnPropertyChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Unsubscribe from old ViewModel
        if (_viewModel != null)
        {
            _viewModel.TargetAreaChanged -= OnTargetAreaChanged;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        // Subscribe to new ViewModel
        _viewModel = DataContext as AppTourViewModel;
        if (_viewModel != null)
        {
            _viewModel.TargetAreaChanged += OnTargetAreaChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppTourViewModel.IsOpen) && _viewModel?.IsOpen == true)
        {
            // When tour opens, calculate initial bounds after layout
            Dispatcher.UIThread.Post(UpdateHighlightBounds, DispatcherPriority.Loaded);
            // Focus on the card keeps Tab among its buttons rather than the app behind it.
            Dispatcher.UIThread.Post(() => NextButton.Focus(), DispatcherPriority.Loaded);
        }
        else if (e.PropertyName == nameof(AppTourViewModel.IsOpen))
        {
            ModalHelper.ReturnFocusToAppShell(this);
        }
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // Recalculate when size changes
        if (e.Property == BoundsProperty && _viewModel?.IsOpen == true)
        {
            UpdateHighlightBounds();
        }
    }

    private void OnTargetAreaChanged(object? sender, EventArgs e)
    {
        // Delay slightly to ensure any animations have started
        Dispatcher.UIThread.Post(UpdateHighlightBounds, DispatcherPriority.Render);
    }

    private void UpdateHighlightBounds()
    {
        if (_viewModel == null || !_viewModel.IsOpen)
            return;

        var targetArea = _viewModel.CurrentTargetArea;

        // For "center" target, hide the highlight
        if (targetArea == "center")
        {
            _viewModel.HideHighlight();
            UpdateBackdropGeometry(null, new CornerRadius(0));
            return;
        }

        // Find the target element
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null)
        {
            _viewModel.HideHighlight();
            UpdateBackdropGeometry(null, new CornerRadius(0));
            return;
        }

        var (element, cornerRadius) = FindTargetElement(window, targetArea);
        if (element == null)
        {
            _viewModel.HideHighlight();
            UpdateBackdropGeometry(null, new CornerRadius(0));
            return;
        }

        var bounds = GetElementBoundsRelativeToOverlay(element, targetArea);
        if (bounds == null)
        {
            _viewModel.HideHighlight();
            UpdateBackdropGeometry(null, new CornerRadius(0));
            return;
        }

        _viewModel.SetHighlightBounds(bounds.Value, cornerRadius);
        UpdateBackdropGeometry(bounds.Value, cornerRadius);
    }

    private static (Control? element, CornerRadius cornerRadius) FindTargetElement(Window window, string targetArea)
    {
        return targetArea switch
        {
            "sidebar" => (TutorialHighlightHelper.FindElementByName<Control>(window, "AppSidebar"), new CornerRadius(8)),
            "searchbar" => (TutorialHighlightHelper.FindElementByName<Control>(window, "SearchBox"), new CornerRadius(6)),
            "content" => (TutorialHighlightHelper.FindElementByName<Control>(window, "AppContent"), new CornerRadius(8)),
            "header" => (TutorialHighlightHelper.FindElementByName<Control>(window, "AppHeader"), new CornerRadius(0, 0, 8, 8)),
            _ => (null, new CornerRadius(8))
        };
    }

    private Rect? GetElementBoundsRelativeToOverlay(Control element, string targetArea)
    {
        const double borderThickness = 3;
        const double edgeOffset = 8; // Extra offset when highlight is at window edge

        try
        {
            // Get the transform from the element to this overlay
            var transform = element.TransformToVisual(this);
            if (transform == null)
                return null;

            var elementBounds = new Rect(0, 0, element.Bounds.Width, element.Bounds.Height);

            // Transform to overlay coordinates
            var topLeft = transform.Value.Transform(elementBounds.TopLeft);
            var bottomRight = transform.Value.Transform(elementBounds.BottomRight);

            // Get overlay bounds for clamping
            var overlayWidth = Bounds.Width;
            var overlayHeight = Bounds.Height;

            double left, top, width, height;

            if (targetArea == "searchbar")
            {
                // SearchBox: draw border OUTSIDE the control
                left = topLeft.X - borderThickness;
                top = topLeft.Y - borderThickness;
                width = (bottomRight.X - topLeft.X) + (borderThickness * 2);
                height = (bottomRight.Y - topLeft.Y) + (borderThickness * 2);
            }
            else if (targetArea == "content")
            {
                // Content/Dashboard: reduced margin (3px less on left, 1px less on top)
                var leftOffset = topLeft.X <= edgeOffset ? edgeOffset - 3 : 0;
                var topOffset = topLeft.Y <= edgeOffset ? edgeOffset - 1 : borderThickness - 1;
                var rightOffset = bottomRight.X >= overlayWidth - edgeOffset ? edgeOffset : borderThickness;
                var bottomOffset = bottomRight.Y >= overlayHeight - edgeOffset ? edgeOffset : borderThickness;

                left = topLeft.X + leftOffset;
                top = topLeft.Y + topOffset;
                width = (bottomRight.X - topLeft.X) - leftOffset - rightOffset;
                height = (bottomRight.Y - topLeft.Y) - topOffset - bottomOffset;
            }
            else
            {
                // Default: Inset the bounds by border thickness so the border draws INSIDE the element
                // Add extra offset when at window edges to prevent overflow
                var leftOffset = topLeft.X <= edgeOffset ? edgeOffset : borderThickness;
                var topOffset = topLeft.Y <= edgeOffset ? edgeOffset : borderThickness;
                var rightOffset = bottomRight.X >= overlayWidth - edgeOffset ? edgeOffset : borderThickness;
                var bottomOffset = bottomRight.Y >= overlayHeight - edgeOffset ? edgeOffset : borderThickness;

                left = topLeft.X + leftOffset;
                top = topLeft.Y + topOffset;
                width = (bottomRight.X - topLeft.X) - leftOffset - rightOffset;
                height = (bottomRight.Y - topLeft.Y) - topOffset - bottomOffset;
            }

            // Ensure we have valid dimensions
            if (width <= 0 || height <= 0)
                return null;

            return new Rect(left, top, width, height);
        }
        catch
        {
            return null;
        }
    }

    private void UpdateBackdropGeometry(Rect? highlightBounds, CornerRadius cornerRadius)
    {
        if (_backdropPath == null)
            return;

        _backdropPath.Data = TutorialHighlightHelper.CreateBackdropGeometry(
            Bounds.Size, highlightBounds, cornerRadius);
    }
}
