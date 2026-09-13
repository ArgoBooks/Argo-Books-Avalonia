#pragma warning disable CS0618 // LabelVisual is obsolete, DrawnLabelVisual is not API-compatible
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using ArgoBooks.ViewModels;
using ArgoBooks.Views;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Avalonia;
using LiveChartsCore.SkiaSharpView.VisualElements;

namespace ArgoBooks.Controls;

/// <summary>
/// A modal overlay that allows charts to be expanded to fill most of the window.
/// Automatically discovers chart panels in the current page and adds expand buttons.
/// In fullscreen mode, provides zoom-based re-bucketing and a granularity toggle
/// (Auto / Day / Week / Month) for time-series charts.
/// </summary>
public partial class ChartExpandOverlay : UserControl
{
    private Panel? _sourcePanel;
    private Button? _expandButton;
    private readonly List<Control> _movedChildren = new();
    private readonly List<(GeoMap original, GeoMap copy)> _geoMapCopies = new();
    private ContentControl? _pageContentControl;
    private Control? _decoratedPageContent;
    private readonly List<(object element, double originalSize)> _originalTitleSizes = new();
    private readonly List<(PieChartLegend legend, double origFontSize, double origIndicatorSize, CornerRadius origCornerRadius, double origMaxHeight, double origWidth, Thickness origMargin)> _originalLegendSizes = new();
    private readonly List<(PieChart chart, Thickness origMargin)> _originalPieChartMargins = new();
    private readonly List<(ChartEmptyState emptyState, EventHandler<AvaloniaPropertyChangedEventArgs> handler)> _emptyStateSubscriptions = new();

    // Zoom and granularity state for fullscreen mode
    private Action? _zoomUnsubscriber;
    private ChartDataType? _expandedChartType;
    private ChartLoaderService? _expandedChartLoaderService;
    private ObservableCollection<ISeries>? _expandedSeries;
    private Axis[]? _expandedXAxes;
    private bool _expandedIsMultiSeries;
    private ReportChartDataService.TimeBucket? _preFullscreenBucket;

    // GeoMap fullscreen state
    private bool _isGeoMapFullscreen;

