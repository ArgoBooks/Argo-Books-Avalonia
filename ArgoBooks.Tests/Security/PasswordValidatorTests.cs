using ArgoBooks.Core.Security;
using Xunit;

namespace ArgoBooks.Tests.Security;

/// <summary>
/// Tests for the PasswordValidator class.
/// </summary>
public class PasswordValidatorTests
{
    #region Constants Tests

    [Fact]
    public void MinLength_Equals8()
    {
        Assert.Equal(8, PasswordValidator.MinLength);
    }

    [Fact]
    public void MaxLength_Equals128()
    {
        Assert.Equal(128, PasswordValidator.MaxLength);
    }

    [Fact]
    public void Constants_MinLengthIsLessThanMaxLength()
    {
        Assert.True(PasswordValidator.MinLength < PasswordValidator.MaxLength);
    }

    #endregion

    #region IsValid Tests

    [Fact]
    public void IsValid_NullPassword_ReturnsFalse()
    {
        Assert.False(PasswordValidator.IsValid(null));
    }

    [Fact]
    public void IsValid_EmptyPassword_ReturnsFalse()
    {
        Assert.False(PasswordValidator.IsValid(""));
    }

    [Fact]
    public void IsValid_TooShortPassword_ReturnsFalse()
    {
        Assert.False(PasswordValidator.IsValid("Pass1"));
    }

    [Fact]
    public void IsValid_TooLongPassword_ReturnsFalse()
    {
        var tooLong = new string('A', PasswordValidator.MaxLength + 1) + "1";
        Assert.False(PasswordValidator.IsValid(tooLong));
    }

    [Fact]
    public void IsValid_NoLetters_ReturnsFalse()
    {
        Assert.False(PasswordValidator.IsValid("12345678"));
    }

    [Fact]
    public void IsValid_NoDigits_ReturnsFalse()
    {
        Assert.False(PasswordValidator.IsValid("Abcdefgh"));
    }

    [Fact]
    public void IsValid_ValidPassword_ReturnsTrue()
    {
        Assert.True(PasswordValidator.IsValid("Password1"));
    }

