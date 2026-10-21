using System.Net;
using System.Text.Json;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Assets;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for CRA's Capital Cost Allowance rates: the file built into the app, the check every copy passes, the download that replaces it, and depreciation by CRA's method.
/// </summary>
public class CcaRateTests : IDisposable
{
    private readonly string _cache = Path.Combine(Path.GetTempPath(), "argo-cca-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cache))
            Directory.Delete(_cache, recursive: true);
        GC.SuppressFinalize(this);
    }

    private CcaRateService Rates() => new(new CacheOnlyPlatformService(_cache));

    private static string FileJson(int revision, decimal vehicleRate = 30m) => $$"""
        {
          "revision": {{revision}},
          "reviewedOn": "2027-06-15",
          "classes": [
            { "number": "8", "description": "Equipment", "rates": [ { "percent": 20 } ] },
            { "number": "10", "description": "Vehicles", "rates": [ { "percent": {{vehicleRate}} } ] }
          ],
          "firstYearRules": [ { "name": "Half-year rule", "factor": 0.5 } ],
          "typeDefaults": { "Vehicle": "10", "Equipment": "8" }
        }
        """;

    #region The built-in file

    [Fact]
    public void BuiltInFile_LoadsAndPassesItsOwnCheck()
    {
        var file = Rates().Current;

        Assert.NotNull(file);
        Assert.True(file.Revision >= 1);
    }

    [Theory]
    [InlineData(AssetType.Building, "1", 4)]
    [InlineData(AssetType.Vehicle, "10", 30)]
    [InlineData(AssetType.Equipment, "8", 20)]
    [InlineData(AssetType.FurnitureAndFixtures, "8", 20)]
    [InlineData(AssetType.ComputerEquipment, "50", 55)]
    [InlineData(AssetType.Other, "8", 20)]
    public void BuiltInFile_GivesEachTypeItsClassAndRate(AssetType type, string expectedClass, decimal expectedRate)
    {
        var file = Rates().Current!;

        var number = file.DefaultClassFor(type);

        Assert.Equal(expectedClass, number);
        Assert.Equal(expectedRate, file.RateOn(number!, new DateTime(2026, 6, 1)));
    }

    [Fact]
    public void BuiltInFile_GivesLandNoClass()
    {
        Assert.Null(Rates().Current!.DefaultClassFor(AssetType.Land));
    }

    [Theory]
    [InlineData("2015-06-01", 0.5)]
    [InlineData("2020-06-01", 1.5)]
    [InlineData("2024-06-01", 1.0)]
    [InlineData("2026-06-01", 1.5)]
    [InlineData("2031-06-01", 1.0)]
    [InlineData("2035-06-01", 0.5)]
    public void BuiltInFile_FirstYearShareFollowsThePurchaseDate(string date, decimal expected)
    {
        Assert.Equal(expected, Rates().Current!.FirstYearFactorOn("10", DateTime.Parse(date)));
    }

    [Theory]
    [InlineData("2024-04-15", 1.0)]
    [InlineData("2024-04-16", 1.8182)]
    [InlineData("2026-12-31", 1.8182)]
    [InlineData("2027-01-01", 1.5)]
    public void BuiltInFile_ComputersBoughtFromApril2024To2026AreWrittenOffInTheFirstYear(string date, decimal expected)
    {
        Assert.Equal(expected, Rates().Current!.FirstYearFactorOn("50", DateTime.Parse(date)));
    }

    [Fact]
    public void BuiltInFile_ComputerWrittenOffInTheFirstYear_HasNothingLeftAfter()
    {
        var file = Rates().Current!;
        var laptop = new FixedAsset
        {
            Id = "AST-001", Name = "Laptop", Type = AssetType.ComputerEquipment, PurchaseDate = new DateTime(2026, 2, 1),
            Cost = 2000m, Currency = "CAD", Method = DepreciationMethod.DecliningBalance,
            RatePercent = file.RateOn("50", new DateTime(2026, 2, 1)), CcaClass = "50",
            FirstYearFactor = file.FirstYearFactorOn("50", new DateTime(2026, 2, 1))
        };

        var schedule = AssetDepreciation.Schedule(laptop);

        Assert.Equal(2000m, Assert.Single(schedule).Amount);
    }

