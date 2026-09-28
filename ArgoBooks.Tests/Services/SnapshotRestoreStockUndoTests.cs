using System.Net;
using System.Reflection;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Undoing an import restores the company from a snapshot. Stock undos further back on the stack
/// hold the stock records, adjustments and queue entries they changed, and change them again: a
/// restore that swapped those for copies left the undo moving stock on a record no longer in the
/// books, so the live stock and its adjustments stopped agreeing.
/// </summary>
[Collection("ExchangeRateSingleton")]
public class SnapshotRestoreStockUndoTests
{
    private static readonly DateTime SaleDate = new(2024, 6, 1);

    public SnapshotRestoreStockUndoTests()
    {
        _ = PendingConversionService.Instance ?? new PendingConversionService();
    }

    [Fact]
    public void SaleThenImportThenUndoImportThenUndoSale_StockAndQueueFollowTheLiveRecords()
    {
        var data = Company();
        var sale = Sale("REV-1", 2, "EUR");
        data.Revenues.Add(sale);
        UsdConversion.Apply(data, sale, rate: null);
        var changes = InventoryStockService.Apply(data, sale.LineItems, sale, isPurchase: false);

        var beforeImport = App.CreateCompanyDataSnapshot(data);
        data.Revenues.Add(Sale("REV-2", 1, "USD"));
        App.RestoreCompanyDataFromSnapshot(data, beforeImport);

        // The add-revenue undo.
        data.Revenues.RemoveRecord(sale);
        UsdConversion.Set(data, UsdConversion.KeyOf(sale), null);
        InventoryStockService.Revert(data, changes);

        AssertStockAgrees(data, expectedInStock: 10m);
        Assert.Empty(data.Revenues);
        Assert.Empty(data.PendingConversions);

        // And its redo.
        data.Revenues.RestoreRecord(sale);
        UsdConversion.Requeue(data, sale);
        InventoryStockService.Apply(data, sale.LineItems, sale, isPurchase: false);

        var live = Assert.Single(data.Revenues);
        Assert.Same(sale, live);
        AssertStockAgrees(data, expectedInStock: 8m);
        var queued = Assert.Single(data.PendingConversions);
        Assert.Equal(UsdConversion.KeyOf(live), queued.Key);
        Assert.Equal(live.Total, queued.Total);
        Assert.Equal(live.Date, queued.TransactionDate);
    }

    [Fact]
    public void EditUndo_AfterAnImportUndo_PutsTheLiveStockBack()
    {
        var data = Company();
        var sale = Sale("REV-1", 2, "USD");
        data.Revenues.Add(sale);
        InventoryStockService.Apply(data, sale.LineItems, sale, isPurchase: false);

        var oldLines = sale.LineItems;
        List<LineItem> newLines = [new LineItem { ProductId = "PRD-1", LocationId = "LOC-1", Quantity = 5, UnitPrice = 10m }];
        sale.LineItems = newLines;
        var edit = InventoryStockService.ApplyEdit(data, oldLines, newLines, sale, isPurchase: false, "Revenue edited");
        AssertStockAgrees(data, expectedInStock: 5m);

        var beforeImport = App.CreateCompanyDataSnapshot(data);
        App.RestoreCompanyDataFromSnapshot(data, beforeImport);

        sale.LineItems = oldLines;
        InventoryStockService.Revert(data, edit);

        AssertStockAgrees(data, expectedInStock: 8m);
    }

    [Fact]
    public void TransferUndo_AfterAnImportUndo_PutsTheLiveStockBack()
    {
        var data = Company();
        data.Locations.Add(new Location { Id = "LOC-2", Name = "Van" });
        var transfer = InventoryStockService.Transfer(data, data.Inventory[0], "LOC-2", 3m, "");

        var beforeImport = App.CreateCompanyDataSnapshot(data);
        App.RestoreCompanyDataFromSnapshot(data, beforeImport);
        InventoryStockService.RevertTransfer(data, transfer);

        AssertStockAgrees(data, expectedInStock: 10m);
        Assert.Empty(data.StockTransfers);
    }

    [Fact]
    public void PurchaseOrderReceiveUndo_AfterAnImportUndo_PutsTheLiveStockBack()
    {
        var data = Company();
        var line = new PurchaseOrderLineItem { ProductId = "PRD-1", Quantity = 5, UnitCost = 5m };
        var order = new PurchaseOrder
        {
            Id = "PO-1", PoNumber = "PO-1", OrderDate = SaleDate, OriginalCurrency = "USD",
            Total = 25m, TotalUSD = 25m, LineItems = [line]
        };
        data.PurchaseOrders.Add(order);
        line.QuantityReceived = 5;
        var received = InventoryStockService.ReceivePurchaseOrder(data, order, [(line, 5m)]);
        AssertStockAgrees(data, expectedInStock: 15m);

        var beforeImport = App.CreateCompanyDataSnapshot(data);
        App.RestoreCompanyDataFromSnapshot(data, beforeImport);
        InventoryStockService.Revert(data, received);

        AssertStockAgrees(data, expectedInStock: 10m);
    }

