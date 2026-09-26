using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ArgoBooks.Controls;

/// <summary>
/// An animated success indicator with green circle, checkmark, and customizable text.
/// </summary>
public partial class SuccessAnimation : UserControl
{
    #region Styled Properties

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<SuccessAnimation, string>(nameof(Title), "Success!");

    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<SuccessAnimation, string?>(nameof(Message));

    public static readonly StyledProperty<bool> ShowContinueButtonProperty =
        AvaloniaProperty.Register<SuccessAnimation, bool>(nameof(ShowContinueButton), true);

    public static readonly StyledProperty<string> ContinueButtonTextProperty =
        AvaloniaProperty.Register<SuccessAnimation, string>(nameof(ContinueButtonText), "Continue");

    public static readonly StyledProperty<ICommand?> ContinueCommandProperty =
        AvaloniaProperty.Register<SuccessAnimation, ICommand?>(nameof(ContinueCommand));

    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<SuccessAnimation, bool>(nameof(IsPlaying));

    public static readonly StyledProperty<int> ContinueButtonDelayMsProperty =
        AvaloniaProperty.Register<SuccessAnimation, int>(nameof(ContinueButtonDelayMs), 1500);

    #endregion

    #region Properties

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public bool ShowContinueButton
    {
        get => GetValue(ShowContinueButtonProperty);
        set => SetValue(ShowContinueButtonProperty, value);
    }

    public string ContinueButtonText
    {
        get => GetValue(ContinueButtonTextProperty);
        set => SetValue(ContinueButtonTextProperty, value);
    }

    public ICommand? ContinueCommand
    {
        get => GetValue(ContinueCommandProperty);
        set => SetValue(ContinueCommandProperty, value);
    }

    public bool IsPlaying
    {
        get => GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public int ContinueButtonDelayMs
    {
        get => GetValue(ContinueButtonDelayMsProperty);
        set => SetValue(ContinueButtonDelayMsProperty, value);
    }

    #endregion

    public SuccessAnimation()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsPlayingProperty)
        {
            if (IsPlaying)
            {
                PlayAnimation();
            }
            else
            {
                ResetAnimation();
            }
        }
    }

    public void PlayAnimation()
    {
        _ = PlayAnimationAsync();
    }

    public void ResetAnimation()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (SuccessGlow != null)
                SuccessGlow.Opacity = 0;

            if (SuccessCircle != null)
                SuccessCircle.Opacity = 0;

            if (SuccessTextPanel != null)
                SuccessTextPanel.Opacity = 0;

            if (ContinueButtonPanel != null)
                ContinueButtonPanel.Opacity = 0;
        }, DispatcherPriority.Background);
    }

    private async Task PlayAnimationAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            // Animate the glow and circle
            if (SuccessGlow != null)
                SuccessGlow.Opacity = 0.4;

            if (SuccessCircle != null)
                SuccessCircle.Opacity = 1;

            // Wait for circle animation
            await Task.Delay(500);

            // Animate text panel
            if (SuccessTextPanel != null)
                SuccessTextPanel.Opacity = 1;

            // Wait for text animation
            await Task.Delay(300);

            // Show continue button after additional delay
            if (ShowContinueButton)
            {
                await Task.Delay(ContinueButtonDelayMs);

                if (ContinueButtonPanel != null)
                    ContinueButtonPanel.Opacity = 1;
            }
        });
    }
}
