using System.Net;
using System.Reflection;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Insights;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// A stock record's UnitCost is kept in USD, while a product's CostPrice is in the company's currency
/// (docs/Calculations.md §14). A new stock record used to copy CostPrice as it was, so a CAD company's
/// stock and cost of goods sold were read as if CAD were USD.
/// </summary>
[Collection("ExchangeRateSingleton")]
public class InventoryCostCurrencyTests
{
    private static readonly DateTime SaleDate = new(2024, 6, 1);
    private static readonly DateTime ReportEnd = new(2024, 12, 31);
    private const decimal UsdToEur = 0.8m;
    private const decimal UsdToCad = 1.25m;

    public InventoryCostCurrencyTests()
    {
        // The queues these tests convert with must not become the shared one the stock service mirrors into.
        _ = PendingConversionService.Instance ?? new PendingConversionService(new MockPlatformService());
    }

    private static CompanyData CadCompany()
    {
        var data = new CompanyData();
        data.Settings.Localization.Currency = "CAD";
        data.Locations.Add(new Location { Id = "LOC-1", Name = "Shop" });
        data.Products.Add(new Product { Id = "PRD-1", Name = "Flour", Sku = "FLR", TrackInventory = true, CostPrice = 12.5m });
        return data;
    }

    private static Revenue Sale(decimal quantity) => new()
    {
        Id = "REV-1", Date = SaleDate, OriginalCurrency = "USD", Total = 100m, Amount = 100m,
        PaymentStatus = RevenuePaymentStatus.Paid,
        LineItems = [new LineItem { ProductId = "PRD-1", Quantity = quantity, UnitPrice = 100m / quantity }]
    };

    [Fact]
    public async Task SaleCreatingAStockRecord_StartsItAtTheCostPriceInUsd()
    {
        var prior = SetInstance(await SeededServiceAsync(SaleDate));
        try
        {
            var data = CadCompany();
            var sale = Sale(2);

            InventoryStockService.Apply(data, sale.LineItems, sale, isPurchase: false);

            // 12.50 CAD at 1.25 CAD per USD.
            Assert.Equal(10m, Assert.Single(data.Inventory).UnitCost);
            Assert.Equal(20m, sale.LineItems[0].CostOfGoodsUSD);
        }
        finally
        {
            SetInstance(prior);
        }
    }

    [Fact]
    public async Task TryCostPriceUSD_RateMissing_IsNotAvailableRatherThanTheUnconvertedPrice()
    {
        var prior = SetInstance(await SeededServiceAsync());
        try
        {
            var data = CadCompany();

            Assert.False(InventoryStockService.TryCostPriceUSD(data, data.Products[0], SaleDate, out _));
        }
        finally
        {
            SetInstance(prior);
        }
    }

    [Fact]
    public void TryCostPriceUSD_UsdCompany_IsTheCostPrice()
    {
        var data = new CompanyData();
        data.Settings.Localization.Currency = "USD";
        var product = new Product { Id = "PRD-1", CostPrice = 12.5m };

        Assert.True(InventoryStockService.TryCostPriceUSD(data, product, SaleDate, out var usd));
        Assert.Equal(12.5m, usd);
    }

    // A missing rate used to start the stock at a cost of 0 for good, so every sale of it saved no
    // cost of goods sold. The cost now waits for its rate like any other amount (§14).
    [Fact]
    public async Task RateMissing_StockCostWaits_ThenFillsTheSaleMadeMeanwhile()
    {
        var prior = SetInstance(await SeededServiceAsync());
        try
        {
            var data = CadCompany();
            var sale = Sale(2);
            data.Revenues.Add(sale);

            InventoryStockService.Apply(data, sale.LineItems, sale, isPurchase: false);

            var item = Assert.Single(data.Inventory);
            Assert.True(item.IsPendingConversion);
            Assert.Equal(0m, item.UnitCost);
            Assert.True(sale.LineItems[0].IsCostOfGoodsPending);
            Assert.Equal(0m, CostOfGoodsAggregator.CostOfGoodsSoldUSD(sale));
            var queued = Assert.Single(data.PendingConversions);
            Assert.Equal((12.5m, "CAD", SaleDate), (queued.Total, queued.OriginalCurrency, queued.TransactionDate));

            await ConvertQueueAsync(data);

            Assert.False(item.IsPendingConversion);
            Assert.Equal(10m, item.UnitCost);
            Assert.False(sale.LineItems[0].IsCostOfGoodsPending);
            Assert.Equal(20m, sale.LineItems[0].CostOfGoodsUSD);
            Assert.Empty(data.PendingConversions);
        }
        finally
        {
            SetInstance(prior);
        }
    }

