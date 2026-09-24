using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace ArgoBooks.Controls;

/// <summary>
/// The company's logo, or its initial on the brand gradient when it has none.
///
/// One control because the same tile appears in the sidebar, the company switcher, the file menu
/// and the welcome screen, and they had drifted: the welcome screen showed a generic building icon
/// and the switcher's recent list a grey letter on no background.
///
/// Size, corner radius and the initial's size come from the control's own Width/Height,
/// CornerRadius and FontSize, so each caller keeps the dimensions it had.
/// </summary>
public partial class CompanyAvatar : UserControl
{
    public static readonly StyledProperty<IImage?> LogoProperty =
        AvaloniaProperty.Register<CompanyAvatar, IImage?>(nameof(Logo));

    public static readonly StyledProperty<string?> InitialProperty =
        AvaloniaProperty.Register<CompanyAvatar, string?>(nameof(Initial), "?");

    public CompanyAvatar()
    {
        InitializeComponent();
    }

    /// <summary>The company logo. Null falls back to the initial.</summary>
    public IImage? Logo
    {
        get => GetValue(LogoProperty);
        set => SetValue(LogoProperty, value);
    }

    /// <summary>The single letter shown when there is no logo.</summary>
    public string? Initial
    {
        get => GetValue(InitialProperty);
        set => SetValue(InitialProperty, value);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
