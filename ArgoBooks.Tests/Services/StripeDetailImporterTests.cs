using ArgoBooks.Core.Data;
using ArgoBooks.Core.Services.Integrations;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class StripeDetailImporterTests
{
    private static StripeChargeDetail Charge(string id, long gross, long fee, long tax, long discount,
        string product = "Premium Plan", string? name = "Jane Doe", string? email = "jane@x.com")
        => new(id, 1700000000, gross, fee, "usd", name, email, product, tax, discount, 0);

    [Fact]
    public void Import_CreatesRevenue_WithProductCustomerTaxDiscount()
    {
        var data = new CompanyData();
        var result = new StripeDetailImporter().ImportCharges(data, [Charge("ch_1", 5000, 175, 400, 500)]);

        Assert.Equal(1, result.RevenuesCreated);
        var rev = Assert.Single(data.Revenues);
        Assert.Equal(50.00m, rev.Total);            // gross
        Assert.Equal(4.00m, rev.TaxAmount);         // tax
        Assert.Equal(5.00m, rev.Discount);          // discount
        Assert.Equal(46.00m, rev.Subtotal);         // gross - tax
        Assert.Equal("ch_1", rev.ReferenceNumber);  // charge id for refund linkage
        Assert.NotEmpty(rev.CustomerId!);
        Assert.Single(rev.LineItems);
        Assert.NotEmpty(rev.LineItems[0].ProductId!);

        // Auto-created customer + product (+ Stripe category) + fee expense.
        Assert.Single(data.Customers);
        Assert.Equal("Jane Doe", data.Customers[0].Name);
        Assert.Single(data.Products);
        Assert.Equal("Premium Plan", data.Products[0].Name);
        Assert.Contains(data.Categories, c => c.Name == "Stripe");
        Assert.Single(data.Expenses);
        Assert.Equal(1.75m, data.Expenses[0].Total);
    }

    [Fact]
    public void Import_ReusesExistingCustomerAndProduct()
    {
        var data = new CompanyData();
        var importer = new StripeDetailImporter();
        importer.ImportCharges(data, [Charge("ch_1", 5000, 100, 0, 0)]);
        importer.ImportCharges(data, [Charge("ch_2", 3000, 100, 0, 0)]);

        Assert.Single(data.Customers);   // same Jane
        Assert.Single(data.Products);    // same Premium Plan
        Assert.Equal(2, data.Revenues.Count);
    }

    /// <summary>Stripe gives JPY, KRW and the like in whole units, so dividing by 100 files 1000 yen as 10.</summary>
    [Fact]
    public void Import_ZeroDecimalCurrency_IsNotDividedBy100()
    {
        var data = new CompanyData();
        new StripeDetailImporter().ImportCharges(data,
            [new StripeChargeDetail("ch_1", 1700000000, 1000, 40, "jpy", null, null, "Tea", 0, 0, 0)]);

        Assert.Equal(1000m, data.Revenues.Single().Total);
        Assert.Equal(40m, data.Expenses.Single().Total);
    }

    [Fact]
    public void Import_NoCustomer_LeavesCustomerEmpty()
    {
        var data = new CompanyData();
        new StripeDetailImporter().ImportCharges(data, [Charge("ch_1", 5000, 0, 0, 0, name: null, email: null)]);
        Assert.Empty(data.Customers);
        Assert.True(string.IsNullOrEmpty(data.Revenues[0].CustomerId));
    }
}