    [Fact]
    public void BuiltInFile_SmallToolsSkipTheHalfYearRule()
    {
        Assert.Equal(1.0m, Rates().Current!.FirstYearFactorOn("12", new DateTime(2015, 6, 1)));
    }

    #endregion

    #region Check

    [Fact]
    public void Check_AcceptsAWellFormedFile()
    {
        Assert.Empty(CcaRateValidator.Validate(JsonSerializer.Deserialize<CcaRateFile>(FileJson(2))));
    }

    [Fact]
    public void Check_RefusesARateOfZero()
    {
        Assert.NotEmpty(CcaRateValidator.Validate(JsonSerializer.Deserialize<CcaRateFile>(FileJson(2, vehicleRate: 0))));
    }

    [Fact]
    public void Check_RefusesADefaultNamingAMissingClass()
    {
        var file = JsonSerializer.Deserialize<CcaRateFile>(FileJson(2))!;
        file.TypeDefaults["Building"] = "1";

        Assert.Contains(CcaRateValidator.Validate(file), p => p.Contains("class 1"));
    }

    [Fact]
    public void Check_RefusesOverlappingRates()
    {
        var file = JsonSerializer.Deserialize<CcaRateFile>(FileJson(2))!;
        file.Classes[0].Rates =
        [
            new CcaRate { Percent = 20, To = new DateTime(2025, 12, 31) },
            new CcaRate { Percent = 25, From = new DateTime(2025, 6, 1) }
        ];

        Assert.Contains(CcaRateValidator.Validate(file), p => p.Contains("overlap"));
    }

    [Fact]
    public void Check_RefusesTwoGeneralFirstYearRulesForTheSameDay()
    {
        var file = JsonSerializer.Deserialize<CcaRateFile>(FileJson(2))!;
        file.FirstYearRules.Add(new CcaFirstYearRule { Name = "Another", Factor = 1.5m, From = new DateTime(2025, 1, 1) });

        Assert.NotEmpty(CcaRateValidator.Validate(file));
    }

    [Fact]
    public void Check_RefusesADuplicateClass()
    {
        var file = JsonSerializer.Deserialize<CcaRateFile>(FileJson(2))!;
        file.Classes.Add(new CcaClass { Number = "8", Description = "Again", Rates = [new CcaRate { Percent = 20 }] });

        Assert.Contains(CcaRateValidator.Validate(file), p => p.Contains("more than once"));
    }

    #endregion

    #region Download

    private CcaRateUpdateService Updater(CcaRateService rates, HttpStatusCode status, string body) =>
        new(rates, new StubHandler(status, body));

    [Fact]
    public async Task Download_ANewerRevisionReplacesTheRates()
    {
        var rates = Rates();
        var builtIn = rates.Current!.Revision;

        var updated = await Updater(rates, HttpStatusCode.OK, FileJson(builtIn + 1, vehicleRate: 35)).TryUpdateAsync();

        Assert.True(updated);
        Assert.Equal(35m, rates.Current!.RateOn("10", DateTime.Today));
        Assert.Equal(35m, Rates().Current!.RateOn("10", DateTime.Today));
    }

    [Fact]
    public async Task Download_TheSameRevisionIsIgnored()
    {
        var rates = Rates();

        var updated = await Updater(rates, HttpStatusCode.OK, FileJson(rates.Current!.Revision, vehicleRate: 35)).TryUpdateAsync();

        Assert.False(updated);
        Assert.Equal(30m, rates.Current!.RateOn("10", DateTime.Today));
    }

