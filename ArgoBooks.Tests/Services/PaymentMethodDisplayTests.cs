using ArgoBooks.Core.Enums;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The transaction forms show payment methods by display name and store them as the enum, so the
/// two have to survive a round trip. They did not: the form loaded a saved value with ToString(),
/// giving "BankTransfer", while the dropdown holds "Bank Transfer". Nothing in the list matched, so
/// the box cleared itself and the save threw the choice away. It broke exactly the methods whose
/// display name is two words, which is why it looked arbitrary rather than total.
/// </summary>
public class PaymentMethodDisplayTests
{
    /// <summary>
    /// The invariant the forms rely on. Every method has to come back from its own display name,
    /// so adding one with a space in it cannot quietly break saving again.
    /// </summary>
    [Fact]
    public void EveryMethodSurvivesADisplayNameRoundTrip()
    {
        foreach (var method in Enum.GetValues<PaymentMethod>())
        {
            Assert.Equal(method, PaymentMethodExtensions.ParseDisplayName(method.GetDisplayName()));
        }
    }

    /// <summary>
    /// The dropdown is built from GetCommonOptions, and the form selects by matching this list.
    /// A display name missing from it cannot be selected, which is the shape of the original bug.
    /// </summary>
    [Theory]
    [InlineData(PaymentMethod.BankTransfer)]
    [InlineData(PaymentMethod.CreditCard)]
    [InlineData(PaymentMethod.DebitCard)]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.Check)]
    [InlineData(PaymentMethod.PayPal)]
    [InlineData(PaymentMethod.Other)]
    public void TheOfferedOptionsContainEachMethodsDisplayName(PaymentMethod method)
    {
        Assert.Contains(method.GetDisplayName(), PaymentMethodExtensions.GetCommonOptions());
    }

    /// <summary>
    /// The defect itself, stated as an assertion. The form used to load a saved value with
    /// ToString(), and for these three that string is not among the options the dropdown offers,
    /// so the box could not select it, cleared itself, and the save lost the choice. Anything
    /// that puts an enum name into that box is wrong, and this says why.
    /// </summary>
    [Theory]
    [InlineData(PaymentMethod.BankTransfer, "Bank Transfer")]
    [InlineData(PaymentMethod.CreditCard, "Credit Card")]
    [InlineData(PaymentMethod.DebitCard, "Debit Card")]
    public void AMultiWordMethodsEnumNameIsNotSelectable(PaymentMethod method, string expected)
    {
        Assert.Equal(expected, method.GetDisplayName());
        Assert.DoesNotContain(method.ToString(), PaymentMethodExtensions.GetCommonOptions());
        Assert.Contains(method.GetDisplayName(), PaymentMethodExtensions.GetCommonOptions());
    }

    /// <summary>
    /// The save path reads whatever the dropdown holds, and a cleared dropdown hands over null.
    /// Falling back rather than throwing is what stops an unrecognised value losing the record.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Something we have never offered")]
    public void AnUnusableValueFallsBackInsteadOfThrowing(string? value)
    {
        Assert.Equal(PaymentMethod.Cash, PaymentMethodExtensions.ParseDisplayName(value));
    }

    /// <summary>Case differences come back from saved files and imports, so matching ignores them.</summary>
    [Theory]
    [InlineData("bank transfer")]
    [InlineData("BANK TRANSFER")]
    public void MatchingIgnoresCase(string value)
    {
        Assert.Equal(PaymentMethod.BankTransfer, PaymentMethodExtensions.ParseDisplayName(value));
    }
}
