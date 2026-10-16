using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace ArgoBooks.Controls;

/// <summary>
/// A collapsible section in the sidebar containing navigation items.
/// </summary>
public partial class SidebarSection : UserControl
{
    #region Animation Fields

    private DispatcherTimer? _animationTimer;
    private double _startHeight;
    private double _targetHeight;
    private double _animationProgress;
    private const double AnimationDuration = 350.0; // milliseconds (matches sidebar toggle animation)
    private double _measuredContentHeight;
    private bool _isFirstLayout = true;

    #endregion

    #region Styled Properties

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SidebarSection, string?>(nameof(Title));

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<SidebarSection, bool>(nameof(IsExpanded), true);

    public static readonly StyledProperty<bool> IsCollapsedProperty =
        AvaloniaProperty.Register<SidebarSection, bool>(nameof(IsCollapsed));

    public static readonly StyledProperty<bool> IsCollapsibleProperty =
        AvaloniaProperty.Register<SidebarSection, bool>(nameof(IsCollapsible));

    public static readonly StyledProperty<bool> ShowHeaderProperty =
        AvaloniaProperty.Register<SidebarSection, bool>(nameof(ShowHeader), true);

    public static readonly StyledProperty<ObservableCollection<SidebarItemModel>?> ItemsProperty =
        AvaloniaProperty.Register<SidebarSection, ObservableCollection<SidebarItemModel>?>(nameof(Items));

    public static readonly StyledProperty<double> ContentMaxHeightProperty =
        AvaloniaProperty.Register<SidebarSection, double>(nameof(ContentMaxHeight), double.PositiveInfinity);

