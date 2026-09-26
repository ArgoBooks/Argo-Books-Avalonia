using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.Integrations;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// A sync preview keeps each amount in its own currency with its date, so its totals are converted
/// row by row (Rule 3a) rather than adding dollars to euros.
/// </summary>
public class IntegrationPreviewAmountsTests
{
    private static ArgoRevenue Sale(long amount, string currency, string date) => new(
        Id: $"rev_{currency}_{amount}", Description: "Order",
        Amount: amount, Currency: currency, TaxAmount: 0, DiscountAmount: 0, FeeAmount: 0,
        OccurredOn: date, Customer: null, Category: null, PaymentMethod: null,
        Reference: null, Notes: null, LineItems: null,
        Import: new ArgoImportState("pending", null, null, null));

    private static ArgoApiSyncPreview Preview(params ArgoRevenue[] sales) =>
        new([], [], [], [], [], sales, [], new Dictionary<string, ArgoExternalRef>());

    [Fact]
    public void MixedCurrencySales_KeepTheirOwnCurrencyAndDate()
    {
        var preview = Preview(Sale(10000, "usd", "2026-08-01"), Sale(5000, "EUR", "2026-08-02"), Sale(1000, "JPY", "2026-08-03"));

        Assert.Equal(
            [
                new IncomingAmount(100m, "USD", new DateTime(2026, 8, 1)),
                new IncomingAmount(50m, "EUR", new DateTime(2026, 8, 2)),
                new IncomingAmount(1000m, "JPY", new DateTime(2026, 8, 3))
            ],
            preview.Sales);
    }

    [Fact]
    public void TheTotal_ConvertsEachAmountAtItsOwnDate()
    {
        var preview = Preview(Sale(10000, "USD", "2026-08-01"), Sale(5000, "EUR", "2026-08-02"));
        decimal? ToUsd(decimal amount, string currency, DateTime date) => currency == "EUR" ? amount * 1.1m : amount;

        var complete = DisplayCurrency.TrySumFromNative(preview.Sales, a => a.Amount, a => a.Currency, a => a.Date, ToUsd, out var total);

        Assert.True(complete);
        Assert.Equal(155m, total);   // 100 + 50 × 1.1, not the 150 a raw sum gives
    }

    [Fact]
    public void TheTotal_IsIncompleteWhileARateIsMissing()
    {
        var preview = Preview(Sale(10000, "USD", "2026-08-01"), Sale(5000, "EUR", "2026-08-02"));
        decimal? ToUsd(decimal amount, string currency, DateTime date) => currency == "EUR" ? null : amount;

        Assert.False(DisplayCurrency.TrySumFromNative(preview.Sales, a => a.Amount, a => a.Currency, a => a.Date, ToUsd, out _));
    }
}
