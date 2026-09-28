namespace ArgoBooks.Core.Enums;

/// <summary>
/// Available accent colors for the application theme.
/// </summary>
public enum AccentColor
{
    Blue,
    Green,
    Purple,
    Pink,
    Orange,
    Teal
}

/// <summary>
/// Extension methods for AccentColor.
/// </summary>
public static class AccentColorExtensions
{
    /// <summary>
    /// Parses a color name string to an AccentColor enum value.
    /// </summary>
    public static AccentColor? ParseAccentColor(string? name)
    {
        return name switch
        {
            "Blue" => AccentColor.Blue,
            "Green" => AccentColor.Green,
            "Purple" => AccentColor.Purple,
            "Pink" => AccentColor.Pink,
            "Orange" => AccentColor.Orange,
            "Teal" => AccentColor.Teal,
            _ => null
        };
    }
}
