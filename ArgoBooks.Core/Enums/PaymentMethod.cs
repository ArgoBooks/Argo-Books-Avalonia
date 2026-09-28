namespace ArgoBooks.Core.Enums;

/// <summary>
/// Payment method used for transactions.
/// </summary>
public enum PaymentMethod
{
    /// <summary>Cash payment.</summary>
    Cash,

    /// <summary>Credit card payment.</summary>
    CreditCard,

    /// <summary>Debit card payment.</summary>
    DebitCard,

    /// <summary>Bank transfer payment.</summary>
    BankTransfer,

    /// <summary>Check payment.</summary>
    Check,

    /// <summary>PayPal payment.</summary>
    PayPal,

    /// <summary>Stripe payment.</summary>
    Stripe,

    /// <summary>Square payment.</summary>
    Square,

    /// <summary>Other payment method.</summary>
    Other
}

/// <summary>
/// Extension methods for PaymentMethod.
/// </summary>
public static class PaymentMethodExtensions
{
    public static string GetDisplayName(this PaymentMethod method)
    {
        return method switch
        {
            PaymentMethod.BankTransfer => "Bank Transfer",
            PaymentMethod.CreditCard => "Credit Card",
            PaymentMethod.DebitCard => "Debit Card",
            PaymentMethod.Stripe => "Stripe",
            _ => method.ToString()
        };
    }

    /// <summary>
    /// Parses a display name back to the PaymentMethod enum. Returns Cash if unrecognized.
    /// </summary>
    public static PaymentMethod ParseDisplayName(string? displayName)
    {
        foreach (var method in Enum.GetValues<PaymentMethod>())
        {
            if (string.Equals(method.GetDisplayName(), displayName, StringComparison.OrdinalIgnoreCase))
                return method;
        }
        return PaymentMethod.Cash;
    }

    /// <summary>
    /// Gets the common payment method options for transaction forms.
    /// </summary>
    public static string[] GetCommonOptions()
    {
        return
        [
            PaymentMethod.Cash.GetDisplayName(),
            PaymentMethod.CreditCard.GetDisplayName(),
            PaymentMethod.DebitCard.GetDisplayName(),
            PaymentMethod.BankTransfer.GetDisplayName(),
            PaymentMethod.Check.GetDisplayName(),
            PaymentMethod.PayPal.GetDisplayName(),
            PaymentMethod.Other.GetDisplayName()
        ];
    }
}
