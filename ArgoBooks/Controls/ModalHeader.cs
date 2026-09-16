using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Metadata;

namespace ArgoBooks.Controls;

/// <summary>
/// The header row of a standard modal: title, optional subtitle, optional extra content under the
/// title, optional actions beside the close button, and the close button itself (shown when
/// <see cref="CloseCommand"/> is set). The template lives in Styles/ModalStyles.axaml.
/// </summary>
public class ModalHeader : TemplatedControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ModalHeader, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<ModalHeader, string?>(nameof(Subtitle));

    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<ModalHeader, ICommand?>(nameof(CloseCommand));

    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<ModalHeader, object?>(nameof(HeaderContent));

    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<ModalHeader, object?>(nameof(Actions));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    [Content]
    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }
}
