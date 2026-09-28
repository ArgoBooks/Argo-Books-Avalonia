using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.Helpers;

/// <summary>
/// Helper class for responsive header layout calculations.
/// Provides properties that adapt based on available width.
/// </summary>
public partial class ResponsiveHeaderHelper : ObservableObject
{
    // Breakpoints
    private const double CompactBreakpoint = 750;
    private const double MediumBreakpoint = 950;

    /// <summary>
    /// Current width of the header area.
    /// </summary>
    [ObservableProperty]
    private double _headerWidth;

    /// <summary>
    /// Whether the header is in compact mode (icon-only buttons).
    /// </summary>
    [ObservableProperty]
    private bool _isCompactMode;

    /// <summary>
    /// Whether the header is in medium mode (smaller text).
    /// </summary>
    [ObservableProperty]
    private bool _isMediumMode;

    /// <summary>
    /// Whether to show button text (false in compact mode).
    /// </summary>
    [ObservableProperty]
    private bool _showButtonText = true;

    /// <summary>
    /// Width of the search box.
    /// </summary>
    [ObservableProperty]
    private double _searchBoxWidth = 250;

    /// <summary>
    /// Spacing between header controls.
    /// </summary>
    [ObservableProperty]
    private double _headerSpacing = 12;

    /// <summary>
    /// Search icon margin.
    /// </summary>
    [ObservableProperty]
    private Thickness _searchIconMargin = new(12, 0, 8, 0);

    /// <summary>
    /// Header padding.
    /// </summary>
    [ObservableProperty]
    private Thickness _headerPadding = new(24, 20);

    /// <summary>
    /// Minimum height for the search box to match button heights.
    /// </summary>
    [ObservableProperty]
    private double _searchBoxMinHeight = 40;

    partial void OnHeaderWidthChanged(double value)
    {
        UpdateResponsiveValues(value);
    }

    private void UpdateResponsiveValues(double width)
    {
        if (width < CompactBreakpoint)
        {
            // Compact mode - icon-only buttons
            IsCompactMode = true;
            IsMediumMode = false;
            ShowButtonText = false;
            SearchBoxWidth = 200;
            HeaderSpacing = 6;
            SearchIconMargin = new Thickness(12, 0, 20, 0);
            HeaderPadding = new Thickness(24, 12);
            SearchBoxMinHeight = 30;
        }
        else if (width < MediumBreakpoint)
        {
            // Medium mode - smaller text
            IsCompactMode = false;
            IsMediumMode = true;
            ShowButtonText = true;
            SearchBoxWidth = 200;
            HeaderSpacing = 8;
            SearchIconMargin = new Thickness(12, 0, 20, 0);
            HeaderPadding = new Thickness(24, 16);
            SearchBoxMinHeight = 30;
        }
        else
        {
            // Full mode
            IsCompactMode = false;
            IsMediumMode = false;
            ShowButtonText = true;
            SearchBoxWidth = 250;
            HeaderSpacing = 12;
            SearchIconMargin = new Thickness(12, 0, 8, 0);
            HeaderPadding = new Thickness(24, 20);
            SearchBoxMinHeight = 36;
        }
    }
}
