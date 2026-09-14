using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ArgoBooks.Helpers;
using ArgoBooks.ViewModels;

namespace ArgoBooks.Modals;

/// <summary>
/// Overlay for a page's first-visit tutorial. The page's icon is the only visual difference.
/// </summary>
public partial class PageTutorialOverlay : UserControl
{
    public static readonly StyledProperty<string?> IconProperty =
        AvaloniaProperty.Register<PageTutorialOverlay, string?>(nameof(Icon));

    /// <summary>Path data for the icon shown above the step title.</summary>
    public string? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    private PageTutorialViewModel? _viewModel;
    private readonly Avalonia.Controls.Shapes.Path? _backdropPath;

    public PageTutorialOverlay()
    {
        InitializeComponent();

        _backdropPath = this.FindControl<Avalonia.Controls.Shapes.Path>("BackdropPath");
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        AttachViewModel(DataContext as PageTutorialViewModel);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        AttachViewModel(null);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        AttachViewModel(DataContext as PageTutorialViewModel);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty && _viewModel?.IsOpen == true)
            UpdateHighlightBounds();
    }

    private void AttachViewModel(PageTutorialViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
            return;

        if (_viewModel != null)
        {
            _viewModel.HighlightAreaChanged -= OnHighlightAreaChanged;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = viewModel;
        if (_viewModel != null)
        {
            _viewModel.HighlightAreaChanged += OnHighlightAreaChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PageTutorialViewModel.IsOpen) && _viewModel?.IsOpen == true)
        {
            Dispatcher.UIThread.Post(UpdateHighlightBounds, DispatcherPriority.Loaded);
        }
    }

    private void OnHighlightAreaChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(UpdateHighlightBounds, DispatcherPriority.Render);
    }

    private void UpdateHighlightBounds()
    {
        if (_viewModel == null || !_viewModel.IsOpen)
            return;

        if (_viewModel.CurrentHighlightArea == "none" || TopLevel.GetTopLevel(this) is not Window window)
        {
            HideHighlight();
            return;
        }

        var element = TutorialHighlightHelper.FindElementByName<Control>(window, "AppContent");
        var bounds = element != null
            ? TutorialHighlightHelper.GetHighlightBounds(this, element)
            : null;

        if (bounds == null)
        {
            HideHighlight();
            return;
        }

        var cornerRadius = new CornerRadius(8);
        _viewModel.SetHighlightBounds(bounds.Value, cornerRadius);
        UpdateBackdropGeometry(bounds.Value, cornerRadius);
    }

    private void HideHighlight()
    {
        _viewModel?.HideHighlight();
        UpdateBackdropGeometry(null, new CornerRadius(0));
    }

    private void UpdateBackdropGeometry(Rect? highlightBounds, CornerRadius cornerRadius)
    {
        if (_backdropPath == null)
            return;

        _backdropPath.Data = TutorialHighlightHelper.CreateBackdropGeometry(
            Bounds.Size, highlightBounds, cornerRadius);
    }
}
