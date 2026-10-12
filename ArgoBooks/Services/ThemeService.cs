using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using ArgoBooks.Core;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Services;

namespace ArgoBooks.Services;

/// <summary>
/// Service for managing application theme with Avalonia support.
/// </summary>
public class ThemeService : IThemeService
{
    private IGlobalSettingsService? _globalSettingsService;

    /// <summary>Whether the OS theme-change subscription is already in place.</summary>
    private bool _systemThemeHookInstalled;

    /// <summary>
    /// Gets the singleton instance of the ThemeService.
    /// </summary>
    public static ThemeService Instance => field ??= new ThemeService();

    // Accent color definitions: (Primary, Hover, Light, Dark, Secondary/Gradient, IconBg)
    private static readonly Dictionary<AccentColor, AccentColorSet> AccentColorSets = new()
    {
        [AccentColor.Blue] = new AccentColorSet(
            Primary: Color.Parse(AppColors.Primary),
            Hover: Color.Parse(AppColors.PrimaryHover),
            Light: Color.Parse(AppColors.PrimaryLight),
            Dark: Color.Parse(AppColors.PrimaryDark),
            Secondary: Color.Parse(AppColors.Indigo),  // Indigo for gradient
            IconBgLight: Color.Parse(AppColors.PrimaryLightest),
            IconBgDark: Color.Parse(AppColors.PrimaryDarkBg)
        ),
        [AccentColor.Green] = new AccentColorSet(
            Primary: Color.Parse(AppColors.Emerald),
            Hover: Color.Parse(AppColors.EmeraldHover),
            Light: Color.Parse(AppColors.EmeraldLight),
            Dark: Color.Parse(AppColors.EmeraldDark),
            Secondary: Color.Parse(AppColors.Teal),  // Teal for gradient
            IconBgLight: Color.Parse(AppColors.EmeraldLightest),
            IconBgDark: Color.Parse(AppColors.TealDarkest)
        ),
        [AccentColor.Purple] = new AccentColorSet(
            Primary: Color.Parse(AppColors.Violet),
            Hover: Color.Parse(AppColors.VioletHover),
            Light: Color.Parse(AppColors.VioletLight),
            Dark: Color.Parse(AppColors.VioletDark),
            Secondary: Color.Parse(AppColors.Pink),  // Pink for gradient
            IconBgLight: Color.Parse(AppColors.VioletLightest),
            IconBgDark: Color.Parse(AppColors.PurpleDarkest)
        ),
        [AccentColor.Pink] = new AccentColorSet(
            Primary: Color.Parse(AppColors.Pink),
            Hover: Color.Parse(AppColors.PinkHover),
            Light: Color.Parse(AppColors.PinkLight),
            Dark: Color.Parse(AppColors.PinkDark),
            Secondary: Color.Parse(AppColors.PinkMedium),  // Light pink for gradient
            IconBgLight: Color.Parse(AppColors.PinkLightest),
            IconBgDark: Color.Parse(AppColors.PinkDarkest)
        ),
        [AccentColor.Orange] = new AccentColorSet(
            Primary: Color.Parse(AppColors.Orange),
            Hover: Color.Parse(AppColors.OrangeHover),
            Light: Color.Parse(AppColors.OrangeLight),
            Dark: Color.Parse(AppColors.OrangeDark),
            Secondary: Color.Parse(AppColors.Amber),  // Yellow for gradient
            IconBgLight: Color.Parse(AppColors.OrangeLightest),
            IconBgDark: Color.Parse(AppColors.OrangeDarkest)
        ),
        [AccentColor.Teal] = new AccentColorSet(
            Primary: Color.Parse(AppColors.Teal),
            Hover: Color.Parse(AppColors.TealHover),
            Light: Color.Parse(AppColors.TealLight),
            Dark: Color.Parse(AppColors.TealDark),
            Secondary: Color.Parse(AppColors.Cyan),  // Cyan for gradient
            IconBgLight: Color.Parse(AppColors.TealLightest),
            IconBgDark: Color.Parse(AppColors.TealDarkest)
        )
    };

    private record AccentColorSet(
        Color Primary,
        Color Hover,
        Color Light,
        Color Dark,
        Color Secondary,
        Color IconBgLight,
        Color IconBgDark
    );

    /// <inheritdoc />
    public ThemeMode CurrentTheme { get; private set; } = ThemeMode.Dark;

    /// <inheritdoc />
    public bool IsDarkTheme
    {
        get
        {
            if (CurrentTheme == ThemeMode.System)
            {
                // Check the actual theme variant from Avalonia
                var app = Application.Current;
                if (app != null)
                {
                    var actualTheme = app.ActualThemeVariant;
                    return actualTheme == ThemeVariant.Dark;
                }
                return false;
            }
            return CurrentTheme == ThemeMode.Dark;
        }
    }

    /// <inheritdoc />
    public event EventHandler<ThemeMode>? ThemeChanged;

    /// <inheritdoc />
    public void SetTheme(ThemeMode theme)
    {
        if (CurrentTheme == theme)
            return;

        CurrentTheme = theme;
        ApplyTheme();
        SaveToSettings();
        ThemeChanged?.Invoke(this, theme);
    }

    /// <summary>
    /// Sets the theme by name (Light, Dark, System).
    /// </summary>
    /// <param name="themeName">The theme name.</param>
    public void SetTheme(string themeName)
    {
        SetTheme(ThemeModeExtensions.ParseThemeMode(themeName));
    }