    public static readonly StyledProperty<string?> SectionKeyProperty =
        AvaloniaProperty.Register<SidebarSection, string?>(nameof(SectionKey));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the section title.
    /// </summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the section is expanded.
    /// </summary>
    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the sidebar is collapsed (icon-only mode).
    /// </summary>
    public bool IsCollapsed
    {
        get => GetValue(IsCollapsedProperty);
        set => SetValue(IsCollapsedProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the section can be collapsed.
    /// </summary>
    public bool IsCollapsible
    {
        get => GetValue(IsCollapsibleProperty);
        set => SetValue(IsCollapsibleProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to show the section header.
    /// </summary>
    public bool ShowHeader
    {
        get => GetValue(ShowHeaderProperty);
        set => SetValue(ShowHeaderProperty, value);
    }

    /// <summary>
    /// Gets or sets the collection of items in this section.
    /// </summary>
    public ObservableCollection<SidebarItemModel>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum height for the content area (used for animation).
    /// </summary>
    public double ContentMaxHeight
    {
        get => GetValue(ContentMaxHeightProperty);
        set => SetValue(ContentMaxHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets a stable key used for persisting the section's expanded state.
    /// </summary>
    public string? SectionKey
    {
        get => GetValue(SectionKeyProperty);
        set => SetValue(SectionKeyProperty, value);
    }

    /// <summary>
    /// Command to toggle the expanded state.
    /// </summary>
    public ICommand ToggleExpandedCommand { get; }

    #endregion

    public SidebarSection()
    {
        ToggleExpandedCommand = new RelayCommand(ToggleExpanded);
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsExpandedProperty)
        {
            var isExpanded = change.GetNewValue<bool>();
            AnimateExpandCollapse(isExpanded);
        }
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Restore persisted expanded state
        if (!string.IsNullOrEmpty(SectionKey))
        {
            var sectionStates = App.SettingsService?.GlobalSettings.Ui.SidebarSectionExpanded;
            if (sectionStates != null && sectionStates.TryGetValue(SectionKey, out var expanded))
            {
                IsExpanded = expanded;
            }
        }

        // Measure and set initial state without animation
        if (ContentItems != null)
        {
            ContentItems.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _measuredContentHeight = ContentItems.DesiredSize.Height;

            // Set initial height based on IsExpanded state (no animation on load)
            ContentMaxHeight = IsExpanded ? double.PositiveInfinity : 0;
            _isFirstLayout = false;
        }
    }

    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
        SaveExpandedState();
    }

    private void SaveExpandedState()
    {
        if (string.IsNullOrEmpty(SectionKey))
            return;

        var settings = App.SettingsService?.GlobalSettings;
        if (settings != null)
        {
            settings.Ui.SidebarSectionExpanded[SectionKey] = IsExpanded;
            _ = App.SettingsService?.SaveGlobalSettingsAsync();
        }
    }

    private void AnimateExpandCollapse(bool isExpanding)
    {
        // Skip animation on first layout
        if (_isFirstLayout || ContentItems == null)
            return;

        _animationTimer?.Stop();

        // Measure current content height
        ContentItems.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _measuredContentHeight = ContentItems.DesiredSize.Height;

        // Set start and target heights
        _startHeight = isExpanding ? 0 : _measuredContentHeight;
        _targetHeight = isExpanding ? _measuredContentHeight : 0;

        // If already at target, no animation needed
        if (Math.Abs(ContentMaxHeight - _targetHeight) < 0.1 && !double.IsPositiveInfinity(ContentMaxHeight))
            return;

        // Start from current position if mid-animation
        if (!double.IsPositiveInfinity(ContentMaxHeight))
        {
            _startHeight = ContentMaxHeight;
        }

        _animationProgress = 0;

        _animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Helpers.FrameAnimation.FrameIntervalMs)
        };
        _animationTimer.Tick += OnAnimationTick;
        _animationTimer.Start();
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        _animationProgress += Helpers.FrameAnimation.FrameIntervalMs;
        var t = Math.Min(_animationProgress / AnimationDuration, 1.0);
        var easedT = Helpers.FrameAnimation.CubicInOut(t);

        ContentMaxHeight = _startHeight + (_targetHeight - _startHeight) * easedT;

        if (t >= 1.0)
        {
            _animationTimer?.Stop();
            // Set to infinity when fully expanded for proper layout
            ContentMaxHeight = _targetHeight == 0 ? 0 : double.PositiveInfinity;
        }
    }
}

/// <summary>
/// Model for a sidebar navigation item.
/// </summary>
public class SidebarItemModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private string? _iconData;
    private string? _badgeText;

    /// <summary>
    /// Display text for the item.
    /// </summary>
    public string? Text
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>
    /// Page name for navigation.
    /// </summary>
    public string? PageName
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>
    /// Icon geometry data as string (parsed as StreamGeometry).
    /// </summary>
    public string? IconData
    {
        get => _iconData;
        set
        {
            if (SetProperty(ref _iconData, value))
            {
                OnPropertyChanged(nameof(Icon));
            }
        }
    }

    /// <summary>
    /// Parsed icon geometry.
    /// </summary>
    public Avalonia.Media.Geometry? Icon =>
        string.IsNullOrEmpty(_iconData) ? null : Avalonia.Media.StreamGeometry.Parse(_iconData);

    /// <summary>
    /// Whether this item is currently active.
    /// </summary>
    public bool IsActive
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>
    /// Whether this item is visible (for feature toggles).
    /// </summary>
    public bool IsVisible
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    /// <summary>
    /// Whether the sidebar is in collapsed mode.
    /// </summary>
    public bool IsCollapsed
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>
    /// Badge text to display.
    /// </summary>
    public string? BadgeText
    {
        get => _badgeText;
        set
        {
            if (SetProperty(ref _badgeText, value))
            {
                OnPropertyChanged(nameof(ShowBadge));
            }
        }
    }

    /// <summary>
    /// Whether to show the badge.
    /// </summary>
    public bool ShowBadge
    {
        get => field || !string.IsNullOrEmpty(_badgeText);
        set => SetProperty(ref field, value);
    }

    /// <summary>
    /// Command to execute when clicked.
    /// </summary>
    public ICommand? Command
    {
        get;
        set => SetProperty(ref field, value);
    }
}