    // A sale takes the cost the stock had when it was sold, even after a later purchase with a known
    // rate has given the stock a new cost.
    [Fact]
    public async Task PendingPurchase_SaleMeanwhile_GetsThatPurchasesCost_NotALaterOne()
    {
        var prior = SetInstance(await SeededServiceAsync());
        try
        {
            var data = CadCompany();
            data.Inventory.Add(new InventoryItem { Id = "INV-1", ProductId = "PRD-1", LocationId = "LOC-1", InStock = 5, UnitCost = 4m });

            var eurPurchase = Purchase("PUR-1", "EUR", unitPrice: 10m, pending: true);
            InventoryStockService.Apply(data, eurPurchase.LineItems, eurPurchase, isPurchase: true);
            var item = data.Inventory[0];
            Assert.True(item.IsPendingConversion);

            var sale = Sale(2);
            data.Revenues.Add(sale);
            InventoryStockService.Apply(data, sale.LineItems, sale, isPurchase: false);
            Assert.True(sale.LineItems[0].IsCostOfGoodsPending);

            var usdPurchase = Purchase("PUR-2", "USD", unitPrice: 7m, pending: false);
            InventoryStockService.Apply(data, usdPurchase.LineItems, usdPurchase, isPurchase: true);
            Assert.False(item.IsPendingConversion);
            Assert.Equal(7m, item.UnitCost);

            await ConvertQueueAsync(data);

            // 10 EUR at 1.25 USD per EUR, for two units; the stock keeps the later purchase's cost.
            Assert.Equal(25m, sale.LineItems[0].CostOfGoodsUSD);
            Assert.False(sale.LineItems[0].IsCostOfGoodsPending);
            Assert.Equal(7m, item.UnitCost);
            Assert.Empty(data.PendingConversions);
        }
        finally
        {
            SetInstance(prior);
        }
    }

    [Fact]
    public async Task OrderStillWaitingForItsRate_LeavesTheNewRecordsCostPending_InTheOrdersCurrency()
    {
        var prior = SetInstance(await SeededServiceAsync());
        try
        {
            var data = new CompanyData();
            data.Settings.Localization.Currency = "USD";
            data.Locations.Add(new Location { Id = "LOC-1", Name = "Warehouse" });
            data.Products.Add(new Product { Id = "PRD-1", Name = "Flour", TrackInventory = true, CostPrice = 5m });
            var order = new PurchaseOrder
            {
                Id = "PO-00001", PoNumber = "#PO-1", OrderDate = SaleDate, OriginalCurrency = "EUR",
                Total = 20m, IsPendingConversion = true,
                LineItems = [new PurchaseOrderLineItem { ProductId = "PRD-1", Quantity = 2, UnitCost = 10m }]
            };

            InventoryStockService.ReceivePurchaseOrder(data, order, [(order.LineItems[0], 2m)]);

            var item = Assert.Single(data.Inventory);
            Assert.True(item.IsPendingConversion);
            var queued = Assert.Single(data.PendingConversions);
            Assert.Equal((10m, "EUR", SaleDate), (queued.Total, queued.OriginalCurrency, queued.TransactionDate));

            await ConvertQueueAsync(data);

            Assert.Equal(12.5m, item.UnitCost);
        }
        finally
        {
            SetInstance(prior);
        }
    }

