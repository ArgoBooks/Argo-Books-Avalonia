using System.Net;
using System.Text;
using System.Text.Json;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using ClosedXML.Excel;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// A purchase's stock is costed at the purchase's own date and currency, so an import that changes
/// either costs it again, as editing the purchase in the app does (docs/Calculations.md §14).
/// </summary>
[Collection("ExchangeRateSingleton")]
public class SpreadsheetImportStockPurchaseCostTests : IDisposable
{
    private static readonly DateTime Bought = new(2025, 3, 3);
    private static readonly DateTime Moved = new(2025, 4, 7);
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"purchase-cost-{Guid.NewGuid():N}.xlsx");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
        GC.SuppressFinalize(this);
    }

    /// <summary>USD to EUR is 0.8 on <see cref="Bought"/> and 0.5 on <see cref="Moved"/>, so a euro is 1.25 then 2 dollars.</summary>
    private static async Task<SpreadsheetImportService> ImporterAsync()
    {
        var rates = new ExchangeRateService(new BrowserPlatformService(), new HttpClient(new DateRateHandler()));
        await rates.GetExchangeRateAsync("USD", "EUR", Bought);
        await rates.GetExchangeRateAsync("USD", "EUR", Moved);
        return new SpreadsheetImportService(exchangeRateService: rates);
    }

    /// <summary>10 units of tracked flour bought at 3 each in <paramref name="currency"/>, costed at the rate on <see cref="Bought"/>.</summary>
    private static CompanyData WithStockPurchase(string currency)
    {
        var data = new CompanyData();
        data.Settings.Localization.Currency = "USD";
        data.Locations.Add(new Location { Id = "LOC-1", Name = "Shop" });
        data.Products.Add(new Product { Id = "PRD-1", Name = "Flour", TrackInventory = true });
        var usdPerUnit = currency == "EUR" ? 1.25m : 1m;
        var purchase = new Expense
        {
            Id = "PUR-1", Date = Bought, Description = "Flour", Quantity = 10, UnitPrice = 3m, Amount = 30m,
            Total = 30m, TotalUSD = 30m * usdPerUnit, OriginalCurrency = currency,
            LineItems = [new LineItem { ProductId = "PRD-1", Description = "Flour", Quantity = 10, UnitPrice = 3m }]
        };
        InventoryStockService.Apply(data, purchase.LineItems, purchase, isPurchase: true);
        data.Expenses.Add(purchase);
        return data;
    }

    // Only a changed product, quantity or price costed the stock again, so moving a euro purchase
    // to another day kept the cost converted at the old day's rate.
    [Fact]
    public async Task SheetUpdate_MovingAPurchaseToAnotherDay_CostsItsStockAtThatDaysRate()
    {
        var data = WithStockPurchase("EUR");
        Assert.Equal(3.75m, Assert.Single(data.Inventory).UnitCost);

        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Expenses");
            ws.Cell(1, 1).Value = "ID";
            ws.Cell(1, 2).Value = "Date";
            ws.Cell(2, 1).Value = "PUR-1";
            ws.Cell(2, 2).Value = Moved;
            wb.SaveAs(_path);
        }
        await (await ImporterAsync()).ImportFromExcelAsync(_path, data, new ImportOptions());

        var item = Assert.Single(data.Inventory);
        Assert.Equal((6m, 10m), (item.UnitCost, item.InStock));
        Assert.True(Assert.Single(Assert.Single(data.Expenses).LineItems).IsStockPurchase);
    }

    [Fact]
    public async Task AiUpdate_ChangingAPurchasesCurrency_CostsItsStockInThatCurrency()
    {
        var data = WithStockPurchase("USD");
        Assert.Equal(3m, Assert.Single(data.Inventory).UnitCost);

        var chunk = new LlmProcessedData { EntityType = SpreadsheetSheetType.Expenses };
        chunk.Entities.Add(JsonDocument.Parse("""{ "id": "PUR-1", "originalCurrency": "EUR" }""").RootElement.Clone());
        (await ImporterAsync()).ImportProcessedEntities(data, [chunk], "Expenses");

        var item = Assert.Single(data.Inventory);
        Assert.Equal((3.75m, 10m), (item.UnitCost, item.InStock));
    }

    [Fact]
    public async Task AiUpdate_LeavingTheDateAndCurrency_KeepsTheStocksCost()
    {
        var data = WithStockPurchase("EUR");
        data.Inventory[0].UnitCost = 4m;

        var chunk = new LlmProcessedData { EntityType = SpreadsheetSheetType.Expenses };
        chunk.Entities.Add(JsonDocument.Parse($$"""{ "id": "PUR-1", "date": "{{Bought:yyyy-MM-dd}}", "originalCurrency": "eur", "notes": "x" }""").RootElement.Clone());
        (await ImporterAsync()).ImportProcessedEntities(data, [chunk], "Expenses");

        Assert.Equal(4m, Assert.Single(data.Inventory).UnitCost);
    }

    private sealed class DateRateHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = request.RequestUri?.Query ?? "";
            var usdToEur = query.Contains(Moved.ToString("yyyy-MM-dd")) ? 0.5m : 0.8m;
            var payload = $$"""{ "success": true, "base": "USD", "rates": { "EUR": {{usdToEur}} } }""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}
