using SkiaSharp;

namespace ArgoBooks.Core.Services;

/// <summary>
/// The UI font, resolved for whatever platform is running.
///
/// Asking Skia for "Segoe UI" by name does not fail anywhere: off Windows it quietly hands
/// back the system default instead, so charts and reports rendered on macOS or Linux came out
/// in a font nobody chose, with different metrics from the Windows original. This walks a
/// candidate list per platform and only falls back once nothing matches.
/// </summary>
public static class PlatformTypefaces
{
    // In priority order. Segoe UI first so Windows is unchanged, then the macOS system font,
    // then the two faces that ship with essentially every desktop Linux.
    private static readonly string[] Candidates =
        ["Segoe UI", ".AppleSystemUIFont", "San Francisco", "Helvetica Neue", "DejaVu Sans", "Liberation Sans", "Noto Sans"];

    /// <summary>The default UI typeface. Cached: resolving walks the whole font list.</summary>
    public static SKTypeface Default { get; } = Resolve();

    /// <summary>The bold weight of <see cref="Default"/>.</summary>
    public static SKTypeface Bold { get; } = Resolve(SKFontStyle.Bold);

    /// <summary>
    /// Resolves a named family, falling back to the platform default when the name is not
    /// installed. Use for a font the user picked, which may well be a Windows-only one.
    /// </summary>
    public static SKTypeface Resolve(string? familyName, SKFontStyle? style = null)
    {
        if (string.IsNullOrWhiteSpace(familyName))
            return style != null ? Resolve(style) : Default;

        var typeface = style != null
            ? SKTypeface.FromFamilyName(familyName, style)
            : SKTypeface.FromFamilyName(familyName);

        // A miss returns the system default rather than null, so a name that did not resolve
        // is spotted by comparing families rather than by a null check.
        if (typeface != null && IsResolvedMatch(familyName, typeface.FamilyName, SKTypeface.Default.FamilyName))
            return typeface;

        return style != null ? Resolve(style) : Default;
    }

    private static SKTypeface Resolve(SKFontStyle? style = null)
    {
        foreach (var candidate in Candidates)
        {
            var typeface = style != null
                ? SKTypeface.FromFamilyName(candidate, style)
                : SKTypeface.FromFamilyName(candidate);

            if (typeface != null && IsResolvedMatch(candidate, typeface.FamilyName, SKTypeface.Default.FamilyName))
                return typeface;
        }

        return style != null
            ? SKTypeface.FromFamilyName(null, style) ?? SKTypeface.Default
            : SKTypeface.Default;
    }

    /// <summary>
    /// Whether a font asked for by name was really found. A miss comes back as the system default,
    /// but on Windows the default is Segoe UI itself, so landing on the default only means a miss
    /// when the default is not what was asked for. macOS aliases like .AppleSystemUIFont report
    /// another name, which is why anything other than the default also counts.
    /// </summary>
    internal static bool IsResolvedMatch(string requested, string resolvedFamily, string systemDefaultFamily) =>
        string.Equals(resolvedFamily, requested, StringComparison.OrdinalIgnoreCase)
        || resolvedFamily != systemDefaultFamily;
}