    public ChartExpandOverlay()
    {
        InitializeComponent();

        // Intercept right-clicks in tunnel phase to prevent LiveCharts selection box
        // and show the chart context menu
        AddHandler(PointerPressedEvent, OnOverlayPointerPressedTunnel,
            RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(Initialize, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_pageContentControl != null)
            _pageContentControl.PropertyChanged -= OnPageContentPropertyChanged;

        ReleasePageSubscriptions();

        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Drops everything this overlay holds on the page it last decorated. The overlay lives
    /// for the whole session while every navigation builds a new page, so holding on would
    /// keep each visited page and its view models alive.
    /// </summary>
    private void ReleasePageSubscriptions()
    {
        foreach (var (emptyState, handler) in _emptyStateSubscriptions)
            emptyState.PropertyChanged -= handler;
        _emptyStateSubscriptions.Clear();
        _decoratedPageContent = null;
    }

    /// <summary>
    /// Finds the page host ContentControl and subscribes to navigation changes.
    /// </summary>
    private void Initialize()
    {
        _pageContentControl = FindPageContentControl();
        if (_pageContentControl != null)
        {
            _pageContentControl.PropertyChanged += OnPageContentPropertyChanged;
            DecorateCurrentPage();
        }
    }

    private void OnPageContentPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ContentProperty) return;

        // Close any open overlay when navigating away
        if (IsVisible) CloseOverlay();

        // Re-discover charts after the new page is loaded
        Dispatcher.UIThread.Post(DecorateCurrentPage, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Finds the PageContentControl by searching the visual tree from the top level.
    /// </summary>
    private ContentControl? FindPageContentControl()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return null;

        return FindVisualDescendant<ContentControl>(topLevel, c => c.Name == "PageContentControl");
    }

    private static T? FindVisualDescendant<T>(Visual root, Func<T, bool> predicate) where T : Visual
    {
        foreach (var child in root.GetVisualChildren())
        {
            if (child is T match && predicate(match))
                return match;
            if (child is Visual visual)
            {
                var result = FindVisualDescendant(visual, predicate);
                if (result != null) return result;
            }
        }

        return null;
    }

    /// <summary>
    /// Discovers chart panels in the current page and adds expand buttons.
    /// </summary>
    private void DecorateCurrentPage()
    {
        var pageContent = _pageContentControl?.Content as Control;

        // Navigation builds a new page instance every time, so let go of the old one first.
        // Same instance means this is a repeat call: keep the subscriptions it already has,
        // since AddExpandButton skips panels that are already decorated.
        if (!ReferenceEquals(pageContent, _decoratedPageContent))
        {
            ReleasePageSubscriptions();
            _decoratedPageContent = pageContent;
        }

        if (pageContent == null) return;

        var chartPanels = new List<Panel>();
        FindChartPanels(pageContent, chartPanels);

        foreach (var panel in chartPanels)
        {
            AddExpandButton(panel);
        }
    }

    /// <summary>
    /// Recursively finds panels that directly contain chart controls
    /// (CartesianChart, PieChart, or GeoMap).
    /// </summary>
    private void FindChartPanels(Control control, List<Panel> result)
    {
        if (control == this) return;

        if (control is Panel panel)
        {
            bool hasChart = false;
            foreach (var child in panel.Children)
            {
                if (child is CartesianChart or PieChart or GeoMap)
                {
                    hasChart = true;
                    break;
                }

                // Check for pie charts inside a Grid (pattern: Grid > Grid > PieChart)
                if (child is Grid grid && !hasChart)
                {
                    hasChart = GridContainsChart(grid);
                }

                if (hasChart) break;
            }

            if (hasChart)
            {
                result.Add(panel);
                return; // Don't recurse into chart panels
            }

            foreach (var child in panel.Children)
            {
                FindChartPanels(child, result);
            }
        }
        else if (control is ContentControl cc && cc.Content is Control content)
        {
            FindChartPanels(content, result);
        }
        else if (control is Decorator decorator && decorator.Child != null)
        {
            FindChartPanels(decorator.Child, result);
        }
        else
        {
            // Fallback: traverse the visual tree for controls that don't expose
            // children via the logical tree (e.g., ContentPresenter, template hosts).
            // This ensures dynamically created charts inside dashboard widgets
            // are always discovered.
            foreach (var child in control.GetVisualChildren())
            {
                if (child is Control childControl)
                    FindChartPanels(childControl, result);
            }
        }
    }

    /// <summary>
    /// Finds the first chart control in a panel, searching one level of nested grids.
    /// </summary>
    private static Control? FindChartControl(Panel panel)
    {
        foreach (var child in panel.Children)
        {
            if (child is CartesianChart or PieChart or GeoMap)
                return child;
            if (child is Grid innerGrid)
            {
                var nested = innerGrid.Children
                    .FirstOrDefault(c => c is CartesianChart or PieChart or GeoMap);
                if (nested != null)
                    return nested;
            }
        }
        return null;
    }

    /// <summary>
    /// Checks if a Grid (or its child Grids) contains a chart control.
    /// </summary>
    private static bool GridContainsChart(Grid grid)
    {
        foreach (var child in grid.Children)
        {
            if (child is PieChart or PieChartLegend or CartesianChart or GeoMap)
                return true;
            if (child is Grid innerGrid && GridContainsChart(innerGrid))
                return true;
        }

        return false;
    }

    private void AddExpandButton(Panel panel)
    {
        // Guard against duplicate buttons
        foreach (var child in panel.Children)
        {
            if (child is Button btn && btn.Classes.Contains("chart-expand-btn"))
                return;
        }

        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(4),
            Content = new PathIcon
            {
                Data = Geometry.Parse(Icons.Fullscreen),
                Width = 12,
                Height = 12
            }
        };
        button.Classes.Add("chart-expand-btn");
        button.Click += OnExpandButtonClick;

        // Hide the expand button when the chart has no data (empty state showing).
        // For pie charts and GeoMaps, the chart control's own IsVisible doesn't
        // reflect data availability, so we watch ChartEmptyState siblings instead.
        var emptyStates = FindDescendants<ChartEmptyState>(panel);
        if (emptyStates.Count > 0)
        {
            void UpdateButtonVisibility(object? s, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property == IsVisibleProperty)
                    button.IsVisible = !emptyStates.Any(es => es.IsVisible);
            }

            foreach (var emptyState in emptyStates)
            {
                emptyState.PropertyChanged += UpdateButtonVisibility;
                _emptyStateSubscriptions.Add((emptyState, UpdateButtonVisibility));
            }

            // Set initial state
            button.IsVisible = !emptyStates.Any(es => es.IsVisible);
        }
        else
        {
            // Fallback: bind to chart control visibility directly
            var chartControl = FindChartControl(panel);
            if (chartControl != null)
            {
                button.Bind(IsVisibleProperty, chartControl.GetObservable(IsVisibleProperty));
            }
        }

