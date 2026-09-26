using System.Text.RegularExpressions;

namespace ArgoBooks.Core.Validation;

/// <summary>
/// Validates data formats.
/// </summary>
public static partial class DataValidator
{
    /// <summary>
    /// Validates an email address format. Canonical email check used across the app:
    /// requires non-empty local part, '@', non-empty domain, '.', and non-empty TLD.
    /// </summary>
    public static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        return EmailRegex().IsMatch(email.Trim());
    }

    /// <summary>
    /// <see cref="IsValidEmail"/> for an address in a form that opened with <paramref name="storedEmail"/>
    /// already filled in. The address on file, left as it was, always passes: one saved before the check
    /// was this strict must not stop the user saving other changes or sending to it.
    /// </summary>
    public static bool IsValidOrUnchangedEmail(string? email, string? storedEmail) =>
        string.Equals((email ?? string.Empty).Trim(), (storedEmail ?? string.Empty).Trim(), StringComparison.Ordinal)
        || IsValidEmail(email ?? string.Empty);

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();
}
