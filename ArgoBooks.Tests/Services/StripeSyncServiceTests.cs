using System.Net;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Services.Integrations;
using Xunit;

namespace ArgoBooks.Tests.Services;

public class StripeSyncServiceTests
{
    // Routes by URL: the payouts list, and the charges list (fetched until a watermark).
    private sealed class RoutingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            string body;
            if (url.Contains("/v1/payouts"))
            {
                // One paid payout po_1, amount 48.25, arrival date.
                body = "{\"has_more\":false,\"data\":[" +
                       "{\"id\":\"po_1\",\"amount\":4825,\"currency\":\"usd\",\"arrival_date\":1700000900,\"created\":1700000800,\"status\":\"paid\"}" +
                       "]}";
            }
            else if (url.Contains("/v1/balance_transactions"))
            {
                // The authoritative fee for ch_1 (source = charge id), $1.75.
                body = "{\"has_more\":false,\"data\":[" +
                       "{\"id\":\"txn_1\",\"type\":\"charge\",\"source\":\"ch_1\",\"fee\":175}" +
                       "]}";
            }
            else // /v1/charges: one succeeded charge. Its inline balance_transaction fee is 0 (the
                 // unexpanded/bug case) so the test proves the fee is filled from the list above.
            {
                body = "{\"has_more\":false,\"data\":[{" +
                       "\"id\":\"ch_1\",\"status\":\"succeeded\",\"paid\":true,\"amount\":5000,\"amount_refunded\":0,\"currency\":\"usd\",\"created\":1700000000," +
                       "\"customer\":{\"id\":\"cus_1\",\"name\":\"Jane Doe\",\"email\":\"jane@x.com\"}," +
                       "\"balance_transaction\":{\"id\":\"txn_1\",\"fee\":0,\"net\":5000}," +
                       "\"invoice\":{\"id\":\"in_1\",\"tax\":0,\"lines\":{\"data\":[{\"description\":\"Premium Plan\"}]}}" +
                       "}]}";
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static StripeSyncService MakeService()
        => new StripeSyncService(new StripeApiClient(new HttpClient(new RoutingHandler())));

    private static CompanyData ConnectedData()
    {
        var data = new CompanyData();
        data.Settings.Integrations.Stripe.ApiKey = "rk_test";
        data.Settings.Integrations.Stripe.Connected = true;
        return data;
    }

    [Fact]
    public async Task Preview_ReturnsChargeRevenue_AndNewPayout()
    {
        var preview = await MakeService().PreviewAsync(ConnectedData());

        Assert.Single(preview.Charges);
        Assert.Equal(50.00m, preview.TotalRevenue);
        Assert.Equal(1.75m, preview.TotalFees);
        Assert.Single(preview.NewPayouts);
        Assert.True(preview.HasActivity);
        Assert.Equal("ch_1", preview.NewCursor);
    }

    [Fact]
    public async Task Import_CreatesRecords_RemembersPayout_AdvancesCursor_ThenSecondPreviewEmpty()
    {
        var svc = MakeService();
        var data = ConnectedData();

        var preview = await svc.PreviewAsync(data);
        var result = svc.ImportPreview(data, preview);

        Assert.Equal(1, result.RevenuesCreated);
        Assert.Single(data.Revenues);
        Assert.Equal("Premium Plan", data.Revenues[0].Description);
        Assert.NotEmpty(data.Revenues[0].CustomerId!);
        Assert.Single(data.Customers);
        Assert.Equal("Jane Doe", data.Customers[0].Name);

        // The processing fee (filled from the balance-transactions list, not the zero inline value)
        // is posted as its own expense linked to the sale.
        var feeExpense = Assert.Single(data.Expenses);
        Assert.Equal("Stripe processing fee", feeExpense.Description);
        Assert.Equal(1.75m, feeExpense.Total);
        Assert.Equal("ch_1", feeExpense.ReferenceNumber);

        var stripe = data.Settings.Integrations.Stripe;
        Assert.Single(stripe.ImportedPayouts);
        Assert.Equal("po_1", stripe.ImportedPayouts[0].StripePayoutId);
        Assert.Equal("USD", stripe.ImportedPayouts[0].Currency);
        Assert.Equal("ch_1", stripe.LastSyncCursor);
        Assert.NotNull(stripe.LastSyncTime);

        // Second sync: the watermark stops the fetch immediately and the payout is known,
        // so there is nothing new.
        var preview2 = await svc.PreviewAsync(data);
        Assert.False(preview2.HasActivity);
        Assert.Empty(preview2.Charges);
        Assert.Empty(preview2.NewPayouts);
    }

    /// <summary>
    /// A EUR charge settled into a USD balance. Stripe states the balance transaction's fee in
    /// the balance transaction's own currency (the settlement currency), not the charge's.
    /// </summary>
    private sealed class SettlementCurrencyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            string body;
            if (url.Contains("/v1/payouts"))
                body = "{\"has_more\":false,\"data\":[]}";
            else if (url.Contains("/v1/balance_transactions"))
                body = "{\"has_more\":false,\"data\":[" +
                       "{\"id\":\"txn_1\",\"type\":\"charge\",\"source\":\"ch_1\",\"amount\":5400,\"currency\":\"usd\",\"fee\":175}" +
                       "]}";
            else
                body = "{\"has_more\":false,\"data\":[{" +
                       "\"id\":\"ch_1\",\"status\":\"succeeded\",\"paid\":true,\"amount\":5000,\"amount_refunded\":0,\"currency\":\"eur\",\"created\":1700000000," +
                       "\"balance_transaction\":{\"id\":\"txn_1\",\"amount\":5400,\"currency\":\"usd\",\"fee\":175,\"net\":5225}" +
                       "}]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task Import_FeeIsRecordedInTheSettlementCurrency()
    {
        var svc = new StripeSyncService(new StripeApiClient(new HttpClient(new SettlementCurrencyHandler())));
        var data = ConnectedData();

        svc.ImportPreview(data, await svc.PreviewAsync(data));

        Assert.Equal("EUR", data.Revenues.Single().OriginalCurrency);
        var fee = Assert.Single(data.Expenses);
        Assert.Equal("USD", fee.OriginalCurrency);
        Assert.Equal(1.75m, fee.Total);
    }

    /// <summary>
    /// Undo must not hand back an id something else took after the import. A phone capture or
    /// portal sync has no undo of its own, so its record is still in the books when the import is
    /// undone, and a lowered counter would give the next new record that same id.
    /// </summary>
    [Fact]
    public async Task Undo_DoesNotLowerACounterPastAnIdIssuedAfterTheImport()
    {
        var svc = MakeService();
        var data = ConnectedData();
        var creation = svc.ImportPreview(data, await svc.PreviewAsync(data));

        data.IdCounters.Revenue++;
        data.Revenues.Add(new ArgoBooks.Core.Models.Transactions.Revenue { Id = $"REV-2026-{data.IdCounters.Revenue:D5}" });
        var afterwards = data.IdCounters.Revenue;

        creation.Undo(data);
        Assert.Equal(afterwards, data.IdCounters.Revenue);

        creation.Redo(data);
        Assert.Equal(afterwards, data.IdCounters.Revenue);
    }

    [Fact]
    public async Task Preview_NoKey_ReturnsNoActivity()
    {
        var preview = await MakeService().PreviewAsync(new CompanyData());
        Assert.False(preview.HasActivity);
    }

    [Fact]
    public async Task Import_Undo_RemovesEverything_Redo_ReAddsIt()
    {
        var svc = MakeService();
        var data = ConnectedData();
        var preview = await svc.PreviewAsync(data);
        var creation = svc.ImportPreview(data, preview);

        Assert.True(creation.AnyCreated);
        Assert.Single(data.Revenues);
        Assert.Single(data.Expenses);
        var stripe = data.Settings.Integrations.Stripe;

        creation.Undo(data);
        Assert.Empty(data.Revenues);
        Assert.Empty(data.Expenses);
        Assert.Empty(data.Customers);
        Assert.Empty(data.Products);
        Assert.Empty(stripe.ImportedPayouts);
        Assert.Null(stripe.LastSyncCursor);
        Assert.Null(stripe.LastSyncTime);

        creation.Redo(data);
        Assert.Single(data.Revenues);
        Assert.Single(data.Expenses);
        Assert.Single(data.Customers);
        Assert.Single(stripe.ImportedPayouts);
        Assert.Equal("ch_1", stripe.LastSyncCursor);
        Assert.NotNull(stripe.LastSyncTime);
    }
}