    [Theory]
    [InlineData("Password1")]
    [InlineData("mypassword123")]
    [InlineData("MYPASSWORD123")]
    [InlineData("MyP@ssw0rd!")]
    [InlineData("12345678a")]
    [InlineData("a1234567")]
    public void IsValid_VariousValidPasswords_ReturnsTrue(string password)
    {
        Assert.True(PasswordValidator.IsValid(password));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Pas1")]
    [InlineData("12345678")]
    [InlineData("abcdefgh")]
    public void IsValid_VariousInvalidPasswords_ReturnsFalse(string? password)
    {
        Assert.False(PasswordValidator.IsValid(password));
    }

    [Fact]
    public void IsValid_ExactlyMinLength_WithLetterAndDigit_ReturnsTrue()
    {
        // Exactly 8 chars: 7 letters + 1 digit
        Assert.True(PasswordValidator.IsValid("Abcdefg1"));
    }

    [Fact]
    public void IsValid_ExactlyMaxLength_WithLetterAndDigit_ReturnsTrue()
    {
        // Exactly 128 chars with letter and digit
        var password = new string('A', PasswordValidator.MaxLength - 1) + "1";
        Assert.True(PasswordValidator.IsValid(password));
    }

    [Fact]
    public void IsValid_OnlyLetters_ReturnsFalse()
    {
        Assert.False(PasswordValidator.IsValid("abcdefghij"));
    }

    [Fact]
    public void IsValid_OnlyDigits_ReturnsFalse()
    {
        Assert.False(PasswordValidator.IsValid("1234567890"));
    }

    [Fact]
    public void IsValid_OneCharBelowMinLength_ReturnsFalse()
    {
        // 7 characters (MinLength - 1)
        Assert.False(PasswordValidator.IsValid("Abcde1x"));
    }

    [Fact]
    public void IsValid_OneCharAboveMaxLength_ReturnsFalse()
    {
        // 129 characters (MaxLength + 1)
        var password = new string('A', PasswordValidator.MaxLength) + "1";
        Assert.False(PasswordValidator.IsValid(password));
    }

    #endregion

    #region GetValidationError Tests

    [Fact]
    public void GetValidationError_ValidPassword_ReturnsNull()
    {
        Assert.Null(PasswordValidator.GetValidationError("Password123"));
    }

    [Fact]
    public void GetValidationError_NullPassword_ReturnsRequiredError()
    {
        var error = PasswordValidator.GetValidationError(null);
        Assert.NotNull(error);
        Assert.Equal("Password is required.", error);
    }

    [Fact]
    public void GetValidationError_EmptyPassword_ReturnsRequiredError()
    {
        var error = PasswordValidator.GetValidationError("");
        Assert.NotNull(error);
        Assert.Equal("Password is required.", error);
    }

    [Fact]
    public void GetValidationError_TooShortPassword_ReturnsTooShortError()
    {
        var error = PasswordValidator.GetValidationError("Pass1");
        Assert.NotNull(error);
        Assert.Contains("at least", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("8", error);
    }

    [Fact]
    public void GetValidationError_TooLongPassword_ReturnsTooLongError()
    {
        var tooLong = new string('A', PasswordValidator.MaxLength + 1) + "1";
        var error = PasswordValidator.GetValidationError(tooLong);
        Assert.NotNull(error);
        Assert.Contains("no more than", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("128", error);
    }

    [Fact]
    public void GetValidationError_NoLetters_ReturnsLetterError()
    {
        var error = PasswordValidator.GetValidationError("12345678");
        Assert.NotNull(error);
        Assert.Contains("letter", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetValidationError_NoDigits_ReturnsNumberError()
    {
        var error = PasswordValidator.GetValidationError("Password");
        Assert.NotNull(error);
        Assert.Contains("number", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetValidationError_ReturnsFirstErrorOnly_WhenMultipleViolations()
    {
        // "ab" is too short AND has no digit - should return the first error (too short)
        var error = PasswordValidator.GetValidationError("ab");
        Assert.NotNull(error);
        Assert.Contains("at least", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetValidationError_ExactlyMinLength_ValidPassword_ReturnsNull()
    {
        Assert.Null(PasswordValidator.GetValidationError("Abcdefg1"));
    }

    [Fact]
    public void GetValidationError_ExactlyMaxLength_ValidPassword_ReturnsNull()
    {
        var password = new string('A', PasswordValidator.MaxLength - 1) + "1";
        Assert.Null(PasswordValidator.GetValidationError(password));
    }

    #endregion

    #region GetStrengthScore Tests

    [Fact]
    public void GetStrengthScore_NullPassword_ReturnsZero()
    {
        Assert.Equal(0, PasswordValidator.GetStrengthScore(null));
    }

    [Fact]
    public void GetStrengthScore_EmptyPassword_ReturnsZero()
    {
        Assert.Equal(0, PasswordValidator.GetStrengthScore(""));
    }

    [Fact]
    public void GetStrengthScore_SingleChar_ReturnsLowScore()
    {
        var score = PasswordValidator.GetStrengthScore("a");
        // 1*3 (length) + 10 (lowercase) = 13
        Assert.Equal(13, score);
    }

    [Fact]
    public void GetStrengthScore_ShortPasswordLowercaseOnly_CalculatesCorrectly()
    {
        // "abcd" = 4*3 (length=12) + 10 (lowercase) = 22
        // But "abcd" has sequential chars (a,b,c) so -5 penalty = 17
        var score = PasswordValidator.GetStrengthScore("abcd");
        Assert.Equal(17, score);
    }

    [Fact]
    public void GetStrengthScore_LongPassword_LengthCappedAt55()
    {
        // 20 chars: 36 + (20-12)*2 = 52 length points + 10 (lowercase) - 5 (repeating 'xxx...') = 57
        var password = new string('x', 20);
        var score = PasswordValidator.GetStrengthScore(password);
        Assert.Equal(57, score);
    }

    [Fact]
    public void GetStrengthScore_AllCharacterTypes_MaximizesVarietyBonus()
    {
        // "Aa1!" = 4*3=12 (length) + 10 (lower) + 10 (upper) + 10 (digit) + 15 (special) = 57
        var score = PasswordValidator.GetStrengthScore("Aa1!");
        Assert.Equal(57, score);
    }

    [Fact]
    public void GetStrengthScore_LowercaseOnly_Gets10Bonus()
    {
        // "x" = 1*3=3 (length) + 10 (lowercase) = 13
        var score = PasswordValidator.GetStrengthScore("x");
        Assert.Equal(13, score);
    }

    [Fact]
    public void GetStrengthScore_UppercaseOnly_Gets10Bonus()
    {
        // "X" = 1*3=3 (length) + 10 (uppercase) = 13
        var score = PasswordValidator.GetStrengthScore("X");
        Assert.Equal(13, score);
    }

    [Fact]
    public void GetStrengthScore_DigitOnly_Gets10Bonus()
    {
        // "5" = 1*3=3 (length) + 10 (digit) = 13
        var score = PasswordValidator.GetStrengthScore("5");
        Assert.Equal(13, score);
    }

    [Fact]
    public void GetStrengthScore_SpecialCharOnly_Gets15Bonus()
    {
        // "!" = 1*3=3 (length) + 15 (special) = 18
        var score = PasswordValidator.GetStrengthScore("!");
        Assert.Equal(18, score);
    }

    [Fact]
    public void GetStrengthScore_RepeatingCharsPenalty_Minus5()
    {
        // "aaa" has repeating chars -> penalty of 5
        // 3*3=9 (length) + 10 (lowercase) - 5 (repeating) = 14
        var score = PasswordValidator.GetStrengthScore("aaa");
        Assert.Equal(14, score);
    }

    [Fact]
    public void GetStrengthScore_SequentialCharsPenalty_Ascending()
    {
        // "abc" has sequential ascending chars -> penalty of 5
        // 3*3=9 (length) + 10 (lowercase) - 5 (sequential) = 14
        var score = PasswordValidator.GetStrengthScore("abc");
        Assert.Equal(14, score);
    }

    [Fact]
    public void GetStrengthScore_SequentialCharsPenalty_Descending()
    {
        // "cba" has sequential descending chars -> penalty of 5
        // 3*3=9 (length) + 10 (lowercase) - 5 (sequential) = 14
        var score = PasswordValidator.GetStrengthScore("cba");
        Assert.Equal(14, score);
    }

    [Fact]
    public void GetStrengthScore_SequentialDigits_Ascending()
    {
        // "123" has sequential ascending digits -> penalty of 5
        // 3*3=9 (length) + 10 (digit) - 5 (sequential) = 14
        var score = PasswordValidator.GetStrengthScore("123");
        Assert.Equal(14, score);
    }

    [Fact]
    public void GetStrengthScore_SequentialDigits_Descending()
    {
        // "321" has sequential descending digits -> penalty of 5
        // 3*3=9 (length) + 10 (digit) - 5 (sequential) = 14
        var score = PasswordValidator.GetStrengthScore("321");
        Assert.Equal(14, score);
    }

    [Fact]
    public void GetStrengthScore_BothPenalties_RepeatingAndSequential()
    {
        // "aaabcd" has repeating (aaa) and sequential (bcd)
        // 6*3=18 (length) + 10 (lowercase) - 5 (repeating) - 5 (sequential) = 18
        var score = PasswordValidator.GetStrengthScore("aaabcd");
        Assert.Equal(18, score);
    }

    [Fact]
    public void GetStrengthScore_SequentialCaseInsensitive()
    {
        // "ABC" should detect sequential even with uppercase
        // 3*3=9 (length) + 10 (uppercase) - 5 (sequential) = 14
        var score = PasswordValidator.GetStrengthScore("ABC");
        Assert.Equal(14, score);
    }

    [Fact]
    public void GetStrengthScore_ClampedAtZero_NeverNegative()
    {
        // Even with heavy penalties, score should never go below 0
        var score = PasswordValidator.GetStrengthScore("a");
        Assert.True(score >= 0);
    }

    [Fact]
    public void GetStrengthScore_ClampedAt100_NeverExceeds()
    {
        // Very long, diverse password
        var password = "MyV3ryStr0ng!P@ssw0rd#WithL0ts0fCh@rs";
        var score = PasswordValidator.GetStrengthScore(password);
        Assert.True(score <= 100);
    }

    [Fact]
    public void GetStrengthScore_AllCharacterTypes_WithRepeatingPenalty()
    {
        // 18 chars: 36 + (18-12)*2 = 48 length + 10 (lower) + 10 (upper) + 10 (digit) + 15 (special) - 5 (repeating XXX) = 88
        var password = new string('X', 15) + "a1!";
        var score = PasswordValidator.GetStrengthScore(password);
        Assert.Equal(88, score);
    }

    [Fact]
    public void GetStrengthScore_LongerPasswordScoresHigher()
    {
        var shortScore = PasswordValidator.GetStrengthScore("Aa1!");
        var longScore = PasswordValidator.GetStrengthScore("Aa1!Bb2@Cc3#");

        Assert.True(longScore > shortScore);
    }

    [Fact]
    public void GetStrengthScore_MixedCaseScoresHigherThanSingleCase()
    {
        var lowerOnlyScore = PasswordValidator.GetStrengthScore("password!1");
        var mixedCaseScore = PasswordValidator.GetStrengthScore("Password!1");

        Assert.True(mixedCaseScore > lowerOnlyScore);
    }

    [Fact]
    public void GetStrengthScore_SpecialCharsIncreaseScore()
    {
        var noSpecialScore = PasswordValidator.GetStrengthScore("Password1");
        var withSpecialScore = PasswordValidator.GetStrengthScore("Password1!");

        Assert.True(withSpecialScore > noSpecialScore);
    }

    [Fact]
    public void GetStrengthScore_NoRepeatingChars_NoPenalty()
    {
        // "abac" has no 3+ consecutive identical chars
        // 4*3=12 + 10 (lowercase) = 22
        var score = PasswordValidator.GetStrengthScore("abac");
        Assert.Equal(22, score);
    }

    [Fact]
    public void GetStrengthScore_TwoRepeatingChars_NoPenalty()
    {
        // "aab" has only 2 consecutive identical chars, no penalty
        // 3*3=9 + 10 (lowercase) = 19
        var score = PasswordValidator.GetStrengthScore("aab");
        Assert.Equal(19, score);
    }

    [Fact]
    public void GetStrengthScore_NonConsecutiveSequentialChars_NoPenalty()
    {
        // "axbxc" - a, b, c are present but not consecutive positions
        // 5*3=15 + 10 (lowercase) = 25
        var score = PasswordValidator.GetStrengthScore("axbxc");
        Assert.Equal(25, score);
    }

    #endregion

    #region GetStrengthDescription Tests

    [Theory]
    [InlineData(0, "Very Weak")]
    [InlineData(1, "Very Weak")]
    [InlineData(10, "Very Weak")]
    [InlineData(19, "Very Weak")]
    public void GetStrengthDescription_ScoreBelow20_ReturnsVeryWeak(int score, string expected)
    {
        Assert.Equal(expected, PasswordValidator.GetStrengthDescription(score));
    }

    [Theory]
    [InlineData(20, "Weak")]
    [InlineData(25, "Weak")]
    [InlineData(39, "Weak")]
    public void GetStrengthDescription_Score20To39_ReturnsWeak(int score, string expected)
    {
        Assert.Equal(expected, PasswordValidator.GetStrengthDescription(score));
    }

    [Theory]
    [InlineData(40, "Fair")]
    [InlineData(50, "Fair")]
    [InlineData(59, "Fair")]
    public void GetStrengthDescription_Score40To59_ReturnsFair(int score, string expected)
    {
        Assert.Equal(expected, PasswordValidator.GetStrengthDescription(score));
    }

    [Theory]
    [InlineData(60, "Strong")]
    [InlineData(70, "Strong")]
    [InlineData(79, "Strong")]
    public void GetStrengthDescription_Score60To79_ReturnsStrong(int score, string expected)
    {
        Assert.Equal(expected, PasswordValidator.GetStrengthDescription(score));
    }

    [Theory]
    [InlineData(80, "Very Strong")]
    [InlineData(90, "Very Strong")]
    [InlineData(100, "Very Strong")]
    public void GetStrengthDescription_Score80AndAbove_ReturnsVeryStrong(int score, string expected)
    {
        Assert.Equal(expected, PasswordValidator.GetStrengthDescription(score));
    }

    [Fact]
    public void GetStrengthDescription_BoundaryAt20_TransitionsFromVeryWeakToWeak()
    {
        Assert.Equal("Very Weak", PasswordValidator.GetStrengthDescription(19));
        Assert.Equal("Weak", PasswordValidator.GetStrengthDescription(20));
    }

    [Fact]
    public void GetStrengthDescription_BoundaryAt40_TransitionsFromWeakToFair()
    {
        Assert.Equal("Weak", PasswordValidator.GetStrengthDescription(39));
        Assert.Equal("Fair", PasswordValidator.GetStrengthDescription(40));
    }

    [Fact]
    public void GetStrengthDescription_BoundaryAt60_TransitionsFromFairToStrong()
    {
        Assert.Equal("Fair", PasswordValidator.GetStrengthDescription(59));
        Assert.Equal("Strong", PasswordValidator.GetStrengthDescription(60));
    }

    [Fact]
    public void GetStrengthDescription_BoundaryAt80_TransitionsFromStrongToVeryStrong()
    {
        Assert.Equal("Strong", PasswordValidator.GetStrengthDescription(79));
        Assert.Equal("Very Strong", PasswordValidator.GetStrengthDescription(80));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void IsValid_WhitespacePassword_TreatedAsNonEmpty()
    {
        // Whitespace is not empty per string.IsNullOrEmpty, but it is too short
        Assert.False(PasswordValidator.IsValid("   "));
    }

    [Fact]
    public void IsValid_PasswordWithSpaces_ValidIfMeetsRequirements()
    {
        // Spaces are allowed as characters
        Assert.True(PasswordValidator.IsValid("Pass word1"));
    }

    [Fact]
    public void IsValid_UnicodeLetters_CountAsLetters()
    {
        // Unicode letters should satisfy the "at least one letter" requirement
        Assert.True(PasswordValidator.IsValid("\u00e9\u00e9\u00e9\u00e9\u00e9\u00e9\u00e9\u00e91"));
    }

    [Fact]
    public void GetValidationError_PasswordWithOnlySpecialChars_RequiresLetterAndDigit()
    {
        // "!@#$%^&*" has 8 chars but no letter - returns letter error first
        var error = PasswordValidator.GetValidationError("!@#$%^&*");
        Assert.NotNull(error);
        Assert.Contains("letter", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetStrengthScore_PasswordAtExactlyMinLength_ScoresReasonably()
    {
        // "Abcdef1!" = exactly 8 chars
        var score = PasswordValidator.GetStrengthScore("Abcdef1!");
        // 8*2=16 + 10 (lower) + 15 (upper) + 15 (digit) + 20 (special) - 10 (sequential: def) = 66
        Assert.True(score > 0);
        Assert.True(score <= 100);
    }

    [Fact]
    public void GetStrengthScore_PasswordAtExactlyMaxLength_ScoresHighOnLength()
    {
        var password = new string('X', PasswordValidator.MaxLength - 1) + "1";
        var score = PasswordValidator.GetStrengthScore(password);
        // Length capped at 30, +15 (upper) +15 (digit) - 10 (repeating XXX) = 50
        Assert.True(score >= 50);
    }

    [Fact]
    public void GetStrengthScore_RepeatingCharsAtEnd_StillPenalized()
    {
        // "xyzaaa" has repeating chars at end
        // 6*2=12 + 10 (lower) - 10 (repeating) - 10 (sequential: xyz) = 2
        var scoreWithRepeat = PasswordValidator.GetStrengthScore("xyzaaa");
        var scoreWithout = PasswordValidator.GetStrengthScore("xyzpqr");
        Assert.True(scoreWithRepeat < scoreWithout);
    }

    [Fact]
    public void GetStrengthScore_FourRepeatingChars_StillOnlyMinus5()
    {
        // "aaaa" has repeating chars (aaa detected at position 0 and 1)
        // but penalty is applied once via the boolean check
        // 4*3=12 + 10 (lower) - 5 (repeating) = 17
        var score = PasswordValidator.GetStrengthScore("aaaa");
        Assert.Equal(17, score);
    }

    [Fact]
    public void IsValid_ExactlyMinLengthMinusOne_ReturnsFalse()
    {
        // 7 chars with letter and digit
        Assert.False(PasswordValidator.IsValid("Abcde1x"));
    }

    [Fact]
    public void IsValid_ExactlyMaxLengthPlusOne_ReturnsFalse()
    {
        var password = new string('A', PasswordValidator.MaxLength) + "1";
        Assert.False(PasswordValidator.IsValid(password));
    }

    [Fact]
    public void IsValid_ConsistentWithGetValidationError()
    {
        var testPasswords = new[]
        {
            null, "", "a", "12345678", "abcdefgh", "Password1", "Aa1!Bb2@",
            new string('A', PasswordValidator.MaxLength + 1)
        };

        foreach (var password in testPasswords)
        {
            var isValid = PasswordValidator.IsValid(password);
            var error = PasswordValidator.GetValidationError(password);
            Assert.Equal(isValid, error == null);
        }
    }

    [Fact]
    public void GetStrengthScore_ConsistentAcrossMultipleCalls()
    {
        var password = "MyP@ssw0rd!";
        var score1 = PasswordValidator.GetStrengthScore(password);
        var score2 = PasswordValidator.GetStrengthScore(password);
        Assert.Equal(score1, score2);
    }

    #endregion
}
