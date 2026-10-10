using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArgoBooks.Controls;

/// <summary>
/// A reusable banner control that displays an icon, message, optional action link, and dismiss button.
/// Used for non-blocking notifications that overlay the top of the content area.
/// </summary>
public partial class InfoBanner : UserControl
{
    #region Styled Properties

    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<InfoBanner, string?>(nameof(Message));

    public static readonly StyledProperty<string?> ActionTextProperty =
        AvaloniaProperty.Register<InfoBanner, string?>(nameof(ActionText));

    public static readonly StyledProperty<ICommand?> ActionCommandProperty =
        AvaloniaProperty.Register<InfoBanner, ICommand?>(nameof(ActionCommand));

    public static readonly StyledProperty<ICommand?> DismissCommandProperty =
        AvaloniaProperty.Register<InfoBanner, ICommand?>(nameof(DismissCommand));

    public static readonly StyledProperty<Geometry?> IconDataProperty =
        AvaloniaProperty.Register<InfoBanner, Geometry?>(nameof(IconData));

    public static readonly StyledProperty<IBrush?> BannerBackgroundProperty =
        AvaloniaProperty.Register<InfoBanner, IBrush?>(nameof(BannerBackground));

    public static readonly StyledProperty<IBrush?> DismissBackgroundProperty =
        AvaloniaProperty.Register<InfoBanner, IBrush?>(nameof(DismissBackground));

    public static readonly StyledProperty<bool> ShowBannerProperty =
        AvaloniaProperty.Register<InfoBanner, bool>(nameof(ShowBanner));

    /// <summary>
    /// Outer margin of the banner. Defaults to a small top gap suited to floating/overlay use (e.g.
    /// the update banner). Inline hosts that already provide their own spacing can set this to 0.
    /// </summary>
    public static readonly StyledProperty<Thickness> BannerMarginProperty =
        AvaloniaProperty.Register<InfoBanner, Thickness>(nameof(BannerMargin), new Thickness(0, 10, 0, 0));

    #endregion

    #region Properties

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string? ActionText
    {
        get => GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    public ICommand? DismissCommand
    {
        get => GetValue(DismissCommandProperty);
        set => SetValue(DismissCommandProperty, value);
    }

    public Geometry? IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public IBrush? BannerBackground
    {
        get => GetValue(BannerBackgroundProperty);
        set => SetValue(BannerBackgroundProperty, value);
    }

    public IBrush? DismissBackground
    {
        get => GetValue(DismissBackgroundProperty);
        set => SetValue(DismissBackgroundProperty, value);
    }

    public bool ShowBanner
    {
        get => GetValue(ShowBannerProperty);
        set => SetValue(ShowBannerProperty, value);
    }

    public Thickness BannerMargin
    {
        get => GetValue(BannerMarginProperty);
        set => SetValue(BannerMarginProperty, value);
    }

    #endregion

    /// <summary>
    /// Raised alongside ActionCommand, for a caller that has to do something first, such as flushing
    /// a native web view's pending edits before the command hides it.
    /// </summary>
    public event EventHandler<Avalonia.Interactivity.RoutedEventArgs>? ActionClick;

    public InfoBanner()
    {
        InitializeComponent();
    }

    private void OnActionClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ActionClick?.Invoke(this, e);
}
