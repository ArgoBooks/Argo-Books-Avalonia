namespace ArgoBooks.Core.Services.InvoiceTemplates;

/// <summary>
/// The online payment processing fee added to an invoice's balance. The website charges the same
/// rate in calculate_invoice_processing_fee() in config/pricing.php, so a change here must be made
/// there too.
/// </summary>
public static class ProcessingFee
{
    /// <summary>Percent of the balance.</summary>
    public const decimal Percent = 2.90m;

    /// <summary>Flat part, in the invoice's own currency; the website converts its own from CAD.</summary>
    public const decimal Fixed = 0.30m;

    /// <summary>The fee on a balance, rounded to cents.</summary>
    public static decimal For(decimal amount) =>
        amount <= 0 ? 0m : Math.Round(amount * Percent / 100m + Fixed, 2, MidpointRounding.AwayFromZero);
}