    [Fact]
    public async Task UndoingAPendingPurchase_PutsTheKnownCostBack_AndDropsTheQueuedOne()
    {
        var prior = SetInstance(await SeededServiceAsync());
        try
        {
            var data = CadCompany();
            data.Inventory.Add(new InventoryItem { Id = "INV-1", ProductId = "PRD-1", LocationId = "LOC-1", InStock = 5, UnitCost = 4m });
            var purchase = Purchase("PUR-1", "EUR", unitPrice: 10m, pending: true);

            var changes = InventoryStockService.Apply(data, purchase.LineItems, purchase, isPurchase: true);
            InventoryStockService.Revert(data, changes);

            var item = data.Inventory[0];
            Assert.False(item.IsPendingConversion);
            Assert.Equal(4m, item.UnitCost);
            Assert.Empty(data.PendingConversions);
        }
        finally
        {
            SetInstance(prior);
        }
    }

    // Undo put back the cost as it was before the sale, pending, even when that cost had converted
    // since, so the stock went back to waiting for a rate it already had.
    [Fact]
    public async Task UndoingASale_AfterThePendingCostConverted_KeepsTheConvertedCost()
    {
        var prior = SetInstance(await SeededServiceAsync());
        try
        {
            var data = CadCompany();
            data.Inventory.Add(new InventoryItem { Id = "INV-1", ProductId = "PRD-1", LocationId = "LOC-1", InStock = 5, UnitCost = 4m });
            var purchase = Purchase("PUR-1", "EUR", unitPrice: 10m, pending: true);
            InventoryStockService.Apply(data, purchase.LineItems, purchase, isPurchase: true);

            var sale = Sale(2);
            data.Revenues.Add(sale);
            var changes = InventoryStockService.Apply(data, sale.LineItems, sale, isPurchase: false);
            await ConvertQueueAsync(data);

            InventoryStockService.Revert(data, changes);

            var item = data.Inventory[0];
            Assert.False(item.IsPendingConversion);
            Assert.Equal(12.5m, item.UnitCost);
            Assert.Empty(data.PendingConversions);
        }
        finally
        {
            SetInstance(prior);
        }
    }

    // A sale deleted while its stock's cost waited was out of the books when the cost converted, and
    // undoing the delete brought its lines back still waiting, with nothing left to fill them.
    [Fact]
    public async Task UndoingASaleDelete_AfterTheCostConverted_GivesItsLinesTheCost()
    {
        var prior = SetInstance(await SeededServiceAsync());
        try
        {
            var data = CadCompany();
            data.Inventory.Add(new InventoryItem { Id = "INV-1", ProductId = "PRD-1", LocationId = "LOC-1", InStock = 5, UnitCost = 4m });
            var purchase = Purchase("PUR-1", "EUR", unitPrice: 10m, pending: true);
            InventoryStockService.Apply(data, purchase.LineItems, purchase, isPurchase: true);

            var sale = Sale(2);
            data.Revenues.Add(sale);
            InventoryStockService.Apply(data, sale.LineItems, sale, isPurchase: false);
            Assert.True(sale.LineItems[0].IsCostOfGoodsPending);

            data.Revenues.Remove(sale);
            var deleted = InventoryStockService.ApplyEdit(data, sale.LineItems, [], sale, isPurchase: false, "Revenue deleted");
            await ConvertQueueAsync(data);

            data.Revenues.Add(sale);
            InventoryStockService.Revert(data, deleted);

            Assert.False(sale.LineItems[0].IsCostOfGoodsPending);
            Assert.Equal(25m, sale.LineItems[0].CostOfGoodsUSD);
            Assert.Equal(12.5m, data.Inventory[0].UnitCost);
            Assert.False(CostOfGoodsAggregator.IsCostOfGoodsPending(data.Revenues));
            Assert.Empty(data.PendingConversions);
        }
        finally
        {
            SetInstance(prior);
        }
    }

