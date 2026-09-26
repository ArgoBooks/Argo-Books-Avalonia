namespace ArgoBooks.Core.Security;

/// <summary>
/// Validates password strength requirements.
/// </summary>
public static class PasswordValidator
{
    /// <summary>
    /// Minimum password length.
    /// </summary>
    public const int MinLength = 8;

    /// <summary>
    /// Maximum password length.
    /// </summary>
    public const int MaxLength = 128;

    /// <summary>
    /// Validates that a password meets the minimum requirements.
    /// </summary>
    /// <param name="password">Password to validate.</param>
    /// <returns>True if password meets requirements.</returns>
    public static bool IsValid(string? password)
    {
        return GetValidationError(password) == null;
    }

    /// <summary>
    /// Gets the validation error for a password, or null if valid.
    /// </summary>
    /// <param name="password">Password to validate.</param>
    /// <returns>Error message or null if valid.</returns>
    public static string? GetValidationError(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return "Password is required.";

        if (password.Length < MinLength)
            return $"Password must be at least {MinLength} characters long.";

        if (password.Length > MaxLength)
            return $"Password must be no more than {MaxLength} characters long.";

        // Check for at least one letter
        if (!password.Any(char.IsLetter))
            return "Password must contain at least one letter.";

        // Check for at least one digit
        if (!password.Any(char.IsDigit))
            return "Password must contain at least one number.";

        return null;
    }

    /// <summary>
    /// Calculates a password strength score (0-100).
    /// </summary>
    /// <param name="password">Password to evaluate.</param>
    /// <returns>Strength score from 0 (weak) to 100 (strong).</returns>
    public static int GetStrengthScore(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return 0;

        var score = 0;

        // Length scoring (up to 55 points): strong growth to 12 chars, then gradual
        var lengthPoints = password.Length <= 12
            ? password.Length * 3
            : 36 + (password.Length - 12) * 2;
        score += Math.Min(lengthPoints, 55);

        // Character variety scoring (up to 45 points)
        if (password.Any(char.IsLower))
            score += 10;
        if (password.Any(char.IsUpper))
            score += 10;
        if (password.Any(char.IsDigit))
            score += 10;
        if (password.Any(c => !char.IsLetterOrDigit(c)))
            score += 15;

        // Minor penalty for common patterns
        if (HasRepeatingCharacters(password))
            score -= 5;
        if (HasSequentialCharacters(password))
            score -= 5;

        return Math.Clamp(score, 0, 100);
    }

    /// <summary>
    /// Gets a strength description based on the score.
    /// </summary>
    /// <param name="score">Strength score (0-100).</param>
    /// <returns>Strength description.</returns>
    public static string GetStrengthDescription(int score)
    {
        return score switch
        {
            < 20 => "Very Weak",
            < 40 => "Weak",
            < 60 => "Fair",
            < 80 => "Strong",
            _ => "Very Strong"
        };
    }

    private static bool HasRepeatingCharacters(string password)
    {
        for (var i = 0; i < password.Length - 2; i++)
        {
            if (password[i] == password[i + 1] && password[i + 1] == password[i + 2])
                return true;
        }
        return false;
    }

    private static bool HasSequentialCharacters(string password)
    {
        var lowerPassword = password.ToLowerInvariant();
        for (var i = 0; i < lowerPassword.Length - 2; i++)
        {
            var c1 = lowerPassword[i];
            var c2 = lowerPassword[i + 1];
            var c3 = lowerPassword[i + 2];

            // Check for ascending sequence (abc, 123)
            if (c2 == c1 + 1 && c3 == c2 + 1)
                return true;

            // Check for descending sequence (cba, 321)
            if (c2 == c1 - 1 && c3 == c2 - 1)
                return true;
        }
        return false;
    }
}
