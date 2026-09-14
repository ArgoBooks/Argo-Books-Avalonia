using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;

namespace ArgoBooks.Controls;

/// <summary>
/// Read-only details of a recorded item: an icon header, the type's own fields in
/// <see cref="Details"/>, then the reason and notes every record has.
/// </summary>
public partial class RecordDetailsModal : UserControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<RecordDetailsModal, bool>(nameof(IsOpen), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<RecordDetailsModal, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<RecordDetailsModal, string?>(nameof(Subtitle));

    public static readonly StyledProperty<Geometry?> IconDataProperty =
        AvaloniaProperty.Register<RecordDetailsModal, Geometry?>(nameof(IconData));

    public static readonly StyledProperty<IBrush?> IconForegroundProperty =
        AvaloniaProperty.Register<RecordDetailsModal, IBrush?>(nameof(IconForeground));

    public static readonly StyledProperty<IBrush?> IconBackgroundProperty =
        AvaloniaProperty.Register<RecordDetailsModal, IBrush?>(nameof(IconBackground));

    public static readonly StyledProperty<string?> ReasonProperty =
        AvaloniaProperty.Register<RecordDetailsModal, string?>(nameof(Reason));

    public static readonly StyledProperty<string?> NotesProperty =
        AvaloniaProperty.Register<RecordDetailsModal, string?>(nameof(Notes));

    public static readonly StyledProperty<object?> DetailsProperty =
        AvaloniaProperty.Register<RecordDetailsModal, object?>(nameof(Details));

    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<RecordDetailsModal, ICommand?>(nameof(CloseCommand));

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

    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public Geometry? IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public IBrush? IconForeground
    {
        get => GetValue(IconForegroundProperty);
        set => SetValue(IconForegroundProperty, value);
    }

    public IBrush? IconBackground
    {
        get => GetValue(IconBackgroundProperty);
        set => SetValue(IconBackgroundProperty, value);
    }

    public string? Reason
    {
        get => GetValue(ReasonProperty);
        set => SetValue(ReasonProperty, value);
    }

    public string? Notes
    {
        get => GetValue(NotesProperty);
        set => SetValue(NotesProperty, value);
    }

    /// <summary>The fields particular to the record type, shown above the reason.</summary>
    public object? Details
    {
        get => GetValue(DetailsProperty);
        set => SetValue(DetailsProperty, value);
    }

    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public RecordDetailsModal()
    {
        InitializeComponent();
    }
}
