using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Input;
using ArgoBooks.Utilities;

namespace ArgoBooks.Controls;

/// <summary>
/// A modal overlay that displays content with a semi-transparent backdrop.
/// </summary>
public partial class ModalOverlay : UserControl
{
    private Panel? _overlayPanel;
    private ContentPresenter? _modalContentPresenter;

    #region Styled Properties

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<ModalOverlay, bool>(nameof(IsOpen), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> CloseOnBackdropClickProperty =
        AvaloniaProperty.Register<ModalOverlay, bool>(nameof(CloseOnBackdropClick), defaultValue: true);

    public static readonly StyledProperty<bool> CloseOnEscapeProperty =
        AvaloniaProperty.Register<ModalOverlay, bool>(nameof(CloseOnEscape), true);

    public static readonly StyledProperty<object?> ModalContentProperty =
        AvaloniaProperty.Register<ModalOverlay, object?>(nameof(ModalContent));

    public static readonly StyledProperty<ICommand?> ClosingCommandProperty =
        AvaloniaProperty.Register<ModalOverlay, ICommand?>(nameof(ClosingCommand));

    public static readonly StyledProperty<double> AvailableWidthProperty =
        AvaloniaProperty.Register<ModalOverlay, double>(nameof(AvailableWidth), double.PositiveInfinity);

    public static readonly StyledProperty<double> AvailableHeightProperty =
        AvaloniaProperty.Register<ModalOverlay, double>(nameof(AvailableHeight), double.PositiveInfinity);

    #endregion

    #region Properties

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public bool CloseOnBackdropClick
    {
        get => GetValue(CloseOnBackdropClickProperty);
        set => SetValue(CloseOnBackdropClickProperty, value);
    }

    public bool CloseOnEscape
    {
        get => GetValue(CloseOnEscapeProperty);
        set => SetValue(CloseOnEscapeProperty, value);
    }

    /// <summary>
    /// Gets or sets the modal content. Use ModalOverlay.ModalContent property element syntax in XAML.
    /// </summary>
    public object? ModalContent
    {
        get => GetValue(ModalContentProperty);
        set => SetValue(ModalContentProperty, value);
    }

    /// <summary>
    /// Gets or sets the command to execute when a close is requested (backdrop click or Escape).
    /// If set, this command is executed instead of firing the Closing event and setting IsOpen = false.
    /// The command should handle any confirmation dialogs and set IsOpen = false if appropriate.
    /// </summary>
    public ICommand? ClosingCommand
    {
        get => GetValue(ClosingCommandProperty);
        set => SetValue(ClosingCommandProperty, value);
    }

    /// <summary>
    /// The room the window leaves for the modal, less a small gap at each edge. A modal with a fixed
    /// Width or Height binds its MaxWidth/MaxHeight to these so it shrinks instead of running off
    /// a window made smaller than it.
    /// </summary>
    public double AvailableWidth
    {
        get => GetValue(AvailableWidthProperty);
        private set => SetValue(AvailableWidthProperty, value);
    }

    /// <inheritdoc cref="AvailableWidth"/>
    public double AvailableHeight
    {
        get => GetValue(AvailableHeightProperty);
        private set => SetValue(AvailableHeightProperty, value);
    }

    private const double WindowEdgeGap = 16;

    #endregion

    #region Events

    public event EventHandler? Opened;
    public event EventHandler? Closed;
    public event EventHandler<ModalClosingEventArgs>? Closing;

    #endregion

    public ModalOverlay()
    {
        InitializeComponent();
        _overlayPanel = this.FindControl<Panel>("OverlayPanel");
        _modalContentPresenter = this.FindControl<ContentPresenter>("ModalContentPresenter");
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (_modalContentPresenter != null)
            _modalContentPresenter.Content = ModalContent;

        if (_overlayPanel != null)
        {
            _overlayPanel.Opacity = IsOpen ? 1 : 0;
            _overlayPanel.IsHitTestVisible = IsOpen;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsOpenProperty)
        {
            OnIsOpenChanged(IsOpen);
        }
        else if (change.Property == ModalContentProperty && _modalContentPresenter != null)
        {
            _modalContentPresenter.Content = ModalContent;
        }
        else if (change.Property == BoundsProperty)
        {
            AvailableWidth = Math.Max(0, Bounds.Width - WindowEdgeGap);
            AvailableHeight = Math.Max(0, Bounds.Height - WindowEdgeGap);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape && IsOpen && CloseOnEscape)
        {
            RequestClose();
            e.Handled = true;
        }
    }

    private void OnIsOpenChanged(bool isOpen)
    {
        if (_overlayPanel != null)
        {
            _overlayPanel.Opacity = isOpen ? 1 : 0;
            _overlayPanel.IsHitTestVisible = isOpen;
        }

        if (isOpen)
        {
            Focus();
            Opened?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Closed?.Invoke(this, EventArgs.Empty);
            ModalHelper.ReturnFocusToAppShell(this);
        }
    }

    private void OnBackdropPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (CloseOnBackdropClick)
            RequestClose();
    }

    public void RequestClose()
    {
        // If a ClosingCommand is set, use it instead of the event
        // The command is responsible for handling confirmation and setting IsOpen = false
        if (ClosingCommand != null)
        {
            if (ClosingCommand.CanExecute(null))
                ClosingCommand.Execute(null);
            return;
        }

        // Fallback to existing event-based behavior
        var args = new ModalClosingEventArgs();
        Closing?.Invoke(this, args);

        if (!args.Cancel)
            IsOpen = false;
    }
}

public class ModalClosingEventArgs : EventArgs
{
    public bool Cancel { get; set; }
}