    // The sale undo reads off the queue entry it holds whether the stock's pending cost converted
    // since. A restore that swapped the entry for a copy hid the conversion, so the undo put the cost
    // back waiting and queued it again.
    [Fact]
    public async Task SaleUndo_AfterAnImportUndo_SeesTheStockCostThatConvertedMeanwhile()
    {
        var prior = SetRates(new ExchangeRateService(new NoDiskPlatform(), new HttpClient(new CadHandler())));
        try
        {
            var data = Company();
            data.Settings.Localization.Currency = "CAD";
            data.Inventory.Clear();
            var first = Sale("REV-1", 1, "USD");
            data.Revenues.Add(first);
            InventoryStockService.Apply(data, first.LineItems, first, isPurchase: false);
            var item = Assert.Single(data.Inventory);
            item.InStock = 10m;
            Assert.True(item.IsPendingConversion);

            var sale = Sale("REV-2", 2, "USD");
            data.Revenues.Add(sale);
            var changes = InventoryStockService.Apply(data, sale.LineItems, sale, isPurchase: false);

            var beforeImport = App.CreateCompanyDataSnapshot(data);
            App.RestoreCompanyDataFromSnapshot(data, beforeImport);

            var queue = new PendingConversionService(exchangeRateService: new ExchangeRateService(new NoDiskPlatform(), new HttpClient(new CadHandler())));
            queue.ReconcileWithCompanyData(data);
            await queue.ProcessPendingConversionsAsync(data);
            Assert.False(item.IsPendingConversion);

            data.Revenues.RemoveRecord(sale);
            InventoryStockService.Revert(data, changes);

            Assert.False(item.IsPendingConversion);
            Assert.Equal(10m, item.UnitCost);
            Assert.DoesNotContain(data.PendingConversions, p => p.TransactionType == PendingConversionType.InventoryItem);
        }
        finally
        {
            SetRates(prior);
        }
    }

    private static void AssertStockAgrees(CompanyData data, decimal expectedInStock)
    {
        var item = data.Inventory.Single(i => i.LocationId == "LOC-1");
        Assert.Equal(expectedInStock, item.InStock);
        var moved = data.StockAdjustments
            .Where(a => a.InventoryItemId == item.Id)
            .Sum(a => a.AdjustmentType == AdjustmentType.Add ? a.Quantity : -a.Quantity);
        Assert.Equal(item.InStock, 10m + moved);
    }

    private static CompanyData Company()
    {
        var data = new CompanyData();
        data.Settings.Localization.Currency = "USD";
        data.Locations.Add(new Location { Id = "LOC-1", Name = "Shop" });
        data.Products.Add(new Product { Id = "PRD-1", Name = "Widget", TrackInventory = true, CostPrice = 12.5m, UnitPrice = 10m });
        data.Inventory.Add(new InventoryItem { Id = "INV-ITM-001", ProductId = "PRD-1", LocationId = "LOC-1", InStock = 10m, UnitCost = 5m });
        return data;
    }

    private static Revenue Sale(string id, decimal quantity, string currency) => new()
    {
        Id = id, Date = SaleDate, OriginalCurrency = currency, Total = quantity * 10m, Amount = quantity * 10m,
        TotalUSD = currency == "USD" ? quantity * 10m : 0m,
        PaymentStatus = RevenuePaymentStatus.Paid,
        LineItems = [new LineItem { ProductId = "PRD-1", LocationId = "LOC-1", Quantity = quantity, UnitPrice = 10m }]
    };

    private static ExchangeRateService? SetRates(ExchangeRateService? service)
    {
        var prop = typeof(ExchangeRateService)
            .GetProperty(nameof(ExchangeRateService.Instance), BindingFlags.Public | BindingFlags.Static)!;
        var prior = (ExchangeRateService?)prop.GetValue(null);
        prop.SetValue(null, service);
        return prior;
    }

    // 1.25 CAD per USD, so the 12.50 CAD cost price is 10 USD.
    private sealed class CadHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            const string payload = """{ "success": true, "base": "USD", "rates": { "CAD": 1.25 } }""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class NoDiskPlatform : IPlatformService
    {
        public PlatformType Platform => PlatformType.Linux;
        public string GetAppDataPath() => Path.GetTempPath();
        public string GetTempPath() => Path.GetTempPath();
        public string GetCachePath() => Path.GetTempPath();
        public void EnsureDirectoryExists(string path) { }
        public bool SupportsFileSystem => false;
        public bool SupportsNativeDialogs => false;
        public bool SupportsBiometrics => false;
        public Task<bool> IsBiometricAvailableAsync() => Task.FromResult(false);
        public Task<string> GetBiometricAvailabilityDetailsAsync() => Task.FromResult("");
        public Task<bool> AuthenticateWithBiometricAsync(string reason) => Task.FromResult(false);
        public void StorePasswordForBiometric(string fileId, string password) { }
        public string? GetPasswordForBiometric(string fileId) => null;
        public void ClearPasswordForBiometric(string fileId) { }
        public bool SupportsAutoUpdate => false;
        public int MaxRecentCompanies => 10;
        public string NormalizePath(string path) => path;
        public string CombinePaths(params string[] paths) => Path.Combine(paths);
        public string GetMachineId() => "test-machine-id";
        public StringComparer PathComparer => StringComparer.Ordinal;
    }
}
