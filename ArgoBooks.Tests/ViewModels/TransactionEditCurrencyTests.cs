using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Editing an expense or revenue entered in another currency, or still waiting for its rate.
/// </summary>
public class TransactionEditCurrencyTests : ModalViewModelTestBase
{
    private static readonly PropertyInfo RatesInstance =
        typeof(ExchangeRateService).GetProperty(nameof(ExchangeRateService.Instance), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>
    /// A row keeps one USD unit price, the average of its lines. Loading every line at that average
    /// turned a 1,000 laptop and ten 10 mice into eleven lines of 505, five times the real total.
    /// </summary>
    [Fact]
    public async Task EditingARowEnteredInAnotherCurrency_KeepsEachLinesOwnPrice()
    {
        UseRates(new NoRatesHandler());
        Company.Settings.Localization.Currency = "USD";
        Company.Products.Add(new Product { Id = "P1", Name = "Laptop" });
        Company.Products.Add(new Product { Id = "P2", Name = "Mouse" });
        // Entered in CAD at 0.5 USD per CAD, before the company moved to USD.
        Company.Expenses.Add(new Expense
        {
            Id = "PUR-2026-00001",
            Date = new DateTime(2026, 3, 1),
            OriginalCurrency = "CAD",
            LineItems =
            [
                new LineItem { ProductId = "P1", Description = "Laptop", Quantity = 1, UnitPrice = 1000m },
                new LineItem { ProductId = "P2", Description = "Mouse", Quantity = 10, UnitPrice = 10m }
            ],
            Quantity = 11,
            UnitPrice = 505m,
            Amount = 1100m,
            Total = 1100m,
            TotalUSD = 550m,
            UnitPriceUSD = 252.5m
        });

        var vm = new ExpenseModalsViewModel();
        vm.OpenEditModal(new ExpenseDisplayItem { Id = "PUR-2026-00001" });
        await vm.SaveExpenseCommand.ExecuteAsync(null);

        var expense = Company.Expenses.Single();
        Assert.Equal(("CAD", 1100m), (expense.OriginalCurrency, expense.Total));
        Assert.Equal([1000m, 10m], expense.LineItems.Select(li => li.UnitPrice));
    }

    /// <summary>
    /// The edit form loaded the entry converted into the company currency and saved it that way, so
    /// changing only the notes on a EUR 100 expense turned it into a USD 125 one.
    /// </summary>
    [Theory]
    [InlineData("USD")]
    [InlineData("CAD")]
    public async Task EditingOnlyTheNotesOfAEuroExpense_KeepsItInEurosAtItsDatesRate(string companyCurrency)
    {
        UseRates(new EurHandler(usdToEur: 0.8m));
        Company.Settings.Localization.Currency = companyCurrency;
        Company.Expenses.Add(EuroEntry(new Expense()));
        var before = Snapshot(Company.Expenses.Single());

        await EditNotesAsync(isExpense: true);

        var expense = Company.Expenses.Single();
        Assert.Equal(
            "EUR total=100 lines=30,20 tax=10 ship=15 disc=5 pending=False totalUSD=125 taxUSD=12.5 shipUSD=18.75 discUSD=6.25 unitUSD=31.25 notes=Edited",
            Snapshot(expense));

        Undo();
        Assert.Equal(before, Snapshot(expense));
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("CAD")]
    public async Task EditingOnlyTheNotesOfAEuroRevenue_KeepsItInEurosAtItsDatesRate(string companyCurrency)
    {
        UseRates(new EurHandler(usdToEur: 0.8m));
        Company.Settings.Localization.Currency = companyCurrency;
        Company.Revenues.Add(EuroEntry(new Revenue()));
        var before = Snapshot(Company.Revenues.Single());

        await EditNotesAsync(isExpense: false);

        var revenue = Company.Revenues.Single();
        Assert.Equal(
            "EUR total=100 lines=30,20 tax=10 ship=15 disc=5 pending=False totalUSD=125 taxUSD=12.5 shipUSD=18.75 discUSD=6.25 unitUSD=31.25 notes=Edited",
            Snapshot(revenue));

        Undo();
        Assert.Equal(before, Snapshot(revenue));
    }

    /// <summary>
    /// With no rate for the entry's date it waits for one in its own currency, never converted at
    /// another date's rate or relabelled in the company's.
    /// </summary>
    [Theory]
    [InlineData("USD")]
    [InlineData("CAD")]
    public async Task EditingAEuroExpenseWithNoRate_KeepsItInEurosAndQueuesItsConversion(string companyCurrency)
    {
        var service = UseRates(new NoRatesHandler());
        Company.Settings.Localization.Currency = companyCurrency;
        Company.Expenses.Add(EuroEntry(new Expense()));
        var before = Snapshot(Company.Expenses.Single());

        await EditNotesAsync(isExpense: true);

        var expense = Company.Expenses.Single();
        Assert.Equal(("EUR", 100m, true), (expense.OriginalCurrency, expense.Total, expense.IsPendingConversion));
        Assert.Equal([30m, 20m], expense.LineItems.Select(li => li.UnitPrice));
        Assert.Equal(("EUR", 100m), Queued(expense.Id));
        Assert.Equal(("EUR", 100m), await Queued(service, expense.Id));

        Undo();
        Assert.Equal(before, Snapshot(expense));
        Assert.Equal((null, null), Queued(expense.Id));
        Assert.Equal((null, null), await Queued(service, expense.Id));
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("CAD")]
    public async Task EditingAEuroRevenueWithNoRate_KeepsItInEurosAndQueuesItsConversion(string companyCurrency)
    {
        var service = UseRates(new NoRatesHandler());
        Company.Settings.Localization.Currency = companyCurrency;
        Company.Revenues.Add(EuroEntry(new Revenue()));
        var before = Snapshot(Company.Revenues.Single());

        await EditNotesAsync(isExpense: false);

        var revenue = Company.Revenues.Single();
        Assert.Equal(("EUR", 100m, true), (revenue.OriginalCurrency, revenue.Total, revenue.IsPendingConversion));
        Assert.Equal([30m, 20m], revenue.LineItems.Select(li => li.UnitPrice));
        Assert.Equal(("EUR", 100m), Queued(revenue.Id));
        Assert.Equal(("EUR", 100m), await Queued(service, revenue.Id));

        Undo();
        Assert.Equal(before, Snapshot(revenue));
        Assert.Equal((null, null), Queued(revenue.Id));
        Assert.Equal((null, null), await Queued(service, revenue.Id));
    }

    [Fact]
    public void EditingAEuroExpense_ShowsItsOwnAmountsInEuros()
    {
        UseRates(new EurHandler(usdToEur: 0.8m));
        Company.Settings.Localization.Currency = "USD";
        Company.Expenses.Add(EuroEntry(new Expense()));

        var vm = new ExpenseModalsViewModel();
        vm.OpenEditModal(new ExpenseDisplayItem { Id = EntryId });

        Assert.Equal([30m, 20m], vm.LineItems.Select(li => li.UnitPrice ?? 0));
        Assert.Equal((10m, 15m, 5m), (vm.ModalTaxAmount, vm.ModalShipping, vm.ModalDiscount));
        Assert.Equal("€100.00 EUR", vm.TotalFormatted);
        Assert.Equal("€60.00", vm.LineItems[0].AmountFormatted);
        Assert.False(vm.HasEditModalChanges);
    }

    private const string EntryId = "TXN-2026-00001";

    /// <summary>
    /// EUR 100 on 1 March, stored at 1.25 USD per EUR: two lines of 30 and one of 20, plus tax 10
    /// and shipping 15, less a discount of 5.
    /// </summary>
    private static T EuroEntry<T>(T entry) where T : Transaction
    {
        entry.Id = EntryId;
        entry.Date = new DateTime(2026, 3, 1);
        entry.OriginalCurrency = "EUR";
        entry.LineItems =
        [
            new LineItem { ProductId = "P1", Description = "Widget", Quantity = 2, UnitPrice = 30m },
            new LineItem { ProductId = "P2", Description = "Gadget", Quantity = 1, UnitPrice = 20m }
        ];
        entry.Quantity = 3;
        entry.UnitPrice = 25m;
        entry.Amount = 80m;
        entry.TaxAmount = 10m;
        entry.ShippingCost = 15m;
        entry.Discount = 5m;
        entry.Total = 100m;
        entry.TotalUSD = 125m;
        entry.TaxAmountUSD = 12.5m;
        entry.ShippingCostUSD = 18.75m;
        entry.DiscountUSD = 6.25m;
        entry.UnitPriceUSD = 31.25m;
        entry.Notes = "Original";
        return entry;
    }

    private async Task EditNotesAsync(bool isExpense)
    {
        Company.Products.Add(new Product { Id = "P1", Name = "Widget" });
        Company.Products.Add(new Product { Id = "P2", Name = "Gadget" });

        if (isExpense)
        {
            var vm = new ExpenseModalsViewModel();
            vm.OpenEditModal(new ExpenseDisplayItem { Id = EntryId });
            vm.ModalNotes = "Edited";
            await vm.SaveExpenseCommand.ExecuteAsync(null);
        }
        else
        {
            var vm = new RevenueModalsViewModel();
            vm.OpenEditModal(new RevenueDisplayItem { Id = EntryId });
            vm.ModalNotes = "Edited";
            await vm.SaveRevenueCommand.ExecuteAsync(null);
        }
    }

    private static string Snapshot(Transaction t) =>
        $"{t.OriginalCurrency} total={N(t.Total)} lines={string.Join(",", t.LineItems.Select(li => N(li.UnitPrice)))} " +
        $"tax={N(t.TaxAmount)} ship={N(t.ShippingCost)} disc={N(t.Discount)} pending={t.IsPendingConversion} " +
        $"totalUSD={N(t.TotalUSD)} taxUSD={N(t.TaxAmountUSD)} shipUSD={N(t.ShippingCostUSD)} discUSD={N(t.DiscountUSD)} " +
        $"unitUSD={N(t.UnitPriceUSD)} notes={t.Notes}";

    private static string N(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private (string? Currency, decimal? Total) Queued(string id)
    {
        var entry = Company.PendingConversions.SingleOrDefault(p => p.TransactionId == id);
        return (entry?.OriginalCurrency, entry?.Total);
    }

    private static async Task<(string? Currency, decimal? Total)> Queued(PendingConversionService service, string id)
    {
        var probe = new CompanyData();
        await service.ReconcileWithCompanyDataAsync(probe);
        var entry = probe.PendingConversions.SingleOrDefault(p => p.TransactionId == id);
        return (entry?.OriginalCurrency, entry?.Total);
    }

    [Fact]
    public async Task UndoingAnEditToAPendingExpense_QueuesItsOriginalAmountAgain()
    {
        var service = UseCurrencyWithNoRate();
        var vm = new ExpenseModalsViewModel();
        vm.OpenAddModal();
        FillLine(vm.LineItems.First(), vm.ProductOptions, 100m);
        await vm.SaveExpenseCommand.ExecuteAsync(null);
        var id = Company.Expenses.Single().Id;

        vm.OpenEditModal(new ExpenseDisplayItem { Id = id });
        vm.LineItems.First().UnitPrice = 250m;
        await vm.SaveExpenseCommand.ExecuteAsync(null);

        Undo();
        var afterUndo = (QueuedTotal(id), await QueuedTotal(service, id));
        Redo();
        var afterRedo = (QueuedTotal(id), await QueuedTotal(service, id));

        Assert.Equal((100m, 100m), afterUndo);
        Assert.Equal((250m, 250m), afterRedo);
    }

    [Fact]
    public async Task UndoingAnEditToAPendingRevenue_QueuesItsOriginalAmountAgain()
    {
        var service = UseCurrencyWithNoRate();
        var vm = new RevenueModalsViewModel();
        vm.OpenAddModal();
        FillLine(vm.LineItems.First(), vm.ProductOptions, 100m);
        await vm.SaveRevenueCommand.ExecuteAsync(null);
        var id = Company.Revenues.Single().Id;

        vm.OpenEditModal(new RevenueDisplayItem { Id = id });
        vm.LineItems.First().UnitPrice = 250m;
        await vm.SaveRevenueCommand.ExecuteAsync(null);

        Undo();
        var afterUndo = (QueuedTotal(id), await QueuedTotal(service, id));
        Redo();
        var afterRedo = (QueuedTotal(id), await QueuedTotal(service, id));

        Assert.Equal((100m, 100m), afterUndo);
        Assert.Equal((250m, 250m), afterRedo);
    }

    /// <summary>
    /// A EUR company whose rate lookups all come back empty, so every save waits for its rate.
    /// Returns the conversion service the view models queue with.
    /// </summary>
    private PendingConversionService UseCurrencyWithNoRate()
    {
        Company.Settings.Localization.Currency = "EUR";
        Company.Products.Add(new Product { Id = "P1", Name = "Widget" });
        return UseRates(new NoRatesHandler());
    }

    /// <summary>
    /// Serves rates from <paramref name="handler"/> for the test. Returns the conversion service the
    /// view models queue with.
    /// </summary>
    private PendingConversionService UseRates(HttpMessageHandler handler)
    {
        UseNoExchangeRates(); // puts the shared rate service back after the test
        RatesInstance.SetValue(null, new ExchangeRateService(new NoDiskPlatform(), new HttpClient(handler)));
        return PendingConversionService.Instance ?? new PendingConversionService(new NoDiskPlatform());
    }

    private static void FillLine(TransactionLineItemBase line, IEnumerable<ProductOption> options, decimal unitPrice)
    {
        line.SelectedProduct = options.First(p => p.Id == "P1");
        line.Quantity = 1;
        line.UnitPrice = unitPrice;
    }

    private decimal? QueuedTotal(string id) =>
        Company.PendingConversions.SingleOrDefault(p => p.TransactionId == id)?.Total;

    /// <summary>
    /// The service's own copy of the queue: reconciling into an empty company copies all of it
    /// across, since none of those rows exist there to have been converted.
    /// </summary>
    private static async Task<decimal?> QueuedTotal(PendingConversionService service, string id)
    {
        var probe = new CompanyData();
        await service.ReconcileWithCompanyDataAsync(probe);
        return probe.PendingConversions.SingleOrDefault(p => p.TransactionId == id)?.Total;
    }

    private sealed class NoRatesHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "success": true, "base": "USD", "rates": {} }""", Encoding.UTF8, "application/json")
            });
    }

    private sealed class EurHandler(decimal usdToEur) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = $$"""{ "success": true, "base": "USD", "rates": { "EUR": {{usdToEur.ToString(CultureInfo.InvariantCulture)}} } }""";
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
        public string GetDefaultDocumentsPath() => Path.GetTempPath();
        public string GetLogsPath() => Path.GetTempPath();
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
        public void RegisterFileTypeAssociations(string iconPath) { }
        public StringComparer PathComparer => StringComparer.Ordinal;
    }
}
