using System.ComponentModel;
using ArgoBooks.ViewModels;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArgoBooks.Views;

/// <summary>
/// Code-behind for the Pay runs page.
/// </summary>
public partial class PayRunsPage : UserControl
{
    /// <summary>
    /// The same radius the Insights page uses, so the two premium teasers look like one feature
    /// rather than two attempts at it.
    ///
    /// Applied here rather than in the markup because Effect is not settable from a binding, and
    /// shared as a single immutable instance because every page that blurs wants the identical
    /// one and building a new effect per toggle would throw away its cached render.
    /// </summary>
    private static readonly ImmutableBlurEffect BlurEffect = new(6);

    private PayRunsPageViewModel? _previousViewModel;

    public PayRunsPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Unsubscribed first, because this page's view model outlives the view: navigating away
        // and back hands the same instance to a new PayRunsPage, and without this each visit
        // would leave another handler attached to it.
        if (_previousViewModel != null)
        {
            _previousViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _previousViewModel = null;
        }

        if (DataContext is PayRunsPageViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            _previousViewModel = vm;
            UpdateBlur(vm.ShowTeaser);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PayRunsPageViewModel.ShowTeaser) && DataContext is PayRunsPageViewModel vm)
        {
            UpdateBlur(vm.ShowTeaser);
        }
    }

    private void UpdateBlur(bool showTeaser) => ContentRoot.Effect = showTeaser ? BlurEffect : null;

    private void OnHeaderSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is PayRunsPageViewModel viewModel && e.WidthChanged)
        {
            viewModel.ResponsiveHeader.HeaderWidth = e.NewSize.Width;
        }
    }
}
