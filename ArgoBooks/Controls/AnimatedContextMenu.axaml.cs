using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ArgoBooks.Utilities;

namespace ArgoBooks.Controls;

/// <summary>
/// A reusable animated context menu with fade-in animation.
/// Provides overlay backdrop, positioning, and common styling.
/// Automatically adjusts position to prevent overflow outside container bounds.
/// </summary>
public partial class AnimatedContextMenu : UserControl, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #region Styled Properties

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<AnimatedContextMenu, bool>(nameof(IsOpen));

    public static readonly StyledProperty<double> MenuXProperty =
        AvaloniaProperty.Register<AnimatedContextMenu, double>(nameof(MenuX));

    public static readonly StyledProperty<double> MenuYProperty =
        AvaloniaProperty.Register<AnimatedContextMenu, double>(nameof(MenuY));

    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<AnimatedContextMenu, ICommand?>(nameof(CloseCommand));

    public static readonly StyledProperty<object?> MenuContentProperty =
        AvaloniaProperty.Register<AnimatedContextMenu, object?>(nameof(MenuContent));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets whether the context menu is visible.
    /// </summary>
    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets the X position of the context menu.
    /// </summary>
    public double MenuX
    {
        get => GetValue(MenuXProperty);
        set => SetValue(MenuXProperty, value);
    }

    /// <summary>
    /// Gets or sets the Y position of the context menu.
    /// </summary>
    public double MenuY
    {
        get => GetValue(MenuYProperty);
        set => SetValue(MenuYProperty, value);
    }

    /// <summary>
    /// Gets the adjusted X position that prevents horizontal overflow.
    /// </summary>
    public double AdjustedMenuX
    {
        get;
        private set
        {
            if (Math.Abs(field - value) > 0.001)
            {
                field = value;
                RaisePropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets the adjusted Y position that prevents vertical overflow.
    /// </summary>
    public double AdjustedMenuY
    {
        get;
        private set
        {
            if (Math.Abs(field - value) > 0.001)
            {
                field = value;
                RaisePropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the command to close the context menu.
    /// </summary>
    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    /// <summary>
    /// Gets or sets the content to display in the context menu.
    /// </summary>
    public object? MenuContent
    {
        get => GetValue(MenuContentProperty);
        set => SetValue(MenuContentProperty, value);
    }

    #endregion

    private IInputElement? _focusBeforeOpen;

    public AnimatedContextMenu()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsOpenProperty)
        {
            var animationBorder = this.FindControl<Border>("AnimationBorder");
            if (animationBorder == null) return;

            if (IsOpen)
            {
                _focusBeforeOpen = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
                // Calculate adjusted position and animate in
                Dispatcher.UIThread.Post(() =>
                {
                    CalculateAdjustedPosition(animationBorder);
                    animationBorder.Opacity = 1;
                    animationBorder.RenderTransform = new TranslateTransform(0, 0);
                    // The menu holds focus so its keys reach it; the arrows then pick an item.
                    animationBorder.Focus();
                }, DispatcherPriority.Render);
            }
            else
            {
                // Reset for next open
                Dispatcher.UIThread.Post(() =>
                {
                    animationBorder.Opacity = 0;
                    animationBorder.RenderTransform = new TranslateTransform(0, -8);
                }, DispatcherPriority.Background);
            }
        }
        else if (change.Property == MenuXProperty || change.Property == MenuYProperty)
        {
            // Recalculate when position changes while open
            if (IsOpen)
            {
                var animationBorder = this.FindControl<Border>("AnimationBorder");
                if (animationBorder != null)
                {
                    Dispatcher.UIThread.Post(() => CalculateAdjustedPosition(animationBorder), DispatcherPriority.Render);
                }
            }
        }
    }

    /// <summary>
    /// Calculates the adjusted menu position to prevent overflow outside container bounds.
    /// </summary>
    private void CalculateAdjustedPosition(Border menuBorder)
    {
        // Get the container bounds (this control should fill the page)
        var containerWidth = Bounds.Width;
        var containerHeight = Bounds.Height;

        menuBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var menuWidth = menuBorder.DesiredSize.Width;
        var menuHeight = menuBorder.DesiredSize.Height;

        // Use a small margin to keep menu away from edges
        const double margin = 8;

        // Calculate adjusted X position
        var adjustedX = MenuX;
        if (MenuX + menuWidth > containerWidth - margin)
        {
            // Would overflow right - shift left
            adjustedX = Math.Max(margin, containerWidth - menuWidth - margin);
        }
        if (adjustedX < margin)
        {
            adjustedX = margin;
        }

        // Calculate adjusted Y position
        var adjustedY = MenuY;
        if (MenuY + menuHeight > containerHeight - margin)
        {
            // Would overflow bottom - shift up
            adjustedY = Math.Max(margin, containerHeight - menuHeight - margin);
        }
        if (adjustedY < margin)
        {
            adjustedY = margin;
        }

        AdjustedMenuX = adjustedX;
        AdjustedMenuY = adjustedY;
    }

    /// <summary>
    /// Handles pointer pressed on the overlay to close the context menu.
    /// </summary>
    private void OnOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Close();
        e.Handled = true;
    }

    private void OnMenuKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Visual menu)
            return;

        switch (e.Key)
        {
            case Key.Down:
                ModalHelper.StepThroughButtons(menu, 1);
                e.Handled = true;
                break;
            case Key.Up:
                ModalHelper.StepThroughButtons(menu, -1);
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                _focusBeforeOpen?.Focus();
                e.Handled = true;
                break;
            case Key.Tab:
                ModalHelper.LeavePanelOnTab(e, _focusBeforeOpen, Close);
                break;
        }
    }

    private void Close()
    {
        if (CloseCommand?.CanExecute(null) == true)
        {
            CloseCommand.Execute(null);
        }
        else
        {
            // Fallback: directly set IsOpen to false
            IsOpen = false;
        }
    }
}
