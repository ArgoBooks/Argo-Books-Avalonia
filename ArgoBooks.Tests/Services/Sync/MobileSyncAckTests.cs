using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.Sync;
using ArgoBooks.Tests.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.Services.Sync;

/// <summary>
/// A phone capture must stay in the server queue until it is in the company file. The server
/// deletes whatever is acknowledged, so acknowledging a capture that only exists in memory loses
/// it for good the moment the app quits without saving.
/// </summary>
public class MobileSyncAckTests : ModalViewModelTestBase
{
    private const int QueueItemId = 7;

    /// <summary>Serves one queued capture on every pull, as the server does until it is acked.</summary>
    private sealed class QueueHandler(string ciphertext) : HttpMessageHandler
    {
        public readonly List<int> Acked = [];

        /// <summary>When set, the pull waits for it, so a test can act while the pull is in flight.</summary>
        public TaskCompletionSource? PullGate { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = "{\"success\":true}";

            if (path.EndsWith("/queue/pull"))
            {
                if (PullGate != null) await PullGate.Task.WaitAsync(ct);
                body =JsonSerializer.Serialize(new { success = true, items = new[] { new { id = QueueItemId, ciphertext } } });
            }
            else if (path.EndsWith("/queue/ack"))
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                foreach (var id in doc.RootElement.GetProperty("ids").EnumerateArray())
                    Acked.Add(id.GetInt32());
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static readonly PropertyInfo SyncServiceProperty =
        typeof(App).GetProperty(nameof(App.SyncService), BindingFlags.Public | BindingFlags.Static)!;

    private async Task RunWithQueueAsync(Func<QueueHandler, Task> test)
    {
        var syncKey = SyncCrypto.GenerateSyncKey();
        var mobileSync = Company.Settings.MobileSync;
        mobileSync.Enabled = true;
        mobileSync.CompanyUid = "company-uid";
        mobileSync.SyncKeyBase64 = syncKey;

        var capture = new CapturedTransaction
        {
            Type = CapturedTransactionType.Expense,
            SupplierOrCustomer = "Office Depot",
            Date = new DateTime(2026, 6, 1),
            Total = 54.00m,
            Tax = 4.00m,
            LineItems = [new CapturedLineItem { Description = "Printer paper", Quantity = 2, UnitPrice = 25.00m, Total = 50.00m }],
            ScanUid = "11111111-1111-1111-1111-111111111111"
        };
        var handler = new QueueHandler(SyncCrypto.Encrypt(JsonSerializer.SerializeToUtf8Bytes(capture), syncKey));

        var prior = SyncServiceProperty.GetValue(null);
        SyncServiceProperty.SetValue(null, new SyncService(new HttpClient(handler)));
        try
        {
            await test(handler);
        }
        finally
        {
            SyncServiceProperty.SetValue(null, prior);
        }
    }

    [Fact]
    public async Task CaptureIngestedWhileEditsAreUnsaved_IsNotAckedOnTheNextCycle()
    {
        await RunWithQueueAsync(async handler =>
        {
            Company.MarkAsModified(); // the user is mid-edit, so the sync must not save

            await App.AutoMobileSyncAsync();
            Assert.Single(Company.Expenses);
            Assert.Empty(handler.Acked);

            // The capture is in memory only. Acking it now would delete the only copy that
            // survives quitting with "Don't save".
            await App.AutoMobileSyncAsync();
            Assert.Single(Company.Expenses);
            Assert.Empty(handler.Acked);
        });
    }

    [Fact]
    public async Task CaptureIngestedWhileEditsAreUnsaved_IsAckedOnceTheUserHasSaved()
    {
        await RunWithQueueAsync(async handler =>
        {
            Company.MarkAsModified();
            await App.AutoMobileSyncAsync();

            Company.MarkAsSaved(); // the user's save wrote the capture along with their edits
            await App.AutoMobileSyncAsync();

            Assert.Equal([QueueItemId], handler.Acked);
            Assert.Single(Company.Expenses);
        });
    }

    // With no company file behind it the save writes nothing, as when it is skipped because another
    // company has been opened. Nothing was saved, so nothing may be acked.
    [Fact]
    public async Task CaptureWhoseSaveWroteNothing_IsNotAcked()
    {
        await RunWithQueueAsync(async handler =>
        {
            await App.AutoMobileSyncAsync();

            Assert.Single(Company.Expenses);
            Assert.Empty(handler.Acked);
            Assert.True(Company.ChangesMade);
        });
    }

    // If another company is opened while the pull is in flight, the captures must not go into the
    // one that was closed, which nobody will save, and must stay in the queue for its next sync.
    [Fact]
    public async Task CapturesArrivingAfterACompanySwitch_AreNotAppliedOrAcked()
    {
        await RunWithQueueAsync(async handler =>
        {
            handler.PullGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var sync = App.AutoMobileSyncAsync();

            App.SetCompanyManagerForTesting(CompanyManager.CreateForTesting(new CompanyData()));

            handler.PullGate.SetResult();
            await sync;

            Assert.Empty(Company.Expenses);
            Assert.Empty(handler.Acked);
        });
    }
}
