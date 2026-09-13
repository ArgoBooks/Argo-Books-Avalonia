using System.Net;
using System.Text;
using System.Text.Json;
using ArgoBooks.Core.Services.Sync;
using ArgoBooks.Shared.Mobile;
using ArgoBooks.Shared.Sync;
using Xunit;

namespace ArgoBooks.Tests.Mobile;

/// <summary>
/// Unit tests for CapturePushCoordinator, using a canned HttpMessageHandler (fakes the sync
/// server's /queue/push response) and an in-memory ISecureStore-backed PairedCompanyStore.
/// </summary>
public class CapturePushCoordinatorTests
{
    private sealed class CannedHandler : HttpMessageHandler
    {
        private readonly string _json;
        private readonly HttpStatusCode _statusCode;
        public HttpRequestMessage? Last;
        public string? LastBody;

        public CannedHandler(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _json = json;
            _statusCode = statusCode;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(_statusCode)
            { Content = new StringContent(_json, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class InMemorySecureStore : ISecureStore
    {
        private readonly Dictionary<string, string> _storage = new();

        public Task SetAsync(string key, string value)
        {
            _storage[key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> GetAsync(string key)
        {
            _storage.TryGetValue(key, out var value);
            return Task.FromResult(value);
        }

        public Task RemoveAsync(string key)
        {
            _storage.Remove(key);
            return Task.CompletedTask;
        }
    }

    private const string SyncKeyBase64 = "MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE=";

    private static CapturedTransaction NewTransaction() => new()
    {
        Type = CapturedTransactionType.Expense,
        SupplierOrCustomer = "Office Depot",
        Date = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        Total = 54.00m,
        Tax = 4.00m,
        LineItems =
        [
            new CapturedLineItem
            {
                Description = "Printer paper",
                Quantity = 2,
                UnitPrice = 25.00m,
                Total = 50.00m,
                ProductName = "Printer paper"
            }
        ],
        ScanUid = "11111111-1111-1111-1111-111111111111"
    };

    private static async Task<PairedCompanyStore> NewActiveStoreAsync()
    {
        var store = new PairedCompanyStore(new InMemorySecureStore());
        await store.SaveAsync(new PairedCompanyRecord
        {
            CompanyUid = "company-uid-456",
            CompanyLabel = "Acme Corp",
            DeviceToken = "device-tok-abc",
            SyncKeyBase64 = SyncKeyBase64,
        });
        await store.SetActiveAsync("company-uid-456");
        return store;
    }

    [Fact]
    public async Task PushAsync_NoActiveCompany_ReturnsFalse()
    {
        var handler = new CannedHandler("{\"success\":true}");
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");
        var store = new PairedCompanyStore(new InMemorySecureStore());
        var coordinator = new CapturePushCoordinator(client, store);

        var result = await coordinator.PushAsync(NewTransaction(), CancellationToken.None);

        Assert.False(result);
        Assert.Null(handler.Last);
    }

    [Fact]
    public async Task PushAsync_ActiveCompany_PostsToQueuePushWithDeviceTokenHeader()
    {
        var handler = new CannedHandler("{\"success\":true}");
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");
        var store = await NewActiveStoreAsync();
        var coordinator = new CapturePushCoordinator(client, store);

        var result = await coordinator.PushAsync(NewTransaction(), CancellationToken.None);

        Assert.True(result);
        Assert.NotNull(handler.Last);
        Assert.EndsWith("/queue/push", handler.Last!.RequestUri!.AbsolutePath);
        Assert.True(handler.Last.Headers.Contains("X-Sync-Device-Token"));
        Assert.Contains("device-tok-abc", handler.Last.Headers.GetValues("X-Sync-Device-Token"));
    }

    [Fact]
    public async Task PushAsync_PostedCiphertext_DecryptsBackToOriginalTransaction()
    {
        var handler = new CannedHandler("{\"success\":true}");
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");
        var store = await NewActiveStoreAsync();
        var coordinator = new CapturePushCoordinator(client, store);
        var original = NewTransaction();

        await coordinator.PushAsync(original, CancellationToken.None);

        Assert.NotNull(handler.LastBody);
        using var doc = JsonDocument.Parse(handler.LastBody!);
        var ciphertext = doc.RootElement.GetProperty("ciphertext").GetString();
        Assert.False(string.IsNullOrEmpty(ciphertext));

        var plaintext = SyncCrypto.Decrypt(ciphertext, SyncKeyBase64);
        var decrypted = JsonSerializer.Deserialize<CapturedTransaction>(plaintext);

        Assert.NotNull(decrypted);
        Assert.Equal(original.Type, decrypted.Type);
        Assert.Equal(original.SupplierOrCustomer, decrypted.SupplierOrCustomer);
        Assert.Equal(original.Date, decrypted.Date);
        Assert.Equal(original.Total, decrypted.Total);
        Assert.Equal(original.Tax, decrypted.Tax);
        Assert.Equal(original.ScanUid, decrypted.ScanUid);
        Assert.Single(decrypted.LineItems);
        Assert.Equal(original.LineItems[0].Description, decrypted.LineItems[0].Description);
        Assert.Equal(original.LineItems[0].ProductName, decrypted.LineItems[0].ProductName);
    }

    [Fact]
    public async Task PushAsync_NamedCompany_UsesThatCompanyNotTheActiveOne()
    {
        var handler = new CannedHandler("{\"success\":true}");
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");
        var store = await NewActiveStoreAsync();
        await store.SaveAsync(new PairedCompanyRecord
        {
            CompanyUid = "company-uid-999",
            CompanyLabel = "Other Co",
            DeviceToken = "device-tok-other",
            SyncKeyBase64 = SyncKeyBase64,
        });
        var coordinator = new CapturePushCoordinator(client, store);

        var result = await coordinator.PushAsync(NewTransaction(), "company-uid-999", CancellationToken.None);

        Assert.True(result);
        Assert.Contains("device-tok-other", handler.Last!.Headers.GetValues("X-Sync-Device-Token"));
    }

    [Fact]
    public async Task PushAsync_NamedCompanyNoLongerPaired_ReturnsFalseWithoutSendingAnywhere()
    {
        var handler = new CannedHandler("{\"success\":true}");
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");
        var store = await NewActiveStoreAsync();
        var coordinator = new CapturePushCoordinator(client, store);

        var result = await coordinator.PushAsync(NewTransaction(), "company-uid-unpaired", CancellationToken.None);

        Assert.False(result);
        Assert.Null(handler.Last);
    }

    [Fact]
    public async Task PushAsync_HttpFailure_ReturnsFalse()
    {
        var handler = new CannedHandler("{\"success\":false}", HttpStatusCode.InternalServerError);
        var client = new MobileSyncClient(new HttpClient(handler), "http://localhost:5000");
        var store = await NewActiveStoreAsync();
        var coordinator = new CapturePushCoordinator(client, store);

        var result = await coordinator.PushAsync(NewTransaction(), CancellationToken.None);

        Assert.False(result);
    }
}