    // The same after a later purchase with a known rate gave the stock a new cost: the sale takes the
    // cost it was waiting for, and the stock keeps the later one.
    [Fact]
    public async Task UndoingASaleDelete_AfterALaterPurchase_GivesItsLinesTheCostTheyWaitedFor()
    {
        var prior = SetInstance(await SeededServiceAsync());
        try
        {
            var data = CadCompany();
            data.Inventory.Add(new InventoryItem { Id = "INV-1", ProductId = "PRD-1", LocationId = "LOC-1", InStock = 5, UnitCost = 4m });
            var eurPurchase = Purchase("PUR-1", "EUR", unitPrice: 10m, pending: true);
            InventoryStockService.Apply(data, eurPurchase.LineItems, eurPurchase, isPurchase: true);

            var sale = Sale(2);
            data.Revenues.Add(sale);
            InventoryStockService.Apply(data, sale.LineItems, sale, isPurchase: false);
            var usdPurchase = Purchase("PUR-2", "USD", unitPrice: 7m, pending: false);
            InventoryStockService.Apply(data, usdPurchase.LineItems, usdPurchase, isPurchase: true);

            data.Revenues.Remove(sale);
            var deleted = InventoryStockService.ApplyEdit(data, sale.LineItems, [], sale, isPurchase: false, "Revenue deleted");
            await ProcessQueueAsync(data);

            data.Revenues.Add(sale);
            InventoryStockService.Revert(data, deleted);

            Assert.False(sale.LineItems[0].IsCostOfGoodsPending);
            Assert.Equal(25m, sale.LineItems[0].CostOfGoodsUSD);
            Assert.Equal(7m, data.Inventory[0].UnitCost);
            Assert.Empty(data.PendingConversions);
        }
        finally
        {
            SetInstance(prior);
        }
    }

    [Fact]
    public async Task UndoingASale_WhileTheCostStillWaits_LeavesItWaiting()
    {
        var prior = SetInstance(await SeededServiceAsync());
        try
        {
            var data = CadCompany();
            data.Inventory.Add(new InventoryItem { Id = "INV-1", ProductId = "PRD-1", LocationId = "LOC-1", InStock = 5, UnitCost = 4m });
            var purchase = Purchase("PUR-1", "EUR", unitPrice: 10m, pending: true);
            InventoryStockService.Apply(data, purchase.LineItems, purchase, isPurchase: true);
            var queued = Assert.Single(data.PendingConversions);

            var sale = Sale(2);
            data.Revenues.Add(sale);
            var changes = InventoryStockService.Apply(data, sale.LineItems, sale, isPurchase: false);
            InventoryStockService.Revert(data, changes);

            Assert.True(data.Inventory[0].IsPendingConversion);
            Assert.Same(queued, Assert.Single(data.PendingConversions));
        }
        finally
        {
            SetInstance(prior);
        }
    }

    // A margin needs both amounts: a line whose cost price can't be converted yet used to count as
    // costing nothing, which inflated the product's margin.
    [Fact]
    public async Task TopPerformingProduct_LeavesOutSalesWhoseCostCantBeConvertedYet()
    {
        var priced = DateTime.Today.AddDays(-10);
        var unpriced = DateTime.Today.AddDays(-20);
        var prior = SetInstance(await SeededServiceAsync(priced));
        try
        {
            var data = CadCompany();
            data.Products[0].CostPrice = 10m;
            foreach (var (id, date) in new[] { ("REV-1", priced), ("REV-2", unpriced) })
            {
                data.Revenues.Add(new Revenue
                {
                    Id = id, Date = date, OriginalCurrency = "USD", Total = 100m, TotalUSD = 100m,
                    PaymentStatus = RevenuePaymentStatus.Paid,
                    LineItems = [new LineItem { ProductId = "PRD-1", Quantity = 1, UnitPrice = 100m }]
                });
            }

            var range = new AnalysisDateRange { StartDate = DateTime.Today.AddMonths(-2), EndDate = DateTime.Today };
            var top = new InsightsService().GenerateRecommendations(data, range)
                .Single(r => r.Title == "Top Performing Product");

            // Only the priced sale: $100 revenue against 10 CAD = $8 cost.
            Assert.Equal(100m, top.MetricValue);
            Assert.Equal(92m, top.PercentageChange);
        }
        finally
        {
            SetInstance(prior);
        }
    }

    private static Expense Purchase(string id, string currency, decimal unitPrice, bool pending) => new()
    {
        Id = id, Date = SaleDate, OriginalCurrency = currency, Total = unitPrice * 4, Amount = unitPrice * 4,
        TotalUSD = pending ? 0m : unitPrice * 4, IsPendingConversion = pending,
        LineItems = [new LineItem { ProductId = "PRD-1", LocationId = "LOC-1", Quantity = 4, UnitPrice = unitPrice }]
    };

