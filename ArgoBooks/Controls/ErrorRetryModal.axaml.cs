using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace ArgoBooks.Controls;

/// <summary>
/// An error card with Cancel and Retry. While <see cref="IsBusy"/>, the buttons are disabled and,
/// when a <see cref="BusyTitle"/> is given, the message is swapped for a busy panel.
/// </summary>
public partial class ErrorRetryModal : UserControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<ErrorRetryModal, bool>(nameof(IsOpen), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ErrorRetryModal, string?>(nameof(Title));

    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<ErrorRetryModal, string?>(nameof(Message));

    public static readonly StyledProperty<bool> IsBusyProperty =
        AvaloniaProperty.Register<ErrorRetryModal, bool>(nameof(IsBusy));

    public static readonly StyledProperty<string?> BusyTitleProperty =
        AvaloniaProperty.Register<ErrorRetryModal, string?>(nameof(BusyTitle));

    public static readonly StyledProperty<string?> BusyMessageProperty =
        AvaloniaProperty.Register<ErrorRetryModal, string?>(nameof(BusyMessage));

    public static readonly StyledProperty<ICommand?> DismissCommandProperty =
        AvaloniaProperty.Register<ErrorRetryModal, ICommand?>(nameof(DismissCommand));

    public static readonly StyledProperty<ICommand?> RetryCommandProperty =
        AvaloniaProperty.Register<ErrorRetryModal, ICommand?>(nameof(RetryCommand));

    /// <summary>
    /// Whether Escape and a backdrop click close the card. Either way they run
    /// <see cref="DismissCommand"/>, the same as Cancel.
    /// </summary>
    public static readonly StyledProperty<bool> AllowLightDismissProperty =
        AvaloniaProperty.Register<ErrorRetryModal, bool>(nameof(AllowLightDismiss), true);

    public static readonly DirectProperty<ErrorRetryModal, bool> ShowBusyPanelProperty =
        AvaloniaProperty.RegisterDirect<ErrorRetryModal, bool>(nameof(ShowBusyPanel), o => o.ShowBusyPanel);

    public static readonly DirectProperty<ErrorRetryModal, bool> ShowButtonSpinnerProperty =
        AvaloniaProperty.RegisterDirect<ErrorRetryModal, bool>(nameof(ShowButtonSpinner), o => o.ShowButtonSpinner);

    private bool _showBusyPanel;
    private bool _showButtonSpinner;

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public bool IsBusy
    {
        get => GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public string? BusyTitle
    {
        get => GetValue(BusyTitleProperty);
        set => SetValue(BusyTitleProperty, value);
    }

    public string? BusyMessage
    {
        get => GetValue(BusyMessageProperty);
        set => SetValue(BusyMessageProperty, value);
    }

    public ICommand? DismissCommand
    {
        get => GetValue(DismissCommandProperty);
        set => SetValue(DismissCommandProperty, value);
    }

    public ICommand? RetryCommand
    {
        get => GetValue(RetryCommandProperty);
        set => SetValue(RetryCommandProperty, value);
    }

    public bool AllowLightDismiss
    {
        get => GetValue(AllowLightDismissProperty);
        set => SetValue(AllowLightDismissProperty, value);
    }

    public bool ShowBusyPanel
    {
        get => _showBusyPanel;
        private set => SetAndRaise(ShowBusyPanelProperty, ref _showBusyPanel, value);
    }

    public bool ShowButtonSpinner
    {
        get => _showButtonSpinner;
        private set => SetAndRaise(ShowButtonSpinnerProperty, ref _showButtonSpinner, value);
    }

    public ErrorRetryModal()
    {
        InitializeComponent();
        UpdateState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsBusyProperty || change.Property == BusyTitleProperty ||
            change.Property == AllowLightDismissProperty)
            UpdateState();
    }

    private void UpdateState()
    {
        var hasBusyPanel = !string.IsNullOrEmpty(BusyTitle);
        ShowBusyPanel = IsBusy && hasBusyPanel;
        ShowButtonSpinner = IsBusy && !hasBusyPanel;

        // Cancel is disabled while busy, so light dismiss is too.
        var lightDismiss = AllowLightDismiss && !IsBusy;
        Overlay.CloseOnEscape = lightDismiss;
        Overlay.CloseOnBackdropClick = lightDismiss;
    }
}
