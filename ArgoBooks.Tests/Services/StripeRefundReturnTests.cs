using ArgoBooks.Core.Data;
using ArgoBooks.Core.Services.Integrations;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class StripeRefundReturnTests
{
    private static StripeChargeDetail Charge(string id, long gross, long refunded)
        => new(id, 1700000000, gross, 100, "usd", "Jane", "jane@x.com", "Premium Plan", 0, 0, refunded);

    [Fact]
    public void Refund_OfImportedCharge_CreatesReturnAgainstRevenue()
    {
        var data = new CompanyData();
        var importer = new StripeDetailImporter();
        importer.ImportCharges(data, [Charge("ch_1", 5000, 0)]);
        var revId = data.Revenues[0].Id;

        var made = importer.ApplyRefunds(data, [Charge("ch_1", 5000, 5000)]);

        Assert.Equal(1, made);
        var ret = Assert.Single(data.Returns);
        Assert.Equal(revId, ret.OriginalTransactionId);
        Assert.Equal(50.00m, ret.RefundAmount);
        Assert.DoesNotContain(data.Expenses, e => e.Description == "Stripe refund"); // no fallback expense
    }

    [Fact]
    public void Refund_NotDoubleRecorded()
    {
        var data = new CompanyData();
        var importer = new StripeDetailImporter();
        importer.ImportCharges(data, [Charge("ch_1", 5000, 0)]);

        importer.ApplyRefunds(data, [Charge("ch_1", 5000, 5000)]);
        importer.ApplyRefunds(data, [Charge("ch_1", 5000, 5000)]);

        Assert.Single(data.Returns);
    }

    [Fact]
    public void Refund_ZeroDecimalCurrency_IsNotDividedBy100()
    {
        var data = new CompanyData();
        var made = new StripeDetailImporter().ApplyRefunds(data,
            [new StripeChargeDetail("ch_old", 1700000000, 1000, 0, "jpy", null, null, "Tea", 0, 0, 1000)]);

        Assert.Equal(1, made);
        Assert.Equal(1000m, data.Expenses.Single().Total);
    }

    [Fact]
    public void Refund_OfUnimportedCharge_FallsBackToExpense()
    {
        var data = new CompanyData();
        var made = new StripeDetailImporter().ApplyRefunds(data, [Charge("ch_old", 5000, 5000)]);

        Assert.Equal(1, made);
        Assert.Empty(data.Returns);
        Assert.Single(data.Expenses);
        Assert.Equal(50.00m, data.Expenses[0].Total);
    }
}