        panel.Children.Add(button);
    }

    private void OnExpandButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (button.Parent is not Panel sourcePanel) return;

        ShowChart(sourcePanel, button);
        e.Handled = true;
    }

    /// <summary>
    /// Shows the chart in the expanded overlay by reparenting its content.
    /// </summary>
    private void ShowChart(Panel sourcePanel, Button expandButton)
    {
        if (IsVisible) return;

        _sourcePanel = sourcePanel;
        _expandButton = expandButton;
        _movedChildren.Clear();

        var contentPanel = this.FindControl<Panel>("ContentPanel");
        if (contentPanel == null) return;

        // Set DataContexts so bindings keep working after reparenting.
        // ChartArea gets the page-level ViewModel (for context menu bindings).
        // ContentPanel gets the source panel's original DataContext (for chart
        // bindings like Series, HasData, etc., critical for dashboard widgets
        // where the chart is bound to a UnifiedChartWidgetViewModel, not the page VM).
        var chartArea = this.FindControl<Panel>("ChartArea");
        if (chartArea != null)
        {
            var dc = sourcePanel.DataContext;
            if (dc is not ChartContextMenuViewModelBase)
            {
                // Walk up to find the page ViewModel
                var parent = sourcePanel.Parent;
                while (parent != null)
                {
                    if (parent.DataContext is ChartContextMenuViewModelBase pageVm)
                    {
                        dc = pageVm;
                        break;
                    }
                    parent = parent.Parent;
                }
            }
            chartArea.DataContext = dc;
        }

        // Capture the DataContext that chart bindings need (e.g., UnifiedChartWidgetViewModel
        // for dashboard widgets, or AnalyticsPageViewModel for the analytics page).
        var sourceDataContext = sourcePanel.DataContext;

        // Suppress LiveCharts entrance animations during reparenting. Without this
        // the chart slides in from the side every time the fullscreen modal opens.
        var savedAnimSpeeds = new List<(Control chart, TimeSpan speed)>();
        foreach (var chart in FindAllDescendants<CartesianChart>(sourcePanel))
        {
            savedAnimSpeeds.Add((chart, chart.AnimationsSpeed));
            chart.AnimationsSpeed = TimeSpan.Zero;
        }
        foreach (var chart in FindAllDescendants<PieChart>(sourcePanel))
        {
            savedAnimSpeeds.Add((chart, chart.AnimationsSpeed));
            chart.AnimationsSpeed = TimeSpan.Zero;
        }

        // Move all children except the expand button to the overlay.
        // GeoMaps cannot be reparented because LiveCharts' DetachedFromVisualTree
        // handler calls CoreChart.Unload() which permanently breaks the control.
        // Instead, we hide original GeoMaps and create fresh copies in the overlay.
        _geoMapCopies.Clear();
        _isGeoMapFullscreen = sourcePanel.Children.OfType<GeoMap>().Any();

        foreach (var child in sourcePanel.Children.ToList())
        {
            if (child == expandButton) continue;

            if (child is GeoMap originalGeo)
            {
                // Copy ALL GeoMaps (both origin and destination) so the toggle works
                originalGeo.IsVisible = false;
                var copy = new GeoMap
                {
                    Series = originalGeo.Series,
                    MapProjection = originalGeo.MapProjection,
                    Tag = originalGeo.Tag,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };
                _geoMapCopies.Add((originalGeo, copy));
                contentPanel.Children.Add(copy);
            }
            else
            {
                // Set an explicit DataContext on each child so it survives reparenting.
                // Without this, the child loses its inherited DataContext when removed
                // from the widget and picks up the overlay's DataContext instead,
                // breaking bindings (e.g., dashboard charts show "no data available").
                child.DataContext = sourceDataContext;
                _movedChildren.Add(child);
                sourcePanel.Children.Remove(child);
                contentPanel.Children.Add(child);
            }
        }

        // Restore animation speeds after reparenting is complete
        foreach (var (chart, speed) in savedAnimSpeeds)
        {
            if (chart is CartesianChart cc) cc.AnimationsSpeed = speed;
            else if (chart is PieChart pc) pc.AnimationsSpeed = speed;
        }

        // Set up GeoMap header with title and origin/destination toggle
        if (_isGeoMapFullscreen)
            SetupGeoMapHeader(sourcePanel);

        // Hide the expand button while overlay is open
        expandButton.IsVisible = false;

        // Enlarge chart titles and legends for the fullscreen view
        EnlargeChartElements(contentPanel);

        // Set up zoom re-bucketing and granularity toggle for CartesianCharts
        SetupFullscreenZoom(contentPanel, sourcePanel.DataContext);

        IsVisible = true;
        Focus();
    }

    /// <summary>
    /// Closes the overlay and returns chart content to its original panel.
    /// </summary>
    public void CloseOverlay()
    {
        if (!IsVisible || _sourcePanel == null) return;

        var contentPanel = this.FindControl<Panel>("ContentPanel");
        if (contentPanel == null) return;

        // Close context menu before clearing DataContext
        var chartArea = this.FindControl<Panel>("ChartArea");
        if (chartArea?.DataContext is ChartContextMenuViewModelBase vm && vm.IsChartContextMenuOpen)
            vm.HideChartContextMenuCommand.Execute(null);

        // Tear down zoom subscription and granularity state
        TeardownFullscreenZoom();

        // Restore original sizes before reparenting back
        RestoreChartElements();

        // Hide GeoMap header
        if (_isGeoMapFullscreen)
            TeardownGeoMapHeader();

        // Remove GeoMap copies from overlay and restore originals
        foreach (var (original, copy) in _geoMapCopies)
        {
            contentPanel.Children.Remove(copy);
            original.ClearValue(IsVisibleProperty);
        }
        _geoMapCopies.Clear();

        // Suppress LiveCharts entrance animations when reparenting back
        var savedAnimSpeeds = new List<(Control chart, TimeSpan speed)>();
        foreach (var child in _movedChildren)
        {
            foreach (var chart in FindAllDescendants<CartesianChart>(child))
            {
                savedAnimSpeeds.Add((chart, chart.AnimationsSpeed));
                chart.AnimationsSpeed = TimeSpan.Zero;
            }
            foreach (var chart in FindAllDescendants<PieChart>(child))
            {
                savedAnimSpeeds.Add((chart, chart.AnimationsSpeed));
                chart.AnimationsSpeed = TimeSpan.Zero;
            }
        }

        // Return non-GeoMap children to the source panel
        var insertIndex = 0;
        foreach (var child in _movedChildren)
        {
            contentPanel.Children.Remove(child);
            // Clear the explicit DataContext so the child resumes inheriting
            // from its original parent (the chart widget).
            child.ClearValue(DataContextProperty);
            _sourcePanel!.Children.Insert(insertIndex, child);
            insertIndex++;
        }

        // Restore animation speeds
        foreach (var (chart, speed) in savedAnimSpeeds)
        {
            if (chart is CartesianChart cc) cc.AnimationsSpeed = speed;
            else if (chart is PieChart pc) pc.AnimationsSpeed = speed;
        }

        if (_expandButton != null)
        {
            _expandButton.ClearValue(IsVisibleProperty);

            // Re-evaluate empty-state-based visibility after restoring the button
            if (_sourcePanel != null)
            {
                var emptyStates = FindDescendants<ChartEmptyState>(_sourcePanel);
                if (emptyStates.Count > 0)
                    _expandButton.IsVisible = !emptyStates.Any(es => es.IsVisible);
            }
        }

        // Clear the borrowed DataContext
        if (chartArea != null)
            chartArea.DataContext = null;

        _movedChildren.Clear();
        _sourcePanel = null;
        _expandButton = null;
        IsVisible = false;
    }

    #region Fullscreen Zoom & Granularity

    /// <summary>
    /// Sets up zoom re-bucketing and the granularity toggle when a CartesianChart is expanded.
    /// </summary>
    private void SetupFullscreenZoom(Panel contentPanel, object? dataContext)
    {
        // Find the CartesianChart in the expanded content
        var cartesianChart = FindDescendant<CartesianChart>(contentPanel);
        if (cartesianChart == null)
        {
            GranularityPanel.IsVisible = false;
            return;
        }

        var chartType = cartesianChart.Tag as ChartDataType?;
        if (!chartType.HasValue)
        {
            GranularityPanel.IsVisible = false;
            return;
        }

        // Get ChartLoaderService from the ViewModel
        var loaderService = GetChartLoaderService(dataContext);
        if (loaderService == null)
        {
            GranularityPanel.IsVisible = false;
            return;
        }

        // Check if this chart has daily data for re-bucketing
        bool hasDailyData = loaderService.HasDailyData(chartType.Value);
        bool hasDailySeriesData = loaderService.HasDailySeriesData(chartType.Value);

        if (!hasDailyData && !hasDailySeriesData)
        {
            GranularityPanel.IsVisible = false;
            return;
        }

        _expandedChartType = chartType.Value;
        _expandedChartLoaderService = loaderService;
        _expandedIsMultiSeries = hasDailySeriesData && !hasDailyData;

        // Get series and axes from the chart control
        _expandedSeries = cartesianChart.Series as ObservableCollection<ISeries>;
        _expandedXAxes = cartesianChart.XAxes as Axis[];

        if (_expandedSeries == null || _expandedXAxes == null || _expandedXAxes.Length == 0)
        {
            GranularityPanel.IsVisible = false;
            return;
        }

        // Remember the current bucket so we can restore it when closing the overlay
        _preFullscreenBucket = loaderService.GetCurrentBucket(chartType.Value);

        // Show the granularity toggle with the current bucket selected (inherit from normal view)
        GranularityPanel.IsVisible = true;
        var currentBucket = _preFullscreenBucket ?? loaderService.GetDefaultBucket(chartType.Value);
        switch (currentBucket)
        {
            case ReportChartDataService.TimeBucket.Week:
                BucketWeek.IsChecked = true;
                break;
            case ReportChartDataService.TimeBucket.Month:
                BucketMonth.IsChecked = true;
                break;
            case ReportChartDataService.TimeBucket.Year:
                BucketYear.IsChecked = true;
                break;
            default:
                BucketDay.IsChecked = true;
                break;
        }

        // Subscribe to zoom re-bucketing (fullscreen only)
        _zoomUnsubscriber = loaderService.SubscribeToAxisZoom(
            _expandedXAxes, chartType.Value, _expandedSeries, _expandedIsMultiSeries);
    }

    /// <summary>
    /// Tears down zoom subscription and resets granularity state when closing the overlay.
    /// </summary>
    private void TeardownFullscreenZoom()
    {
        // Unsubscribe from zoom
        _zoomUnsubscriber?.Invoke();
        _zoomUnsubscriber = null;

        // Clear manual bucket override and restore the pre-fullscreen bucket
        // so the normal view is not affected by fullscreen bucketing changes
        if (_expandedChartType.HasValue && _expandedChartLoaderService != null)
        {
            _expandedChartLoaderService.ClearManualBucketOverride(_expandedChartType.Value);

            if (_preFullscreenBucket.HasValue && _expandedSeries != null && _expandedXAxes != null)
            {
                _expandedChartLoaderService.RestoreBucket(
                    _expandedChartType.Value,
                    _preFullscreenBucket.Value,
                    _expandedSeries,
                    _expandedXAxes,
                    _expandedIsMultiSeries);
            }
        }

        // Hide granularity toggle
        GranularityPanel.IsVisible = false;

        _expandedChartType = null;
        _expandedChartLoaderService = null;
        _expandedSeries = null;
        _expandedXAxes = null;
        _preFullscreenBucket = null;
    }

    /// <summary>
    /// Handles granularity toggle button clicks (Auto / Day / Week / Month).
    /// </summary>
    private void OnGranularityChanged(object? sender, RoutedEventArgs e)
    {
        if (_expandedChartType == null || _expandedChartLoaderService == null ||
            _expandedSeries == null || _expandedXAxes == null)
            return;

        var chartType = _expandedChartType.Value;
        ReportChartDataService.TimeBucket selectedBucket;

        if (BucketWeek.IsChecked == true)
            selectedBucket = ReportChartDataService.TimeBucket.Week;
        else if (BucketMonth.IsChecked == true)
            selectedBucket = ReportChartDataService.TimeBucket.Month;
        else if (BucketYear.IsChecked == true)
            selectedBucket = ReportChartDataService.TimeBucket.Year;
        else
            selectedBucket = ReportChartDataService.TimeBucket.Day;

        // Pin the selected granularity so zoom won't change it
        _expandedChartLoaderService.SetManualBucketOverride(chartType, selectedBucket);

        // Reset zoom before applying new bucket so the chart shows all data points
        var contentPanel = this.FindControl<Panel>("ContentPanel");
        var cartesianChart = contentPanel != null ? FindDescendant<CartesianChart>(contentPanel) : null;
        var yAxes = cartesianChart?.YAxes as Axis[];
        ChartLoaderService.ResetZoom(_expandedXAxes, yAxes);

        if (_expandedIsMultiSeries)
            _expandedChartLoaderService.ApplyBucketMultiSeries(
                chartType, selectedBucket, _expandedSeries, _expandedXAxes);
        else
            _expandedChartLoaderService.ApplyBucket(
                chartType, selectedBucket, _expandedSeries, _expandedXAxes);
    }

    /// <summary>
    /// Gets the ChartLoaderService from the page ViewModel.
    /// </summary>
    private static ChartLoaderService? GetChartLoaderService(object? dataContext)
    {
        // Dashboard charts are widgets, each loading through its own service, so the widget is what
        // the expanded chart's panel carries, not the dashboard page.
        return dataContext switch
        {
            ViewModels.Dashboard.UnifiedChartWidgetViewModel widget => widget.ChartLoaderService,
            DashboardPageViewModel dashboard => dashboard.ChartLoaderService,
            AnalyticsPageViewModel analytics => analytics.ChartLoaderService,
            _ => null
        };
    }

    /// <summary>
    /// Finds the first descendant of type T in the visual tree.
    /// </summary>
    private static T? FindDescendant<T>(Control root) where T : Control
    {
        if (root is T match)
            return match;

        if (root is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                var result = FindDescendant<T>(child);
                if (result != null) return result;
            }
        }
        else if (root is ContentControl cc && cc.Content is Control content)
        {
            return FindDescendant<T>(content);
        }
        else if (root is Decorator decorator && decorator.Child != null)
        {
            return FindDescendant<T>(decorator.Child);
        }

        return null;
    }

    /// <summary>
    /// Finds all descendants of type T using the visual tree.
    /// Works on any control, including dynamically created widget content.
    /// </summary>
    private static List<T> FindAllDescendants<T>(Control root) where T : Control
    {
        var results = new List<T>();
        foreach (var child in root.GetVisualChildren())
        {
            if (child is T match)
                results.Add(match);
            if (child is Control childControl)
                results.AddRange(FindAllDescendants<T>(childControl));
        }
        return results;
    }

    /// <summary>
    /// Finds all descendants of type T in the logical children of a panel.
    /// </summary>
    private static List<T> FindDescendants<T>(Panel root) where T : Control
    {
        var results = new List<T>();
        FindDescendantsRecursive(root, results);
        return results;
    }

    private static void FindDescendantsRecursive<T>(Control control, List<T> results) where T : Control
    {
        if (control is T match)
            results.Add(match);

        if (control is Panel panel)
        {
            foreach (var child in panel.Children)
                FindDescendantsRecursive(child, results);
        }
        else if (control is ContentControl cc && cc.Content is Control content)
        {
            FindDescendantsRecursive(content, results);
        }
        else if (control is Decorator decorator && decorator.Child != null)
        {
            FindDescendantsRecursive(decorator.Child, results);
        }
    }

    #endregion

    #region GeoMap Fullscreen Header

    /// <summary>
    /// Sets up the GeoMap header panel with the title and origin/destination toggle.
    /// Reads the title and radio button labels from the source page's parent elements.
    /// </summary>
    private void SetupGeoMapHeader(Panel sourcePanel)
    {
        // Find the title from the source panel's parent structure:
        // Panel (sourcePanel) > Border > Grid (row grid) > Border (header) > Grid > TextBlock
        var titleText = "World Map Overview";
        string? originLabel = null;
        string? destinationLabel = null;

        if (sourcePanel.Parent is Border border && border.Parent is Grid rowGrid)
        {
            foreach (var child in rowGrid.Children)
            {
                if (child is not Border headerBorder || Grid.GetRow(child) != 0) continue;
                if (headerBorder.Child is not Grid headerGrid) continue;

                foreach (var headerChild in headerGrid.Children)
                {
                    if (headerChild is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
                        titleText = tb.Text;

                    // Find the segmented button labels from the source toggle
                    if (headerChild is Border toggleBorder &&
                        toggleBorder.Child is StackPanel togglePanel)
                    {
                        var radioButtons = togglePanel.Children.OfType<RadioButton>().ToList();
                        if (radioButtons.Count >= 2)
                        {
                            originLabel = radioButtons[0].Content?.ToString();
                            destinationLabel = radioButtons[1].Content?.ToString();
                        }
                    }
                }
            }
        }

        GeoMapTitle.Text = titleText;
        GeoMapOriginButton.Content = originLabel ?? "Origin";
        GeoMapDestinationButton.Content = destinationLabel ?? "Destination";

        // Set checked state based on the ViewModel's IsMapModeOrigin (the first GeoMap
        // in the source panel is the origin map; if it was the one being shown, select origin)
        bool isOriginMode = true;
        if (sourcePanel.DataContext is AnalyticsPageViewModel analyticsVm)
            isOriginMode = analyticsVm.IsMapModeOrigin;
        GeoMapOriginButton.IsChecked = isOriginMode;
        GeoMapDestinationButton.IsChecked = !isOriginMode;

        // Set initial visibility: show only the copy that corresponds to the checked button
        UpdateGeoMapCopyVisibility();

        GeoMapHeaderPanel.IsVisible = true;
    }

    private void TeardownGeoMapHeader()
    {
        GeoMapHeaderPanel.IsVisible = false;
        _isGeoMapFullscreen = false;
    }

    private void OnGeoMapModeChanged(object? sender, RoutedEventArgs e)
    {
        UpdateGeoMapCopyVisibility();
    }

    /// <summary>
    /// Shows/hides GeoMap copies based on the origin/destination toggle state.
    /// </summary>
    private void UpdateGeoMapCopyVisibility()
    {
        if (_geoMapCopies.Count < 2) return;

        bool showOrigin = GeoMapOriginButton.IsChecked == true;
        _geoMapCopies[0].copy.IsVisible = showOrigin;
        _geoMapCopies[1].copy.IsVisible = !showOrigin;
    }

    #endregion

    #region Right-Click Context Menu

    /// <summary>
    /// Intercepts right-clicks on chart controls in the overlay to prevent LiveCharts'
    /// selection box and to show the chart context menu.
    /// </summary>
    private void OnOverlayPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (!IsVisible) return;

        var source = e.Source as Control;
        var chart = source?.FindAncestorOfType<CartesianChart>() ?? source as CartesianChart;
        var pieChart = source?.FindAncestorOfType<PieChart>() ?? source as PieChart;
        var geoMap = source?.FindAncestorOfType<GeoMap>() ?? source as GeoMap;

        // Handle PieChartLegend: find the associated PieChart sibling
        if (chart == null && pieChart == null && geoMap == null)
        {
            var legend = source?.FindAncestorOfType<PieChartLegend>() ?? source as PieChartLegend;
            if (legend?.Parent is Grid parentGrid)
            {
                foreach (var child in parentGrid.Children)
                {
                    if (child is PieChart siblingChart)
                    {
                        pieChart = siblingChart;
                        break;
                    }
                }
            }
        }

        if (chart == null && pieChart == null && geoMap == null) return;
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;

        // Prevent LiveCharts selection box
        e.Handled = true;

        var clickedControl = (Control?)chart ?? (Control?)pieChart ?? geoMap;
        var title = GetClickedChartTitle(clickedControl);

        // Set clicked chart on the source page for export operations
        SetPageClickedChart(clickedControl, title);

        // Show context menu via ViewModel
        var chartArea = this.FindControl<Panel>("ChartArea");
        if (chartArea?.DataContext is ChartContextMenuViewModelBase vm)
        {
            var position = e.GetPosition(chartArea);
            var chartDataType = clickedControl?.Tag as ChartDataType?;
            vm.ShowChartContextMenu(position.X, position.Y,
                chartDataType: chartDataType,
                isPieChart: pieChart != null,
                isGeoMap: geoMap != null,
                parentWidth: chartArea.Bounds.Width,
                parentHeight: chartArea.Bounds.Height);
        }
    }

    #endregion

    #region Page Integration

    /// <summary>
    /// Sets the clicked chart reference on the source page so export operations work correctly.
    /// </summary>
    private void SetPageClickedChart(Control? chart, string name)
    {
        if (_pageContentControl?.Content is DashboardPage dashPage)
            dashPage.SetClickedChart(chart, name);
        else if (_pageContentControl?.Content is AnalyticsPage analyticsPage)
            analyticsPage.SetClickedChart(chart, name);
    }

    /// <summary>
    /// Gets the chart title from a specific clicked chart control.
    /// Mirrors the page-level GetChartTitle approach so that the chart title
    /// matches what ChartLoaderService expects for exports.
    /// </summary>
    private static string GetClickedChartTitle(Control? control)
    {
        if (control is CartesianChart cc &&
            cc.Title is LabelVisual cartLabel &&
            !string.IsNullOrWhiteSpace(cartLabel.Text))
        {
            return cartLabel.Text;
        }

        if (control is PieChart pieChart &&
            pieChart.Title is LabelVisual pieLabel &&
            !string.IsNullOrWhiteSpace(pieLabel.Text))
        {
            return pieLabel.Text;
        }

        // For PieChart/GeoMap without a Title property, navigate parent Grids to
        // find the TextBlock title: Grid(Row) > Grid(Column) > PieChart/GeoMap
        if (control is PieChart or PieChartLegend or GeoMap)
        {
            var columnGrid = control.Parent as Grid;
            var rowGrid = columnGrid?.Parent as Grid;
            if (rowGrid != null)
            {
                foreach (var child in rowGrid.Children)
                {
                    if (child is TextBlock textBlock &&
                        Grid.GetRow(textBlock) == 0 &&
                        !string.IsNullOrWhiteSpace(textBlock.Text))
                    {
                        return textBlock.Text;
                    }
                }
            }
        }

        return "Chart";
    }

    #endregion

    #region Chart Element Sizing

    /// <summary>
    /// Enlarges chart titles and pie chart legends for the fullscreen modal view.
    /// </summary>
    private void EnlargeChartElements(Panel contentPanel)
    {
        _originalTitleSizes.Clear();
        _originalLegendSizes.Clear();

        EnlargeChartElementsRecursive(contentPanel);
    }

    private void EnlargeChartElementsRecursive(Control control)
    {
        // Enlarge CartesianChart titles
        if (control is CartesianChart cc && cc.Title is LabelVisual cartLabel)
        {
            _originalTitleSizes.Add((cartLabel, cartLabel.TextSize));
            cartLabel.TextSize = 22;
        }

        // Enlarge PieChart titles and add margin to shrink pie slightly
        if (control is PieChart pc)
        {
            if (pc.Title is LabelVisual pieLabel)
            {
                _originalTitleSizes.Add((pieLabel, pieLabel.TextSize));
                pieLabel.TextSize = 22;
            }

            _originalPieChartMargins.Add((pc, pc.Margin));
            pc.Margin = new Thickness(40, 20, 0, 20);
        }

        // Enlarge TextBlock titles (used by pie charts and other charts without LabelVisual)
        if (control is TextBlock tb && tb.FontWeight == FontWeight.SemiBold && tb.FontSize < 20)
        {
            _originalTitleSizes.Add((tb, tb.FontSize));
            tb.FontSize = 20;
        }

        // Enlarge PieChartLegend
        if (control is PieChartLegend legend)
        {
            _originalLegendSizes.Add((legend, legend.LegendFontSize, legend.IndicatorSize, legend.IndicatorCornerRadius, legend.MaxHeightOverride, legend.Width, legend.Margin));
            legend.LegendFontSize = 20;
            legend.IndicatorSize = 18;
            legend.IndicatorCornerRadius = new CornerRadius(9);
            legend.MaxHeightOverride = 600;
            legend.Width = 340;
            legend.Margin = new Thickness(8, 0, 24, 0);
        }

        // Recurse into children
        if (control is Panel panel)
        {
            foreach (var child in panel.Children)
                EnlargeChartElementsRecursive(child);
        }
        else if (control is ContentControl contentControl && contentControl.Content is Control content)
        {
            EnlargeChartElementsRecursive(content);
        }
        else if (control is Decorator decorator && decorator.Child != null)
        {
            EnlargeChartElementsRecursive(decorator.Child);
        }
    }

    /// <summary>
    /// Restores chart titles and legends to their original sizes.
    /// </summary>
    private void RestoreChartElements()
    {
        foreach (var (element, originalSize) in _originalTitleSizes)
        {
            if (element is LabelVisual label)
                label.TextSize = originalSize;
            else if (element is TextBlock tb)
                tb.FontSize = originalSize;
        }

        foreach (var (legend, origFontSize, origIndicatorSize, origCornerRadius, origMaxHeight, origWidth, origMargin) in _originalLegendSizes)
        {
            legend.LegendFontSize = origFontSize;
            legend.IndicatorSize = origIndicatorSize;
            legend.IndicatorCornerRadius = origCornerRadius;
            legend.MaxHeightOverride = origMaxHeight;
            legend.Width = origWidth;
            legend.Margin = origMargin;
        }

        foreach (var (chart, origMargin) in _originalPieChartMargins)
        {
            chart.Margin = origMargin;
        }

        _originalTitleSizes.Clear();
        _originalLegendSizes.Clear();
        _originalPieChartMargins.Clear();
    }

    #endregion

    #region Event Handlers

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        CloseOverlay();
    }

    private void OnBackdropClick(object? sender, PointerPressedEventArgs e)
    {
        CloseOverlay();
    }

    private void OnModalContentClick(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsVisible)
        {
            CloseOverlay();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    #endregion
}
