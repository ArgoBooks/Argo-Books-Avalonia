using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace ArgoBooks.Controls;

/// <summary>
/// Asks to confirm undoing a recorded item, with an optional reason.
/// </summary>
public partial class UndoRecordModal : UserControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<UndoRecordModal, bool>(nameof(IsOpen), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<UndoRecordModal, string?>(nameof(Title));

    public static readonly StyledProperty<string?> ItemLabelProperty =
        AvaloniaProperty.Register<UndoRecordModal, string?>(nameof(ItemLabel));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<UndoRecordModal, string?>(nameof(Description));

    public static readonly StyledProperty<string?> ReasonProperty =
        AvaloniaProperty.Register<UndoRecordModal, string?>(nameof(Reason), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> ReasonPlaceholderProperty =
        AvaloniaProperty.Register<UndoRecordModal, string?>(nameof(ReasonPlaceholder));

    public static readonly StyledProperty<string?> ConfirmTextProperty =
        AvaloniaProperty.Register<UndoRecordModal, string?>(nameof(ConfirmText));

    public static readonly StyledProperty<ICommand?> ConfirmCommandProperty =
        AvaloniaProperty.Register<UndoRecordModal, ICommand?>(nameof(ConfirmCommand));

    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<UndoRecordModal, ICommand?>(nameof(CloseCommand));

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

    public string? ItemLabel
    {
        get => GetValue(ItemLabelProperty);
        set => SetValue(ItemLabelProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string? Reason
    {
        get => GetValue(ReasonProperty);
        set => SetValue(ReasonProperty, value);
    }

    public string? ReasonPlaceholder
    {
        get => GetValue(ReasonPlaceholderProperty);
        set => SetValue(ReasonPlaceholderProperty, value);
    }

    public string? ConfirmText
    {
        get => GetValue(ConfirmTextProperty);
        set => SetValue(ConfirmTextProperty, value);
    }

    public ICommand? ConfirmCommand
    {
        get => GetValue(ConfirmCommandProperty);
        set => SetValue(ConfirmCommandProperty, value);
    }

    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public UndoRecordModal()
    {
        InitializeComponent();
    }
}
