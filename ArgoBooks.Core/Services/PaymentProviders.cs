namespace ArgoBooks.Core.Services;

/// <summary>The names the payment portal server uses for each provider, in requests and in what it sends back.</summary>
public static class PaymentProviders
{
    public const string Stripe = "stripe";
    public const string PayPal = "paypal";
    public const string Square = "square";
}
