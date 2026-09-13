using System.Net;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Services.Integrations;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// A refund pushed through the Argo Books API for a sale the merchant has since deleted.
///
/// The server still remembers the local id the sale was given, so the reference resolves to a
/// record that is no longer there. The import used to throw halfway, leaving the rows before it
/// in the books with no claim and no undo, so every retry added the same batch again.
/// </summary>
public class ArgoApiRefundImportTests
{
    private const string Key = "ab_test";

    private static CompanyData Company()
    {
        var data = new CompanyData();
        var api = data.Settings.Integrations.ArgoApi;
        api.Enabled = true;
        api.CompanyUid = "uid";
        api.DesktopKey = Key;
        return data;
    }

    private static ArgoRevenue Sale(string id) => new(
        Id: id, Description: "Order #1042",
        Amount: 11300, Currency: "USD", TaxAmount: 0, DiscountAmount: 0, FeeAmount: 0,
        OccurredOn: "2026-08-14", Customer: null, Category: null, PaymentMethod: null,
        Reference: null, Notes: null, LineItems: null,
        Import: new ArgoImportState("pending", null, null, null));

    private static ArgoRefund RefundOf(string revenueId) => new(
        Id: "ref_1", Revenue: revenueId, Amount: 5000, Currency: "USD", Reason: "Damaged",
        OccurredOn: "2026-08-20", Import: new ArgoImportState("pending", null, null, null));

    /// <summary>The sale was imported earlier as REV-2026-00001 and then deleted here.</summary>
    private static ArgoApiSyncPreview PreviewWithRefundOfDeletedSale() =>
        new([], [], [], [], [], [Sale("rev_new")], [RefundOf("rev_old")],
            new Dictionary<string, ArgoExternalRef>
            {
                ["rev_old"] = new("rev_old", "REV-2026-00001", null, null)
            });

    private sealed class ClaimHandler : HttpMessageHandler
    {
        public int Claims;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.ToString().Contains("/import_batches"))
                Claims++;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"id\":\"imb_1\",\"status\":\"imported\"}", Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public void RefundForASaleDeletedLocally_IsBookedAsAStandaloneExpense()
    {
        var data = Company();
        data.IdCounters.Revenue = 1; // REV-2026-00001 was issued, then deleted
        var creation = new ArgoApiImportCreation();

        new ArgoApiImporter().Import(data, PreviewWithRefundOfDeletedSale(), creation);

        Assert.Single(data.Revenues);
        Assert.Empty(data.Returns);
        var expense = Assert.Single(data.Expenses);
        Assert.Equal(50.00m, expense.Total);
        Assert.Contains("ref_1", creation.ClaimedObjectIds);
    }

    /// <summary>
    /// Whatever makes an import fail partway, the rows it already wrote must go with it: nothing
    /// was claimed, so the next sync offers the same objects again and would add them twice.
    /// </summary>
    [Fact]
    public async Task ImportThatFailsPartway_LeavesNothingBehind()
    {
        var handler = new ClaimHandler();
        var svc = new ArgoApiSyncService(new ArgoApiClient(new HttpClient(handler)));
        var data = Company();
        var preview = new ArgoApiSyncPreview([], [], [], [], [], [Sale("rev_new")], [null!],
            new Dictionary<string, ArgoExternalRef>());

        await Assert.ThrowsAnyAsync<Exception>(() => svc.ImportPreviewAsync(data, preview));

        Assert.Empty(data.Revenues);
        Assert.Equal(0, data.IdCounters.Revenue);
        Assert.Equal(0, handler.Claims);
    }

    /// <summary>See StripeSyncServiceTests.Undo_DoesNotLowerACounterPastAnIdIssuedAfterTheImport.</summary>
    [Fact]
    public async Task Undo_DoesNotLowerACounterPastAnIdIssuedAfterTheImport()
    {
        var svc = new ArgoApiSyncService(new ArgoApiClient(new HttpClient(new ClaimHandler())));
        var data = Company();
        var creation = await svc.ImportPreviewAsync(data,
            new ArgoApiSyncPreview([], [], [], [], [], [Sale("rev_new")], [], new Dictionary<string, ArgoExternalRef>()));

        data.IdCounters.Revenue++;
        data.Revenues.Add(new ArgoBooks.Core.Models.Transactions.Revenue { Id = $"REV-2026-{data.IdCounters.Revenue:D5}" });
        var afterwards = data.IdCounters.Revenue;

        creation.Undo(data);
        Assert.Equal(afterwards, data.IdCounters.Revenue);

        creation.Redo(data);
        Assert.Equal(afterwards, data.IdCounters.Revenue);
    }
}
