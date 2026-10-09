using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ArgoBooks.Helpers;

/// <summary>
/// Attached behavior for banner fade-in/fade-out animations.
/// Similar to ModalAnimationBehavior but supports nested property paths.
/// Opens with a slow animation (1 second) and closes quickly (0.15 seconds).
/// </summary>
public static class BannerAnimationBehavior
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Border, bool>("IsEnabled", typeof(BannerAnimationBehavior));

    public static readonly AttachedProperty<bool> IsVisibleProperty =
        AvaloniaProperty.RegisterAttached<Border, bool>("IsVisible", typeof(BannerAnimationBehavior));

    private static readonly AttachedProperty<Transitions?> OriginalTransitionsProperty =
        AvaloniaProperty.RegisterAttached<Border, Transitions?>("OriginalTransitions", typeof(BannerAnimationBehavior));

    public static bool GetIsEnabled(Border element) => element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(Border element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsVisible(Border element) => element.GetValue(IsVisibleProperty);
    public static void SetIsVisible(Border element, bool value) => element.SetValue(IsVisibleProperty, value);

    private static void SetOriginalTransitions(Border element, Transitions? value) => element.SetValue(OriginalTransitionsProperty, value);

    private static readonly TimeSpan FadeInDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FadeOutDuration = TimeSpan.FromSeconds(0.15);

    static BannerAnimationBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<Border>(OnIsEnabledChanged);
        IsVisibleProperty.Changed.AddClassHandler<Border>(OnIsVisibleChanged);
    }

    private static void OnIsEnabledChanged(Border border, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            // Store original transitions (from CSS style) for restore during animate-in
            SetOriginalTransitions(border, border.Transitions);

            // Only AnimateIn turns hit testing back on, so a banner already showing when the behaviour attaches takes its start state from the flag instead of assuming hidden.
            var shown = GetIsVisible(border);
            border.Opacity = shown ? 1 : 0;
            border.IsHitTestVisible = shown;
        }
    }

    private static void OnIsVisibleChanged(Border border, AvaloniaPropertyChangedEventArgs e)
    {
        if (!GetIsEnabled(border))
            return;

        if (e.NewValue is true)
        {
            AnimateIn(border);
        }
        else
        {
            AnimateOut(border);
        }
    }

    private static void AnimateIn(Border border)
    {
        // Use Dispatcher.Post to ensure the animation happens after render
        Dispatcher.UIThread.Post(() =>
        {
            // Set transition duration for fade-in (1 second)
            SetTransitionDuration(border, FadeInDuration);
            border.Opacity = 1;
            border.IsHitTestVisible = true;
        }, DispatcherPriority.Render);
    }

    private static void AnimateOut(Border border)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Set transition duration for fade-out (0.15 seconds)
            SetTransitionDuration(border, FadeOutDuration);
            border.Opacity = 0;
            border.IsHitTestVisible = false;
        }, DispatcherPriority.Background);
    }

    private static void SetTransitionDuration(Border border, TimeSpan duration)
    {
        border.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Border.OpacityProperty,
                Duration = duration,
                Easing = new Avalonia.Animation.Easings.CubicEaseOut()
            }
        };
    }
}