    [Fact]
    public async Task Download_AFileThatFailsTheCheckIsIgnored()
    {
        var rates = Rates();

        var updated = await Updater(rates, HttpStatusCode.OK, FileJson(999, vehicleRate: 0)).TryUpdateAsync();

        Assert.False(updated);
        Assert.False(File.Exists(Path.Combine(_cache, "Assets", CcaRateService.FileName)));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "")]
    [InlineData(HttpStatusCode.OK, "<html>Sign in to the network</html>")]
    public async Task Download_AMissingOrUnreadableFileChangesNothing(HttpStatusCode status, string body)
    {
        var rates = Rates();

        Assert.False(await Updater(rates, status, body).TryUpdateAsync());
        Assert.Equal(30m, rates.Current!.RateOn("10", DateTime.Today));
    }

    #endregion

    #region Depreciation by CRA's method

    private static FixedAsset Truck(decimal firstYearFactor) => new()
    {
        Id = "AST-001",
        Name = "Truck",
        Type = AssetType.Vehicle,
        PurchaseDate = new DateTime(2026, 3, 15),
        Cost = 10000m,
        Currency = "CAD",
        Method = DepreciationMethod.DecliningBalance,
        RatePercent = 30m,
        CcaClass = "10",
        FirstYearFactor = firstYearFactor
    };

    [Fact]
    public void Cca_FirstYearTakesItsShareOfTheRate_ThenTheRateOfWhatIsLeft()
    {
        var schedule = AssetDepreciation.Schedule(Truck(0.5m));

        Assert.Equal(2026, schedule[0].Year);
        Assert.Equal(1500m, schedule[0].Amount);
        Assert.Equal(2550m, schedule[1].Amount);
        Assert.Equal(5950m, schedule[1].BookValueAfter);
    }

    [Fact]
    public void Cca_AcceleratedFirstYear()
    {
        var schedule = AssetDepreciation.Schedule(Truck(1.5m));

        Assert.Equal(4500m, schedule[0].Amount);
        Assert.Equal(1650m, schedule[1].Amount);
    }

    [Fact]
    public void Cca_TheYearItLeavesTheBooksHasNone()
    {
        var truck = Truck(0.5m);
        truck.DisposedDate = new DateTime(2027, 11, 1);
        truck.DisposalKind = AssetDisposalKind.Sold;
        truck.SalePrice = 9000m;

        Assert.Equal(0m, AssetDepreciation.Between(truck, new DateTime(2027, 1, 1), new DateTime(2027, 12, 31)));
        Assert.Equal(8500m, AssetDepreciation.BookValueAtDisposal(truck));
        Assert.Equal(500m, AssetDepreciation.GainOnDisposal(truck));
    }

    [Fact]
    public void Cca_ACarriedOverAssetGetsNoFirstYearShare()
    {
        var truck = Truck(0.5m);
        truck.PurchaseDate = new DateTime(2023, 5, 1);
        truck.TrackedFrom = new DateTime(2026, 1, 1);
        truck.PriorDepreciation = 4000m;

        Assert.Equal(1800m, AssetDepreciation.Between(truck, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)));
    }

    [Fact]
    public void ExpenseLink_WithAClass_FollowsCrasMethod_AndKeepsARateChangedByHand()
    {
        var data = new CompanyData();
        var expense = new Expense
        {
            Id = "EXP-1", Date = new DateTime(2026, 3, 15), Description = "Truck", OriginalCurrency = "CAD",
            Total = 10000m, TotalUSD = 7300m
        };
        data.Expenses.Add(expense);
        var cca = new ExpenseAssetLink.CcaChoice("10", 30m, 1.5m);

        ExpenseAssetLink.Apply(data, expense, null, null, true, AssetType.Vehicle, null, cca);
        var asset = Assert.Single(data.Assets);
        Assert.True(asset.FollowsCca);
        Assert.Equal(30m, asset.RatePercent);

        asset.RatePercent = 25m;
        ExpenseAssetLink.Apply(data, expense, 10000m, "CAD", true, AssetType.Vehicle, null, cca);

        Assert.Equal(25m, asset.RatePercent);
    }

    #endregion

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    private sealed class CacheOnlyPlatformService(string cachePath) : IPlatformService
    {
        public PlatformType Platform => PlatformType.Linux;
        public string GetAppDataPath() => cachePath;
        public string GetCachePath() => cachePath;
        public void EnsureDirectoryExists(string path) => Directory.CreateDirectory(path);
        public bool SupportsFileSystem => true;
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
