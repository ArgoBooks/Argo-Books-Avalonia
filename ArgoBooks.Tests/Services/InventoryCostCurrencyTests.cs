using System.Net;
using System.Reflection;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
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
    public async Task CostPriceUSD_RateMissing_IsZeroRatherThanTheUnconvertedPrice()
    {
        var prior = SetInstance(await SeededServiceAsync());
        try
        {
            var data = CadCompany();

            Assert.Equal(0m, InventoryStockService.CostPriceUSD(data, data.Products[0], SaleDate));
        }
        finally
        {
            SetInstance(prior);
        }
    }

    [Fact]
    public void CostPriceUSD_UsdCompany_IsTheCostPrice()
    {
        var data = new CompanyData();
        data.Settings.Localization.Currency = "USD";
        var product = new Product { Id = "PRD-1", CostPrice = 12.5m };

        Assert.Equal(12.5m, InventoryStockService.CostPriceUSD(data, product, SaleDate));
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
