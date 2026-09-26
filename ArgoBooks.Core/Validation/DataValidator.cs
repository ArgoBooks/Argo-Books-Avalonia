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

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();
}
