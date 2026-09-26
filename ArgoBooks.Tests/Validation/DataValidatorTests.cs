using ArgoBooks.Core.Validation;
using Xunit;

namespace ArgoBooks.Tests.Validation;

/// <summary>
/// Tests for DataValidator.IsValidEmail.
/// </summary>
public class DataValidatorTests
{
    [Theory]
    [InlineData("test@example.com")]
    [InlineData("user.name@domain.org")]
    [InlineData("user+tag@company.co.uk")]
    [InlineData("firstname.lastname@sub.domain.com")]
    [InlineData("  padded@example.com  ")]
    public void IsValidEmail_ValidEmail_ReturnsTrue(string email)
    {
        Assert.True(DataValidator.IsValidEmail(email));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("@domain.com")]
    [InlineData("user@")]
    [InlineData("user@domain")]
    [InlineData("user domain.com")]
    [InlineData("user@@domain.com")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValidEmail_InvalidEmail_ReturnsFalse(string email)
    {
        Assert.False(DataValidator.IsValidEmail(email));
    }
}
