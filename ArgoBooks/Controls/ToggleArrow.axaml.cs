using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ArgoBooks.Controls;

/// <summary>
/// Orientation for the ToggleArrow control.
/// </summary>
public enum ToggleArrowOrientation
{
    /// <summary>
    /// Horizontal orientation: points left when expanded, right when collapsed.
    /// </summary>
    Horizontal,

    /// <summary>
    /// Vertical orientation: points up when expanded, down when collapsed.
    /// </summary>
    Vertical
}

/// <summary>
/// An animated arrow control that rotates 180 degrees when collapsed.
/// Used for toggle buttons in sidebars and collapsible panels.
/// Horizontal: Points left when expanded, points right when collapsed.
/// Vertical: Points up when expanded, points down when collapsed.
/// </summary>
public partial class ToggleArrow : UserControl
{
    private readonly RotateTransform _rotateTransform;
    private DispatcherTimer? _animationTimer;
    private double _startAngle;
    private double _targetAngle;
    private double _animationProgress;
    private const double AnimationDuration = 500.0; // milliseconds

    /// <summary>
    /// Defines the IsCollapsed property.
    /// When true, the arrow rotates 180 degrees.
    /// </summary>
    public static readonly StyledProperty<bool> IsCollapsedProperty =
        AvaloniaProperty.Register<ToggleArrow, bool>(nameof(IsCollapsed));

    /// <summary>
    /// Defines the IconSize property.
    /// Controls both width and height of the arrow icon.
    /// </summary>
    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<ToggleArrow, double>(nameof(IconSize), 18.0);

    /// <summary>
    /// Defines the Foreground property.
    /// Controls the color of the arrow icon.
    /// </summary>
    public new static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<ToggleArrow, IBrush?>(nameof(Foreground));

    /// <summary>
    /// Defines the Orientation property.
    /// Controls whether the arrow animates horizontally (left/right) or vertically (up/down).
    /// </summary>
    public static readonly StyledProperty<ToggleArrowOrientation> OrientationProperty =
        AvaloniaProperty.Register<ToggleArrow, ToggleArrowOrientation>(nameof(Orientation));

    /// <summary>
    /// Gets or sets whether the arrow is in collapsed state (rotated 180 degrees).
    /// </summary>
    public bool IsCollapsed
    {
        get => GetValue(IsCollapsedProperty);
        set => SetValue(IsCollapsedProperty, value);
    }

    /// <summary>
    /// Gets or sets the size of the arrow icon (width and height).
    /// </summary>
    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the foreground brush for the arrow icon.
    /// </summary>
    public new IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the orientation of the arrow animation.
    /// Horizontal: left/right, Vertical: up/down.
    /// </summary>
    public ToggleArrowOrientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public ToggleArrow()
    {
        InitializeComponent();

        _rotateTransform = new RotateTransform();
        ArrowIcon.RenderTransform = _rotateTransform;
        ArrowIcon.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);

        // Set initial icon based on default orientation
        UpdateIcon();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsCollapsedProperty)
        {
            var isCollapsed = change.GetNewValue<bool>();
            AnimateRotation(GetTargetAngle(isCollapsed));
        }
        else if (change.Property == OrientationProperty)
        {
            UpdateIcon();
            // Update rotation immediately without animation when orientation changes
            _rotateTransform.Angle = GetTargetAngle(IsCollapsed);
        }
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        // Set initial icon and rotation without animation
        UpdateIcon();
        _rotateTransform.Angle = GetTargetAngle(IsCollapsed);
    }

    private void UpdateIcon()
    {
        // Horizontal uses ChevronLeft, Vertical uses ChevronDown
        ArrowIcon.Data = Orientation == ToggleArrowOrientation.Vertical
            ? Geometry.Parse(Icons.ChevronDown)
            : Geometry.Parse(Icons.ChevronLeft);
    }

    private double GetTargetAngle(bool isCollapsed)
    {
        if (Orientation == ToggleArrowOrientation.Vertical)
        {
            // Vertical: up (180°) when expanded, down (0°) when collapsed
            return isCollapsed ? 0 : 180;
        }
        else
        {
            // Horizontal: left (0°) when expanded, right (180°) when collapsed
            return isCollapsed ? 180 : 0;
        }
    }

    private void AnimateRotation(double targetAngle)
    {
        _animationTimer?.Stop();

        _startAngle = _rotateTransform.Angle;
        _targetAngle = targetAngle;
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

        _rotateTransform.Angle = _startAngle + (_targetAngle - _startAngle) * easedT;

        if (t >= 1.0)
        {
            _animationTimer?.Stop();
            _rotateTransform.Angle = _targetAngle;
        }
    }
}
