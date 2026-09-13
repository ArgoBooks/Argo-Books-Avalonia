using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Models.Integrations;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class BankMatchingStripePayoutTests
{
    [Fact]
    public void Deposit_MatchingRememberedPayout_IsAutoIgnored()
    {
        var data = new CompanyData();
        data.Settings.Integrations.Stripe.ImportedPayouts.Add(
            new StripePayoutRecord { StripePayoutId = "po_1", AmountCents = 4825, Date = new DateTime(2026, 1, 15) });

        var line = new BankStatementLine { Id = "L1", Date = new DateTime(2026, 1, 16), Description = "STRIPE PAYOUT", Amount = 48.25m };
        new BankMatchingService().MatchDeterministic(new[] { line }, data, new BankMatchingOptions());

        Assert.Equal(BankLineMatchStatus.Ignored, line.MatchStatus);
        Assert.Contains("Stripe", line.IgnoreReason ?? "");
    }

    [Fact]
    public void Deposit_NotMatchingAnyPayout_IsNotAutoIgnored()
    {
        var data = new CompanyData();
        data.Settings.Integrations.Stripe.ImportedPayouts.Add(
            new StripePayoutRecord { StripePayoutId = "po_1", AmountCents = 4825, Date = new DateTime(2026, 1, 15) });

        var line = new BankStatementLine { Id = "L2", Date = new DateTime(2026, 1, 16), Description = "OTHER DEPOSIT", Amount = 999.99m };
        new BankMatchingService().MatchDeterministic(new[] { line }, data, new BankMatchingOptions());

        Assert.NotEqual(BankLineMatchStatus.Ignored, line.MatchStatus);
    }

    [Fact]
    public void MoneyOut_LineIsNeverAutoIgnoredAsPayout()
    {
        var data = new CompanyData();
        data.Settings.Integrations.Stripe.ImportedPayouts.Add(
            new StripePayoutRecord { StripePayoutId = "po_1", AmountCents = 4825, Date = new DateTime(2026, 1, 15) });

        var line = new BankStatementLine { Id = "L3", Date = new DateTime(2026, 1, 15), Description = "PAYMENT", Amount = -48.25m };
        new BankMatchingService().MatchDeterministic(new[] { line }, data, new BankMatchingOptions());

        Assert.NotEqual(BankLineMatchStatus.Ignored, line.MatchStatus);
    }

    // A payout accounts for one deposit. A customer's transfer of the same amount a day later is a
    // different deposit and must stay open for matching.
    [Fact]
    public void OnePayout_IgnoresOnlyTheClosestDeposit()
    {
        var data = new CompanyData();
        data.Settings.Integrations.Stripe.ImportedPayouts.Add(
            new StripePayoutRecord { StripePayoutId = "po_1", AmountCents = 50000, Date = new DateTime(2026, 1, 3) });

        var transfer = new BankStatementLine { Id = "T", Date = new DateTime(2026, 1, 4), Description = "E-TRANSFER J SMITH", Amount = 500m };
        var payout = new BankStatementLine { Id = "P", Date = new DateTime(2026, 1, 3), Description = "STRIPE", Amount = 500m };

        new BankMatchingService().MatchDeterministic(new[] { transfer, payout }, data, new BankMatchingOptions());

        Assert.Equal(BankLineMatchStatus.Ignored, payout.MatchStatus);
        Assert.NotEqual(BankLineMatchStatus.Ignored, transfer.MatchStatus);
    }

    // Matching reruns every time the page loads, so a payout already spent on one line must stay
    // spent; otherwise the next statement's deposit of the same amount is ignored as well.
    [Fact]
    public void SpentPayout_IsNotReusedByALaterRun()
    {
        var data = new CompanyData();
        data.Settings.Integrations.Stripe.ImportedPayouts.Add(
            new StripePayoutRecord { StripePayoutId = "po_1", AmountCents = 50000, Date = new DateTime(2026, 1, 3) });
        var matcher = new BankMatchingService();

        var payout = new BankStatementLine { Id = "P", Date = new DateTime(2026, 1, 3), Description = "STRIPE", Amount = 500m };
        matcher.MatchDeterministic(new[] { payout }, data, new BankMatchingOptions());

        var later = new BankStatementLine { Id = "T", Date = new DateTime(2026, 1, 4), Description = "E-TRANSFER J SMITH", Amount = 500m };
        matcher.MatchDeterministic(new[] { payout, later }, data, new BankMatchingOptions());

        Assert.Equal(BankLineMatchStatus.Ignored, payout.MatchStatus);
        Assert.NotEqual(BankLineMatchStatus.Ignored, later.MatchStatus);
    }

    private static CompanyData YenCompanyWithPayout(string? currency)
    {
        var data = new CompanyData();
        data.Settings.Localization.Currency = "JPY";
        // Stripe states yen in whole units, so a 10,000 yen payout is 10000, not 1000000.
        data.Settings.Integrations.Stripe.ImportedPayouts.Add(
            new StripePayoutRecord { StripePayoutId = "po_1", AmountCents = 10000, Currency = currency, Date = new DateTime(2026, 1, 15) });
        return data;
    }

    [Fact]
    public void YenPayout_IgnoresTheEqualYenDeposit()
    {
        var data = YenCompanyWithPayout("JPY");
        var line = new BankStatementLine { Id = "L1", Date = new DateTime(2026, 1, 16), Description = "STRIPE", Amount = 10000m };

        new BankMatchingService().MatchDeterministic(new[] { line }, data, new BankMatchingOptions());

        Assert.Equal(BankLineMatchStatus.Ignored, line.MatchStatus);
    }

    [Fact]
    public void YenPayout_DoesNotIgnoreADepositAHundredthItsSize()
    {
        var data = YenCompanyWithPayout("JPY");
        var line = new BankStatementLine { Id = "L1", Date = new DateTime(2026, 1, 16), Description = "E-TRANSFER", Amount = 100m };

        new BankMatchingService().MatchDeterministic(new[] { line }, data, new BankMatchingOptions());

        Assert.NotEqual(BankLineMatchStatus.Ignored, line.MatchStatus);
    }

    // Payouts saved before the currency was recorded are read in the company's currency, the
    // currency bank lines are taken to be in.
    [Fact]
    public void PayoutWithNoStoredCurrency_IsReadInTheCompanyCurrency()
    {
        var data = YenCompanyWithPayout(null);
        var line = new BankStatementLine { Id = "L1", Date = new DateTime(2026, 1, 16), Description = "STRIPE", Amount = 10000m };

        new BankMatchingService().MatchDeterministic(new[] { line }, data, new BankMatchingOptions());

        Assert.Equal(BankLineMatchStatus.Ignored, line.MatchStatus);
    }

    // The payout's own currency wins over the company's: Stripe pays into an account in the
    // payout currency, so that account's statement is in it too.
    [Fact]
    public void DollarPayout_MatchesInCents_EvenForAYenCompany()
    {
        var data = new CompanyData();
        data.Settings.Localization.Currency = "JPY";
        data.Settings.Integrations.Stripe.ImportedPayouts.Add(
            new StripePayoutRecord { StripePayoutId = "po_1", AmountCents = 4825, Currency = "USD", Date = new DateTime(2026, 1, 15) });
        var line = new BankStatementLine { Id = "L1", Date = new DateTime(2026, 1, 16), Description = "STRIPE", Amount = 48.25m };

        new BankMatchingService().MatchDeterministic(new[] { line }, data, new BankMatchingOptions());

        Assert.Equal(BankLineMatchStatus.Ignored, line.MatchStatus);
    }
}