    /// <summary>
    /// Sets the global settings service for theme persistence.
    /// </summary>
    /// <param name="settingsService">The global settings service.</param>
    public void SetGlobalSettingsService(IGlobalSettingsService? settingsService)
    {
        _globalSettingsService = settingsService;
    }

    /// <summary>
    /// Loads theme and accent color from global settings.
    /// </summary>
    public void LoadFromSettings()
    {
        var settings = _globalSettingsService?.GetSettings();
        if (settings?.Ui != null)
        {
            CurrentTheme = ThemeModeExtensions.ParseThemeMode(settings.Ui.Theme);
            var parsed = AccentColorExtensions.ParseAccentColor(settings.Ui.AccentColor);
            CurrentAccentColor = parsed != null ? settings.Ui.AccentColor : nameof(AccentColor.Blue);
        }
    }

    /// <summary>
    /// Saves current theme and accent color to global settings.
    /// </summary>
    private void SaveToSettings()
    {
        var settings = _globalSettingsService?.GetSettings();
        if (settings != null)
        {
            settings.Ui.Theme = CurrentThemeName;
            settings.Ui.AccentColor = CurrentAccentColor;
            _globalSettingsService?.SaveSettings(settings);
        }
    }

    public string CurrentThemeName => CurrentTheme.GetDisplayName();

    /// <summary>
    /// Gets or sets the current accent color name.
    /// </summary>
    public string CurrentAccentColor { get; private set; } = nameof(AccentColor.Blue);

    /// <summary>
    /// Sets the accent color by name.
    /// </summary>
    /// <param name="colorName">The color name (Blue, Green, Purple, Pink, Orange, Teal).</param>
    public void SetAccentColor(string colorName)
    {
        if (AccentColorExtensions.ParseAccentColor(colorName) == null)
            return;

        CurrentAccentColor = colorName;
        ApplyAccentColor();
        SaveToSettings();
    }

    private void ApplyAccentColor()
    {
        var app = Application.Current;
        var parsedColor = AccentColorExtensions.ParseAccentColor(CurrentAccentColor);
        if (app == null || parsedColor == null || !AccentColorSets.TryGetValue(parsedColor.Value, out var colors))
            return;

        // Update primary colors
        app.Resources["PrimaryColor"] = colors.Primary;
        app.Resources["PrimaryHoverColor"] = colors.Hover;
        app.Resources["PrimaryLightColor"] = colors.Light;
        app.Resources["PrimaryDarkColor"] = colors.Dark;

        // Update primary brushes
        app.Resources["PrimaryBrush"] = new SolidColorBrush(colors.Primary);
        app.Resources["PrimaryHoverBrush"] = new SolidColorBrush(colors.Hover);
        app.Resources["PrimaryLightBrush"] = new SolidColorBrush(colors.Light);
        app.Resources["PrimaryDarkBrush"] = new SolidColorBrush(colors.Dark);

        // Update accent/secondary colors (used in gradients)
        app.Resources["AccentColor"] = colors.Secondary;
        app.Resources["AccentBrush"] = new SolidColorBrush(colors.Secondary);

        // Update sidebar active color (used for navigation buttons)
        app.Resources["SidebarActiveColor"] = colors.Primary;
        app.Resources["SidebarActiveBrush"] = new SolidColorBrush(colors.Primary);

        app.Resources["InputFocusBorderColor"] = colors.Primary;
        app.Resources["InputFocusBorderBrush"] = new SolidColorBrush(colors.Primary);

        // Update icon background colors (depends on current theme)
        var iconBgColor = IsDarkTheme ? colors.IconBgDark : colors.IconBgLight;
        app.Resources["PrimaryIconBgColor"] = iconBgColor;
        app.Resources["PrimaryIconBgBrush"] = new SolidColorBrush(iconBgColor);

        // Update FluentTheme Slider resources
        app.Resources["SliderThumbBackground"] = new SolidColorBrush(colors.Primary);
        app.Resources["SliderThumbBackgroundPointerOver"] = new SolidColorBrush(colors.Hover);
        app.Resources["SliderThumbBackgroundPressed"] = new SolidColorBrush(colors.Dark);
        app.Resources["SliderTrackValueFill"] = new SolidColorBrush(colors.Primary);
        app.Resources["SliderTrackValueFillPointerOver"] = new SolidColorBrush(colors.Hover);
        app.Resources["SliderTrackValueFillPressed"] = new SolidColorBrush(colors.Dark);
    }

    /// <summary>
    /// Initializes the theme service and sets up system theme detection.
    /// </summary>
    public void Initialize()
    {
        var app = Application.Current;
        if (app != null && !_systemThemeHookInstalled)
        {
            // Guarded: startup calls Initialize twice (before the splash, then again in
            // the async init path) and a second subscribe would double-fire.
            _systemThemeHookInstalled = true;
            app.ActualThemeVariantChanged += OnSystemThemeChanged;
        }

        // Load saved settings if available
        LoadFromSettings();

        ApplyTheme();
        ApplyAccentColor();
    }

    private void ApplyTheme()
    {
        var app = Application.Current;
        if (app == null)
            return;

        app.RequestedThemeVariant = CurrentTheme switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        // Reapply accent colors since icon backgrounds depend on theme
        ApplyAccentColor();
    }

    private void OnSystemThemeChanged(object? sender, EventArgs e)
    {
        // Only react if we're following the system theme
        if (CurrentTheme == ThemeMode.System)
        {
            // Icon-background brushes are theme-dependent and get baked into application resources by ApplyAccentColor using IsDarkTheme.
            ApplyAccentColor();
            ThemeChanged?.Invoke(this, CurrentTheme);
        }
    }
}
