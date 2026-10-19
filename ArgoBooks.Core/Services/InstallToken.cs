using System.Text.RegularExpressions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// The 8-character referral token the website adds to a downloaded installer's name ("..._abcdef12.exe"), which ties an install back to the ad click that led to it.
/// </summary>
public static partial class InstallToken
{
    /// <summary>The file the Windows install step writes the token to, and the first-run report reads it from.</summary>
    public const string FileName = "install_token.txt";

    /// <summary>
    /// The token in a file name or its folder's name, or null when neither carries one.
    /// </summary>
    public static string? FromPath(string path)
    {
        foreach (var segment in new[] { Path.GetFileName(path), Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty) })
        {
            if (string.IsNullOrEmpty(segment))
                continue;
            var match = SuffixPattern().Match(segment);
            if (match.Success)
                return match.Groups[1].Value.ToLowerInvariant();
        }
        return null;
    }

    public static bool IsValid(string token) => TokenPattern().IsMatch(token);

    // A browser saving the same file twice adds " (1)" or "(1)" before the extension, which still carries the token.
    [GeneratedRegex(@"_([0-9a-f]{8})(?: ?\(\d+\))?(?:\.|$)", RegexOptions.IgnoreCase)]
    private static partial Regex SuffixPattern();

    [GeneratedRegex("^[0-9a-fA-F]{8}$")]
    private static partial Regex TokenPattern();
}
