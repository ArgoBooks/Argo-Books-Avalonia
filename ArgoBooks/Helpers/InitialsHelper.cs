namespace ArgoBooks.Helpers;

/// <summary>
/// The one rule for avatar initials: the first letter of the first and last words, or the
/// first two letters of a single word.
/// </summary>
public static class InitialsHelper
{
    public static string From(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "?";

        var words = name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var initials = words.Length >= 2
            ? $"{words[0][0]}{words[^1][0]}"
            : words[0][..Math.Min(2, words[0].Length)];
        return initials.ToUpperInvariant();
    }

    public static string From(string? firstName, string? lastName) => From($"{firstName} {lastName}");
}
