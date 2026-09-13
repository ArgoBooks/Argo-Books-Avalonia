using System.Net;
using System.Text;
using System.Text.Json;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using ClosedXML.Excel;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for in-cell currency detection: the <see cref="CurrencyImportPreparer"/> pre-pass and
/// its end-to-end effect on a Tier 1 import (per-row OriginalCurrency), plus Tier 2 normalization
/// of a symbol/code the LLM emits.
/// </summary>
public class CurrencyImportPreparerTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { if (File.Exists(f)) File.Delete(f); } catch { /* best effort */ }
    }

    private string NewTempXlsx() { var p = Path.Combine(Path.GetTempPath(), $"argo-cur-{Guid.NewGuid():N}.xlsx"); _tempFiles.Add(p); return p; }

    /// <summary>A Revenue sheet whose Total cells carry mixed in-cell currencies.</summary>
    private string BuildMixedCurrencyRevenueSheet()
    {
        var path = NewTempXlsx();
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Revenue");
        ws.Cell(1, 1).Value = "ID";
        ws.Cell(1, 2).Value = "Date";
        ws.Cell(1, 3).Value = "Total";
        ws.Cell(1, 4).Value = "Description";

        var rows = new[]
        {
            ("R1", "£100",     "London sale"),   // unambiguous -> GBP
            ("R2", "$50 CAD",  "Toronto sale"),  // explicit code -> CAD
            ("R3", "€20",      "Paris sale"),    // unambiguous -> EUR
            ("R4", "$30",      "USD warehouse"), // ambiguous "$" (description has "USD" but is not scanned)
        };
        for (int i = 0; i < rows.Length; i++)
        {
            ws.Cell(i + 2, 1).Value = rows[i].Item1;
            ws.Cell(i + 2, 2).Value = $"2026-01-0{i + 1}";
            ws.Cell(i + 2, 3).Value = rows[i].Item2; // text cells, symbol/code preserved
            ws.Cell(i + 2, 4).Value = rows[i].Item3;
        }
        wb.SaveAs(path);
        return path;
    }

    /// <summary>A Revenue sheet whose Total cells carry the "¥" glyph in the NUMBER FORMAT, with
    /// JPY (0 decimals) and CNY (2 decimals) distinguished only by their decimal formatting.</summary>
    private string BuildYenFormatRevenueSheet()
    {
        var path = NewTempXlsx();
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Revenue");
        ws.Cell(1, 1).Value = "ID";
        ws.Cell(1, 2).Value = "Date";
        ws.Cell(1, 3).Value = "Total";
        ws.Cell(1, 4).Value = "Description";

        // Row 1: yen, formatted with 0 decimals -> "¥95,000"
        ws.Cell(2, 1).Value = "R1";
        ws.Cell(2, 2).Value = "2026-01-01";
        ws.Cell(2, 3).Value = 95000;
        ws.Cell(2, 3).Style.NumberFormat.Format = "\"¥\"#,##0";
        ws.Cell(2, 4).Value = "Tokyo sale";

        // Row 2: yuan, formatted with 2 decimals -> "¥1,200.00"
        ws.Cell(3, 1).Value = "R2";
        ws.Cell(3, 2).Value = "2026-01-02";
        ws.Cell(3, 3).Value = 1200;
        ws.Cell(3, 3).Style.NumberFormat.Format = "\"¥\"#,##0.00";
        ws.Cell(3, 4).Value = "Beijing sale";

        wb.SaveAs(path);
        return path;
    }

    [Fact]
    public void ScanWorkbook_YenSymbol_DisambiguatedByDecimalFormat_NotAmbiguous()
    {
        var path = BuildYenFormatRevenueSheet();

        var scan = CurrencyImportPreparer.ScanWorkbook(path, RevenueAnalysis());

        var rows = scan.Resolved["Revenue"];
        Assert.Equal("JPY", rows[0]); // "¥"#,##0    (0 decimals) -> Japanese Yen
        Assert.Equal("CNY", rows[1]); // "¥"#,##0.00 (2 decimals) -> Chinese Yuan

        // ¥ is resolved from the cell's decimal formatting, so it is not left ambiguous.
        Assert.DoesNotContain(scan.Ambiguities, a => a.Symbol == "¥");
    }

    private static SpreadsheetAnalysisResult RevenueAnalysis() => new()
    {
        Sheets =
        [
            new SheetAnalysis
            {
                SourceSheetName = "Revenue",
                DetectedType = SpreadsheetSheetType.Revenue,
                Tier = ProcessingTier.Tier1_Mapping,
                IsIncluded = true
            }
        ]
    };

    // ─── Pre-pass scan ───────────────────────────────────────────────────────

    [Fact]
    public void ScanWorkbook_ResolvesExplicitAndUnambiguous_FlagsAmbiguousDollar()
    {
        var path = BuildMixedCurrencyRevenueSheet();

        var scan = CurrencyImportPreparer.ScanWorkbook(path, RevenueAnalysis());

        var rows = scan.Resolved["Revenue"];
        Assert.Equal("GBP", rows[0]);
        Assert.Equal("CAD", rows[1]);
        Assert.Equal("EUR", rows[2]);
        Assert.False(rows.ContainsKey(3)); // ambiguous "$" not resolved yet

        var dollar = Assert.Single(scan.Ambiguities);
        Assert.Equal("$", dollar.Symbol);
        Assert.Equal(1, dollar.RowCount);
        Assert.Contains("USD", dollar.Candidates);
    }

    [Fact]
    public void ApplyResolution_FillsAmbiguousRowsFromUserChoice()
    {
        var path = BuildMixedCurrencyRevenueSheet();
        var scan = CurrencyImportPreparer.ScanWorkbook(path, RevenueAnalysis());

        CurrencyImportPreparer.ApplyResolution(scan, new Dictionary<string, string> { ["$"] = "AUD" });

        Assert.Equal("AUD", scan.Resolved["Revenue"][3]);
    }

    // ─── Tier 1 end-to-end ───────────────────────────────────────────────────

    [Fact]
    public async Task Tier1Import_AppliesPerRowCurrency_FromScan()
    {
        var path = BuildMixedCurrencyRevenueSheet();
        var analysis = RevenueAnalysis();

        var scan = CurrencyImportPreparer.ScanWorkbook(path, analysis);
        CurrencyImportPreparer.ApplyResolution(scan, new Dictionary<string, string> { ["$"] = "AUD" });

        var data = new CompanyData();
        data.Settings.Localization.Currency = "USD";
        var svc = new SpreadsheetImportService();
        var options = new ImportOptions { RowCurrencyBySheet = scan.Resolved };

        await svc.ImportWithMappingsAsync(path, data, analysis, options);

        Assert.Equal("GBP", data.Revenues.Single(r => r.Id == "R1").OriginalCurrency);
        Assert.Equal("CAD", data.Revenues.Single(r => r.Id == "R2").OriginalCurrency);
        Assert.Equal("EUR", data.Revenues.Single(r => r.Id == "R3").OriginalCurrency);
        Assert.Equal("AUD", data.Revenues.Single(r => r.Id == "R4").OriginalCurrency);

        // Amounts parsed (symbols stripped) regardless of currency.
        Assert.Equal(100m, data.Revenues.Single(r => r.Id == "R1").Total);
        Assert.Equal(50m, data.Revenues.Single(r => r.Id == "R2").Total);
    }

    private static readonly DateTime PlainDate = new(2026, 1, 1);

    private string BuildPlainNumberRevenueSheet()
    {
        var path = NewTempXlsx();
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Revenue");
        ws.Cell(1, 1).Value = "ID"; ws.Cell(1, 2).Value = "Date"; ws.Cell(1, 3).Value = "Total";
        ws.Cell(2, 1).Value = "R1"; ws.Cell(2, 2).Value = PlainDate.ToString("yyyy-MM-dd"); ws.Cell(2, 3).Value = 100;
        wb.SaveAs(path);
        return path;
    }

    [Fact]
    public async Task Tier1Import_NoDetectedCurrency_ConvertsFromCompanyCurrencyAtTheRowDate()
    {
        // A plain number in a CAD company is 100 CAD, so its USD base is 100 at that day's rate,
        // not 100 as though it were already dollars.
        var path = BuildPlainNumberRevenueSheet();
        var analysis = RevenueAnalysis();
        var scan = CurrencyImportPreparer.ScanWorkbook(path, analysis);
        Assert.Empty(scan.Ambiguities);

        var data = new CompanyData();
        data.Settings.Localization.Currency = "CAD";
        var svc = new SpreadsheetImportService(exchangeRateService: await CadRatesAsync(1.25m, PlainDate));
        await svc.ImportWithMappingsAsync(path, data, analysis, new ImportOptions { RowCurrencyBySheet = scan.Resolved });

        var rev = data.Revenues.Single(r => r.Id == "R1");
        Assert.Equal("CAD", rev.OriginalCurrency);
        Assert.False(rev.IsPendingConversion);
        Assert.Equal(100m * (1m / 1.25m), rev.TotalUSD);
    }

    [Fact]
    public async Task Tier1Import_NoDetectedCurrency_NoRateForTheDate_ImportsPendingAndQueued()
    {
        var path = BuildPlainNumberRevenueSheet();
        var analysis = RevenueAnalysis();
        var scan = CurrencyImportPreparer.ScanWorkbook(path, analysis);

        var data = new CompanyData();
        data.Settings.Localization.Currency = "CAD";
        var noRates = new ExchangeRateService(new MockPlatformService(), new HttpClient(new CadRatesHandler(1.25m)));
        var svc = new SpreadsheetImportService(exchangeRateService: noRates);
        await svc.ImportWithMappingsAsync(path, data, analysis, new ImportOptions { RowCurrencyBySheet = scan.Resolved });

        var rev = data.Revenues.Single(r => r.Id == "R1");
        Assert.Equal("CAD", rev.OriginalCurrency);
        Assert.True(rev.IsPendingConversion);
        Assert.Equal(0m, rev.TotalUSD);
        var entry = Assert.Single(data.PendingConversions);
        Assert.Equal("R1", entry.TransactionId);
        Assert.Equal("CAD", entry.OriginalCurrency);
        Assert.Equal(100m, entry.Total);
    }

    [Fact]
    public async Task Tier1Import_NoDetectedCurrency_InvoicesPaymentsAndOrdersConvertFromCompanyCurrency()
    {
        var path = NewTempXlsx();
        using (var wb = new XLWorkbook())
        {
            var inv = wb.AddWorksheet("Invoices");
            inv.Cell(1, 1).Value = "Invoice #"; inv.Cell(1, 2).Value = "Issue Date"; inv.Cell(1, 3).Value = "Total"; inv.Cell(1, 4).Value = "Paid";
            inv.Cell(2, 1).Value = "INV-1"; inv.Cell(2, 2).Value = "2026-01-01"; inv.Cell(2, 3).Value = 200; inv.Cell(2, 4).Value = 200;

            var pay = wb.AddWorksheet("Payments");
            pay.Cell(1, 1).Value = "ID"; pay.Cell(1, 2).Value = "Date"; pay.Cell(1, 3).Value = "Amount";
            pay.Cell(2, 1).Value = "PAY-1"; pay.Cell(2, 2).Value = "2026-01-01"; pay.Cell(2, 3).Value = 50;

            var po = wb.AddWorksheet("Purchase Orders");
            po.Cell(1, 1).Value = "ID"; po.Cell(1, 2).Value = "Order Date"; po.Cell(1, 3).Value = "Total";
            po.Cell(2, 1).Value = "PO-1"; po.Cell(2, 2).Value = "2026-01-01"; po.Cell(2, 3).Value = 400;
            wb.SaveAs(path);
        }

        SheetAnalysis Sheet(string name, SpreadsheetSheetType type) => new()
        {
            SourceSheetName = name, DetectedType = type, Tier = ProcessingTier.Tier1_Mapping, IsIncluded = true
        };
        var analysis = new SpreadsheetAnalysisResult
        {
            Sheets =
            [
                Sheet("Invoices", SpreadsheetSheetType.Invoices),
                Sheet("Payments", SpreadsheetSheetType.Payments),
                Sheet("Purchase Orders", SpreadsheetSheetType.PurchaseOrders),
            ]
        };

        var data = new CompanyData();
        data.Settings.Localization.Currency = "CAD";
        var svc = new SpreadsheetImportService(exchangeRateService: await CadRatesAsync(1.25m, PlainDate));
        await svc.ImportWithMappingsAsync(path, data, analysis, new ImportOptions());

        const decimal cadToUsd = 1m / 1.25m;
        var invoice = data.Invoices.Single(i => i.Id == "INV-1");
        Assert.Equal("CAD", invoice.OriginalCurrency);
        Assert.Equal(200m * cadToUsd, invoice.TotalUSD);
        Assert.Equal(200m * cadToUsd, data.Revenues.Single(r => r.InvoiceId == "INV-1").TotalUSD);
        Assert.Equal(50m * cadToUsd, data.Payments.Single(p => p.Id == "PAY-1").AmountUSD);
        Assert.Equal(400m * cadToUsd, data.PurchaseOrders.Single(p => p.Id == "PO-1").TotalUSD);
    }

    /// <summary>An exchange-rate service holding USD-&gt;CAD for <paramref name="date"/> only.</summary>
    private static async Task<ExchangeRateService> CadRatesAsync(decimal usdToCad, DateTime date)
    {
        var service = new ExchangeRateService(new MockPlatformService(), new HttpClient(new CadRatesHandler(usdToCad)));
        await service.GetExchangeRateAsync("USD", "CAD", date);
        return service;
    }

    private sealed class CadRatesHandler(decimal usdToCad) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = $$"""{ "success": true, "base": "USD", "rates": { "CAD": {{usdToCad}} } }""";
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
        public string GetDefaultDocumentsPath() => Path.GetTempPath();
        public string GetLogsPath() => Path.GetTempPath();
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
        public void RegisterFileTypeAssociations(string iconPath) { }
        public StringComparer PathComparer => StringComparer.Ordinal;
    }

    // ─── The shipped sample file imports correctly via Tier 1 ────────────────

    [Fact]
    public async Task SampleFile_MultiCurrency_ImportsWithCorrectCurrencies()
    {
        // Resolve the committed sample under the repo's TestData.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ArgoBooks.sln"))) dir = dir.Parent;
        var path = Path.Combine(dir!.FullName, "TestData", "MainImporter", "multi_currency_sample.xlsx");
        Assert.True(File.Exists(path), $"sample missing: {path}");

        var analysis = new SpreadsheetAnalysisResult
        {
            Sheets =
            [
                new SheetAnalysis { SourceSheetName = "Sales", DetectedType = SpreadsheetSheetType.Revenue, Tier = ProcessingTier.Tier1_Mapping, IsIncluded = true },
                new SheetAnalysis { SourceSheetName = "Expenses", DetectedType = SpreadsheetSheetType.Expenses, Tier = ProcessingTier.Tier1_Mapping, IsIncluded = true },
            ]
        };

        var scan = CurrencyImportPreparer.ScanWorkbook(path, analysis);

        // Only "$" is genuinely ambiguous. "¥" is resolved from the cell's 0-decimal format
        // ("¥"#,##0) to JPY automatically, so it is not flagged for the user.
        Assert.Contains(scan.Ambiguities, a => a.Symbol == "$");
        Assert.DoesNotContain(scan.Ambiguities, a => a.Symbol == "¥");
        Assert.Equal("JPY", scan.Resolved["Sales"][3]); // R4 (¥95,000) resolved before any prompt
        CurrencyImportPreparer.ApplyResolution(scan, new Dictionary<string, string> { ["$"] = "USD" });

        var data = new CompanyData();
        data.Settings.Localization.Currency = "USD";
        var svc = new SpreadsheetImportService();
        await svc.ImportWithMappingsAsync(path, data, analysis, new ImportOptions { RowCurrencyBySheet = scan.Resolved });

        // Expenses: amounts non-zero, currency detected from the cell format/code, and the
        // record is internally consistent (line-item amount == total, so the edit modal agrees).
        var e1 = data.Expenses.Single(e => e.Id == "E1");
        Assert.Equal(140m, e1.Total);
        Assert.Equal(140m, e1.Amount);
        Assert.Equal("GBP", e1.OriginalCurrency);
        Assert.Equal("CAD", data.Expenses.Single(e => e.Id == "E2").OriginalCurrency);
        Assert.Equal("USD", data.Expenses.Single(e => e.Id == "E3").OriginalCurrency); // resolved $
        Assert.Equal(1500m, data.Expenses.Single(e => e.Id == "E4").Total);           // plain number

        // Revenue: a spread of currencies, all amounts non-zero and consistent.
        Assert.Equal("GBP", data.Revenues.Single(r => r.Id == "R1").OriginalCurrency);
        Assert.Equal("EUR", data.Revenues.Single(r => r.Id == "R2").OriginalCurrency);
        Assert.Equal("CAD", data.Revenues.Single(r => r.Id == "R3").OriginalCurrency);
        Assert.Equal("JPY", data.Revenues.Single(r => r.Id == "R4").OriginalCurrency); // resolved ¥
        Assert.Equal(800m, data.Revenues.Single(r => r.Id == "R2").Total);
        Assert.All(data.Revenues, r => Assert.True(r.Total > 0 && r.Amount == r.Total,
            $"{r.Id}: total={r.Total} amount={r.Amount} should be equal and > 0"));
    }

    // ─── Tier 2 normalization (LLM-emitted symbol/code) ──────────────────────

    [Fact]
    public void Tier2_NormalizesUnambiguousSymbol_ToCode()
    {
        var data = new CompanyData();
        data.Settings.Localization.Currency = "USD";
        var svc = new SpreadsheetImportService();

        var row = JsonDocument.Parse(
            """{ "id": "EXP-1", "date": "2026-03-15", "total": 100, "description": "x", "originalCurrency": "£" }""")
            .RootElement.Clone();

        var chunk = new LlmProcessedData { EntityType = SpreadsheetSheetType.Expenses };
        chunk.Entities.Add(row);
        svc.ImportProcessedEntities(data, [chunk], "Expenses");

        Assert.Equal("GBP", data.Expenses.Single(e => e.Id == "EXP-1").OriginalCurrency);
    }

    [Fact]
    public void Tier2_ResolvesAmbiguousSymbol_ViaSymbolResolution()
    {
        var data = new CompanyData();
        data.Settings.Localization.Currency = "USD";
        var svc = new SpreadsheetImportService();
        var options = new ImportOptions { SymbolResolution = new() { ["$"] = "CAD" } };

        var row = JsonDocument.Parse(
            """{ "id": "EXP-2", "date": "2026-03-15", "total": 100, "description": "x", "originalCurrency": "$" }""")
            .RootElement.Clone();

        var chunk = new LlmProcessedData { EntityType = SpreadsheetSheetType.Expenses };
        chunk.Entities.Add(row);
        svc.ImportProcessedEntities(data, [chunk], "Expenses", options);

        Assert.Equal("CAD", data.Expenses.Single(e => e.Id == "EXP-2").OriginalCurrency);
    }
}