    /// <summary>Converts the company's queued amounts the way the app does once the rates can be fetched.</summary>
    private static async Task ConvertQueueAsync(CompanyData data)
    {
        var rates = new ExchangeRateService(new MockPlatformService(), new HttpClient(new FixedRatesHandler()));
        var queue = new PendingConversionService(new MockPlatformService(), exchangeRateService: rates);
        await queue.ReconcileWithCompanyDataAsync(data);
        await queue.ProcessPendingConversionsAsync(data);
    }

    /// <summary>
    /// A conversion pass while the company stays open, over the entries the queue already holds.
    /// Unlike opening the company, it doesn't first drop an entry nothing is waiting on.
    /// </summary>
    private static async Task ProcessQueueAsync(CompanyData data)
    {
        var rates = new ExchangeRateService(new MockPlatformService(), new HttpClient(new FixedRatesHandler()));
        var queue = new PendingConversionService(new MockPlatformService(), exchangeRateService: rates);
        foreach (var entry in data.PendingConversions.ToList())
            await queue.AddPendingConversionAsync(entry);
        await queue.ProcessPendingConversionsAsync(data);
    }

    [Fact]
    public void UsdPerNative_PurchaseOrder_UsesTheOrdersOwnRate_AndNoneWhilePending()
    {
        var order = new PurchaseOrder { OriginalCurrency = "EUR", Total = 200m, TotalUSD = 250m };
        Assert.Equal(1.25m, InventoryStockService.UsdPerNative(order));

        order.IsPendingConversion = true;
        Assert.Null(InventoryStockService.UsdPerNative(order));
    }

    [Fact]
    public async Task ReportInventoryTable_ConvertsTheUsdCostAtTheReportsEndDate()
    {
        var prior = SetInstance(await SeededServiceAsync(ReportEnd));
        try
        {
            var data = CadCompany();
            data.Inventory.Add(new InventoryItem { Id = "INV-1", ProductId = "PRD-1", LocationId = "LOC-1", InStock = 3, UnitCost = 10m });

            var table = new TableReportElement { TransactionType = TransactionType.Inventory, MaxRows = 0 };
            var config = new ReportConfiguration
            {
                Filters = new ReportFilters { StartDate = new DateTime(2024, 1, 1), EndDate = ReportEnd }
            };
            config.Elements.Add(table);

            using var renderer = new ReportRenderer(config, data);
            renderer.ComputeContinuationPlan();
            var plan = renderer.GetContinuationPlan()!;
            var row = Assert.Single(plan.CachedNormalTableData[table.Id]);
            var columns = plan.CachedNormalTableColumns[table.Id];

            Assert.Equal(CurrencyInfo.FormatAmount(12.5m, "CAD"), row[columns.IndexOf("Unit Cost")]);
            Assert.Equal(CurrencyInfo.FormatAmount(37.5m, "CAD"), row[columns.IndexOf("Total")]);
        }
        finally
        {
            SetInstance(prior);
        }
    }

    /// <summary>A rate service whose cache holds the stub's rates for <paramref name="seedDates"/> only.</summary>
    private static async Task<ExchangeRateService> SeededServiceAsync(params DateTime[] seedDates)
    {
        var service = new ExchangeRateService(new MockPlatformService(), new HttpClient(new FixedRatesHandler()));
        foreach (var d in seedDates)
            await service.GetExchangeRateAsync("USD", "EUR", d);
        return service;
    }

    private static ExchangeRateService? SetInstance(ExchangeRateService? service)
    {
        var prop = typeof(ExchangeRateService)
            .GetProperty(nameof(ExchangeRateService.Instance), BindingFlags.Public | BindingFlags.Static)!;
        var prior = (ExchangeRateService?)prop.GetValue(null);
        prop.SetValue(null, service);
        return prior;
    }

    private sealed class FixedRatesHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = $$"""{ "success": true, "base": "USD", "rates": { "EUR": {{UsdToEur}}, "CAD": {{UsdToCad}} } }""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class MockPlatformService : IPlatformService
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
        public Task<string> GetBiometricAvailabilityDetailsAsync() => Task.FromResult("Not supported");
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
