using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Rentals;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;
using ClosedXML.Excel;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Options for controlling import behavior.
/// </summary>
public class ImportOptions
{
    /// <summary>
    /// If true, automatically create placeholder entities for missing references.
    /// </summary>
    public bool AutoCreateMissingReferences { get; set; }

    /// <summary>
    /// Specific reference types to auto-create (if AutoCreateMissingReferences is false).
    /// Keys: "Products", "Categories", "Customers", "Suppliers", "Locations", etc.
    /// </summary>
    public HashSet<string> AutoCreateTypes { get; set; } = [];

    /// <summary>
    /// If true, skip records that already exist instead of overwriting them.
    /// </summary>
    public bool SkipExistingRecords { get; set; }

    /// <summary>
    /// Tracks the number of records actually skipped during import.
    /// Reset before each sheet import. Used internally by import methods.
    /// </summary>
    internal int SkippedCount { get; set; }

    /// <summary>
    /// Tracks the number of existing records updated in place during import (matched by id
    /// and not skipped). Reset before each sheet import. Used internally by import methods so
    /// the per-sheet result can report updates instead of misattributing them to dropped rows.
    /// </summary>
    internal int UpdatedCount { get; set; }

    /// <summary>
    /// Tracks rows inserted for grouped sheet types whose entities are not added 1:1 to a
    /// collection (purchase-order line items, which are merged onto their parent order). Reset
    /// before each sheet import. Used internally so the count cannot be inferred from a
    /// collection-size delta.
    /// </summary>
    internal int InsertedCount { get; set; }

    /// <summary>
    /// Per-row currency resolved deterministically from the amount cells before import
    /// (see <see cref="CurrencyImportPreparer"/>): sheet name -> (0-based data-row ordinal -> ISO code).
    /// When a row has an entry, financial builders set <c>OriginalCurrency</c> to that code and
    /// convert amounts to USD. Rows without an entry keep the existing company-currency behavior.
    /// Applies to deterministic (Tier 1) imports.
    /// </summary>
    public Dictionary<string, Dictionary<int, string>> RowCurrencyBySheet { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Global resolution of ambiguous currency symbols chosen by the user (e.g. "$" -> "CAD").
    /// Used to normalize a symbol the LLM emits into <c>originalCurrency</c> on the Tier 2 path,
    /// where per-row ordinals are not available.
    /// </summary>
    public Dictionary<string, string> SymbolResolution { get; set; }
        = new(StringComparer.Ordinal);
}

/// <summary>
/// Represents a single entity that could not be imported, with the reason and identifying
/// information for later reporting or export.
/// </summary>
public sealed class UnimportedRow
{
    public required string Sheet { get; init; }
    public required string Reason { get; init; }
    public int RowNumber { get; init; }     // 0 when not known (Tier 1 aggregate)
    public string? RawValue { get; init; }   // e.g. the entity id or json snippet
}

/// <summary>
/// Per-sheet import result breakdown.
/// </summary>
public class SheetImportResult
{
    public required string SheetName { get; init; }
    public required string EntityType { get; init; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }

    /// <summary>
    /// Rows routed to the Bank Matching feature (added as a <see cref="Models.BankMatching.BankImportSession"/>)
    /// rather than committed as book records. Reported on its own line so it is clear they went elsewhere.
    /// </summary>
    public int BankMatchingImported { get; set; }
    public List<string> SkipReasons { get; } = [];
    public List<UnimportedRow> UnimportedRows { get; } = [];

    /// <summary>
    /// Non-fatal warnings surfaced during import (e.g. a referenced customer/supplier name
    /// could not be confidently matched and a placeholder was created instead of a link).
    /// The row IS still imported, so these are warnings rather than <see cref="UnimportedRows"/>.
    /// </summary>
    public List<string> Warnings { get; } = [];
}

/// <summary>
/// Result of a spreadsheet import operation, tracking what was imported and any issues.
/// </summary>
public class SpreadsheetImportResult
{
    public int TotalImported { get; set; }
    public int TotalUpdated { get; set; }
    public int TotalSkipped { get; set; }
    public List<string> Warnings { get; } = [];
    public List<SheetImportResult> SheetResults { get; } = [];
}

/// <summary>
/// Result of importing a single entity.
/// </summary>
public enum ImportEntityResult
{
    Failed,
    Inserted,
    Updated,
    SkippedExisting
}

/// <summary>
/// Service for importing company data from spreadsheet formats (xlsx).
/// </summary>
public class SpreadsheetImportService
{
    private readonly IErrorLogger? _errorLogger;
    private readonly ITelemetryManager? _telemetryManager;
    private readonly IGeminiService? _geminiService;
    private readonly ExchangeRateService? _exchangeRateService;

    /// <summary>
    /// Creates a new SpreadsheetImportService.
    /// </summary>
    /// <param name="exchangeRateService">
    /// Optional exchange-rate service used for per-row currency conversion when a Currency
    /// column is mapped. Defaults to <see cref="ExchangeRateService.Instance"/> so production
    /// reuses the same singleton (and cached rates) as manual entry; tests can inject a seeded
    /// instance for determinism.
    /// </param>
    public SpreadsheetImportService(IErrorLogger? errorLogger = null, ITelemetryManager? telemetryManager = null, IGeminiService? geminiService = null, ExchangeRateService? exchangeRateService = null)
    {
        _errorLogger = errorLogger;
        _telemetryManager = telemetryManager;
        _geminiService = geminiService;
        _exchangeRateService = exchangeRateService;
    }

    /// <summary>
    /// The exchange-rate service to use for per-row currency conversion. Falls back to the
    /// shared singleton when one was not explicitly injected.
    /// </summary>
    private ExchangeRateService? ExchangeRates => _exchangeRateService ?? ExchangeRateService.Instance;

    /// <summary>
    /// Per-import context carrying the name-to-id indexes used to resolve references by NAME
    /// before falling back to creating placeholder stubs, plus a sink for any warnings raised
    /// when a reference could not be confidently matched.
    ///
    /// Built once per import and threaded through the call chain (never stored on the service)
    /// so that concurrent imports on a shared service instance cannot interfere with each other.
    /// </summary>
    private sealed class ReferenceResolutionContext
    {
        public required Dictionary<string, string> CustomerIndex { get; init; }
        public required Dictionary<string, string> SupplierIndex { get; init; }
        public List<string> Warnings { get; } = [];

        public static ReferenceResolutionContext Build(CompanyData data) => new()
        {
            CustomerIndex = ReferenceResolver.BuildIndex(data.Customers.Select(c => (c.Id, c.Name))),
            SupplierIndex = ReferenceResolver.BuildIndex(data.Suppliers.Select(s => (s.Id, s.Name)))
        };
    }
    /// <summary>
    /// Validates an Excel file before importing, checking for missing references.
    /// </summary>
    public async Task<ImportValidationResult> ValidateImportAsync(
        string filePath,
        CompanyData companyData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(companyData);

        return await Task.Run(() =>
        {
            var result = new ImportValidationResult();

            try
            {
                // Open file with read sharing to allow importing even if file is open in Excel
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var workbook = new XLWorkbook(fileStream);

                // First pass: collect all IDs that will be imported
                var importedIds = CollectImportedIds(workbook);

                // Second pass: validate references
                foreach (var worksheet in workbook.Worksheets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ValidateWorksheet(worksheet, companyData, importedIds, result);
                }
            }
            catch (Exception ex)
            {
                _errorLogger?.LogError(ex, ErrorCategory.Import, $"Failed to validate import file: {Path.GetFileName(filePath)}");
                result.Errors.Add($"Failed to read file: {ex.Message}");
            }

            return result;
        }, cancellationToken);
    }

    /// <summary>
    /// Imports data from an Excel file into the company data using merge logic.
    /// Existing records with matching IDs are updated, new records are added.
    /// </summary>
    public async Task ImportFromExcelAsync(
        string filePath,
        CompanyData companyData,
        ImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(companyData);

        options ??= new ImportOptions();

        try
        {
            await Task.Run(() =>
            {
                // Open file with read sharing to allow importing even if file is open in Excel
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var workbook = new XLWorkbook(fileStream);

                var sheets = workbook.Worksheets
                    .Select(ws => ReadSheet(ws, SpreadsheetSheetTypeExtensions.ParseSheetName(ws.Name)))
                    .OfType<ImportSheet>()
                    .ToList();
                ReserveIdNumbers(companyData, sheets);

                // If auto-creating references, do that first
                if (options.AutoCreateMissingReferences || options.AutoCreateTypes.Count > 0)
                {
                    CreateMissingReferences(workbook, companyData, options);
                }

                foreach (var sheet in sheets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    BeginSheet(sheet.Rows);
                    ImportBySheetType(sheet.Type, companyData, sheet.Headers, sheet.Rows, options);
                }

                // Update ID counters based on imported data
                FinishImport(companyData);

                companyData.MarkAsModified();
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _errorLogger?.LogError(ex, ErrorCategory.Import, $"Failed to import from: {Path.GetFileName(filePath)}");
            throw;
        }
    }

    #region AI-Mapped Import

    /// <summary>
    /// Imports data from an Excel file using AI-generated column mappings (Tier 1).
    /// Headers are renamed according to the analysis result before standard import logic runs.
    /// </summary>
    public async Task<SpreadsheetImportResult> ImportWithMappingsAsync(
        string filePath,
        CompanyData companyData,
        SpreadsheetAnalysisResult analysis,
        ImportOptions? options = null,
        CancellationToken cancellationToken = default,
        IProgress<(string detail, double percent)>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(companyData);
        ArgumentNullException.ThrowIfNull(analysis);

        // Route CSV files through the RFC-4180-compliant importer
        if (filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return await ImportCsvWithMappingsAsync(filePath, companyData, analysis, options, cancellationToken, progress);

        options ??= new ImportOptions();
        var result = new SpreadsheetImportResult();

        try
        {
            await Task.Run(() =>
            {
                progress?.Report(("Reading spreadsheet...", -1));
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var workbook = new XLWorkbook(fileStream);

                var worksheets = workbook.Worksheets.ToList();
                var sheets = worksheets.Select(ws => ReadMappedSheet(ws, analysis, result)).ToList();
                ReserveIdNumbers(companyData, sheets.OfType<ImportSheet>());

                if (options.AutoCreateMissingReferences || options.AutoCreateTypes.Count > 0)
                {
                    progress?.Report(("Creating missing references...", -1));
                    CreateMissingReferences(workbook, companyData, options);
                }

                var totalSteps = worksheets.Count;
                for (int i = 0; i < worksheets.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var pct = (double)i / totalSteps * 100;
                    progress?.Report(($"Importing {worksheets[i].Name} ({i + 1}/{worksheets.Count})...", pct));
                    if (sheets[i] is { } sheet)
                        ImportMappedSheet(sheet, companyData, result, options);
                }

                FinishImport(companyData);
                companyData.MarkAsModified();
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _errorLogger?.LogError(ex, ErrorCategory.Import, $"Failed AI-mapped import from: {Path.GetFileName(filePath)}");
            throw;
        }

        return result;
    }

    /// <summary>
    /// Imports data from a CSV file using AI-generated column mappings (Tier 1).
    /// </summary>
    public async Task<SpreadsheetImportResult> ImportCsvWithMappingsAsync(
        string filePath,
        CompanyData companyData,
        SpreadsheetAnalysisResult analysis,
        ImportOptions? options = null,
        CancellationToken cancellationToken = default,
        IProgress<(string detail, double percent)>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(companyData);
        ArgumentNullException.ThrowIfNull(analysis);

        var result = new SpreadsheetImportResult();

        try
        {
            await Task.Run(() =>
            {
                progress?.Report(("Reading CSV file...", 0));
                var dataRows = CsvReader.ReadAllRows(filePath, out var headers);
                if (headers.Count == 0)
                {
                    result.Warnings.Add("CSV file has no headers.");
                    return;
                }
                if (dataRows.Count == 0)
                {
                    result.Warnings.Add("CSV file has no data rows.");
                    return;
                }

                progress?.Report(($"Processing {dataRows.Count:N0} rows...", 20));
                var rows = dataRows.Select(r => r.Cast<object?>().ToList()).ToList();

                var sheetAnalysis = analysis.Sheets.FirstOrDefault();

                if (sheetAnalysis != null)
                {
                    // Ensure a non-null options so per-sheet insert/update/skip counts are tracked
                    // (the counters live on ImportOptions); otherwise updates would be misreported.
                    options ??= new ImportOptions();
                    progress?.Report(($"Importing {rows.Count:N0} records...", 50));
                    ApplyColumnMapping(headers, sheetAnalysis);
                    var sheetType = sheetAnalysis.DetectedType;
                    var csvSheetName = Path.GetFileNameWithoutExtension(filePath);

                    // The xlsx workbook scan doesn't cover CSV, so detect per-row currency here (an
                    // in-cell symbol/code or a "Currency" column) and feed it to the importer keyed by
                    // the same row index, so CSV imports honor per-row currency like Excel does.
                    var csvCurrency = CurrencyImportPreparer.ScanRows(headers, rows);
                    if (csvCurrency.Count > 0)
                    {
                        options ??= new ImportOptions();
                        options.RowCurrencyBySheet ??= new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
                        options.RowCurrencyBySheet[csvSheetName] = csvCurrency;
                    }

                    ReserveIdNumbers(companyData, [new ImportSheet(csvSheetName, sheetType, headers, rows)]);
                    var sheetResult = ImportBySheetTypeWithCount(sheetType, companyData, headers, rows, csvSheetName, options);
                    result.TotalImported += sheetResult.Inserted;
                    result.TotalUpdated += sheetResult.Updated;
                    result.TotalSkipped += sheetResult.Skipped;
                    result.SheetResults.Add(sheetResult);
                    if (sheetResult.Inserted == 0 && sheetResult.Updated == 0 && sheetResult.Skipped == 0)
                        result.Warnings.Add($"Sheet detected as '{sheetType}' but 0 records were imported from {rows.Count} rows.");
                }
                else
                {
                    result.Warnings.Add("No sheet analysis found for CSV file.");
                }

                FinishImport(companyData);
                companyData.MarkAsModified();
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _errorLogger?.LogError(ex, ErrorCategory.Import, $"Failed AI-mapped CSV import from: {Path.GetFileName(filePath)}");
            throw;
        }

        return result;
    }

    /// <summary>
    /// Validates an Excel file using AI-generated column mappings.
    /// </summary>
    public async Task<ImportValidationResult> ValidateWithMappingsAsync(
        string filePath,
        CompanyData companyData,
        SpreadsheetAnalysisResult analysis,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(companyData);

        return await Task.Run(() =>
        {
            var result = new ImportValidationResult();

            try
            {
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var workbook = new XLWorkbook(fileStream);

                var importedIds = CollectImportedIds(workbook);

                foreach (var worksheet in workbook.Worksheets)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Apply column mapping before validation
                    var headers = GetHeaders(worksheet);
                    if (headers.Count == 0) continue;

                    var sheetAnalysis = analysis.Sheets.FirstOrDefault(
                        s => s.SourceSheetName == worksheet.Name);
                    if (sheetAnalysis != null)
                        ApplyColumnMapping(headers, sheetAnalysis);

                    // Validation uses the mapped headers
                    var rows = GetDataRows(worksheet, headers.Count);
                    if (rows.Count == 0) continue;
                    ValidateWorksheetData(worksheet.Name, headers, rows, companyData, importedIds, result);
                }
            }
            catch (Exception ex)
            {
                _errorLogger?.LogError(ex, ErrorCategory.Import, $"Failed to validate AI-mapped import file: {Path.GetFileName(filePath)}");
                result.Errors.Add($"Failed to read file: {ex.Message}");
            }

            return result;
        }, cancellationToken);
    }

    /// <summary>
    /// Imports pre-processed entities from LLM Tier 2 processing.
    /// Returns (imported count, skipped count) for reporting.
    /// </summary>
    public SheetImportResult ImportProcessedEntities(
        CompanyData companyData,
        List<LlmProcessedData> processedData,
        string sheetName,
        ImportOptions? options = null)
    {
        return ImportProcessedEntitiesCore(companyData, processedData, sheetName, options);
    }

    private SheetImportResult ImportProcessedEntitiesCore(
        CompanyData companyData,
        List<LlmProcessedData> processedData,
        string sheetName,
        ImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(companyData);
        ArgumentNullException.ThrowIfNull(processedData);

        var firstType = processedData.FirstOrDefault()?.EntityType;
        var entityType = firstType == SpreadsheetSheetType.BankStatement
            ? "Bank Matching"
            : firstType?.ToString() ?? "Unknown";
        var sheetResult = new SheetImportResult
        {
            SheetName = sheetName,
            EntityType = entityType
        };

        // Rows whose AI call failed produced no entities; list them rather than let them vanish.
        foreach (var rawRow in processedData.SelectMany(chunk => chunk.FailedRows))
        {
            const string failedReason = "AI couldn't read this row, so it wasn't imported";
            sheetResult.Skipped++;
            sheetResult.SkipReasons.Add(failedReason);
            sheetResult.UnimportedRows.Add(new UnimportedRow
            {
                Sheet = sheetName,
                Reason = failedReason,
                RawValue = rawRow
            });
        }

        // Bank statement rows are reference data for the Bank Matching feature, never committed as
        // book transactions. The normal importer hands them to the dedicated bank importer, but this
        // AI path has no per-entity bank importer (they would fall through ImportSingleEntity to
        // Failed). Build the bank lines here and add them as a single import session, exactly the
        // shape the Bank Matching page reads. Reported on their own line (not as new/updated book
        // records) so it is clear they landed on a different page.
        if (firstType == SpreadsheetSheetType.BankStatement)
        {
            var lines = new List<BankStatementLine>();
            foreach (var chunk in processedData)
            {
                foreach (var entityJson in chunk.Entities)
                {
                    BankStatementLine? line;
                    try
                    {
                        line = JsonSerializer.Deserialize<BankStatementLine>(entityJson.GetRawText(), ImportJsonOptions);
                    }
                    catch (JsonException)
                    {
                        line = null;
                    }
                    if (line == null) continue;

                    line.Id = Guid.NewGuid().ToString("N");
                    // Fall back to Credit - Debit when the AI mapped separate columns instead of a
                    // single signed amount (matches BankStatementImportService: in is positive, out negative).
                    if (line.Amount == 0 && (line.Debit.HasValue || line.Credit.HasValue))
                        line.Amount = (line.Credit ?? 0) - (line.Debit ?? 0);
                    lines.Add(line);
                }
            }

            if (lines.Count > 0)
            {
                companyData.BankImportSessions.Add(new BankImportSession
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ImportedAt = DateTime.UtcNow,
                    SourceFileName = sheetName,
                    Lines = lines
                });
                sheetResult.BankMatchingImported = lines.Count;
                companyData.MarkAsModified();
            }
            return sheetResult;
        }

        // Build the name->id indexes once for this import so reference resolution can link a
        // by-name reference to an existing customer/supplier instead of creating a placeholder.
        var refContext = ReferenceResolutionContext.Build(companyData);

        // Deduplicate entities across chunks by ID, later chunks win on conflict.
        // The LLM processes chunks independently and may produce duplicate IDs,
        // especially at chunk boundaries.
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Collect all (chunk, entityJson) pairs, then reverse-iterate to keep the last occurrence of each ID
        var allEntities = new List<(SpreadsheetSheetType EntityType, JsonElement Entity)>();
        foreach (var chunk in processedData)
        {
            foreach (var entityJson in chunk.Entities)
                allEntities.Add((chunk.EntityType, entityJson));
        }

        // Walk backwards so the last occurrence of a duplicate ID wins
        var deduplicatedEntities = new List<(SpreadsheetSheetType EntityType, JsonElement Entity)>();
        for (int i = allEntities.Count - 1; i >= 0; i--)
        {
            var id = ExtractEntityId(allEntities[i].Entity);
            if (string.IsNullOrEmpty(id) || seenIds.Add(id))
                deduplicatedEntities.Add(allEntities[i]);
        }
        deduplicatedEntities.Reverse(); // restore original order

        var duplicatesRemoved = allEntities.Count - deduplicatedEntities.Count;
        if (duplicatesRemoved > 0)
        {
            _errorLogger?.LogWarning(
                $"Removed {duplicatesRemoved} duplicate entities (by ID) across AI chunks for sheet '{sheetName}'");
        }

        // ---------------------------------------------------------------------------------
        // Task 2C: deterministic natural-key ids for id-less Tier 2 rows + re-import detection.
        //
        // For every entity that arrives WITHOUT an id we derive a deterministic id from a
        // small set of identifying fields (the "natural key"). This makes such rows importable
        // (today they are dropped) AND idempotent: re-importing the same file reproduces the
        // same ids, so the existing merge-by-id logic UPDATES the prior row instead of
        // duplicating it.
        //
        // Safety invariant ("no silent drops / never collapse distinct rows"): two genuinely
        // identical rows in the SAME import share a natural key but MUST both survive. We keep
        // them apart by appending an ordinal (-2, -3, ...) to the 2nd, 3rd ... occurrence in
        // order of appearance. The natural key is NEVER used to merge two same-import rows; it
        // only seeds the deterministic id. Cross-import updates are governed solely by the
        // existing merge-by-id path.
        // ---------------------------------------------------------------------------------
        var entitiesToImport = new List<(SpreadsheetSheetType EntityType, JsonElement Entity, bool SkipImport)>();
        var naturalKeyOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);

        // Snapshot the ids that already exist for each entity type BEFORE we import, so we can
        // count how many incoming rows land on a pre-existing record (a re-import).
        var existingIdSnapshots = new Dictionary<SpreadsheetSheetType, HashSet<string>>();
        HashSet<string> ExistingIdsFor(SpreadsheetSheetType type)
        {
            if (!existingIdSnapshots.TryGetValue(type, out var set))
            {
                set = new HashSet<string>(GetExistingEntityIds(companyData, type), StringComparer.OrdinalIgnoreCase);
                existingIdSnapshots[type] = set;
            }
            return set;
        }

        int reimportMatches = 0;

        foreach (var (chunkEntityType, entityJson) in deduplicatedEntities)
        {
            var existingId = ExtractEntityId(entityJson);
            if (!string.IsNullOrEmpty(existingId))
            {
                // Real id (the common case): flow through unchanged. Count a re-import match if
                // it lands on a record that already existed before this import.
                if (ExistingIdsFor(chunkEntityType).Contains(existingId))
                    reimportMatches++;
                entitiesToImport.Add((chunkEntityType, entityJson, false));
                continue;
            }

            // An invoice with no id is identified by its number, which is what payments and line
            // items name. A derived id would leave "1001" unfindable, so the number becomes the id,
            // as it does on the Tier 1 path and in ImportSingleEntity.
            if (chunkEntityType == SpreadsheetSheetType.Invoices
                && entityJson.TryGetProperty("invoiceNumber", out var numberProp)
                && numberProp.ValueKind == JsonValueKind.String
                && numberProp.GetString()?.Trim() is { Length: > 0 } invoiceNumber)
            {
                if (ExistingIdsFor(chunkEntityType).Contains(invoiceNumber))
                    reimportMatches++;
                entitiesToImport.Add((chunkEntityType, WithId(entityJson, invoiceNumber), false));
                continue;
            }

            // Id-less row: try to derive a deterministic id from its natural key.
            var naturalKey = NaturalKey(chunkEntityType, entityJson);
            if (naturalKey == null)
            {
                // Not enough fields to form a meaningful key. Do not
                // invent an opaque id that could collide arbitrarily. The row is recorded as
                // unimported below (never silently dropped) by passing it through with the
                // SkipImport flag so the existing "missing/empty ID" reporting path fires.
                entitiesToImport.Add((chunkEntityType, entityJson, true));
                continue;
            }

            // Disambiguate same-import rows that share a natural key with an ordinal so all of
            // them survive. The 1st occurrence keeps the base id; the Nth gets "-N".
            var ordinal = naturalKeyOrdinals.TryGetValue(naturalKey, out var seen) ? seen + 1 : 1;
            naturalKeyOrdinals[naturalKey] = ordinal;

            var baseId = $"{TypePrefix(chunkEntityType)}-{StableHash(naturalKey)}";
            var derivedId = ordinal == 1 ? baseId : $"{baseId}-{ordinal}";

            if (ExistingIdsFor(chunkEntityType).Contains(derivedId))
                reimportMatches++;

            var withId = WithId(entityJson, derivedId);
            entitiesToImport.Add((chunkEntityType, withId, false));
        }

        UpdateIdCounters(companyData);
        foreach (var group in entitiesToImport.Where(e => !e.SkipImport).GroupBy(e => e.EntityType))
            RaiseIdCounter(companyData, group.Key, group.Select(e => ExtractEntityId(e.Entity)));

        // Only claim "updated" when existing records are actually overwritten. With
        // SkipExistingRecords on (the default), these rows are skipped instead, and that is
        // already reported via the per-row skipped/unimported path, so the warning would be
        // both wrong ("updated") and a duplicate.
        if (reimportMatches > 0 && options?.SkipExistingRecords != true)
            sheetResult.Warnings.Add($"{reimportMatches} row(s) look like a re-import and were updated.");

        foreach (var (chunkEntityType, entityJson, skipImport) in entitiesToImport)
        {
            try
            {
                if (skipImport)
                {
                    var missingReason = $"Row had missing id and insufficient fields to form a key ({chunkEntityType})";
                    sheetResult.Skipped++;
                    sheetResult.SkipReasons.Add(missingReason);
                    sheetResult.UnimportedRows.Add(new UnimportedRow
                    {
                        Sheet = sheetName,
                        Reason = missingReason,
                        RawValue = entityJson.GetRawText()
                    });
                    continue;
                }

                var singleResult = ImportSingleEntity(companyData, chunkEntityType, entityJson, options, refContext);
                if (singleResult == ImportEntityResult.Inserted)
                    sheetResult.Inserted++;
                else if (singleResult == ImportEntityResult.Updated)
                    sheetResult.Updated++;
                else if (singleResult == ImportEntityResult.SkippedExisting)
                {
                    var skipReason = $"Existing {chunkEntityType} record skipped";
                    sheetResult.Skipped++;
                    sheetResult.SkipReasons.Add(skipReason);
                    sheetResult.UnimportedRows.Add(new UnimportedRow
                    {
                        Sheet = sheetName,
                        Reason = skipReason,
                        RawValue = ExtractEntityId(entityJson) ?? entityJson.GetRawText()
                    });
                }
                else
                {
                    var failReason = $"Row had missing or empty ID ({chunkEntityType})";
                    sheetResult.Skipped++;
                    sheetResult.SkipReasons.Add(failReason);
                    sheetResult.UnimportedRows.Add(new UnimportedRow
                    {
                        Sheet = sheetName,
                        Reason = failReason,
                        RawValue = ExtractEntityId(entityJson) ?? entityJson.GetRawText()
                    });
                }
            }
            catch (Exception ex)
            {
                var errorReason = $"Error importing {chunkEntityType}: {ex.Message}";
                sheetResult.Skipped++;
                sheetResult.SkipReasons.Add(errorReason);
                sheetResult.UnimportedRows.Add(new UnimportedRow
                {
                    Sheet = sheetName,
                    Reason = errorReason,
                    RawValue = ExtractEntityId(entityJson) ?? entityJson.GetRawText()
                });
                _errorLogger?.LogError(ex, ErrorCategory.Import,
                    $"Failed to import {chunkEntityType} entity from AI processing");
            }
        }

        // Surface any reference-resolution warnings (unmatched/ambiguous names) for reporting.
        sheetResult.Warnings.AddRange(refContext.Warnings);

        FinishImport(companyData);
        companyData.MarkAsModified();

        return sheetResult;
    }

    /// <summary>A sheet read ahead of the import, so every sheet's ids are known before any row is added.</summary>
    private sealed record ImportSheet(string Name, SpreadsheetSheetType Type, List<string> Headers, List<List<object?>> Rows);

    private static ImportSheet? ReadSheet(IXLWorksheet worksheet, SpreadsheetSheetType type)
    {
        var headers = GetHeaders(worksheet);
        if (headers.Count == 0) return null;

        var rows = GetDataRows(worksheet, headers.Count);
        return rows.Count == 0 ? null : new ImportSheet(worksheet.Name, type, headers, rows);
    }

    /// <summary>
    /// Reads a Tier 1 sheet with the analysis's column mapping applied, or returns null for a sheet
    /// the import leaves out (empty, excluded, or handled by Tier 2 via ProcessedEntities).
    /// </summary>
    private static ImportSheet? ReadMappedSheet(IXLWorksheet worksheet, SpreadsheetAnalysisResult analysis, SpreadsheetImportResult result)
    {
        var sheetName = worksheet.Name;
        var headers = GetHeaders(worksheet);
        if (headers.Count == 0)
        {
            result.Warnings.Add($"Sheet '{sheetName}': no headers found, skipped.");
            return null;
        }

        var rows = GetDataRows(worksheet, headers.Count);
        if (rows.Count == 0)
        {
            result.Warnings.Add($"Sheet '{sheetName}': no data rows found, skipped.");
            return null;
        }

        var sheetAnalysis = analysis.Sheets.FirstOrDefault(s => s.SourceSheetName == sheetName);
        if (sheetAnalysis == null || !sheetAnalysis.IsIncluded || sheetAnalysis.Tier == ProcessingTier.Tier2_LlmProcessing)
            return null;

        ApplyColumnMapping(headers, sheetAnalysis);
        return new ImportSheet(sheetName, sheetAnalysis.DetectedType, headers, rows);
    }

    private void ImportMappedSheet(ImportSheet sheet, CompanyData data, SpreadsheetImportResult result, ImportOptions? options = null)
    {
        var sheetResult = ImportBySheetTypeWithCount(sheet.Type, data, sheet.Headers, sheet.Rows, sheet.Name, options);
        result.TotalImported += sheetResult.Inserted;
        result.TotalUpdated += sheetResult.Updated;
        result.TotalSkipped += sheetResult.Skipped;
        result.SheetResults.Add(sheetResult);
        if (sheetResult.Inserted == 0 && sheetResult.Updated == 0 && sheetResult.Skipped == 0 && sheet.Rows.Count > 0)
            result.Warnings.Add($"Sheet '{sheet.Name}': detected as '{sheet.Type}' but 0 records were imported from {sheet.Rows.Count} rows.");
    }

    private void ImportBySheetType(SpreadsheetSheetType sheetType, CompanyData data, List<string> headers, List<List<object?>> rows, ImportOptions? options = null)
    {
        switch (sheetType)
        {
            case SpreadsheetSheetType.Customers:
                ImportCustomers(data, headers, rows, options);
                break;
            case SpreadsheetSheetType.Invoices:
                ImportInvoices(data, headers, rows, options);
                break;
            case SpreadsheetSheetType.Expenses:
                ImportPurchases(data, headers, rows, options);
                break;
            case SpreadsheetSheetType.Products:
                ImportProducts(data, headers, rows, options);
                break;
            case SpreadsheetSheetType.Inventory:
                ImportInventory(data, headers, rows, options);
                break;
            case SpreadsheetSheetType.Payments:
                ImportPayments(data, headers, rows, options);
                break;
            case SpreadsheetSheetType.Suppliers:
                ImportSuppliers(data, headers, rows, options);
                break;
            case SpreadsheetSheetType.Revenue:
                ImportSales(data, headers, rows, options);
                break;
            case SpreadsheetSheetType.RentalInventory:
                ImportRentalInventory(data, headers, rows, options);
                break;
            case SpreadsheetSheetType.RentalRecords:
                ImportRentalRecords(data, headers, rows, options);
                break;
            case SpreadsheetSheetType.Categories:
                ImportCategories(data, headers, rows, options);
                break;
            case SpreadsheetSheetType.Locations:
                ImportLocations(data, headers, rows, options);
                break;
            case SpreadsheetSheetType.RecurringInvoices:
                ImportRecurringInvoices(data, headers, rows, options);
                break;
            case SpreadsheetSheetType.StockAdjustments:
                ImportStockAdjustments(data, headers, rows, options);
                break;
            case SpreadsheetSheetType.PurchaseOrders:
                ImportPurchaseOrders(data, headers, rows, options);
                break;
            case SpreadsheetSheetType.InvoiceLineItems:
                ImportInvoiceLineItems(data, headers, rows, options);
                break;
            case SpreadsheetSheetType.PurchaseOrderLineItems:
                ImportPurchaseOrderLineItems(data, headers, rows, options);
                break;
            case SpreadsheetSheetType.Employees:
                ImportEmployees(data, headers, rows, options);
                break;
            case SpreadsheetSheetType.PayRuns:
                // Export only. An approved run's figures are frozen so a stub reprinted next
                // year still matches the one the employee was handed, and reading them back
                // from a sheet somebody could have typed in would defeat that. Listed rather
                // than left to fall through, so the decision is visible here.
                break;
            case SpreadsheetSheetType.Returns:
                ImportReturns(data, headers, rows, options);
                break;
            case SpreadsheetSheetType.LostDamaged:
                ImportLostDamaged(data, headers, rows, options);
                break;
            case SpreadsheetSheetType.BankStatement:
                // Bank statements are reference data for the Bank Matching feature and must never
                // be committed as book transactions. They are parsed by BankStatementImportService
                // instead. Reaching here means a bank file was routed to the normal importer.
                _errorLogger?.LogWarning(
                    "A bank statement sheet was detected by the spreadsheet importer and skipped. " +
                    "Use the Bank Matching feature to import bank statements.");
                break;
        }
    }

    private static int GetEntityCount(CompanyData data, SpreadsheetSheetType type) => type switch
    {
        SpreadsheetSheetType.Customers => data.Customers.Count,
        SpreadsheetSheetType.Invoices => data.Invoices.Count,
        SpreadsheetSheetType.Expenses => data.Expenses.Count,
        SpreadsheetSheetType.Products => data.Products.Count,
        SpreadsheetSheetType.Inventory => data.Inventory.Count,
        SpreadsheetSheetType.Payments => data.Payments.Count,
        SpreadsheetSheetType.Suppliers => data.Suppliers.Count,
        SpreadsheetSheetType.Revenue => data.Revenues.Count,
        SpreadsheetSheetType.RentalInventory => data.RentalInventory.Count,
        SpreadsheetSheetType.RentalRecords => data.Rentals.Count,
        SpreadsheetSheetType.Categories => data.Categories.Count,
        SpreadsheetSheetType.Locations => data.Locations.Count,
        SpreadsheetSheetType.RecurringInvoices => data.RecurringInvoices.Count,
        SpreadsheetSheetType.StockAdjustments => data.StockAdjustments.Count,
        SpreadsheetSheetType.PurchaseOrders => data.PurchaseOrders.Count,
        SpreadsheetSheetType.InvoiceLineItems => data.Invoices.SelectMany(i => i.LineItems).Count(),
        SpreadsheetSheetType.PurchaseOrderLineItems => data.PurchaseOrders.SelectMany(po => po.LineItems).Count(),
        SpreadsheetSheetType.Employees => data.Employees.Count,
        SpreadsheetSheetType.Returns => data.Returns.Count,
        SpreadsheetSheetType.LostDamaged => data.LostDamaged.Count,
        _ => 0
    };

    private SheetImportResult ImportBySheetTypeWithCount(
        SpreadsheetSheetType sheetType, CompanyData data, List<string> headers, List<List<object?>> rows,
        string sheetName, ImportOptions? options = null)
    {
        var countBefore = GetEntityCount(data, sheetType);
        if (options != null)
        {
            options.SkippedCount = 0;
            options.UpdatedCount = 0;
            options.InsertedCount = 0;
        }

        // Make this sheet's per-row currency (resolved by CurrencyImportPreparer) available to the
        // financial builders for the duration of this sheet import, then clear it.
        _currentSheetRowCurrency = options?.RowCurrencyBySheet is { } bySheet
            && bySheet.TryGetValue(sheetName, out var rowMap) ? rowMap : null;
        try
        {
            BeginSheet(rows);
            ImportBySheetType(sheetType, data, headers, rows, options);
        }
        finally
        {
            _currentSheetRowCurrency = null;
        }
        var countAfter = GetEntityCount(data, sheetType);

        // Line items are merged onto their parent order or invoice rather than added as
        // first-class entities, so the collection-count delta doesn't reflect the rows processed.
        // Use the explicit per-row count the importer recorded instead.
        bool mergedOntoParent = sheetType is SpreadsheetSheetType.PurchaseOrderLineItems
                                          or SpreadsheetSheetType.InvoiceLineItems;

        var inserted = mergedOntoParent && options != null
            ? options.InsertedCount
            : Math.Max(0, countAfter - countBefore);

        var result = new SheetImportResult
        {
            SheetName = sheetName,
            EntityType = sheetType.ToString(),
            Inserted = inserted,
            // Updates mutate an existing record in place (no collection growth), so they have to be
            // counted explicitly; otherwise they'd be misreported as dropped "missing field" rows.
            Updated = options?.UpdatedCount ?? 0
        };

        if (options?.SkipExistingRecords == true)
        {
            result.Skipped = options.SkippedCount;
            if (result.Skipped > 0)
                result.SkipReasons.Add($"{result.Skipped} {sheetType} records skipped (already exist)");
        }

        // Detect rows that were silently dropped (e.g., title rows, blank rows, summary rows).
        // Only meaningful where one row maps to one entity. Grouped sheet types (rental records
        // span several rows; purchase-order line items merge onto a parent) legitimately have
        // more rows than entities, so the difference there is expected, not a dropped row.
        bool rowMapsToEntity = sheetType is not (
            SpreadsheetSheetType.RentalRecords or SpreadsheetSheetType.PurchaseOrderLineItems
            or SpreadsheetSheetType.InvoiceLineItems);
        if (rowMapsToEntity)
        {
            var totalAccountedFor = result.Inserted + result.Updated + result.Skipped;
            var unaccounted = rows.Count - totalAccountedFor;
            if (unaccounted > 0)
            {
                result.Skipped += unaccounted;
                result.SkipReasons.Add($"{unaccounted} rows with missing or empty required fields");
            }
        }

        return result;
    }

    /// <summary>
    /// Collects every transaction date in the included Tier 1 financial sheets, using the SAME
    /// parser the import uses (so it cannot diverge from how dates are read) and the correct mapped
    /// date-column name per sheet type. Lets the import rate gate pre-fetch each date's exact rate.
    /// Best-effort: CSV and unparseable dates are skipped; any row the gate misses still self-heals
    /// via <see cref="Transaction.IsPendingConversion"/> + <see cref="PendingConversionService"/>.
    /// </summary>
    public List<DateTime> CollectTransactionDates(string filePath, SpreadsheetAnalysisResult analysis)
    {
        var dates = new List<DateTime>();
        if (analysis is null || filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return dates;
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var wb = new XLWorkbook(stream);
            foreach (var sheet in analysis.Sheets)
            {
                if (!sheet.IsIncluded || sheet.Tier != ProcessingTier.Tier1_Mapping)
                    continue;
                var dateColumn = sheet.DetectedType switch
                {
                    SpreadsheetSheetType.Revenue or SpreadsheetSheetType.Expenses
                        or SpreadsheetSheetType.Payments => "Date",
                    SpreadsheetSheetType.Invoices => "Issue Date",
                    SpreadsheetSheetType.PurchaseOrders => "Order Date",
                    _ => null
                };
                if (dateColumn is null) continue;
                if (!wb.TryGetWorksheet(sheet.SourceSheetName, out var ws)) continue;

                var headers = SpreadsheetRowReader.GetHeaders(ws);
                ApplyColumnMapping(headers, sheet); // source -> target names, in place
                var rows = SpreadsheetRowReader.GetDataRows(ws, headers.Count);
                var order = SpreadsheetRowReader.DetectDateOrder(rows, headers, dateColumn);
                foreach (var row in rows)
                {
                    var d = SpreadsheetRowReader.GetNullableDateTime(row, headers, dateColumn, order);
                    if (d.HasValue) dates.Add(d.Value.Date);
                }
            }
        }
        catch (Exception ex)
        {
            _errorLogger?.LogWarning($"Could not pre-scan import dates: {ex.Message}", "Import");
        }
        return dates;
    }

    internal static void ApplyColumnMapping(List<string> headers, SheetAnalysis sheetAnalysis)
    {
        foreach (var mapping in sheetAnalysis.ColumnMappings)
        {
            var idx = headers.FindIndex(h =>
                string.Equals(h, mapping.SourceColumn, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                headers[idx] = mapping.TargetColumn;
        }
    }

    private static readonly JsonSerializerOptions ImportJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new LenientEnumConverterFactory() }
    };

    /// <summary>
    /// Extracts the "id" field from a JSON entity element for deduplication.
    /// </summary>
    private static string? ExtractEntityId(JsonElement entityJson)
    {
        if (entityJson.TryGetProperty("id", out var idProp))
            return idProp.GetString();
        return null;
    }

    /// <summary>
    /// Reads a per-row currency from the entity JSON's <c>originalCurrency</c> (mapped from a
    /// currency column, or emitted by the LLM from an in-cell symbol/code) and normalizes it to
    /// an ISO code. Returns <c>null</c> when no currency is present or it cannot be resolved, so
    /// the importer keeps its existing company-currency behavior.
    /// </summary>
    private static string? ExtractRowCurrency(JsonElement entityJson, ImportOptions? options = null)
    {
        if (entityJson.ValueKind == JsonValueKind.Object
            && entityJson.TryGetProperty("originalCurrency", out var curProp)
            && curProp.ValueKind == JsonValueKind.String)
        {
            return NormalizeCurrencyToken(curProp.GetString(), options);
        }
        return null;
    }

    /// <summary>
    /// Normalizes a raw currency token into an ISO code: a known code is used as-is; an
    /// unambiguous symbol resolves to its code; an ambiguous symbol resolves via the user's
    /// choice (<see cref="ImportOptions.SymbolResolution"/>). Blank/unknown returns <c>null</c>.
    /// </summary>
    internal static string? NormalizeCurrencyToken(string? raw, ImportOptions? options)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var token = raw.Trim();

        if (CurrencyInfo.All.ContainsKey(token))
            return token.ToUpperInvariant();
        if (CurrencyInfo.TryResolveSymbol(token, out var code))
            return code;
        if (options?.SymbolResolution is { } map && map.TryGetValue(token, out var chosen))
            return chosen;
        return null;
    }

    /// <summary>
    /// The current Tier 1 sheet's per-row currency (data-row ordinal -> ISO code), set for the
    /// duration of one sheet import so deterministic builders can resolve currency by row order
    /// without threading the sheet name through every builder. <c>null</c> when no currency was
    /// detected for the sheet.
    /// </summary>
    private Dictionary<int, string>? _currentSheetRowCurrency;

    /// <summary>The ISO code detected for the given Tier 1 data-row ordinal, or <c>null</c>.</summary>
    private string? Tier1RowCurrency(int rowIndex)
        => _currentSheetRowCurrency is { } map && map.TryGetValue(rowIndex, out var code) ? code : null;

    /// <summary>
    /// The rows of the sheet being imported, so each date column's day/month order is decided once
    /// from all of its values rather than per cell, which read 15/03 as March and 05/03 in the same
    /// column as May. See <see cref="SpreadsheetRowReader.DetectDateOrder"/>.
    /// </summary>
    private List<List<object?>>? _sheetRows;
    private readonly Dictionary<string, SpreadsheetRowReader.DateOrder> _sheetDateOrders = new(StringComparer.OrdinalIgnoreCase);

    private void BeginSheet(List<List<object?>> rows)
    {
        _sheetRows = rows;
        _sheetDateOrders.Clear();
    }

    private SpreadsheetRowReader.DateOrder DateOrderOf(List<string> headers, string columnName)
    {
        if (_sheetRows == null)
            return SpreadsheetRowReader.DateOrder.Unknown;
        if (!_sheetDateOrders.TryGetValue(columnName, out var order))
            _sheetDateOrders[columnName] = order = SpreadsheetRowReader.DetectDateOrder(_sheetRows, headers, columnName);
        return order;
    }

    /// <summary>
    /// The currency of an amount that names none. It is the company's, which is not necessarily
    /// USD, so it still has to be converted to the USD base like any other.
    /// </summary>
    private static string CompanyCurrency(CompanyData data)
        => string.IsNullOrWhiteSpace(data.Settings.Localization.Currency) ? "USD" : data.Settings.Localization.Currency;

    /// <summary>
    /// Sets <c>OriginalCurrency</c> and the USD fields on a Revenue/Expense from the per-row
    /// detected currency, or else <paramref name="currentCurrency"/> (an updated record's own), or
    /// else the company currency.
    /// </summary>
    private void ApplyTransactionCurrency(Transaction txn, int rowIndex, CompanyData data, string? currentCurrency = null)
        => ApplyTransactionCurrencyCode(txn, Tier1RowCurrency(rowIndex) ?? currentCurrency ?? CompanyCurrency(data), data);

    /// <summary>
    /// The exact date's rate from the cache, which the import's rate gate filled beforehand, so a
    /// miss means the date can't be priced yet (a future date) and the row waits in the queue.
    /// </summary>
    private decimal? RateOn(string code, DateTime date) => UsdConversion.CachedRate(code, date, ExchangeRates);

    /// <summary>Per-row currency for a Payment, else <paramref name="currentCurrency"/> (an updated record's own), else the company currency.</summary>
    private void ApplyPaymentCurrency(Payment payment, int rowIndex, CompanyData data, string? currentCurrency = null)
        => ApplyPaymentCurrencyCode(payment, Tier1RowCurrency(rowIndex) ?? currentCurrency ?? CompanyCurrency(data), data);

    /// <summary>
    /// Stamps a Payment with <paramref name="code"/> and stores its USD amount at its exact date, or
    /// leaves it pending in the conversion queue. Shared by the Tier 1 and Tier 2 import paths.
    /// </summary>
    private void ApplyPaymentCurrencyCode(Payment payment, string code, CompanyData data)
    {
        payment.OriginalCurrency = code;
        UsdConversion.Apply(data, payment, RateOn(code, payment.Date));
    }

    /// <summary>
    /// Stamps an Invoice with <paramref name="code"/> and stores its Total and Balance in USD at its
    /// exact issue date, or leaves it pending in the conversion queue. Shared by the Tier 1 and Tier 2
    /// import paths.
    /// </summary>
    private void ApplyInvoiceCurrencyCode(Invoice invoice, string code, CompanyData data)
    {
        invoice.OriginalCurrency = code;
        UsdConversion.Apply(data, invoice, RateOn(code, invoice.IssueDate));
    }

    /// <summary>Per-row currency for a PurchaseOrder, else <paramref name="currentCurrency"/> (an updated record's own), else the company currency.</summary>
    private void ApplyPurchaseOrderCurrency(PurchaseOrder po, int rowIndex, CompanyData data, string? currentCurrency = null)
        => ApplyPurchaseOrderCurrencyCode(po, Tier1RowCurrency(rowIndex) ?? currentCurrency ?? CompanyCurrency(data), data);

    /// <summary>
    /// Stamps a PurchaseOrder with <paramref name="code"/> and stores its Total in USD at its exact
    /// order date, or leaves it pending in the conversion queue. Shared by the Tier 1 and Tier 2
    /// import paths.
    /// </summary>
    private void ApplyPurchaseOrderCurrencyCode(PurchaseOrder po, string code, CompanyData data)
    {
        po.OriginalCurrency = code;
        UsdConversion.Apply(data, po, RateOn(code, po.OrderDate));
    }

    /// <summary>
    /// Stamps a Revenue/Expense with <paramref name="code"/> and stores every amount in USD at its
    /// exact date, or leaves it pending in the conversion queue. The decision rests on the rate, not
    /// the total, so a row with a zero total but a tax or fee still waits rather than losing them.
    /// </summary>
    private void ApplyTransactionCurrencyCode(Transaction txn, string code, CompanyData data)
    {
        txn.OriginalCurrency = code;
        UsdConversion.Apply(data, txn, RateOn(code, txn.Date));
    }

    /// <summary>Paid invoices brought in by the current import, given their revenue by <see cref="FinishImport"/>.</summary>
    private readonly List<Invoice> _invoicesAwaitingRevenue = [];

    /// <summary>
    /// Imported revenue rows that named an invoice, settled by <see cref="FinishImport"/>, with whether
    /// each is new and the invoice it named before the import.
    /// </summary>
    private readonly List<(Revenue Revenue, bool IsNew, string? InvoiceBefore)> _revenuesNamingAnInvoice = [];

    private void NoteRevenueNamingAnInvoice(Revenue revenue, bool isNew, string? invoiceBefore)
    {
        if (!string.IsNullOrEmpty(revenue.InvoiceId))
            _revenuesNamingAnInvoice.Add((revenue, isNew, invoiceBefore));
    }

    /// <summary>
    /// Closes out an import: brings the id counters up to date, settles revenue that named an
    /// invoice, then gives each paid invoice it brought in a revenue unless one came in with it.
    /// Held until every sheet is in because the Revenue sheet can come before or after the
    /// Invoices sheet, and so the new revenue is numbered past every imported id rather than
    /// from a counter that has not caught up with them.
    /// </summary>
    private void FinishImport(CompanyData data)
    {
        UpdateIdCounters(data);

        foreach (var (revenue, isNew, invoiceBefore) in _revenuesNamingAnInvoice)
        {
            if (!data.Revenues.Contains(revenue))
                continue;

            var invoice = data.Invoices.FirstOrDefault(i => i.Id == revenue.InvoiceId)
                          ?? data.Invoices.FirstOrDefault(i => i.InvoiceNumber == revenue.InvoiceId);

            // Only a new revenue, one with no lines, or one the row moved to another invoice takes
            // lines here. An existing sale keeps its own, with the cost of goods sold they carry.
            var sameInvoice = invoiceBefore != null && (invoice == null
                ? invoiceBefore == revenue.InvoiceId
                : invoiceBefore == invoice.Id || invoiceBefore == invoice.InvoiceNumber);
            var takesLines = isNew || revenue.LineItems.Count == 0 || !sameInvoice;

            // No such invoice, so it is an ordinary sale, the way a payment naming a missing
            // invoice has its reference cleared.
            if (invoice == null)
            {
                revenue.InvoiceId = null;
                if (!revenue.IsKeptDeposit && takesLines)
                    SetSingleLine(data, revenue, isNew ? null : revenue.LineItems, LineFields.All, isPurchase: false);
                continue;
            }

            revenue.InvoiceId = invoice.Id;

            // Its description only summarises the invoice's lines ("Widget (+2 more)"), so the
            // lines come from the invoice. A kept deposit has none, as when the app records one.
            if (takesLines && !revenue.IsKeptDeposit && invoice.LineItems.Count > 0)
                ReplaceLines(data, revenue, revenue.LineItems, CopyLines(invoice), isPurchase: false);
        }
        _revenuesNamingAnInvoice.Clear();

        foreach (var awaiting in _invoicesAwaitingRevenue)
        {
            if (data.Invoices.Contains(awaiting))
                AddAutoRevenueForInvoice(data, awaiting);
        }
        _invoicesAwaitingRevenue.Clear();
    }

    /// <summary>
    /// Creates the linked Revenue for a paid/partially-paid imported invoice (so it shows on the
    /// dashboard and analytics). When the invoice's USD value is not yet known (future-dated, or a
    /// rate the gate missed), the Revenue is created pending and enqueued so it converts at the
    /// exact date later, instead of storing the native amount as if it were USD.
    /// </summary>
    private static void AddAutoRevenueForInvoice(CompanyData data, Invoice invoice)
    {
        // A kept deposit is linked to the invoice too, but it is not the invoice's revenue.
        if (invoice.AmountPaid <= 0 || data.Revenues.Any(r => r.InvoiceId == invoice.Id && !r.IsKeptDeposit))
            return;

        var revenueId = new IdGenerator(data).NextRevenueId(invoice.IssueDate);
        var isPaid = invoice.Status == InvoiceStatus.Paid || invoice.Balance <= 0;

        var revenue = new Revenue
        {
            Id = revenueId,
            Date = invoice.IssueDate,
            CustomerId = invoice.CustomerId,
            Description = $"Invoice {invoice.InvoiceNumber}",
            // Sales by Product counts revenue only through its lines, as when the app makes one.
            LineItems = CopyLines(invoice),
            Quantity = 1,
            UnitPrice = invoice.Subtotal,
            Subtotal = invoice.Subtotal,
            Amount = invoice.Subtotal,
            TaxAmount = invoice.TaxAmount,
            Total = invoice.Total,
            PaymentMethod = PaymentMethod.Other,
            PaymentStatus = isPaid ? RevenuePaymentStatus.Paid : RevenuePaymentStatus.Partial,
            Notes = $"Auto-created from imported invoice {invoice.InvoiceNumber}",
            InvoiceId = invoice.Id,
            ReferenceNumber = invoice.InvoiceNumber,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            OriginalCurrency = invoice.OriginalCurrency
        };
        UsdConversion.Apply(data, revenue, UsdConversion.InvoiceRate(invoice));
        data.Revenues.Add(revenue);
    }

    private static List<LineItem> CopyLines(Invoice invoice) =>
        invoice.LineItems.Select(li => new LineItem
        {
            ProductId = li.ProductId,
            Description = li.Description,
            Quantity = li.Quantity,
            UnitPrice = li.UnitPrice,
            TaxRate = li.TaxRate,
            Discount = li.Discount
        }).ToList();

    #region Task 2C: natural-key identity for id-less rows

    /// <summary>
    /// Builds a stable, normalized natural key from a small set of identifying fields for the
    /// given entity type. Used ONLY to derive a deterministic id for an id-less row so that
    /// re-importing the same file is idempotent. Returns <c>null</c> when there are not enough
    /// fields to form a meaningful key (caller then keeps today's behavior and records the row
    /// as unimported rather than inventing an arbitrary id).
    ///
    /// This is deliberately NOT used to merge two rows arriving in the same import: identical
    /// rows share a key but are kept distinct by the caller's ordinal scheme.
    /// </summary>
    internal static string? NaturalKey(SpreadsheetSheetType type, JsonElement json)
    {
        // Each entity type contributes a small, stable set of identifying fields. A field only
        // "counts" toward the key when it carries a non-empty value; we require at least two
        // present fields (checked below) so a near-empty row does not get a meaningless (and
        // collision-prone) key.
        string[] fields = type switch
        {
            SpreadsheetSheetType.Expenses or SpreadsheetSheetType.Revenue =>
                ["date", "amount", "total", "description"],
            SpreadsheetSheetType.Invoices =>
                ["invoiceNumber", "issueDate", "total"],
            SpreadsheetSheetType.Payments =>
                ["date", "amount", "customerId", "invoiceId"],
            _ =>
                ["date", "amount", "total", "description", "name", "customerId", "supplierId"]
        };

        var parts = new List<string>();
        int present = 0;
        foreach (var field in fields)
        {
            var value = NormalizeKeyField(json, field);
            if (!string.IsNullOrEmpty(value))
                present++;
            // Include the field (even if empty) positionally so the key stays stable and two
            // rows that differ only in one field produce different keys.
            parts.Add($"{field}={value}");
        }

        // Need at least two populated identifying fields for a meaningful key. A single value
        // (e.g. just an amount, or just a date) is too weak to safely deduplicate on.
        if (present < 2)
            return null;

        return string.Join("|", parts);
    }

    /// <summary>
    /// Reads a field from the raw JSON and normalizes it for keying: numbers use the invariant
    /// round-trip form (so "10" and "10.0" key the same), dates use the date component, strings
    /// are trimmed and lowercased. Missing/null returns an empty string.
    /// </summary>
    private static string NormalizeKeyField(JsonElement json, string field)
    {
        // Property lookup is case-insensitive to mirror the deserializer (camelCase tolerance).
        JsonElement prop = default;
        bool found = false;
        foreach (var p in json.EnumerateObject())
        {
            if (string.Equals(p.Name, field, StringComparison.OrdinalIgnoreCase))
            {
                prop = p.Value;
                found = true;
                break;
            }
        }
        if (!found) return string.Empty;

        switch (prop.ValueKind)
        {
            case JsonValueKind.Null or JsonValueKind.Undefined:
                return string.Empty;
            case JsonValueKind.Number:
                return prop.TryGetDecimal(out var dec)
                    ? dec.ToString(CultureInfo.InvariantCulture)
                    : prop.GetRawText();
            case JsonValueKind.True:
                return "true";
            case JsonValueKind.False:
                return "false";
            case JsonValueKind.String:
                var s = prop.GetString() ?? string.Empty;
                s = s.Trim();
                // Normalize numeric strings so "10" and "10.00" collapse, and dates to date-only
                // so a time component or format drift does not split the same logical row.
                if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var sdec))
                    return sdec.ToString(CultureInfo.InvariantCulture);
                if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var sdate))
                    return sdate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return s.ToLowerInvariant();
            default:
                return string.Empty;
        }
    }

    /// <summary>
    /// Stable hash of a natural key: SHA-256, hex, truncated to 16 chars. Deliberately NOT
    /// <see cref="object.GetHashCode"/> / <see cref="string.GetHashCode()"/>, which are not
    /// stable across runs/platforms and would break idempotent re-import.
    /// </summary>
    private static string StableHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes, 0, 8); // 8 bytes -> 16 hex chars
    }

    /// <summary>Short id prefix per entity type for derived natural-key ids.</summary>
    private static string TypePrefix(SpreadsheetSheetType type) => type switch
    {
        SpreadsheetSheetType.Expenses => "EXP",
        SpreadsheetSheetType.Revenue => "REV",
        SpreadsheetSheetType.Invoices => "INV",
        SpreadsheetSheetType.Payments => "PAY",
        SpreadsheetSheetType.Customers => "CUS",
        SpreadsheetSheetType.Suppliers => "SUP",
        SpreadsheetSheetType.Products => "PRD",
        _ => type.ToString().ToUpperInvariant()
    };

    /// <summary>
    /// Returns a new <see cref="JsonElement"/> equal to <paramref name="source"/> but with an
    /// "id" property set to <paramref name="id"/> (added or overwritten). Used to stamp the
    /// derived deterministic id onto an id-less row before it flows through the normal importer.
    /// </summary>
    private static JsonElement WithId(JsonElement source, string id)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("id", id);
            if (source.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in source.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "id", StringComparison.OrdinalIgnoreCase))
                        continue; // replaced above
                    prop.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
        }
        using var doc = JsonDocument.Parse(buffer.ToArray());
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Snapshot of the ids currently present for a given entity type. Used to count how many
    /// incoming rows land on a pre-existing record (re-import detection).
    /// </summary>
    private static IEnumerable<string> GetExistingEntityIds(CompanyData data, SpreadsheetSheetType type) => type switch
    {
        SpreadsheetSheetType.Customers => data.Customers.Select(c => c.Id),
        SpreadsheetSheetType.Suppliers => data.Suppliers.Select(s => s.Id),
        SpreadsheetSheetType.Products => data.Products.Select(p => p.Id),
        SpreadsheetSheetType.Invoices => data.Invoices.Select(i => i.Id),
        SpreadsheetSheetType.Expenses => data.Expenses.Select(e => e.Id),
        SpreadsheetSheetType.Revenue => data.Revenues.Select(r => r.Id),
        SpreadsheetSheetType.Payments => data.Payments.Select(p => p.Id),
        SpreadsheetSheetType.Categories => data.Categories.Select(c => c.Id),
        SpreadsheetSheetType.Locations => data.Locations.Select(l => l.Id),
        SpreadsheetSheetType.Inventory => data.Inventory.Select(i => i.Id),
        SpreadsheetSheetType.RentalInventory => data.RentalInventory.Select(r => r.Id),
        SpreadsheetSheetType.RentalRecords => data.Rentals.Select(r => r.Id),
        SpreadsheetSheetType.RecurringInvoices => data.RecurringInvoices.Select(r => r.Id),
        SpreadsheetSheetType.StockAdjustments => data.StockAdjustments.Select(s => s.Id),
        SpreadsheetSheetType.PurchaseOrders => data.PurchaseOrders.Select(p => p.Id),
        SpreadsheetSheetType.Returns => data.Returns.Select(r => r.Id),
        SpreadsheetSheetType.LostDamaged => data.LostDamaged.Select(l => l.Id),
        _ => []
    };

    #endregion

    private ImportEntityResult ImportSingleEntity(CompanyData data, SpreadsheetSheetType entityType, JsonElement entityJson, ImportOptions? options = null, ReferenceResolutionContext? refContext = null)
    {
        var jsonStr = entityJson.GetRawText();
        var opts = ImportJsonOptions;
        var skipExisting = options?.SkipExistingRecords == true;
        var given = GivenFields(entityJson);

        // An update changes only what the row gives, as the column import does; a new record takes
        // the row as it is. Derived fields the importer works out from given ones count as given.
        T Merge<T>(T incoming, T? existing, params string[] alsoGiven) where T : class
        {
            if (existing == null) return incoming;
            var fields = new HashSet<string>(given, StringComparer.OrdinalIgnoreCase);
            fields.UnionWith(alsoGiven);
            return incoming.FillAbsent(existing, fields);
        }

        // Whether the row changes any of these fields, which a new record always does.
        bool Changes(object? existing, string[] fields) => existing == null || fields.Any(given.Contains);

        switch (entityType)
        {
            case SpreadsheetSheetType.Customers:
                var customer = JsonSerializer.Deserialize<Customer>(jsonStr, opts);
                if (customer != null && !string.IsNullOrEmpty(customer.Id))
                {
                    var existing = data.Customers.FirstOrDefault(c => c.Id == customer.Id);
                    if (skipExisting && existing != null) return ImportEntityResult.SkippedExisting;
                    customer = Merge(customer, existing);
                    customer.Name = NameOrUnknown(customer.Name);
                    data.Customers.AddOrUpdate(existing, customer);
                    return existing != null ? ImportEntityResult.Updated : ImportEntityResult.Inserted;
                }
                return ImportEntityResult.Failed;
            case SpreadsheetSheetType.Suppliers:
                var supplier = JsonSerializer.Deserialize<Supplier>(jsonStr, opts);
                if (supplier != null && !string.IsNullOrEmpty(supplier.Id))
                {
                    var existing = data.Suppliers.FirstOrDefault(s => s.Id == supplier.Id);
                    if (skipExisting && existing != null) return ImportEntityResult.SkippedExisting;
                    data.Suppliers.AddOrUpdate(existing, Merge(supplier, existing));
                    return existing != null ? ImportEntityResult.Updated : ImportEntityResult.Inserted;
                }
                return ImportEntityResult.Failed;
            case SpreadsheetSheetType.Products:
                var product = JsonSerializer.Deserialize<Product>(jsonStr, opts);
                if (product != null && !string.IsNullOrEmpty(product.Id))
                {
                    var existing = data.Products.FirstOrDefault(p => p.Id == product.Id);
                    if (skipExisting && existing != null) return ImportEntityResult.SkippedExisting;

                    // A category named by the row replaces the one the product had.
                    var categoryGiven = given.Contains("categoryName") || given.Contains("categoryId");
                    product = Merge(product, existing, categoryGiven ? ["categoryId"] : []);

                    // Auto-create category if product has a category name but no matching category
                    if (existing == null || categoryGiven)
                        ResolveProductCategory(data, product, entityJson);

                    data.Products.AddOrUpdate(existing, product);
                    return existing != null ? ImportEntityResult.Updated : ImportEntityResult.Inserted;
                }
                return ImportEntityResult.Failed;
            case SpreadsheetSheetType.Invoices:
                var invoice = JsonSerializer.Deserialize<Invoice>(jsonStr, opts);

                // Either column can identify the invoice, and each fills in for the other.
                // Sheets from elsewhere usually carry only an invoice number, and this app's own
                // export carries both, so neither can be assumed present.
                if (invoice != null)
                {
                    if (string.IsNullOrEmpty(invoice.Id))
                        invoice.Id = invoice.InvoiceNumber;
                    else if (string.IsNullOrEmpty(invoice.InvoiceNumber))
                        invoice.InvoiceNumber = invoice.Id;
                }

                if (invoice != null && !string.IsNullOrEmpty(invoice.Id))
                {
                    var existing = data.Invoices.FirstOrDefault(i => i.Id == invoice.Id);
                    if (skipExisting && existing != null) return ImportEntityResult.SkippedExisting;
                    invoice = Merge(invoice, existing);

                    // The currency first: the invoice's payments are matched to it by currency.
                    var priced = Changes(existing, InvoicePriceFields);
                    if (priced)
                        invoice.OriginalCurrency = ExtractRowCurrency(entityJson, options) ?? existing?.OriginalCurrency ?? CompanyCurrency(data);

                    var givenStatus = ParseImportedInvoiceStatus(JsonText(entityJson, "status"));
                    SetImportedBalance(invoice,
                        given.Contains("amountPaid") ? invoice.AmountPaid : null,
                        given.Contains("balance") ? invoice.Balance : null,
                        recompute: Changes(existing, ["amountPaid", "total"]), givenStatus, data.Payments);
                    if (Changes(existing, ["status", "amountPaid", "balance", "total"]))
                        SetImportedStatus(invoice, givenStatus ?? existing?.Status ?? InvoiceStatus.Draft,
                            amountsSet: Changes(existing, ["amountPaid", "balance", "total"]));

                    // Convert Total/Balance at the exact issue date, deferring (pending + enqueue)
                    // when unpriceable. Left as it is when nothing it is priced from changed.
                    if (priced)
                        ApplyInvoiceCurrencyCode(invoice, invoice.OriginalCurrency, data);

                    // Resolve customer reference by name, else create a placeholder
                    if (Changes(existing, ["customerId"]))
                        invoice.CustomerId = EnsureCustomerExists(data, invoice.CustomerId, refContext) ?? invoice.CustomerId;

                    _invoicesAwaitingRevenue.Add(data.Invoices.AddOrUpdate(existing, invoice));

                    return existing != null ? ImportEntityResult.Updated : ImportEntityResult.Inserted;
                }
                return ImportEntityResult.Failed;
            case SpreadsheetSheetType.Expenses:
                var expense = JsonSerializer.Deserialize<Expense>(jsonStr, opts);
                if (expense != null && !string.IsNullOrEmpty(expense.Id))
                {
                    // Checked before anything is queued or created for the row, so a skipped row
                    // leaves the existing record's queued conversion alone.
                    var existing = data.Expenses.FirstOrDefault(e => e.Id == expense.Id);
                    if (skipExisting && existing != null) return ImportEntityResult.SkippedExisting;

                    // The AI emits quantity + unit price but not the pre-tax Amount, so derive it
                    // (Quantity defaults to 1) before building the line item, so the line-item
                    // subtotal agrees with the stored Total.
                    var expenseAmountDerived = !given.Contains("amount") && Changes(existing, ["quantity", "unitPrice"]);
                    expense = Merge(expense, existing, expenseAmountDerived ? ["amount"] : []);
                    if (expenseAmountDerived || expense.Amount == 0)
                        expense.Amount = expense.Quantity * expense.UnitPrice;

                    // Convert each amount to USD at the transaction's EXACT date, from the row's own
                    // currency or else the record's or the company's. Future-dated/unpriceable rows
                    // become pending. Left as it is when nothing it is priced from changed.
                    if (Changes(existing, TransactionPriceFields))
                        ApplyTransactionCurrencyCode(expense, ExtractRowCurrency(entityJson, options) ?? existing?.OriginalCurrency ?? CompanyCurrency(data), data);

                    // Resolve supplier reference by name, else create a placeholder
                    if (!string.IsNullOrEmpty(expense.SupplierId) && Changes(existing, ["supplierId"]))
                        expense.SupplierId = EnsureSupplierExists(data, expense.SupplierId, refContext);

                    SetImportedLines(data, expense, existing, entityJson, given, isPurchase: true);

                    data.Expenses.AddOrUpdate(existing, expense);
                    return existing != null ? ImportEntityResult.Updated : ImportEntityResult.Inserted;
                }
                return ImportEntityResult.Failed;
            case SpreadsheetSheetType.Revenue:
                var revenue = JsonSerializer.Deserialize<Revenue>(jsonStr, opts);
                if (revenue != null && !string.IsNullOrEmpty(revenue.Id))
                {
                    var existing = data.Revenues.FirstOrDefault(r => r.Id == revenue.Id);
                    if (skipExisting && existing != null) return ImportEntityResult.SkippedExisting;

                    // The AI emits quantity + unit price but not the pre-tax Amount, so derive it
                    // (Quantity defaults to 1) before building the line item.
                    var revenueAmountDerived = !given.Contains("amount") && Changes(existing, ["quantity", "unitPrice"]);
                    revenue = Merge(revenue, existing, revenueAmountDerived ? ["amount"] : []);
                    if (revenueAmountDerived || revenue.Amount == 0)
                        revenue.Amount = revenue.Quantity * revenue.UnitPrice;

                    // PaymentStatus is already normalized by the enum's JSON
                    // converter (legacy typos → Paid fallback), no separate call.
                    // Convert each amount to USD at the transaction's EXACT date, from the row's own
                    // currency or else the record's or the company's. Future-dated/unpriceable rows
                    // become pending. Left as it is when nothing it is priced from changed.
                    if (Changes(existing, TransactionPriceFields))
                        ApplyTransactionCurrencyCode(revenue, ExtractRowCurrency(entityJson, options) ?? existing?.OriginalCurrency ?? CompanyCurrency(data), data);

                    // Resolve customer reference by name, else create a placeholder
                    if (!string.IsNullOrEmpty(revenue.CustomerId) && Changes(existing, ["customerId"]))
                        revenue.CustomerId = EnsureCustomerExists(data, revenue.CustomerId, refContext) ?? revenue.CustomerId;

                    // Revenue from an invoice takes the invoice's lines instead, once every sheet is in.
                    if (string.IsNullOrEmpty(revenue.InvoiceId))
                        SetImportedLines(data, revenue, existing, entityJson, given, isPurchase: false);

                    var invoiceBefore = existing?.InvoiceId;
                    var liveRevenue = data.Revenues.AddOrUpdate(existing, revenue);
                    NoteRevenueNamingAnInvoice(liveRevenue, existing == null, invoiceBefore);
                    return existing != null ? ImportEntityResult.Updated : ImportEntityResult.Inserted;
                }
                return ImportEntityResult.Failed;
            case SpreadsheetSheetType.Payments:
                var payment = JsonSerializer.Deserialize<Payment>(jsonStr, opts);
                if (payment != null && !string.IsNullOrEmpty(payment.Id))
                {
                    var existing = data.Payments.FirstOrDefault(p => p.Id == payment.Id);
                    if (skipExisting && existing != null) return ImportEntityResult.SkippedExisting;
                    payment = Merge(payment, existing);

                    // Convert at the exact payment date from the row's own currency or else the
                    // record's or the company's, deferring (pending + enqueue) when unpriceable so it
                    // self-heals later rather than being stuck at 0. Shared with the Tier 1 path.
                    if (Changes(existing, ["date", "amount", "originalCurrency"]))
                        ApplyPaymentCurrencyCode(payment, ExtractRowCurrency(entityJson, options) ?? existing?.OriginalCurrency ?? CompanyCurrency(data), data);

                    // Resolve customer reference by name, else create a placeholder
                    if (Changes(existing, ["customerId", "invoiceId"]))
                    {
                        payment.CustomerId = EnsureCustomerExists(data, payment.CustomerId, refContext) ?? payment.CustomerId;
                        payment.InvoiceId = EnsureInvoiceExists(data, payment.InvoiceId, payment.CustomerId) ?? payment.InvoiceId;
                    }

                    data.Payments.AddOrUpdate(existing, payment);
                    return existing != null ? ImportEntityResult.Updated : ImportEntityResult.Inserted;
                }
                return ImportEntityResult.Failed;
            case SpreadsheetSheetType.Categories:
                var category = JsonSerializer.Deserialize<Category>(jsonStr, opts);
                if (category != null && !string.IsNullOrEmpty(category.Id))
                {
                    var existing = data.Categories.FirstOrDefault(c => c.Id == category.Id);
                    if (skipExisting && existing != null) return ImportEntityResult.SkippedExisting;
                    data.Categories.AddOrUpdate(existing, Merge(category, existing));
                    return existing != null ? ImportEntityResult.Updated : ImportEntityResult.Inserted;
                }
                return ImportEntityResult.Failed;
            case SpreadsheetSheetType.Locations:
                var location = JsonSerializer.Deserialize<Location>(jsonStr, opts);
                if (location != null && !string.IsNullOrEmpty(location.Id))
                {
                    var existing = data.Locations.FirstOrDefault(l => l.Id == location.Id);
                    if (skipExisting && existing != null) return ImportEntityResult.SkippedExisting;
                    data.Locations.AddOrUpdate(existing, Merge(location, existing));
                    return existing != null ? ImportEntityResult.Updated : ImportEntityResult.Inserted;
                }
                return ImportEntityResult.Failed;
            case SpreadsheetSheetType.Inventory:
                var invItem = JsonSerializer.Deserialize<InventoryItem>(jsonStr, opts);
                if (invItem != null && !string.IsNullOrEmpty(invItem.Id))
                {
                    if (skipExisting && data.Inventory.Any(i => i.Id == invItem.Id)) return ImportEntityResult.SkippedExisting;
                    return InventoryStockService.ImportStockRecord(data, invItem, given)
                        ? ImportEntityResult.Inserted
                        : ImportEntityResult.Updated;
                }
                return ImportEntityResult.Failed;
            case SpreadsheetSheetType.RentalInventory:
                var rentalItem = JsonSerializer.Deserialize<RentalItem>(jsonStr, opts);
                if (rentalItem != null && !string.IsNullOrEmpty(rentalItem.Id))
                {
                    var existing = data.RentalInventory.FirstOrDefault(r => r.Id == rentalItem.Id);
                    if (skipExisting && existing != null) return ImportEntityResult.SkippedExisting;
                    data.RentalInventory.AddOrUpdate(existing, Merge(rentalItem, existing));
                    return existing != null ? ImportEntityResult.Updated : ImportEntityResult.Inserted;
                }
                return ImportEntityResult.Failed;
            case SpreadsheetSheetType.RentalRecords:
                var rental = JsonSerializer.Deserialize<RentalRecord>(jsonStr, opts);
                if (rental != null && !string.IsNullOrEmpty(rental.Id))
                {
                    var existing = data.Rentals.FirstOrDefault(r => r.Id == rental.Id);
                    if (skipExisting && existing != null) return ImportEntityResult.SkippedExisting;
                    rental = Merge(rental, existing);
                    if (!string.IsNullOrEmpty(rental.CustomerId) && Changes(existing, ["customerId"]))
                        rental.CustomerId = EnsureCustomerExists(data, rental.CustomerId, refContext) ?? rental.CustomerId;
                    data.Rentals.AddOrUpdate(existing, rental);
                    return existing != null ? ImportEntityResult.Updated : ImportEntityResult.Inserted;
                }
                return ImportEntityResult.Failed;
            case SpreadsheetSheetType.RecurringInvoices:
                var recurring = JsonSerializer.Deserialize<RecurringInvoice>(jsonStr, opts);
                if (recurring != null && !string.IsNullOrEmpty(recurring.Id))
                {
                    var existing = data.RecurringInvoices.FirstOrDefault(r => r.Id == recurring.Id);
                    if (skipExisting && existing != null) return ImportEntityResult.SkippedExisting;
                    recurring = Merge(recurring, existing);
                    if (!string.IsNullOrEmpty(recurring.CustomerId) && Changes(existing, ["customerId"]))
                        recurring.CustomerId = EnsureCustomerExists(data, recurring.CustomerId, refContext) ?? recurring.CustomerId;
                    if (recurring.Status == default)
                        recurring.Status = RecurringInvoiceStatus.Active;
                    data.RecurringInvoices.AddOrUpdate(existing, recurring);
                    return existing != null ? ImportEntityResult.Updated : ImportEntityResult.Inserted;
                }
                return ImportEntityResult.Failed;
            case SpreadsheetSheetType.StockAdjustments:
                var adjustment = JsonSerializer.Deserialize<StockAdjustment>(jsonStr, opts);
                if (adjustment != null && !string.IsNullOrEmpty(adjustment.Id))
                {
                    var existing = data.StockAdjustments.FirstOrDefault(s => s.Id == adjustment.Id);
                    if (skipExisting && existing != null) return ImportEntityResult.SkippedExisting;
                    data.StockAdjustments.AddOrUpdate(existing, Merge(adjustment, existing));
                    return existing != null ? ImportEntityResult.Updated : ImportEntityResult.Inserted;
                }
                return ImportEntityResult.Failed;
            case SpreadsheetSheetType.PurchaseOrders:
                var po = JsonSerializer.Deserialize<PurchaseOrder>(jsonStr, opts);
                if (po != null && !string.IsNullOrEmpty(po.Id))
                {
                    var existing = data.PurchaseOrders.FirstOrDefault(p => p.Id == po.Id);
                    if (skipExisting && existing != null) return ImportEntityResult.SkippedExisting;
                    po = Merge(po, existing);

                    // Convert at the exact order date from the row's own currency or else the
                    // record's or the company's, deferring (pending + enqueue) when unpriceable.
                    // Shared with Tier 1.
                    if (Changes(existing, ["orderDate", "total", "originalCurrency"]))
                        ApplyPurchaseOrderCurrencyCode(po, ExtractRowCurrency(entityJson, options) ?? existing?.OriginalCurrency ?? CompanyCurrency(data), data);

                    if (!string.IsNullOrEmpty(po.SupplierId) && Changes(existing, ["supplierId"]))
                        po.SupplierId = EnsureSupplierExists(data, po.SupplierId, refContext) ?? po.SupplierId;

                    data.PurchaseOrders.AddOrUpdate(existing, po);
                    return existing != null ? ImportEntityResult.Updated : ImportEntityResult.Inserted;
                }
                return ImportEntityResult.Failed;
            case SpreadsheetSheetType.InvoiceLineItems:
                // Like PO line items below: they belong to an invoice rather than to a
                // collection of their own, so the parent has to be found first.
                var invoiceLineItem = JsonSerializer.Deserialize<LineItem>(jsonStr, opts);
                if (invoiceLineItem != null
                    && entityJson.TryGetProperty("invoiceId", out var invoiceIdEl))
                {
                    var lineInvoiceId = invoiceIdEl.GetString();
                    var parentInvoice = data.Invoices.FirstOrDefault(i => i.Id == lineInvoiceId)
                                        ?? data.Invoices.FirstOrDefault(i => i.InvoiceNumber == lineInvoiceId);
                    if (parentInvoice != null)
                    {
                        parentInvoice.LineItems.Add(invoiceLineItem);
                        return ImportEntityResult.Inserted;
                    }
                }
                return ImportEntityResult.Failed;
            case SpreadsheetSheetType.PurchaseOrderLineItems:
                // PO line items need special handling - they belong to a PurchaseOrder
                var poLineItem = JsonSerializer.Deserialize<PurchaseOrderLineItem>(jsonStr, opts);
                if (poLineItem != null)
                {
                    // Try to find PO ID from the JSON (schema uses "PO ID" field)
                    if (entityJson.TryGetProperty("poId", out var poIdEl))
                    {
                        var poId = poIdEl.GetString();
                        var parentPo = data.PurchaseOrders.FirstOrDefault(p => p.Id == poId);
                        if (parentPo != null)
                        {
                            parentPo.LineItems.Add(poLineItem);
                            return ImportEntityResult.Inserted;
                        }
                    }
                }
                return ImportEntityResult.Failed;
            case SpreadsheetSheetType.Returns:
                var returnRecord = JsonSerializer.Deserialize<Return>(jsonStr, opts);
                if (returnRecord != null && !string.IsNullOrEmpty(returnRecord.Id))
                {
                    var existing = data.Returns.FirstOrDefault(r => r.Id == returnRecord.Id);
                    if (skipExisting && existing != null) return ImportEntityResult.SkippedExisting;
                    returnRecord = Merge(returnRecord, existing);
                    if (!string.IsNullOrEmpty(returnRecord.CustomerId) && Changes(existing, ["customerId"]))
                        returnRecord.CustomerId = EnsureCustomerExists(data, returnRecord.CustomerId, refContext) ?? returnRecord.CustomerId;
                    if (!string.IsNullOrEmpty(returnRecord.SupplierId) && Changes(existing, ["supplierId"]))
                        returnRecord.SupplierId = EnsureSupplierExists(data, returnRecord.SupplierId, refContext) ?? returnRecord.SupplierId;
                    data.Returns.AddOrUpdate(existing, returnRecord);
                    return existing != null ? ImportEntityResult.Updated : ImportEntityResult.Inserted;
                }
                return ImportEntityResult.Failed;
            case SpreadsheetSheetType.LostDamaged:
                var lostDamaged = JsonSerializer.Deserialize<LostDamaged>(jsonStr, opts);
                if (lostDamaged != null && !string.IsNullOrEmpty(lostDamaged.Id))
                {
                    var existing = data.LostDamaged.FirstOrDefault(ld => ld.Id == lostDamaged.Id);
                    if (skipExisting && existing != null) return ImportEntityResult.SkippedExisting;
                    data.LostDamaged.AddOrUpdate(existing, Merge(lostDamaged, existing));
                    return existing != null ? ImportEntityResult.Updated : ImportEntityResult.Inserted;
                }
                return ImportEntityResult.Failed;
            case SpreadsheetSheetType.Employees:
                // Reachable: ImportSchemaDefinition publishes an Employees schema and the
                // rescue classifier can return it. Same shape as Customers above.
                var employee = JsonSerializer.Deserialize<Models.Payroll.Employee>(jsonStr, opts);
                if (employee != null && !string.IsNullOrEmpty(employee.Id))
                {
                    var existingEmployee = data.Employees.FirstOrDefault(e => e.Id == employee.Id);
                    if (skipExisting && existingEmployee != null) return ImportEntityResult.SkippedExisting;
                    employee = Merge(employee, existingEmployee);
                    employee.Name = NameOrUnknown(employee.Name);
                    data.Employees.AddOrUpdate(existingEmployee, employee);
                    return existingEmployee != null ? ImportEntityResult.Updated : ImportEntityResult.Inserted;
                }
                return ImportEntityResult.Failed;

            default:
                return ImportEntityResult.Failed;
        }
    }

    #endregion

    #region Validation

    private Dictionary<string, HashSet<string>> CollectImportedIds(XLWorkbook workbook)
    {
        var ids = new Dictionary<string, HashSet<string>>();

        foreach (var worksheet in workbook.Worksheets)
        {
            var headers = GetHeaders(worksheet);
            if (headers.Count == 0) continue;

            var rows = GetDataRows(worksheet, headers.Count);
            var sheetName = worksheet.Name;

            // The Invoices sheet exports both "ID" (INV-2026-00001) and "Invoice #"
            // (#INV-2026-00001), and the line item and payment sheets reference the ID, so that
            // is what has to be collected. But ImportInvoices falls back to the number when a
            // sheet has no ID column, and the schema documents that a sheet carrying only
            // "Invoice #" still works, so this has to mirror that fallback per row or every
            // child row of such a sheet is flagged as an orphan.
            bool invoices = sheetName == "Invoices";

            if (!headers.Contains("ID") && !(invoices && headers.Contains("Invoice #"))) continue;

            var entityType = GetEntityTypeFromSheetName(sheetName);
            if (string.IsNullOrEmpty(entityType)) continue;

            if (!ids.ContainsKey(entityType))
                ids[entityType] = [];

            foreach (var row in rows)
            {
                var id = GetString(row, headers, "ID");

                if (string.IsNullOrEmpty(id) && invoices)
                    id = GetString(row, headers, "Invoice #");

                if (!string.IsNullOrEmpty(id))
                    ids[entityType].Add(id);
            }

            // Also collect product names for name-based lookups
            if (sheetName == "Products" && headers.Contains("Name"))
            {
                if (!ids.ContainsKey("ProductNames"))
                    ids["ProductNames"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var row in rows)
                {
                    var name = GetString(row, headers, "Name");
                    if (!string.IsNullOrEmpty(name))
                        ids["ProductNames"].Add(name);
                }
            }
        }

        return ids;
    }

    private static string GetEntityTypeFromSheetName(string sheetName)
    {
        return sheetName switch
        {
            "Customers" => "Customers",
            "Suppliers" => "Suppliers",
            "Products" => "Products",
            "Categories" => "Categories",
            "Locations" => "Locations",
            "Invoices" => "Invoices",
            "Inventory" => "Inventory",
            "Rental Inventory" => "RentalInventory",
            "Purchase Orders" => "PurchaseOrders",
            "Expenses" or "Purchases" => "Expenses",
            "Revenue" or "Sales" => "Revenue",
            _ => string.Empty
        };
    }

    private void ValidateWorksheet(
        IXLWorksheet worksheet,
        CompanyData data,
        Dictionary<string, HashSet<string>> importedIds,
        ImportValidationResult result)
    {
        var headers = GetHeaders(worksheet);
        if (headers.Count == 0) return;

        var rows = GetDataRows(worksheet, headers.Count);
        if (rows.Count == 0) return;

        ValidateWorksheetData(worksheet.Name, headers, rows, data, importedIds, result);
    }

    private void ValidateWorksheetData(
        string sheetName,
        List<string> headers,
        List<List<object?>> rows,
        CompanyData data,
        Dictionary<string, HashSet<string>> importedIds,
        ImportValidationResult result)
    {
        // Count new vs updated records
        var idColumn = SpreadsheetSheetTypeExtensions.ParseSheetName(sheetName) == SpreadsheetSheetType.Invoices
            ? "Invoice #"
            : "ID";

        if (headers.Contains(idColumn))
        {
            var summary = new ImportSummary { TotalInFile = rows.Count };
            var existingIds = GetExistingIds(sheetName, data);

            foreach (var row in rows)
            {
                var id = GetString(row, headers, idColumn);
                if (existingIds.Contains(id))
                    summary.UpdatedRecords++;
                else
                    summary.NewRecords++;
            }

            result.ImportSummaries[sheetName] = summary;
        }

        // Validate references based on sheet type
        var sheetType = SpreadsheetSheetTypeExtensions.ParseSheetName(sheetName);
        switch (sheetType)
        {
            case SpreadsheetSheetType.Products:
                ValidateProductReferences(sheetName, rows, headers, data, importedIds, result);
                break;
            case SpreadsheetSheetType.Invoices:
                ValidateInvoiceReferences(sheetName, rows, headers, data, importedIds, result);
                break;
            case SpreadsheetSheetType.Expenses:
                ValidateExpenseReferences(sheetName, rows, headers, data, importedIds, result);
                break;
            case SpreadsheetSheetType.Inventory:
                ValidateInventoryReferences(sheetName, rows, headers, data, importedIds, result);
                break;
            case SpreadsheetSheetType.Payments:
                ValidatePaymentReferences(sheetName, rows, headers, data, importedIds, result);
                break;
            case SpreadsheetSheetType.Revenue:
                ValidateRevenueReferences(sheetName, rows, headers, data, importedIds, result);
                break;
            case SpreadsheetSheetType.RentalRecords:
                ValidateRentalRecordReferences(sheetName, rows, headers, data, importedIds, result);
                break;
            case SpreadsheetSheetType.Categories:
                ValidateCategoryReferences(sheetName, rows, headers, data, importedIds, result);
                break;
            case SpreadsheetSheetType.RecurringInvoices:
                ValidateRecurringInvoiceReferences(sheetName, rows, headers, data, importedIds, result);
                break;
            case SpreadsheetSheetType.StockAdjustments:
                ValidateStockAdjustmentReferences(sheetName, rows, headers, data, importedIds, result);
                break;
            case SpreadsheetSheetType.PurchaseOrders:
                ValidateExpenseOrderReferences(sheetName, rows, headers, data, importedIds, result);
                break;
            case SpreadsheetSheetType.InvoiceLineItems:
                ValidateInvoiceLineItemReferences(sheetName, rows, headers, data, importedIds, result);
                break;
            case SpreadsheetSheetType.PurchaseOrderLineItems:
                ValidatePurchaseOrderLineItemReferences(sheetName, rows, headers, data, importedIds, result);
                break;
            case SpreadsheetSheetType.Returns:
                ValidateReturnsReferences(sheetName, rows, headers, data, importedIds, result);
                break;
            case SpreadsheetSheetType.LostDamaged:
                ValidateLostDamagedReferences(sheetName, rows, headers, data, importedIds, result);
                break;
        }
    }

    private HashSet<string> GetExistingIds(string sheetName, CompanyData data)
    {
        return SpreadsheetSheetTypeExtensions.ParseSheetName(sheetName) switch
        {
            SpreadsheetSheetType.Customers => data.Customers.Select(c => c.Id).ToHashSet(),
            SpreadsheetSheetType.Suppliers => data.Suppliers.Select(s => s.Id).ToHashSet(),
            SpreadsheetSheetType.Products => data.Products.Select(p => p.Id).ToHashSet(),
            SpreadsheetSheetType.Categories => data.Categories.Select(c => c.Id).ToHashSet(),
            SpreadsheetSheetType.Locations => data.Locations.Select(l => l.Id).ToHashSet(),
            SpreadsheetSheetType.Invoices => data.Invoices.Select(i => i.Id).ToHashSet(),
            SpreadsheetSheetType.Expenses => data.Expenses.Select(p => p.Id).ToHashSet(),
            SpreadsheetSheetType.Inventory => data.Inventory.Select(i => i.Id).ToHashSet(),
            SpreadsheetSheetType.Payments => data.Payments.Select(p => p.Id).ToHashSet(),
            SpreadsheetSheetType.Revenue => data.Revenues.Select(s => s.Id).ToHashSet(),
            SpreadsheetSheetType.RentalInventory => data.RentalInventory.Select(r => r.Id).ToHashSet(),
            SpreadsheetSheetType.RentalRecords => data.Rentals.Select(r => r.Id).ToHashSet(),
            SpreadsheetSheetType.RecurringInvoices => data.RecurringInvoices.Select(r => r.Id).ToHashSet(),
            SpreadsheetSheetType.StockAdjustments => data.StockAdjustments.Select(s => s.Id).ToHashSet(),
            SpreadsheetSheetType.PurchaseOrders => data.PurchaseOrders.Select(p => p.Id).ToHashSet(),
            _ => []
        };
    }

    private void ValidateProductReferences(
        string sheetName,
        List<List<object?>> rows, List<string> headers,
        CompanyData data, Dictionary<string, HashSet<string>> importedIds,
        ImportValidationResult result)
    {
        var existingCategories = data.Categories.Select(c => c.Id).ToHashSet();
        var existingSuppliers = data.Suppliers.Select(s => s.Id).ToHashSet();
        var importedCategories = importedIds.GetValueOrDefault("Categories") ?? [];
        var importedSuppliers = importedIds.GetValueOrDefault("Suppliers") ?? [];

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 2;
            var id = GetString(row, headers, "ID");
            var categoryId = GetNullableString(row, headers, "Category ID");
            var supplierId = GetNullableString(row, headers, "Supplier ID");

            if (!string.IsNullOrEmpty(categoryId) &&
                !existingCategories.Contains(categoryId) &&
                !importedCategories.Contains(categoryId))
            {
                result.AddIssue(sheetName, rowNumber, "Category ID", categoryId, "Categories",
                    $"Category '{categoryId}' not found", isAutoFixable: true, rowId: id);
            }

            if (!string.IsNullOrEmpty(supplierId) &&
                !existingSuppliers.Contains(supplierId) &&
                !importedSuppliers.Contains(supplierId))
            {
                result.AddIssue(sheetName, rowNumber, "Supplier ID", supplierId, "Suppliers",
                    $"Supplier '{supplierId}' not found", isAutoFixable: true, rowId: id);
            }
        }
    }

    private void ValidateInvoiceReferences(
        string sheetName,
        List<List<object?>> rows, List<string> headers,
        CompanyData data, Dictionary<string, HashSet<string>> importedIds,
        ImportValidationResult result)
    {
        var existingCustomers = data.Customers.Select(c => c.Id).ToHashSet();
        var importedCustomers = importedIds.GetValueOrDefault("Customers") ?? [];

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 2;
            var id = GetString(row, headers, "Invoice #");
            var customerId = GetNullableString(row, headers, "Customer ID");

            if (!string.IsNullOrEmpty(customerId) &&
                !existingCustomers.Contains(customerId) &&
                !importedCustomers.Contains(customerId))
            {
                result.AddIssue(sheetName, rowNumber, "Customer ID", customerId, "Customers",
                    $"Customer '{customerId}' not found", isAutoFixable: true, rowId: id);
            }
        }
    }

    private void ValidateExpenseReferences(
        string sheetName,
        List<List<object?>> rows, List<string> headers,
        CompanyData data, Dictionary<string, HashSet<string>> importedIds,
        ImportValidationResult result)
    {
        var existingSuppliers = data.Suppliers.Select(s => s.Id).ToHashSet();
        var existingProducts = data.Products.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var importedSuppliers = importedIds.GetValueOrDefault("Suppliers") ?? [];
        var importedProductNames = importedIds.GetValueOrDefault("ProductNames") ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 2;
            var id = GetString(row, headers, "ID");
            var supplierId = GetNullableString(row, headers, "Supplier ID");
            var productName = GetString(row, headers, "Product");
            if (string.IsNullOrEmpty(productName))
                productName = GetString(row, headers, "Description");

            if (!string.IsNullOrEmpty(supplierId) &&
                !existingSuppliers.Contains(supplierId) &&
                !importedSuppliers.Contains(supplierId))
            {
                result.AddIssue(sheetName, rowNumber, "Supplier ID", supplierId, "Suppliers",
                    $"Supplier '{supplierId}' not found", isAutoFixable: true, rowId: id);
            }

            // Validate product exists (by name, since Sales/Purchases use product name)
            if (!string.IsNullOrEmpty(productName) &&
                !existingProducts.Contains(productName) &&
                !importedProductNames.Contains(productName))
            {
                result.AddIssue(sheetName, rowNumber, "Product", productName, "Products (by name)",
                    $"Product '{productName}' not found", isAutoFixable: true, rowId: id);
            }
        }
    }

    private void ValidateInventoryReferences(
        string sheetName,
        List<List<object?>> rows, List<string> headers,
        CompanyData data, Dictionary<string, HashSet<string>> importedIds,
        ImportValidationResult result)
    {
        var existingProducts = data.Products.Select(p => p.Id).ToHashSet();
        var existingLocations = data.Locations.Select(l => l.Id).ToHashSet();
        var importedProducts = importedIds.GetValueOrDefault("Products") ?? [];
        var importedLocations = importedIds.GetValueOrDefault("Locations") ?? [];

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 2;
            var id = GetString(row, headers, "ID");
            var productId = GetNullableString(row, headers, "Product ID");
            var locationId = GetNullableString(row, headers, "Location ID");

            if (!string.IsNullOrEmpty(productId) &&
                !existingProducts.Contains(productId) &&
                !importedProducts.Contains(productId))
            {
                result.AddIssue(sheetName, rowNumber, "Product ID", productId, "Products",
                    $"Product '{productId}' not found", isAutoFixable: false, rowId: id);
            }

            if (!string.IsNullOrEmpty(locationId) &&
                !existingLocations.Contains(locationId) &&
                !importedLocations.Contains(locationId))
            {
                result.AddIssue(sheetName, rowNumber, "Location ID", locationId, "Locations",
                    $"Location '{locationId}' not found", isAutoFixable: true, rowId: id);
            }
        }
    }

    private void ValidatePaymentReferences(
        string sheetName,
        List<List<object?>> rows, List<string> headers,
        CompanyData data, Dictionary<string, HashSet<string>> importedIds,
        ImportValidationResult result)
    {
        var existingInvoices = data.Invoices.Select(i => i.Id).ToHashSet();
        var existingCustomers = data.Customers.Select(c => c.Id).ToHashSet();
        var importedInvoices = importedIds.GetValueOrDefault("Invoices") ?? [];
        var importedCustomers = importedIds.GetValueOrDefault("Customers") ?? [];

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 2;
            var id = GetString(row, headers, "ID");
            var invoiceId = GetNullableString(row, headers, "Invoice ID");
            var customerId = GetNullableString(row, headers, "Customer ID");

            if (!string.IsNullOrEmpty(invoiceId) &&
                !existingInvoices.Contains(invoiceId) &&
                !importedInvoices.Contains(invoiceId))
            {
                result.AddIssue(sheetName, rowNumber, "Invoice ID", invoiceId, "Invoices",
                    $"Invoice '{invoiceId}' not found, reference will be cleared", isAutoFixable: true, rowId: id);
            }

            if (!string.IsNullOrEmpty(customerId) &&
                !existingCustomers.Contains(customerId) &&
                !importedCustomers.Contains(customerId))
            {
                result.AddIssue(sheetName, rowNumber, "Customer ID", customerId, "Customers",
                    $"Customer '{customerId}' not found", isAutoFixable: true, rowId: id);
            }
        }
    }

    private void ValidateRevenueReferences(
        string sheetName,
        List<List<object?>> rows, List<string> headers,
        CompanyData data, Dictionary<string, HashSet<string>> importedIds,
        ImportValidationResult result)
    {
        var existingCustomers = data.Customers.Select(c => c.Id).ToHashSet();
        var existingProducts = data.Products.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var importedCustomers = importedIds.GetValueOrDefault("Customers") ?? [];
        var importedProductNames = importedIds.GetValueOrDefault("ProductNames") ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 2;
            var id = GetString(row, headers, "ID");
            var customerId = GetNullableString(row, headers, "Customer ID");
            var productName = GetString(row, headers, "Product");
            if (string.IsNullOrEmpty(productName))
                productName = GetString(row, headers, "Description");

            if (!string.IsNullOrEmpty(customerId) &&
                !existingCustomers.Contains(customerId) &&
                !importedCustomers.Contains(customerId))
            {
                result.AddIssue(sheetName, rowNumber, "Customer ID", customerId, "Customers",
                    $"Customer '{customerId}' not found", isAutoFixable: true, rowId: id);
            }

            // Validate product exists (by name)
            if (!string.IsNullOrEmpty(productName) &&
                !existingProducts.Contains(productName) &&
                !importedProductNames.Contains(productName))
            {
                result.AddIssue(sheetName, rowNumber, "Product", productName, "Products (by name)",
                    $"Product '{productName}' not found", isAutoFixable: true, rowId: id);
            }
        }
    }

    private void ValidateRentalRecordReferences(
        string sheetName,
        List<List<object?>> rows, List<string> headers,
        CompanyData data, Dictionary<string, HashSet<string>> importedIds,
        ImportValidationResult result)
    {
        var existingCustomers = data.Customers.Select(c => c.Id).ToHashSet();
        var existingRentalItems = data.RentalInventory.Select(r => r.Id).ToHashSet();
        var importedCustomers = importedIds.GetValueOrDefault("Customers") ?? [];
        var importedRentalItems = importedIds.GetValueOrDefault("RentalInventory") ?? [];

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 2;
            var id = GetString(row, headers, "ID");
            var customerId = GetNullableString(row, headers, "Customer ID");
            var rentalItemId = GetNullableString(row, headers, "Rental Item ID");

            if (!string.IsNullOrEmpty(customerId) &&
                !existingCustomers.Contains(customerId) &&
                !importedCustomers.Contains(customerId))
            {
                result.AddIssue(sheetName, rowNumber, "Customer ID", customerId, "Customers",
                    $"Customer '{customerId}' not found", isAutoFixable: true, rowId: id);
            }

            if (!string.IsNullOrEmpty(rentalItemId) &&
                !existingRentalItems.Contains(rentalItemId) &&
                !importedRentalItems.Contains(rentalItemId))
            {
                result.AddIssue(sheetName, rowNumber, "Rental Item ID", rentalItemId, "Rental Items",
                    $"Rental item '{rentalItemId}' not found", isAutoFixable: false, rowId: id);
            }
        }
    }

    private void ValidateCategoryReferences(
        string sheetName,
        List<List<object?>> rows, List<string> headers,
        CompanyData data, Dictionary<string, HashSet<string>> importedIds,
        ImportValidationResult result)
    {
        var existingCategories = data.Categories.Select(c => c.Id).ToHashSet();
        var importedCategories = importedIds.GetValueOrDefault("Categories") ?? [];

        // Also collect IDs from this sheet for self-reference validation
        var sheetIds = new HashSet<string>();
        foreach (var row in rows)
        {
            var id = GetString(row, headers, "ID");
            if (!string.IsNullOrEmpty(id))
                sheetIds.Add(id);
        }

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 2;
            var id = GetString(row, headers, "ID");
            var parentId = GetNullableString(row, headers, "Parent ID");

            if (!string.IsNullOrEmpty(parentId) &&
                !existingCategories.Contains(parentId) &&
                !importedCategories.Contains(parentId) &&
                !sheetIds.Contains(parentId))
            {
                result.AddIssue(sheetName, rowNumber, "Parent ID", parentId, "Categories (parent)",
                    $"Parent category '{parentId}' not found", isAutoFixable: false, rowId: id);
            }
        }
    }

    private void ValidateRecurringInvoiceReferences(
        string sheetName,
        List<List<object?>> rows, List<string> headers,
        CompanyData data, Dictionary<string, HashSet<string>> importedIds,
        ImportValidationResult result)
    {
        var existingCustomers = data.Customers.Select(c => c.Id).ToHashSet();
        var importedCustomers = importedIds.GetValueOrDefault("Customers") ?? [];

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 2;
            var id = GetString(row, headers, "ID");
            var customerId = GetNullableString(row, headers, "Customer ID");

            if (!string.IsNullOrEmpty(customerId) &&
                !existingCustomers.Contains(customerId) &&
                !importedCustomers.Contains(customerId))
            {
                result.AddIssue(sheetName, rowNumber, "Customer ID", customerId, "Customers",
                    $"Customer '{customerId}' not found", isAutoFixable: true, rowId: id);
            }
        }
    }

    private void ValidateStockAdjustmentReferences(
        string sheetName,
        List<List<object?>> rows, List<string> headers,
        CompanyData data, Dictionary<string, HashSet<string>> importedIds,
        ImportValidationResult result)
    {
        var existingInventory = data.Inventory.Select(i => i.Id).ToHashSet();
        var importedInventory = importedIds.GetValueOrDefault("Inventory") ?? [];

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 2;
            var id = GetString(row, headers, "ID");
            var inventoryItemId = GetNullableString(row, headers, "Inventory Item ID");

            if (!string.IsNullOrEmpty(inventoryItemId) &&
                !existingInventory.Contains(inventoryItemId) &&
                !importedInventory.Contains(inventoryItemId))
            {
                result.AddIssue(sheetName, rowNumber, "Inventory Item ID", inventoryItemId, "Inventory Items",
                    $"Inventory item '{inventoryItemId}' not found", isAutoFixable: false, rowId: id);
            }
        }
    }

    private void ValidateExpenseOrderReferences(
        string sheetName,
        List<List<object?>> rows, List<string> headers,
        CompanyData data, Dictionary<string, HashSet<string>> importedIds,
        ImportValidationResult result)
    {
        var existingSuppliers = data.Suppliers.Select(s => s.Id).ToHashSet();
        var importedSuppliers = importedIds.GetValueOrDefault("Suppliers") ?? [];

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 2;
            var id = GetString(row, headers, "ID");
            var supplierId = GetNullableString(row, headers, "Supplier ID");

            if (!string.IsNullOrEmpty(supplierId) &&
                !existingSuppliers.Contains(supplierId) &&
                !importedSuppliers.Contains(supplierId))
            {
                result.AddIssue(sheetName, rowNumber, "Supplier ID", supplierId, "Suppliers",
                    $"Supplier '{supplierId}' not found", isAutoFixable: true, rowId: id);
            }
        }
    }

    private void ValidateInvoiceLineItemReferences(
        string sheetName,
        List<List<object?>> rows, List<string> headers,
        CompanyData data, Dictionary<string, HashSet<string>> importedIds,
        ImportValidationResult result)
    {
        var existingProducts = data.Products.Select(p => p.Id).ToHashSet();
        var importedProducts = importedIds.GetValueOrDefault("Products") ?? [];

        // Either column can identify an invoice, so both count as known. Checking only the id
        // would flag every line on a sheet that identifies its invoices by number.
        var existingInvoices = data.Invoices.Select(i => i.Id)
            .Concat(data.Invoices.Select(i => i.InvoiceNumber))
            .Where(v => !string.IsNullOrEmpty(v))
            .ToHashSet(StringComparer.Ordinal);
        var importedInvoices = importedIds.GetValueOrDefault("Invoices") ?? [];

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 2;
            var invoiceId = GetNullableString(row, headers, "Invoice ID");
            var productId = GetNullableString(row, headers, "Product ID");

            if (!string.IsNullOrEmpty(productId) &&
                !existingProducts.Contains(productId) &&
                !importedProducts.Contains(productId))
            {
                result.AddIssue(sheetName, rowNumber, "Product ID", productId, "Products",
                    $"Product '{productId}' not found", isAutoFixable: false, rowId: invoiceId);
            }

            if (!string.IsNullOrEmpty(invoiceId) &&
                !existingInvoices.Contains(invoiceId) &&
                !importedInvoices.Contains(invoiceId))
            {
                result.AddIssue(sheetName, rowNumber, "Invoice ID", invoiceId, "Invoices",
                    $"Invoice '{invoiceId}' not found", isAutoFixable: false, rowId: invoiceId);
            }
        }
    }

    private void ValidatePurchaseOrderLineItemReferences(
        string sheetName,
        List<List<object?>> rows, List<string> headers,
        CompanyData data, Dictionary<string, HashSet<string>> importedIds,
        ImportValidationResult result)
    {
        var existingProducts = data.Products.Select(p => p.Id).ToHashSet();
        var existingPurchaseOrders = data.PurchaseOrders.Select(p => p.Id).ToHashSet();
        var importedProducts = importedIds.GetValueOrDefault("Products") ?? [];
        var importedPurchaseOrders = importedIds.GetValueOrDefault("PurchaseOrders") ?? [];

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 2;
            var id = GetString(row, headers, "ID");
            var productId = GetNullableString(row, headers, "Product ID");
            var poId = GetNullableString(row, headers, "PO ID");

            if (!string.IsNullOrEmpty(productId) &&
                !existingProducts.Contains(productId) &&
                !importedProducts.Contains(productId))
            {
                result.AddIssue(sheetName, rowNumber, "Product ID", productId, "Products",
                    $"Product '{productId}' not found", isAutoFixable: false, rowId: id);
            }

            if (!string.IsNullOrEmpty(poId) &&
                !existingPurchaseOrders.Contains(poId) &&
                !importedPurchaseOrders.Contains(poId))
            {
                result.AddIssue(sheetName, rowNumber, "PO ID", poId, "Purchase Orders",
                    $"Purchase order '{poId}' not found", isAutoFixable: false, rowId: id);
            }
        }
    }

    private void ValidateReturnsReferences(
        string sheetName,
        List<List<object?>> rows, List<string> headers,
        CompanyData data, Dictionary<string, HashSet<string>> importedIds,
        ImportValidationResult result)
    {
        var existingCustomers = data.Customers.Select(c => c.Id).ToHashSet();
        var existingSuppliers = data.Suppliers.Select(s => s.Id).ToHashSet();
        var existingProducts = data.Products.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingExpenses = data.Expenses.Select(e => e.Id).ToHashSet();
        var existingRevenues = data.Revenues.Select(r => r.Id).ToHashSet();
        var importedCustomers = importedIds.GetValueOrDefault("Customers") ?? [];
        var importedSuppliers = importedIds.GetValueOrDefault("Suppliers") ?? [];
        var importedExpenses = importedIds.GetValueOrDefault("Expenses") ?? [];
        var importedRevenues = importedIds.GetValueOrDefault("Revenue") ?? [];
        var importedProductNames = importedIds.GetValueOrDefault("ProductNames") ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 2; // Excel row number (1-based, after header)
            var id = GetString(row, headers, "ID");
            var customerId = GetNullableString(row, headers, "Customer ID");
            var supplierId = GetNullableString(row, headers, "Supplier ID");
            var productName = GetNullableString(row, headers, "Product");
            var originalTransactionId = GetNullableString(row, headers, "Original Transaction ID");

            if (!string.IsNullOrEmpty(customerId) &&
                !existingCustomers.Contains(customerId) &&
                !importedCustomers.Contains(customerId))
            {
                result.AddIssue(
                    sheetName, rowNumber, "Customer ID", customerId, "Customers",
                    $"Customer '{customerId}' not found", isAutoFixable: true, rowId: id);
            }

            if (!string.IsNullOrEmpty(supplierId) &&
                !existingSuppliers.Contains(supplierId) &&
                !importedSuppliers.Contains(supplierId))
            {
                result.AddIssue(
                    sheetName, rowNumber, "Supplier ID", supplierId, "Suppliers",
                    $"Supplier '{supplierId}' not found", isAutoFixable: true, rowId: id);
            }

            if (!string.IsNullOrEmpty(productName) &&
                !existingProducts.Contains(productName) &&
                !importedProductNames.Contains(productName))
            {
                result.AddIssue(
                    sheetName, rowNumber, "Product", productName, "Products (by name)",
                    $"Product '{productName}' not found", isAutoFixable: false, rowId: id);
            }

            if (!string.IsNullOrEmpty(originalTransactionId) &&
                !existingExpenses.Contains(originalTransactionId) &&
                !existingRevenues.Contains(originalTransactionId) &&
                !importedExpenses.Contains(originalTransactionId) &&
                !importedRevenues.Contains(originalTransactionId))
            {
                result.AddIssue(
                    sheetName, rowNumber, "Original Transaction ID", originalTransactionId, "Transactions",
                    $"Transaction '{originalTransactionId}' not found in Expenses or Revenue", isAutoFixable: false, rowId: id);
            }
        }
    }

    private void ValidateLostDamagedReferences(
        string sheetName,
        List<List<object?>> rows, List<string> headers,
        CompanyData data, Dictionary<string, HashSet<string>> importedIds,
        ImportValidationResult result)
    {
        var existingProducts = data.Products.Select(p => p.Id).ToHashSet();
        var existingProductNames = data.Products.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingExpenses = data.Expenses.Select(e => e.Id).ToHashSet();
        var existingRevenues = data.Revenues.Select(r => r.Id).ToHashSet();
        var importedProducts = importedIds.GetValueOrDefault("Products") ?? [];
        var importedExpenses = importedIds.GetValueOrDefault("Expenses") ?? [];
        var importedRevenues = importedIds.GetValueOrDefault("Revenue") ?? [];
        var importedProductNames = importedIds.GetValueOrDefault("ProductNames") ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 2; // Excel row number (1-based, after header)
            var id = GetString(row, headers, "ID");
            var productId = GetNullableString(row, headers, "Product ID");
            var productName = GetNullableString(row, headers, "Product");
            var inventoryItemId = GetNullableString(row, headers, "Inventory Item ID");

            // Check product by ID first
            if (!string.IsNullOrEmpty(productId) &&
                !existingProducts.Contains(productId) &&
                !importedProducts.Contains(productId))
            {
                result.AddIssue(
                    sheetName, rowNumber, "Product ID", productId, "Products",
                    $"Product '{productId}' not found", isAutoFixable: false, rowId: id);
            }
            // If no product ID, check by name
            else if (string.IsNullOrEmpty(productId) && !string.IsNullOrEmpty(productName))
            {
                if (!existingProductNames.Contains(productName) &&
                    !importedProductNames.Contains(productName))
                {
                    result.AddIssue(
                        sheetName, rowNumber, "Product", productName, "Products (by name)",
                        $"Product '{productName}' not found", isAutoFixable: false, rowId: id);
                }
            }
            // Warn if neither product ID nor product name is provided
            else if (string.IsNullOrEmpty(productId) && string.IsNullOrEmpty(productName))
            {
                result.AddIssue(
                    sheetName, rowNumber, "Product", "", "Products",
                    "No Product ID or Product name specified", isAutoFixable: false, rowId: id);
            }

            // InventoryItemId references the original expense/revenue transaction
            if (!string.IsNullOrEmpty(inventoryItemId) &&
                !existingExpenses.Contains(inventoryItemId) &&
                !existingRevenues.Contains(inventoryItemId) &&
                !importedExpenses.Contains(inventoryItemId) &&
                !importedRevenues.Contains(inventoryItemId))
            {
                result.AddIssue(
                    sheetName, rowNumber, "Inventory Item ID", inventoryItemId, "Transactions",
                    $"Transaction '{inventoryItemId}' not found in Expenses or Revenue", isAutoFixable: false, rowId: id);
            }
        }
    }

    #endregion

    #region Auto-Create Missing References

    private void CreateMissingReferences(XLWorkbook workbook, CompanyData data, ImportOptions options)
    {
        var result = new ImportValidationResult();
        var importedIds = CollectImportedIds(workbook);

        foreach (var worksheet in workbook.Worksheets)
        {
            ValidateWorksheet(worksheet, data, importedIds, result);
        }

        foreach (var (refType, ids) in result.MissingReferences)
        {
            if (!options.AutoCreateMissingReferences && !options.AutoCreateTypes.Contains(refType))
                continue;

            foreach (var id in ids)
            {
                CreatePlaceholderEntity(refType, id, data);
            }
        }
    }

    private void CreatePlaceholderEntity(string refType, string id, CompanyData data)
    {
        // Note: refType values here are reference type labels (e.g., "Categories (parent)", "Products (by name)")
        // which don't map cleanly to SpreadsheetSheetType since they include qualifier suffixes.
        switch (refType)
        {
            case "Categories":
            case "Categories (parent)":
                if (data.Categories.All(c => c.Id != id))
                {
                    data.Categories.Add(new Category
                    {
                        Id = id,
                        Name = id,
                        Type = CategoryType.Revenue,
                        Icon = "📦"
                    });
                }
                break;

            case "Suppliers":
                if (data.Suppliers.All(s => s.Id != id))
                {
                    data.Suppliers.Add(new Supplier
                    {
                        Id = id,
                        Name = id
                    });
                }
                break;

            case "Customers":
                if (data.Customers.All(c => c.Id != id))
                {
                    data.Customers.Add(new Customer
                    {
                        Id = id,
                        Name = id,
                        Status = EntityStatus.Active
                    });
                }
                break;

            case "Products":
                if (data.Products.All(p => p.Id != id))
                {
                    data.Products.Add(new Product
                    {
                        Id = id,
                        Name = id,
                        Type = CategoryType.Revenue,
                        ItemType = "Product"
                    });
                }
                break;

            case "Products (by name)":
                if (data.Products.All(p => p.Name != id))
                {
                    var newId = new IdGenerator(data).NextPlaceholderProductId();
                    data.Products.Add(new Product
                    {
                        Id = newId,
                        Name = id,
                        Type = CategoryType.Revenue,
                        ItemType = "Product"
                    });
                }
                break;

            case "Locations":
                if (data.Locations.All(l => l.Id != id))
                {
                    data.Locations.Add(new Location
                    {
                        Id = id,
                        Name = id
                    });
                }
                break;

            case "Rental Items":
                if (data.RentalInventory.All(r => r.Id != id))
                {
                    data.RentalInventory.Add(new RentalItem
                    {
                        Id = id,
                        Status = EntityStatus.Active
                    });
                }
                break;
        }
    }

    #endregion

    #region Helper Methods

    // Row-reading and value-parsing helpers live in SpreadsheetRowReader (extracted so the
    // bank statement importer can reuse them). These thin wrappers preserve existing call sites.
    private static int FindHeaderRow(IXLWorksheet worksheet) => SpreadsheetRowReader.FindHeaderRow(worksheet);

    private static List<string> GetHeaders(IXLWorksheet worksheet) => SpreadsheetRowReader.GetHeaders(worksheet);

    private static List<string> GetHeaders(IXLWorksheet worksheet, int headerRow) => SpreadsheetRowReader.GetHeaders(worksheet, headerRow);

    private static List<List<object?>> GetDataRows(IXLWorksheet worksheet, int columnCount) => SpreadsheetRowReader.GetDataRows(worksheet, columnCount);

    private static object? GetCellValue(IXLCell cell) => SpreadsheetRowReader.GetCellValue(cell);

    private static int GetColumnIndex(List<string> headers, string columnName) => SpreadsheetRowReader.GetColumnIndex(headers, columnName);

    private static string GetString(List<object?> row, List<string> headers, string columnName) => SpreadsheetRowReader.GetString(row, headers, columnName);

    /// <summary>
    /// Tries multiple column name variants and returns the first match.
    /// Used for address fields that have country-specific labels.
    /// </summary>
    private static string GetStringMulti(List<object?> row, List<string> headers, params string[] columnNames)
    {
        foreach (var name in columnNames)
        {
            var result = GetString(row, headers, name);
            if (!string.IsNullOrEmpty(result)) return result;
        }
        return string.Empty;
    }

    private static readonly string[] PostalCodeVariants = ["Postal Code", "ZIP Code", "Postcode", "PIN Code"];
    private static readonly string[] StateVariants = ["State", "State/Province", "Province", "County", "Prefecture", "Region"];
    private static readonly string[] AddressColumns = ["Street", "City", "Country", .. StateVariants, .. PostalCodeVariants];

    /// <summary>
    /// The address with each part the sheet has a column for taken from the row, and the rest as it
    /// was, so a sheet with only a City column keeps the street and country, as the AI import does.
    /// </summary>
    private static Address ReadAddress(Address current, List<object?> row, List<string> headers) => new()
    {
        Street = headers.Contains("Street") ? GetString(row, headers, "Street") : current.Street,
        City = headers.Contains("City") ? GetString(row, headers, "City") : current.City,
        State = StateVariants.Any(headers.Contains) ? GetStringMulti(row, headers, StateVariants) : current.State,
        ZipCode = PostalCodeVariants.Any(headers.Contains) ? GetStringMulti(row, headers, PostalCodeVariants) : current.ZipCode,
        Country = headers.Contains("Country") ? GetString(row, headers, "Country") : current.Country
    };

    // The columns a revenue or expense is priced from. Those its single line is built from are in ColumnLineFields.
    private static readonly string[] TransactionPriceColumns = ["Date", "Quantity", "Unit Price", "Tax", "Total", "Shipping", "Currency"];

    private static readonly string[] InvoicePriceColumns = ["Issue Date", "Subtotal", "Tax", "Total", "Paid", "Balance", "Currency"];

    // The same, as the AI import's rows name them.
    private static readonly string[] TransactionPriceFields =
        ["date", "quantity", "unitPrice", "amount", "taxAmount", "total", "shippingCost", "discount", "fee", "originalCurrency"];
    private static readonly string[] InvoicePriceFields =
        ["issueDate", "subtotal", "taxAmount", "total", "amountPaid", "balance", "originalCurrency"];
    private static readonly string[] RentalLineColumns = ["Rental Item ID", "Quantity", "Rate Type", "Rate Amount", "Security Deposit"];

    private static string? GetNullableString(List<object?> row, List<string> headers, string columnName) => SpreadsheetRowReader.GetNullableString(row, headers, columnName);

    /// <summary>
    /// Normalizes free-form payment status strings from spreadsheet imports
    /// into the canonical <see cref="RevenuePaymentStatus"/> enum. Uses
    /// substring matching to handle typos and variations (e.g., "Piad",
    /// "Compelted"). Falls back to Paid on unrecognised input.
    /// </summary>
    internal static RevenuePaymentStatus NormalizePaymentStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return RevenuePaymentStatus.Paid;

        var s = status.Trim().ToLowerInvariant();

        // Partial must be checked before "paid" substring match
        if (s.Contains("partial"))
            return RevenuePaymentStatus.Partial;

        // Negated forms contain the Paid words below ("unpaid" contains "paid")
        if (s.Contains("unpaid") || s.Contains("not paid") ||
            s.Contains("unsettled") || s.Contains("not settled") ||
            s.Contains("uncollected") || s.Contains("not collected") ||
            s.Contains("not received") || s.Contains("uncleared") || s.Contains("not cleared") ||
            s.Contains("incomplete") || s.Contains("not complete"))
            return RevenuePaymentStatus.Unpaid;

        // Paid and common synonyms/typos
        if (s.Contains("paid") || s.Contains("piad") ||
            s.Contains("complet") || s.Contains("settle") ||
            s.Contains("receive") || s.Contains("clear") ||
            s.Contains("collect"))
            return RevenuePaymentStatus.Paid;

        // Overdue
        if (s.Contains("overdue") || s.Contains("past due") ||
            s.Contains("pastdue") || s.Contains("late"))
            return RevenuePaymentStatus.Overdue;

        // Pending
        if (s.Contains("pending") || s.Contains("pend") ||
            s.Contains("processing") || s.Contains("progress") ||
            s.Contains("awaiting") || s.Contains("waiting"))
            return RevenuePaymentStatus.Pending;

        // Unpaid and common synonyms
        if (s.Contains("unpaid") || s.Contains("not paid") ||
            s.Contains("outstanding") || s.Contains("open") ||
            s.Contains("due") || s.Contains("owe") ||
            s.Contains("unsettled"))
            return RevenuePaymentStatus.Unpaid;

        // Fallback: unrecognized → default to Paid
        return RevenuePaymentStatus.Paid;
    }

    private static decimal GetDecimal(List<object?> row, List<string> headers, string columnName) => SpreadsheetRowReader.GetDecimal(row, headers, columnName);

    private static decimal ParseDecimalString(string s) => SpreadsheetRowReader.ParseDecimalString(s);

    private static int GetInt(List<object?> row, List<string> headers, string columnName) => SpreadsheetRowReader.GetInt(row, headers, columnName);

    private DateTime GetDateTime(List<object?> row, List<string> headers, string columnName)
        => SpreadsheetRowReader.GetDateTime(row, headers, columnName, DateOrderOf(headers, columnName));

    private DateTime? GetNullableDateTime(List<object?> row, List<string> headers, string columnName)
        => SpreadsheetRowReader.GetNullableDateTime(row, headers, columnName, DateOrderOf(headers, columnName));

    /// <summary>
    /// The balance an imported invoice still owes: the total less the amount paid when the row gives
    /// one, else the row's own balance. With neither, and only when <paramref name="recompute"/>,
    /// nothing when the row says it was paid (Paid, Refunded or PartiallyRefunded, which are all paid
    /// in full before any refund), else the total less what was already paid. Every invoice import
    /// sets it this way before working out the status, so a row with a total and an amount paid but
    /// no balance isn't read as paid in full, and one marked paid with no amounts isn't read as owed.
    /// An invoice marked paid that has payments recorded against it takes its amounts from them
    /// instead, so they agree with its payments.
    /// </summary>
    private static void SetImportedBalance(
        Invoice invoice, decimal? paid, decimal? balance, bool recompute, InvoiceStatus? givenStatus,
        IReadOnlyCollection<Payment> payments)
    {
        if (paid.HasValue)
            invoice.Balance = Math.Max(0m, invoice.Total - paid.Value);
        else if (balance.HasValue)
            invoice.Balance = Math.Max(0m, balance.Value);
        else if (!recompute)
            return;
        else if (givenStatus is InvoiceStatus.Paid or InvoiceStatus.Refunded or InvoiceStatus.PartiallyRefunded)
        {
            if (payments.Any(p => p.InvoiceId == invoice.Id))
            {
                InvoiceTotalsService.RecalculateFromPayments(invoice, payments);
                return;
            }

            invoice.AmountPaid = invoice.Total;
            invoice.Balance = 0m;
        }
        else
            invoice.Balance = Math.Max(0m, invoice.Total - invoice.AmountPaid);
    }

    // Spellings other software uses for a status, read as ours. Unpaid and Open have no status of
    // their own: the amounts decide those.
    private static readonly Dictionary<string, InvoiceStatus> InvoiceStatusAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["canceled"] = InvoiceStatus.Cancelled,
        ["void"] = InvoiceStatus.Cancelled,
        ["voided"] = InvoiceStatus.Cancelled,
        ["paidinfull"] = InvoiceStatus.Paid,
        ["partiallypaid"] = InvoiceStatus.Partial
    };

    /// <summary>
    /// The invoice status a sheet's text names, or null when it names none, which leaves the status
    /// to the amounts. Only a status's own name (any case, spaces, hyphens and underscores ignored)
    /// or a known alias counts: an unrecognised word must not be taken as a Draft the sheet chose.
    /// </summary>
    internal static InvoiceStatus? ParseImportedInvoiceStatus(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var key = text.Replace(" ", "").Replace("-", "").Replace("_", "");
        if (InvoiceStatusAliases.TryGetValue(key, out var alias)) return alias;
        foreach (var status in Enum.GetValues<InvoiceStatus>())
        {
            if (string.Equals(status.ToString(), key, StringComparison.OrdinalIgnoreCase))
                return status;
        }
        return null;
    }

    /// <summary>
    /// The fields an AI-extracted row gives a value for, any case. A null, blank or empty value gives
    /// none, so an update leaves that field as it is. A nested object's fields are listed as dotted
    /// paths ("address.city") as well as the object itself, so <see cref="RecordLists.FillAbsent"/>
    /// keeps the ones it leaves out. The row is all there is to go on: a 0 or false the model writes
    /// for a cell the sheet left empty can't be told from a real one, which is why the prompt tells it
    /// to leave such fields out (docs/AISpreadsheetImport.md).
    /// </summary>
    private static HashSet<string> GivenFields(JsonElement row)
    {
        var given = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddGivenFields(row, "", given);
        return given;
    }

    private static bool AddGivenFields(JsonElement value, string prefix, HashSet<string> given)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return false;

        var any = false;
        foreach (var p in value.EnumerateObject())
        {
            var path = prefix + p.Name;
            var isGiven = p.Value.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => false,
                JsonValueKind.String => !string.IsNullOrWhiteSpace(p.Value.GetString()),
                JsonValueKind.Array => p.Value.GetArrayLength() > 0,
                JsonValueKind.Object => AddGivenFields(p.Value, path + ".", given),
                _ => true
            };
            if (isGiven)
            {
                given.Add(path);
                any = true;
            }
        }
        return any;
    }

    /// <summary>An AI-extracted row's text for <paramref name="name"/> (any case), or null.</summary>
    private static string? JsonText(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object
            ? row.EnumerateObject()
                .Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                .Select(p => p.Value.GetString())
                .FirstOrDefault()
            : null;

    /// <summary>
    /// Gives an imported invoice its status once its amounts are set (docs/Calculations.md §6).
    /// Overdue is worked out from the due date and never saved, so a sheet saying Overdue is taken to
    /// mean sent. Cancelled, Refunded and PartiallyRefunded can't be worked out from an amount paid,
    /// so one the sheet gives, or an existing invoice already has, is kept whatever the amounts. Any
    /// other status, Draft included, is one the amounts decide: it starts from the status the invoice
    /// had before any payment (Sent when it has none) and the amount paid moves it on the way a
    /// recorded payment does (<see cref="InvoiceTotalsService.RecalculateStatus"/>), so an Overdue
    /// invoice with half paid is Partial, a Draft with a payment is Partial or Paid, and one marked
    /// Paid with nothing paid is owed. An update that sets only the status, such as marking a batch
    /// paid, is taken as given, except Overdue, which is never a status of its own.
    /// </summary>
    private static void SetImportedStatus(Invoice invoice, InvoiceStatus status, bool amountsSet)
    {
        invoice.Status = status == InvoiceStatus.Overdue ? InvoiceStatus.Sent : status;
        if (status is InvoiceStatus.Cancelled or InvoiceStatus.Refunded or InvoiceStatus.PartiallyRefunded)
            return;
        if (!amountsSet && status != InvoiceStatus.Overdue)
            return;
        if (invoice.Status is InvoiceStatus.Paid or InvoiceStatus.Partial)
            invoice.Status = invoice.StatusBeforePayment ?? InvoiceStatus.Sent;
        InvoiceTotalsService.RecalculateStatus(invoice);
    }

    private static TEnum ParseEnum<TEnum>(string value, TEnum defaultValue) where TEnum : struct, Enum
    {
        if (string.IsNullOrEmpty(value)) return defaultValue;
        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var result) ? result : defaultValue;
    }

    /// <summary>
    /// Finds an existing category by name (case-insensitive) or creates a new one.
    /// </summary>
    /// <summary>
    /// Lazily-built lookup for FindOrCreateCategory to avoid O(N) scans per call.
    /// Keyed by lowercase category name. Invalidated when new categories are added.
    /// </summary>
    private Dictionary<string, Category>? _categoryByNameCache;

    private Dictionary<string, Category> GetCategoryByNameCache(CompanyData data)
    {
        if (_categoryByNameCache != null)
            return _categoryByNameCache;

        _categoryByNameCache = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
        // Last-wins: earlier entries are overwritten, so the first match per name is kept
        // by iterating in reverse
        for (int i = data.Categories.Count - 1; i >= 0; i--)
        {
            var c = data.Categories[i];
            _categoryByNameCache[c.Name] = c;
        }
        return _categoryByNameCache;
    }

    private Category FindOrCreateCategory(CompanyData data, string categoryName, CategoryType type)
    {
        var cache = GetCategoryByNameCache(data);

        // Try to find existing category by name (cache handles case-insensitivity)
        if (cache.TryGetValue(categoryName, out var existing))
        {
            // Prefer exact type match - if this one matches, return it
            if (existing.Type == type)
                return existing;

            // Check if there's a type-specific match by scanning (rare path)
            var typeMatch = data.Categories.FirstOrDefault(c =>
                string.Equals(c.Name, categoryName, StringComparison.OrdinalIgnoreCase) &&
                c.Type == type);
            if (typeMatch != null)
                return typeMatch;

            // Return the name-only match
            return existing;
        }

        // Create new category
        var idGen = new IdGenerator(data);
        var category = new Category
        {
            Id = idGen.NextCategoryId(type),
            Name = categoryName,
            Type = type
        };
        data.Categories.Add(category);
        cache[categoryName] = category;
        return category;
    }

    /// <summary>
    /// Resolves the category for an imported product: if the product has a categoryName in the JSON
    /// (or a categoryId that doesn't match any existing category), auto-creates the category.
    /// </summary>
    private void ResolveProductCategory(CompanyData data, Product product, JsonElement entityJson)
    {
        // Extract categoryName from the raw JSON (not part of the Product model)
        var categoryName = JsonText(entityJson, "categoryName");

        // If we have a valid categoryId that matches an existing category, nothing to do
        if (!string.IsNullOrEmpty(product.CategoryId))
        {
            if (data.Categories.Any(c => c.Id == product.CategoryId))
            {
                return;
            }
        }

        // If we have a category name, find or create the category
        if (!string.IsNullOrEmpty(categoryName))
        {
            var category = FindOrCreateCategory(data, categoryName, product.Type);
            product.CategoryId = category.Id;
            return;
        }

        // If categoryId was set but doesn't exist and no name provided, use the categoryId as the name
        if (!string.IsNullOrEmpty(product.CategoryId))
        {
            var category = FindOrCreateCategory(data, product.CategoryId, product.Type);
            product.CategoryId = category.Id;
        }

        // A product the row gives no category is left without one, as the column import leaves it,
        // for AiCategorizeMissingProductsAsync to categorize once the import is in.
    }

    /// <summary>
    /// Uses AI to suggest categories for products that have no category assigned.
    /// Batches all uncategorized products into a single AI call for efficiency.
    /// Falls back to using the product name as the category name if AI is unavailable.
    /// </summary>
    public async Task AiCategorizeMissingProductsAsync(CompanyData data, CancellationToken cancellationToken)
    {
        var uncategorized = data.Products
            .Where(p => string.IsNullOrEmpty(p.CategoryId))
            .ToList();

        if (uncategorized.Count == 0)
        {
            return;
        }

        // Try AI categorization if the service is available
        if (_geminiService?.IsConfigured == true)
        {
            try
            {
                var existingCategories = data.Categories
                    .Select(c => $"- {c.Name} ({c.Type})")
                    .ToList();

                var productList = uncategorized
                    .Select(p => $"- \"{p.Name}\" (Type={p.Type}, ItemType={p.ItemType}, Description=\"{p.Description}\")")
                    .ToList();

                var prompt = $@"You are categorizing products for a small business bookkeeping application.

## Existing Categories
{(existingCategories.Count > 0 ? string.Join("\n", existingCategories) : "(none)")}

## Uncategorized Products
{string.Join("\n", productList)}

For each product, suggest the best category name. Prefer matching an existing category when appropriate.
If no existing category fits, suggest a short, clear new category name (2-4 words).

Respond with ONLY a JSON array, one entry per product in the same order:
[
  {{ ""productName"": ""..."", ""categoryName"": ""..."" }}
]";

                var response = await _geminiService.SendChatAsync(
                    "You are a helpful assistant that categorizes business products. Always respond with valid JSON only, no markdown.",
                    prompt,
                    maxTokens: Math.Max(500, uncategorized.Count * 50),
                    temperature: 0.1,
                    cancellationToken);

                if (!string.IsNullOrEmpty(response))
                {
                    var suggestions = ParseAiCategorySuggestions(response);

                    foreach (var product in uncategorized)
                    {
                        var suggestion = suggestions.FirstOrDefault(s =>
                            string.Equals(s.ProductName, product.Name, StringComparison.OrdinalIgnoreCase));

                        if (!string.IsNullOrEmpty(suggestion.CategoryName))
                        {
                            var category = FindOrCreateCategory(data, suggestion.CategoryName, product.Type);
                            product.CategoryId = category.Id;
                        }
                        else
                        {
                            // AI didn't return a match for this product, use product name as fallback
                            var category = FindOrCreateCategory(data, product.Name, product.Type);
                            product.CategoryId = category.Id;
                        }
                    }
                    return;
                }

            }
            catch (Exception ex)
            {
                _errorLogger?.LogError(ex, ErrorCategory.Import, "AI categorization failed, falling back to product name as category");
            }
        }

        // Fallback: use product name as category name (same as Tier 2 last-resort logic)
        foreach (var product in uncategorized)
        {
            if (!string.IsNullOrEmpty(product.Name))
            {
                var category = FindOrCreateCategory(data, product.Name, product.Type);
                product.CategoryId = category.Id;
            }
        }
    }

    private static List<(string ProductName, string CategoryName)> ParseAiCategorySuggestions(string response)
    {
        var results = new List<(string ProductName, string CategoryName)>();

        // Strip markdown code fences if present
        var clean = response.Trim();
        if (clean.StartsWith("```"))
        {
            var firstNewline = clean.IndexOf('\n');
            if (firstNewline >= 0) clean = clean[(firstNewline + 1)..];
            if (clean.EndsWith("```")) clean = clean[..^3];
            clean = clean.Trim();
        }

        using var doc = JsonDocument.Parse(clean);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var productName = el.TryGetProperty("productName", out var pn) ? pn.GetString() ?? "" : "";
            var categoryName = el.TryGetProperty("categoryName", out var cn) ? cn.GetString() ?? "" : "";
            if (!string.IsNullOrEmpty(productName) && !string.IsNullOrEmpty(categoryName))
                results.Add((productName, categoryName));
        }

        return results;
    }

    #endregion

    #region Import Methods (Merge Logic)

    /// <summary>
    /// A blank imported name falls back to "Unknown" so a record that has other data (e.g. a customer
    /// with only an email) is never shown nameless.
    /// </summary>
    private static string NameOrUnknown(string? name) => string.IsNullOrWhiteSpace(name) ? "Unknown" : name;

    /// <summary>
    /// The ids a blank-ID row must not be given: every one the company has and every one written
    /// on the sheet, before or after that row. An id typed in by hand or given on a sheet does not
    /// move the counter, so the counter can reach one that is already taken.
    /// </summary>
    private static HashSet<string> TakenIds(IEnumerable<string> existingIds, List<string> headers, List<List<object?>> rows, params string[] idColumns)
    {
        var taken = IdGenerator.TakenSet(existingIds);
        foreach (var row in rows)
        {
            foreach (var column in idColumns)
            {
                var id = GetString(row, headers, column);
                if (!string.IsNullOrWhiteSpace(id))
                    taken.Add(id);
            }
        }
        return taken;
    }

    private void ImportCustomers(CompanyData data, List<string> headers, List<List<object?>> rows, ImportOptions? options = null)
    {
        var takenIds = TakenIds(data.Customers.Select(c => c.Id), headers, rows, "ID");

        foreach (var row in rows)
        {
            var id = GetString(row, headers, "ID");
            var name = GetString(row, headers, "Name");

            // Skip fully-empty rows so trailing/blank template rows aren't imported as junk records.
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name))
                continue;

            // Blank ID: mint a unique one so distinct rows aren't collapsed into a single record
            // (or skipped as "already exists"). Mirrors ImportPurchases/ImportPayments/ImportSales.
            if (string.IsNullOrWhiteSpace(id))
                id = new IdGenerator(data).NextCustomerId(takenIds);

            var existing = data.Customers.FirstOrDefault(c => c.Id == id);
            if (options?.SkipExistingRecords == true && existing != null) { options.SkippedCount++; continue; }

            var customer = existing ?? new Customer();

            // Updating a customer changes only what the sheet has columns for; a new one takes every field.
            bool Set(params string[] columns) => existing == null || columns.Any(headers.Contains);

            customer.Id = id;
            if (Set("Name"))
                customer.Name = NameOrUnknown(name);
            if (Set("Company"))
                customer.CompanyName = GetNullableString(row, headers, "Company");
            if (Set("Email"))
                customer.Email = GetString(row, headers, "Email");
            if (Set("Phone"))
                customer.Phone = GetString(row, headers, "Phone");
            if (Set(AddressColumns))
            {
                customer.Address = ReadAddress(customer.Address, row, headers);
            }
            if (Set("Notes"))
                customer.Notes = GetString(row, headers, "Notes");
            if (Set("Status"))
                customer.Status = ParseEnum(GetString(row, headers, "Status"), EntityStatus.Active);
            if (Set("Total Purchases"))
                customer.TotalPurchases = GetDecimal(row, headers, "Total Purchases");

            if (existing == null)
                data.Customers.Add(customer);
            else if (options != null)
                options.UpdatedCount++;
        }
    }

    private void ImportInvoices(CompanyData data, List<string> headers, List<List<object?>> rows, ImportOptions? options = null)
    {
        // Numbers too: a row with only a number is identified by it, and line items match on either.
        var takenIds = TakenIds(data.Invoices.SelectMany(i => new[] { i.Id, i.InvoiceNumber }), headers, rows, "ID", "Invoice #");

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];

            // Two columns, either of which can identify the invoice. This app's own export
            // carries both; a sheet from elsewhere usually has only the number. Whichever is
            // present fills in for the other, so payments and line items still find their
            // parent either way.
            var invoiceId = GetString(row, headers, "ID");
            var invoiceNumber = GetString(row, headers, "Invoice #");
            var customerId = GetString(row, headers, "Customer ID");
            var issueDate = GetDateTime(row, headers, "Issue Date");
            var total = GetDecimal(row, headers, "Total");

            // Skip fully-empty rows (no id, number, customer, date, or amount).
            if (string.IsNullOrWhiteSpace(invoiceId) && string.IsNullOrWhiteSpace(invoiceNumber)
                && string.IsNullOrWhiteSpace(customerId)
                && issueDate == DateTime.MinValue && total == 0)
                continue;

            if (string.IsNullOrWhiteSpace(invoiceId))
                invoiceId = invoiceNumber;
            if (string.IsNullOrWhiteSpace(invoiceNumber))
                invoiceNumber = invoiceId;

            // Blank on both: mint a unique one so distinct rows aren't collapsed into a single record.
            if (string.IsNullOrWhiteSpace(invoiceId))
            {
                var ids = new IdGenerator(data);
                invoiceId = ids.NextInvoiceId(takenIds);
                invoiceNumber = ids.NextInvoiceNumber();
            }

            var existing = data.Invoices.FirstOrDefault(i => i.Id == invoiceId);
            if (options?.SkipExistingRecords == true && existing != null) { options.SkippedCount++; continue; }

            var invoice = existing ?? new Invoice();

            // Updating an invoice changes only what the sheet has columns for; a new one takes every field.
            bool Set(params string[] columns) => existing == null || columns.Any(headers.Contains);

            invoice.Id = invoiceId;
            if (Set("Invoice #"))
                invoice.InvoiceNumber = invoiceNumber;
            if (Set("Customer ID"))
                invoice.CustomerId = customerId;
            if (Set("Issue Date"))
                invoice.IssueDate = issueDate;
            if (Set("Due Date"))
                invoice.DueDate = GetDateTime(row, headers, "Due Date");
            if (Set("Subtotal"))
                invoice.Subtotal = GetDecimal(row, headers, "Subtotal");
            if (Set("Tax"))
                invoice.TaxAmount = GetDecimal(row, headers, "Tax");
            if (Set("Total"))
                invoice.Total = total;
            // Per-row currency detected from the amount cells, else the record's own when updating,
            // else the company currency. Set before the amounts, whose payments match it by currency.
            var priced = Set(InvoicePriceColumns);
            if (priced)
                invoice.OriginalCurrency = Tier1RowCurrency(rowIndex) ?? existing?.OriginalCurrency ?? CompanyCurrency(data);
            // Detect whether a "Paid" amount was actually supplied (GetNullableDecimal returns null for
            // an absent column, vs 0 for a genuine zero). When it is, derive the balance from it;
            // otherwise trust the imported "Balance" column, and with neither, a new total keeps
            // what was already paid. Clamp so an over-payment (Paid > Total) can never persist a
            // negative balance.
            var paid = SpreadsheetRowReader.GetNullableDecimal(row, headers, "Paid");
            if (Set("Paid"))
                invoice.AmountPaid = paid ?? 0m;
            var givenStatus = Set("Status") ? ParseImportedInvoiceStatus(GetString(row, headers, "Status")) : null;
            SetImportedBalance(invoice, paid, SpreadsheetRowReader.GetNullableDecimal(row, headers, "Balance"),
                recompute: Set("Paid", "Total"), givenStatus, data.Payments);
            // A blank or unrecognised status keeps an existing invoice's own; a new one's is left to its amounts.
            if (Set("Status", "Paid", "Balance", "Total"))
                SetImportedStatus(invoice, givenStatus ?? existing?.Status ?? InvoiceStatus.Draft,
                    amountsSet: Set("Paid", "Balance", "Total"));

            // Converted once its amounts are set. Left as it is when nothing it is priced from changed.
            if (priced)
                ApplyInvoiceCurrencyCode(invoice, invoice.OriginalCurrency, data);

            if (existing == null)
                data.Invoices.Add(invoice);
            else if (options != null)
                options.UpdatedCount++;

            _invoicesAwaitingRevenue.Add(invoice);
        }
    }

    private void ImportPurchases(CompanyData data, List<string> headers, List<List<object?>> rows, ImportOptions? options = null)
    {
        var takenIds = TakenIds(data.Expenses.Select(e => e.Id), headers, rows, "ID");

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var id = GetString(row, headers, "ID");

            // Support both "Product" (new) and "Description" (legacy) column names
            var description = GetString(row, headers, "Product");
            if (string.IsNullOrEmpty(description))
                description = GetString(row, headers, "Description");

            var date = GetDateTime(row, headers, "Date");
            var supplierId = GetNullableString(row, headers, "Supplier ID");

            // Skip summary/blank rows (e.g. "Subtotal", "Grand Total") that carry an amount but no
            // real expense content, so they aren't imported as junk records.
            if (string.IsNullOrWhiteSpace(id) && date == DateTime.MinValue
                && string.IsNullOrWhiteSpace(description) && string.IsNullOrWhiteSpace(supplierId))
                continue;

            // No ID column (or a blank ID): mint a unique one so distinct rows aren't collapsed into a
            // single record (or skipped as "already exists") when the sheet has no identifier. Without
            // this, an ID-less sheet imports only its first row.
            if (string.IsNullOrWhiteSpace(id))
                id = new IdGenerator(data).NextExpenseId(date, takenIds);

            var existing = data.Expenses.FirstOrDefault(p => p.Id == id);
            if (options?.SkipExistingRecords == true && existing != null) { options.SkippedCount++; continue; }

            var purchase = existing ?? new Expense();

            // Updating an expense changes only what the sheet has columns for; a new one takes every field.
            bool Set(params string[] columns) => existing == null || columns.Any(headers.Contains);

            purchase.Id = id;
            if (Set("Date"))
                purchase.Date = date;
            if (Set("Supplier ID"))
                purchase.SupplierId = supplierId;
            if (Set("Product", "Description"))
                purchase.Description = description;

            // Quantity is optional: sheets that list a single amount per row have no quantity
            // column, so default to 1. When a quantity column IS present, the pre-tax Amount is
            // Quantity * UnitPrice so the line-item subtotal reconciles with the stored Total.
            if (Set("Quantity"))
            {
                var quantity = GetDecimal(row, headers, "Quantity");
                purchase.Quantity = quantity <= 0 ? 1 : quantity;
            }
            if (Set("Unit Price"))
                purchase.UnitPrice = GetDecimal(row, headers, "Unit Price");
            if (Set("Quantity", "Unit Price"))
                purchase.Amount = purchase.Quantity * purchase.UnitPrice;
            if (Set("Tax"))
                purchase.TaxAmount = GetDecimal(row, headers, "Tax");
            if (Set("Total"))
                purchase.Total = GetDecimal(row, headers, "Total");
            if (Set("Reference"))
                purchase.ReferenceNumber = GetString(row, headers, "Reference");
            if (Set("Payment Method"))
                purchase.PaymentMethod = ParseEnum(GetString(row, headers, "Payment Method"), PaymentMethod.Cash);
            if (Set("Shipping"))
                purchase.ShippingCost = GetDecimal(row, headers, "Shipping");

            // Per-row currency detected from the amount cells, else the record's own when updating,
            // else the company currency. Left as it is when nothing it is priced from changed.
            if (Set(TransactionPriceColumns))
                ApplyTransactionCurrency(purchase, rowIndex, data, existing?.OriginalCurrency);

            SetSingleLine(data, purchase, existing?.LineItems, ColumnLineFields(headers), isPurchase: true);

            if (existing == null)
                data.Expenses.Add(purchase);
            else if (options != null)
                options.UpdatedCount++;
        }
    }

    private void ImportProducts(CompanyData data, List<string> headers, List<List<object?>> rows, ImportOptions? options = null)
    {
        // Build lookup dictionaries to avoid O(N) scans per row
        var productsById = data.Products.ToDictionary(p => p.Id, p => p);
        var productsByName = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in data.Products)
            productsByName.TryAdd(p.Name, p);
        var suppliersByName = new Dictionary<string, Supplier>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in data.Suppliers)
            suppliersByName.TryAdd(s.Name, s);
        var categoriesById = data.Categories.ToDictionary(c => c.Id, c => c);
        var takenIds = TakenIds(data.Products.Select(p => p.Id), headers, rows, "ID");

        foreach (var row in rows)
        {
            var id = GetString(row, headers, "ID");
            var name = GetString(row, headers, "Name");

            // Skip fully-empty rows so trailing/blank template rows aren't imported as junk records.
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name))
                continue;

            // Blank ID: mint a unique one so distinct rows aren't collapsed into a single record.
            if (string.IsNullOrWhiteSpace(id))
                id = new IdGenerator(data).NextProductId(takenIds);

            // Check for existing product by ID first
            productsById.TryGetValue(id, out var existing);

            // Match by name only to adopt an auto-created placeholder product (created from a
            // foreign key reference; these always carry a "PRD-IMP-" id). Two real products that
            // share a name but have their own explicit ids must stay distinct, otherwise the second
            // row would overwrite the first one's id below and orphan anything referencing it
            // (e.g. a sellable product vs its purchase-side twin both named "ProBook 5500 Laptop").
            if (existing == null && !string.IsNullOrEmpty(name))
            {
                productsByName.TryGetValue(name, out var placeholder);
                if (placeholder != null && placeholder.Id.StartsWith("PRD-IMP-", StringComparison.OrdinalIgnoreCase))
                    existing = placeholder;
            }
            if (options?.SkipExistingRecords == true && existing != null) { options.SkippedCount++; continue; }

            var typeStr = GetString(row, headers, "Type");
            var productType = typeStr.ToLowerInvariant() switch
            {
                "revenue" or "sales" => CategoryType.Revenue,
                "expenses" or "purchase" => CategoryType.Expense,
                "rental" => CategoryType.Rental,
                _ => CategoryType.Revenue
            };

            var itemTypeRaw = GetString(row, headers, "Item Type");
            // Normalize item type to proper casing (case-insensitive match, trim whitespace)
            var itemType = itemTypeRaw.Trim().ToLowerInvariant() switch
            {
                "service" => "Service",
                _ => "Product"
            };

            var product = existing ?? new Product();

            // Updating a product changes only what the sheet has columns for; a new one takes every field.
            bool Set(params string[] columns) => existing == null || columns.Any(headers.Contains);

            product.Id = id;
            if (Set("Name"))
                product.Name = name;
            if (Set("Type"))
                product.Type = productType;
            if (Set("Item Type"))
                product.ItemType = itemType;
            if (Set("SKU"))
                product.Sku = GetString(row, headers, "SKU");
            if (Set("Description"))
                product.Description = GetString(row, headers, "Description");

            // Handle Category - prefer ID, fall back to name lookup, auto-create if needed
            var categoryId = GetNullableString(row, headers, "Category ID");
            var categoryName = GetNullableString(row, headers, "Category Name");

            if (!string.IsNullOrEmpty(categoryId))
            {
                // Validate that the categoryId references an existing category
                categoriesById.TryGetValue(categoryId, out var existingCat);
                if (existingCat == null)
                {
                    var category = FindOrCreateCategory(data, categoryId, product.Type);
                    categoryId = category.Id;
                }
                else
                {
                }
            }
            else if (!string.IsNullOrEmpty(categoryName))
            {
                var category = FindOrCreateCategory(data, categoryName, product.Type);
                categoryId = category.Id;
            }
            else
            {
            }
            if (Set("Category ID", "Category Name"))
                product.CategoryId = categoryId;

            // Handle Supplier - prefer ID, fall back to name lookup
            var supplierId = GetNullableString(row, headers, "Supplier ID");
            if (string.IsNullOrEmpty(supplierId))
            {
                var supplierName = GetNullableString(row, headers, "Supplier Name");
                if (!string.IsNullOrEmpty(supplierName) && suppliersByName.TryGetValue(supplierName, out var supplier))
                {
                    supplierId = supplier.Id;
                }
            }
            if (Set("Supplier ID", "Supplier Name"))
                product.SupplierId = supplierId;

            // Handle Reorder Point and Overstock Threshold
            if (Set("Reorder Point"))
                product.ReorderPoint = GetDecimal(row, headers, "Reorder Point");
            if (Set("Overstock Threshold"))
                product.OverstockThreshold = GetDecimal(row, headers, "Overstock Threshold");

            // Set TrackInventory based on whether reorder/overstock values are set
            if (product.ReorderPoint > 0 || product.OverstockThreshold > 0)
            {
                product.TrackInventory = true;
            }

            if (existing == null)
            {
                data.Products.Add(product);
                productsById.TryAdd(product.Id, product);
                if (!string.IsNullOrEmpty(product.Name))
                    productsByName.TryAdd(product.Name, product);
            }
            else if (options != null)
                options.UpdatedCount++;
        }
    }

    /// <summary>The Inventory sheet's columns and the stock record fields they fill.</summary>
    private static readonly (string Column, string Field)[] InventoryColumns =
    [
        ("Product ID", "productId"), ("Location ID", "locationId"), ("In Stock", "inStock"),
        ("Reserved", "reserved"), ("Reorder Point", "reorderPoint"), ("Unit Cost", "unitCost"),
        ("Last Updated", "lastUpdated")
    ];

    private void ImportInventory(CompanyData data, List<string> headers, List<List<object?>> rows, ImportOptions? options = null)
    {
        var takenIds = TakenIds(data.Inventory.Select(i => i.Id), headers, rows, "ID");

        foreach (var row in rows)
        {
            var id = GetString(row, headers, "ID");
            var productId = GetString(row, headers, "Product ID");
            var locationId = GetString(row, headers, "Location ID");

            // Skip fully-empty rows (no id and no product/location reference).
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(productId) && string.IsNullOrWhiteSpace(locationId))
                continue;

            // Blank ID: mint a unique one so distinct rows aren't collapsed into a single record.
            if (string.IsNullOrWhiteSpace(id))
                id = new IdGenerator(data).NextInventoryItemId(takenIds);

            var existing = data.Inventory.Any(i => i.Id == id);
            if (options?.SkipExistingRecords == true && existing) { options.SkippedCount++; continue; }

            // Updating a stock level changes only what the sheet has columns for.
            var fields = InventoryColumns
                .Where(c => headers.Contains(c.Column))
                .Select(c => c.Field)
                .ToHashSet();
            var record = new InventoryItem
            {
                Id = id,
                ProductId = productId,
                LocationId = locationId,
                InStock = GetDecimal(row, headers, "In Stock"),
                Reserved = GetDecimal(row, headers, "Reserved"),
                ReorderPoint = GetDecimal(row, headers, "Reorder Point"),
                UnitCost = GetDecimal(row, headers, "Unit Cost"),
                LastUpdated = GetDateTime(row, headers, "Last Updated")
            };

            if (!InventoryStockService.ImportStockRecord(data, record, fields) && options != null)
                options.UpdatedCount++;
        }
    }

    private void ImportPayments(CompanyData data, List<string> headers, List<List<object?>> rows, ImportOptions? options = null)
    {
        var takenIds = TakenIds(data.Payments.Select(p => p.Id), headers, rows, "ID");

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var id = GetString(row, headers, "ID");

            var date = GetDateTime(row, headers, "Date");
            var amount = GetDecimal(row, headers, "Amount");
            var customerId = GetString(row, headers, "Customer ID");
            var invoiceId = GetString(row, headers, "Invoice ID");

            // Skip summary/blank rows that carry no real payment content.
            if (string.IsNullOrWhiteSpace(id) && date == DateTime.MinValue && amount == 0
                && string.IsNullOrWhiteSpace(customerId) && string.IsNullOrWhiteSpace(invoiceId))
                continue;

            // No ID column (or a blank ID): mint a unique one so distinct rows aren't collapsed into a
            // single record (or skipped as "already exists") when the sheet has no identifier. Without
            // this, an ID-less sheet imports only its first row. (Mirrors ImportPurchases.)
            if (string.IsNullOrWhiteSpace(id))
                id = new IdGenerator(data).NextPaymentId(takenIds);

            var existing = data.Payments.FirstOrDefault(p => p.Id == id);
            if (options?.SkipExistingRecords == true && existing != null) { options.SkippedCount++; continue; }

            var payment = existing ?? new Payment();

            // Updating a payment changes only what the sheet has columns for; a new one takes every field.
            bool Set(params string[] columns) => existing == null || columns.Any(headers.Contains);

            payment.Id = id;
            if (Set("Invoice ID"))
                payment.InvoiceId = !string.IsNullOrEmpty(invoiceId) && data.Invoices.Any(inv => inv.Id == invoiceId)
                    ? invoiceId : "";
            if (Set("Customer ID"))
                payment.CustomerId = customerId;
            if (Set("Date"))
                payment.Date = date;
            if (Set("Amount"))
                payment.Amount = amount;
            if (Set("Payment Method"))
                payment.PaymentMethod = ParseEnum(GetString(row, headers, "Payment Method"), PaymentMethod.Cash);
            if (Set("Reference"))
                payment.ReferenceNumber = GetNullableString(row, headers, "Reference");
            if (Set("Notes"))
                payment.Notes = GetString(row, headers, "Notes");

            // Per-row currency detected from the amount cells, else the record's own when updating,
            // else the company currency. Left as it is when nothing it is priced from changed.
            if (Set("Date", "Amount", "Currency"))
                ApplyPaymentCurrency(payment, rowIndex, data, existing?.OriginalCurrency);

            if (existing == null)
                data.Payments.Add(payment);
            else if (options != null)
                options.UpdatedCount++;
        }
    }

    private void ImportSuppliers(CompanyData data, List<string> headers, List<List<object?>> rows, ImportOptions? options = null)
    {
        var takenIds = TakenIds(data.Suppliers.Select(s => s.Id), headers, rows, "ID");

        foreach (var row in rows)
        {
            var id = GetString(row, headers, "ID");
            var name = GetString(row, headers, "Name");

            // Skip fully-empty rows so trailing/blank template rows aren't imported as junk records.
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name))
                continue;

            // Blank ID: mint a unique one so distinct rows aren't collapsed into a single record.
            if (string.IsNullOrWhiteSpace(id))
                id = new IdGenerator(data).NextSupplierId(takenIds);

            var existing = data.Suppliers.FirstOrDefault(s => s.Id == id);
            if (options?.SkipExistingRecords == true && existing != null) { options.SkippedCount++; continue; }

            var supplier = existing ?? new Supplier();

            // Updating a supplier changes only what the sheet has columns for; a new one takes every field.
            bool Set(params string[] columns) => existing == null || columns.Any(headers.Contains);

            supplier.Id = id;
            if (Set("Name"))
                supplier.Name = name;
            if (Set("Email"))
                supplier.Email = GetString(row, headers, "Email");
            if (Set("Phone"))
                supplier.Phone = GetString(row, headers, "Phone");
            if (Set("Website"))
                supplier.Website = GetNullableString(row, headers, "Website") ?? "";
            if (Set(AddressColumns))
            {
                supplier.Address = ReadAddress(supplier.Address, row, headers);
            }
            if (Set("Notes"))
                supplier.Notes = GetString(row, headers, "Notes");

            if (existing == null)
                data.Suppliers.Add(supplier);
            else if (options != null)
                options.UpdatedCount++;
        }
    }

    /// <summary>
    /// The payroll list. An ordinary entity sheet, unlike the pay runs themselves, which are
    /// export only because an approved run's figures are frozen.
    /// </summary>
    private void ImportEmployees(CompanyData data, List<string> headers, List<List<object?>> rows, ImportOptions? options = null)
    {
        var takenIds = TakenIds(data.Employees.Select(e => e.Id), headers, rows, "ID");

        foreach (var row in rows)
        {
            var id = GetString(row, headers, "ID");
            var name = GetString(row, headers, "Name");

            // A single Name column is what this app exports, but almost nothing else does: payroll
            // systems and HR exports split the name in two. Resolved BEFORE the emptiness test
            // below, or a sheet with no ID column has both blank on every row and imports nobody,
            // which is exactly the shape this fallback exists for.
            if (string.IsNullOrWhiteSpace(name))
            {
                name = string.Join(' ', new[]
                {
                    GetString(row, headers, "First Name"),
                    GetString(row, headers, "Last Name"),
                }.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
            }

            // Skip fully-empty rows so trailing/blank template rows aren't imported as junk records.
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name))
                continue;

            // Blank ID: mint a unique one so distinct rows aren't collapsed into a single record.
            if (string.IsNullOrWhiteSpace(id))
                id = new IdGenerator(data).NextEmployeeId(takenIds);

            var existing = data.Employees.FirstOrDefault(e => e.Id == id);
            if (options?.SkipExistingRecords == true && existing != null) { options.SkippedCount++; continue; }

            var employee = existing ?? new Models.Payroll.Employee();

            // A column the sheet does not carry leaves the stored value alone. Writing every
            // field unconditionally meant importing an ID plus Notes sheet to annotate staff
            // turned every hourly employee into a salaried one at nil pay, blanked their social
            // insurance number and wiped their address: the next pay run would pay them nothing
            // and their T4 could not be filed. Province already guarded for exactly this.
            bool Has(params string[] columns) => columns.Any(headers.Contains);

            employee.Id = id;
            if (Has("Name", "First Name", "Last Name"))
                employee.Name = name;

            if (Has("Employee #"))
                employee.EmployeeNumber = GetString(row, headers, "Employee #");

            // Digits only, the way the employee form stores it. People write it with spaces or
            // dashes, and a T4 will not file unless it is nine digits.
            if (Has("SIN"))
                employee.Sin = new string(GetString(row, headers, "SIN").Where(char.IsAsciiDigit).ToArray());

            var province = GetString(row, headers, "Province of Employment");
            if (!string.IsNullOrWhiteSpace(province))
                employee.Province = province.Trim().ToUpperInvariant();

            // "Salary Type" and "Salary Amount" are the common names elsewhere for what this app
            // calls Pay Type and Pay Rate. Only consulted when the app's own column is absent or
            // empty, so an export from Argo Books still wins.
            if (Has("Pay Type", "Salary Type"))
            {
                var payTypeText = GetString(row, headers, "Pay Type");
                if (string.IsNullOrWhiteSpace(payTypeText))
                    payTypeText = GetString(row, headers, "Salary Type");

                // Anything not explicitly hourly is salaried, which is what "Annual" means.
                employee.PayType = payTypeText.Trim().Equals("Hourly", StringComparison.OrdinalIgnoreCase)
                    ? Models.Payroll.PayType.Hourly
                    : Models.Payroll.PayType.Salary;
            }

            if (Has("Pay Rate", "Salary Amount"))
            {
                decimal rate = GetDecimal(row, headers, "Pay Rate");
                if (rate == 0m)
                    rate = GetDecimal(row, headers, "Salary Amount");

                employee.PayRate = rate;
            }

            // Hyphens and spaces stripped, so "Bi-weekly" and "Semi Monthly" land on the enum
            // rather than silently falling back to the default.
            if (Has("Pay Frequency"))
            {
                var frequencyText = new string(GetString(row, headers, "Pay Frequency")
                    .Where(char.IsAsciiLetter).ToArray());
                employee.PayFrequency = ParseEnum(frequencyText, Models.Payroll.PayFrequency.Biweekly);
            }

            // Null rather than zero when the cell is blank. Zero reads as "worked no hours" on a
            // record of employment, which costs the employee their claim.
            if (Has("Standard Hours Per Week"))
                employee.StandardHoursPerWeek =
                    SpreadsheetRowReader.GetNullableDecimal(row, headers, "Standard Hours Per Week");

            if (Has("Federal Claim Amount"))
                employee.FederalClaimAmount = GetDecimal(row, headers, "Federal Claim Amount");

            if (Has("Provincial Claim Amount"))
                employee.ProvincialClaimAmount = GetDecimal(row, headers, "Provincial Claim Amount");

            // Only from a sheet that has the column. An older export wrote 0 for "no TD1 filed",
            // and reading that as "claims nothing" would take away the basic personal amount.
            if (Has("Federal Claims Zero"))
                employee.FederalClaimIsZero = ReadBool(row, headers, "Federal Claims Zero");

            if (Has("Provincial Claims Zero"))
                employee.ProvincialClaimIsZero = ReadBool(row, headers, "Provincial Claims Zero");

            if (Has("Ontario Dependants"))
                employee.OntarioDependants = Math.Max(0, GetInt(row, headers, "Ontario Dependants"));

            if (Has("CPP Exempt"))
                employee.IsCppExempt = ReadBool(row, headers, "CPP Exempt");

            if (Has("EI Exempt"))
                employee.IsEiExempt = ReadBool(row, headers, "EI Exempt");

            if (Has("Dental Benefit"))
                employee.DentalBenefit = ParseEnum(GetString(row, headers, "Dental Benefit"),
                    Models.Payroll.DentalBenefitCode.NotEligible);

            // "Hire Date" is the usual name for it outside this app.
            if (Has("Start Date", "Hire Date"))
                employee.StartDate = GetNullableDateTime(row, headers, "Start Date")
                                     ?? GetNullableDateTime(row, headers, "Hire Date");

            if (Has("End Date"))
                employee.EndDate = GetNullableDateTime(row, headers, "End Date");

            // Replaced wholesale rather than merged, so a sheet carrying an address is
            // authoritative for all of it, but only when it carries one at all.
            if (Has("Street", "City", "Country") || Has(StateVariants) || Has(PostalCodeVariants))
            {
                employee.Address = ReadAddress(employee.Address, row, headers);
            }

            if (Has("Status"))
                employee.IsArchived = GetString(row, headers, "Status")
                    .Trim().Equals("Archived", StringComparison.OrdinalIgnoreCase);
            if (Has("Notes"))
                employee.Notes = GetString(row, headers, "Notes");

            if (existing == null)
                data.Employees.Add(employee);
            else if (options != null)
                options.UpdatedCount++;
        }
    }

    /// <summary>
    /// Reads a yes/no cell. Excel gives a real bool, a CSV gives whatever was typed, and the
    /// app's own export writes True/False, so all three have to be understood.
    /// </summary>
    private static bool ReadBool(List<object?> row, List<string> headers, string columnName)
    {
        var text = GetString(row, headers, columnName).Trim();

        if (bool.TryParse(text, out bool parsed))
            return parsed;

        return text.Equals("yes", StringComparison.OrdinalIgnoreCase)
               || text.Equals("y", StringComparison.OrdinalIgnoreCase)
               || text == "1";
    }

    private void ImportSales(CompanyData data, List<string> headers, List<List<object?>> rows, ImportOptions? options = null)
    {
        var takenIds = TakenIds(data.Revenues.Select(r => r.Id), headers, rows, "ID");

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var id = GetString(row, headers, "ID");

            // Support both "Product" (new) and "Description" (legacy) column names
            var description = GetString(row, headers, "Product");
            if (string.IsNullOrEmpty(description))
                description = GetString(row, headers, "Description");

            var date = GetDateTime(row, headers, "Date");
            var customerId = GetNullableString(row, headers, "Customer ID");

            // Skip summary/blank rows (e.g. "Subtotal", "Grand Total") that carry an amount but no
            // real revenue content, so they aren't imported as junk records.
            if (string.IsNullOrWhiteSpace(id) && date == DateTime.MinValue
                && string.IsNullOrWhiteSpace(description) && string.IsNullOrWhiteSpace(customerId))
                continue;

            // No ID column (or a blank ID): mint a unique one so distinct rows aren't collapsed into a
            // single record (or skipped as "already exists") when the sheet has no identifier. Without
            // this, an ID-less sheet imports only its first row. (Mirrors ImportPurchases.)
            if (string.IsNullOrWhiteSpace(id))
                id = new IdGenerator(data).NextRevenueId(date, takenIds);

            var existing = data.Revenues.FirstOrDefault(s => s.Id == id);
            if (options?.SkipExistingRecords == true && existing != null) { options.SkippedCount++; continue; }

            var revenue = existing ?? new Revenue();
            var invoiceBefore = existing?.InvoiceId;

            // Updating a revenue changes only what the sheet has columns for; a new one takes every
            // field. Without this an update read a missing Payment Status as Paid.
            bool Set(params string[] columns) => existing == null || columns.Any(headers.Contains);

            revenue.Id = id;
            if (Set("Date"))
                revenue.Date = date;
            if (Set("Customer ID"))
                revenue.CustomerId = customerId;
            if (Set("Product", "Description"))
                revenue.Description = description;

            // Quantity is optional (see ImportPurchases): default 1, and the pre-tax Amount is
            // Quantity * UnitPrice so the line-item subtotal reconciles with the stored Total.
            if (Set("Quantity"))
            {
                var quantity = GetDecimal(row, headers, "Quantity");
                revenue.Quantity = quantity <= 0 ? 1 : quantity;
            }
            if (Set("Unit Price"))
                revenue.UnitPrice = GetDecimal(row, headers, "Unit Price");
            if (Set("Quantity", "Unit Price"))
                revenue.Amount = revenue.Quantity * revenue.UnitPrice;
            if (Set("Tax"))
                revenue.TaxAmount = GetDecimal(row, headers, "Tax");
            if (Set("Total"))
                revenue.Total = GetDecimal(row, headers, "Total");
            if (Set("Reference"))
                revenue.ReferenceNumber = GetString(row, headers, "Reference");
            if (Set("Payment Status"))
                revenue.PaymentStatus = NormalizePaymentStatus(GetString(row, headers, "Payment Status"));
            if (Set("Shipping"))
                revenue.ShippingCost = GetDecimal(row, headers, "Shipping");

            if (headers.Contains("Invoice ID"))
                revenue.InvoiceId = GetNullableString(row, headers, "Invoice ID");
            if (headers.Contains("Kept Deposit"))
                revenue.IsKeptDeposit = ReadBool(row, headers, "Kept Deposit");

            // Per-row currency detected from the amount cells, else the record's own when updating,
            // else the company currency. Left as it is when nothing it is priced from changed.
            if (Set(TransactionPriceColumns))
                ApplyTransactionCurrency(revenue, rowIndex, data, existing?.OriginalCurrency);

            // Revenue from an invoice takes its lines from the invoice, which may be on a later
            // sheet, so it is settled once every sheet is in.
            if (!string.IsNullOrEmpty(revenue.InvoiceId))
                NoteRevenueNamingAnInvoice(revenue, existing == null, invoiceBefore);
            else
                SetSingleLine(data, revenue, existing?.LineItems, ColumnLineFields(headers), isPurchase: false);

            if (existing == null)
                data.Revenues.Add(revenue);
            else if (options != null)
                options.UpdatedCount++;
        }
    }

    /// <summary>
    /// Resolves a customer reference, returning the id the foreign key should point at.
    ///
    /// Behavior:
    /// - Empty reference: returned unchanged.
    /// - Reference is already an existing customer id: returned unchanged (the common re-import case).
    /// - Reference is NOT an existing id: consult the name index. On a confident match the
    ///   reference is REWRITTEN to the matched id (links to the existing record, nothing created).
    ///   On no match a new record is created, named after the reference. On an ambiguous match a new
    ///   record is likewise created and a warning is recorded; an ambiguous match is NEVER
    ///   auto-linked to a guess.
    /// </summary>
    private static string? EnsureCustomerExists(CompanyData data, string? customerId, ReferenceResolutionContext? ctx)
    {
        if (string.IsNullOrEmpty(customerId)) return customerId;
        if (data.Customers.Any(c => c.Id == customerId)) return customerId;

        if (ctx != null)
        {
            var (matchedId, isAmbiguous) = ReferenceResolver.Resolve(customerId, ctx.CustomerIndex);
            if (matchedId != null)
                return matchedId; // link to the existing record; no placeholder

            if (isAmbiguous)
                ctx.Warnings.Add($"Referenced customer '{customerId}' matched more than one existing customer; created a new customer instead of guessing.");
            else
                ctx.Warnings.Add($"Referenced customer '{customerId}' was not found; created a new customer.");
        }

        data.Customers.Add(new Customer { Id = customerId, Name = customerId });
        return customerId;
    }

    /// <summary>
    /// Resolves a supplier reference. See <see cref="EnsureCustomerExists"/> for the resolution rules.
    /// </summary>
    private static string? EnsureSupplierExists(CompanyData data, string? supplierId, ReferenceResolutionContext? ctx)
    {
        if (string.IsNullOrEmpty(supplierId)) return supplierId;
        if (data.Suppliers.Any(s => s.Id == supplierId)) return supplierId;

        if (ctx != null)
        {
            var (matchedId, isAmbiguous) = ReferenceResolver.Resolve(supplierId, ctx.SupplierIndex);
            if (matchedId != null)
                return matchedId; // link to the existing record; no placeholder

            if (isAmbiguous)
                ctx.Warnings.Add($"Referenced supplier '{supplierId}' matched more than one existing supplier; created a new supplier instead of guessing.");
            else
                ctx.Warnings.Add($"Referenced supplier '{supplierId}' was not found; created a new supplier.");
        }

        data.Suppliers.Add(new Supplier { Id = supplierId, Name = supplierId });
        return supplierId;
    }

    /// <summary>
    /// Resolves a payment's invoice reference to the id of the invoice it names, matching either
    /// the id or the invoice number as the line item importer does, else creates a placeholder
    /// invoice with that id. Returns the id the payment should point at.
    /// </summary>
    private static string? EnsureInvoiceExists(CompanyData data, string? invoiceId, string? customerId)
    {
        if (string.IsNullOrEmpty(invoiceId)) return invoiceId;
        if (data.Invoices.Any(i => i.Id == invoiceId)) return invoiceId;
        if (data.Invoices.FirstOrDefault(i => i.InvoiceNumber == invoiceId) is { } byNumber)
            return byNumber.Id;

        data.Invoices.Add(new Invoice
        {
            Id = invoiceId,
            CustomerId = customerId ?? string.Empty,
            OriginalCurrency = data.Settings.Localization.Currency
        });
        return invoiceId;
    }

    /// <summary>
    /// Finds a product by name, preferring products whose category matches the given type.
    /// This handles the case where the same product name exists under both Revenue and Expense categories.
    /// </summary>
    private static Product? FindProductByName(CompanyData data, string name, CategoryType preferredCategoryType)
    {
        var categoriesById = data.Categories.ToDictionary(c => c.Id, c => c);
        Product? fallback = null;
        foreach (var p in data.Products)
        {
            if (!string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrEmpty(p.CategoryId) && categoriesById.TryGetValue(p.CategoryId, out var category) && category.Type == preferredCategoryType)
                return p;

            fallback ??= p;
        }
        return fallback;
    }

    /// <summary>Which of the fields a revenue's or expense's single line is built from an import row gives.</summary>
    private readonly record struct LineFields(bool Product, bool Quantity, bool UnitPrice, bool Tax)
    {
        public static readonly LineFields All = new(true, true, true, true);
        public bool Any => Product || Quantity || UnitPrice || Tax;
    }

    private static LineFields ColumnLineFields(List<string> headers) =>
        new(headers.Contains("Product") || headers.Contains("Description"), headers.Contains("Quantity"),
            headers.Contains("Unit Price"), headers.Contains("Tax"));

    /// <summary>
    /// The AI import's side of <see cref="SetSingleLine"/>, from the fields its row gives. A row that
    /// brings lines of its own keeps them, each one without a product linked to the one its
    /// description names.
    /// </summary>
    private void SetImportedLines(CompanyData data, Transaction txn, Transaction? existing, JsonElement row, HashSet<string> given, bool isPurchase)
    {
        // A report's category, which the mixed-report rescue gives; normal rows leave it out.
        var category = JsonText(row, "categoryName");

        if (!given.Contains("lineItems"))
        {
            var fields = existing == null
                ? LineFields.All
                : new LineFields(given.Contains("description"), given.Contains("quantity"), given.Contains("unitPrice"),
                    given.Contains("taxAmount") || given.Contains("amount"));
            SetSingleLine(data, txn, existing?.LineItems, fields, isPurchase, category);
            return;
        }

        if (!string.IsNullOrEmpty(txn.Description) && txn.LineItems.Any(li => string.IsNullOrEmpty(li.ProductId)))
        {
            var product = ProductNamed(data, txn, isPurchase, category);
            foreach (var li in txn.LineItems.Where(li => string.IsNullOrEmpty(li.ProductId)))
                li.ProductId = product.Id;
        }
        if (existing != null)
            ReplaceLines(data, txn, existing.LineItems, txn.LineItems, isPurchase);
    }

    /// <summary>
    /// Gives an imported revenue or expense its single line, the one rule both imports follow. A new
    /// record (<paramref name="currentLines"/> null) gets a line for the product its description
    /// names. An existing one with a single line, or none, has it rebuilt when the row gives any field
    /// it is made from, keeping the line's own values for those the row leaves out; one with several
    /// lines keeps them, since one row can't describe them. The line keeps the stock it took and its
    /// cost of goods sold while its product and quantity (and a purchase's price) are unchanged;
    /// otherwise the record goes through <see cref="ReplaceLines"/>.
    /// </summary>
    private void SetSingleLine(CompanyData data, Transaction txn, List<LineItem>? currentLines, LineFields fields, bool isPurchase, string? category = null)
    {
        if (currentLines != null && (!fields.Any || currentLines.Count > 1))
            return;

        var old = currentLines?.SingleOrDefault();
        if (old == null && string.IsNullOrEmpty(txn.Description))
            return;

        var all = old == null;
        var line = old?.Clone() ?? new LineItem();
        if ((all || fields.Product) && !string.IsNullOrEmpty(txn.Description))
        {
            line.ProductId = ProductNamed(data, txn, isPurchase, category).Id;
            line.Description = txn.Description;
        }
        if (all || fields.Quantity)
            line.Quantity = txn.Quantity;
        if (all || fields.UnitPrice)
            line.UnitPrice = txn.UnitPrice;
        if (all || fields.Tax || fields.Quantity || fields.UnitPrice)
            line.TaxRate = txn.Amount > 0 ? txn.TaxAmount / txn.Amount : 0;

        List<LineItem> lines = [line];
        if (currentLines != null
            && (old == null || line.ProductId != old.ProductId || line.Quantity != old.Quantity
                || (isPurchase && line.UnitPrice != old.UnitPrice)))
            ReplaceLines(data, txn, currentLines, lines, isPurchase);
        else
            txn.LineItems = lines;
    }

    /// <summary>
    /// Gives an existing revenue or expense new lines. One that moved stock, or whose lines say what
    /// stock they took, is applied to stock again the way editing it in the app is
    /// (<see cref="InventoryStockService.ApplyEdit"/>), so its cost of goods sold is worked out afresh
    /// rather than lost (docs/Calculations.md §14). The import moves no stock for any other record.
    /// </summary>
    private static void ReplaceLines(CompanyData data, Transaction txn, List<LineItem> oldLines, List<LineItem> newLines, bool isPurchase)
    {
        if (oldLines.Any(l => l.CostOfGoodsUSD != null || l.IsStockPurchase || l.OpeningUnitsUsed != 0)
            || data.StockAdjustments.Any(a => a.IsAutoGenerated && a.ReferenceNumber == txn.Id))
            InventoryStockService.ApplyEdit(data, oldLines, newLines, txn, isPurchase, isPurchase ? "Expense edited" : "Revenue edited");
        txn.LineItems = newLines;
    }

    /// <summary>The product a revenue's or expense's description names, found by name (preferring one on the same side of the books) or created.</summary>
    private Product ProductNamed(CompanyData data, Transaction txn, bool isPurchase, string? category)
    {
        var type = isPurchase ? CategoryType.Expense : CategoryType.Revenue;
        return FindProductByName(data, txn.Description, type)
               ?? AutoCreateProduct(data, txn.Description, txn.UnitPrice, type, category);
    }

    /// <summary>
    /// Auto-creates a product from revenue/expense data when no matching product exists.
    /// Uses the product name from the transaction description and sets a sensible unit price.
    /// </summary>
    private Product AutoCreateProduct(CompanyData data, string name, decimal unitPrice, CategoryType type, string? categoryName = null)
    {
        var newId = new IdGenerator(data).NextPlaceholderProductId();
        var product = new Product
        {
            Id = newId,
            Name = name,
            UnitPrice = unitPrice,
            Type = type,
            ItemType = "Product"
        };

        // Prefer an explicit category (e.g. from a report's grouping); otherwise fall back to the
        // product name so no product is left uncategorized.
        var categoryLabel = !string.IsNullOrWhiteSpace(categoryName) ? categoryName : name;
        var category = FindOrCreateCategory(data, categoryLabel, type);
        product.CategoryId = category.Id;

        data.Products.Add(product);
        return product;
    }

    private void ImportRentalInventory(CompanyData data, List<string> headers, List<List<object?>> rows, ImportOptions? options = null)
    {
        var takenIds = TakenIds(data.RentalInventory.Select(r => r.Id), headers, rows, "ID");
        var takenInventoryIds = TakenIds(data.Inventory.Select(i => i.Id), [], []);

        foreach (var row in rows)
        {
            var id = GetString(row, headers, "ID");

            // Skip fully-empty rows (no id and no inventory-item/product reference).
            if (string.IsNullOrWhiteSpace(id)
                && string.IsNullOrWhiteSpace(GetString(row, headers, "Inventory Item ID"))
                && string.IsNullOrWhiteSpace(GetString(row, headers, "Product ID")))
                continue;

            // Blank ID: mint a unique one so distinct rows aren't collapsed into a single record.
            if (string.IsNullOrWhiteSpace(id))
                id = new IdGenerator(data).NextRentalItemId(takenIds);

            var existing = data.RentalInventory.FirstOrDefault(r => r.Id == id);
            if (options?.SkipExistingRecords == true && existing != null) { options.SkippedCount++; continue; }

            var item = existing ?? new RentalItem();

            // Updating a rental item changes only what the sheet has columns for; a new one takes every field.
            bool Set(params string[] columns) => existing == null || columns.Any(headers.Contains);

            item.Id = id;

            // Prefer explicit "Inventory Item ID"; otherwise resolve from "Product ID" so
            // sheets that link rental items to products directly still chain through to a name.
            if (Set("Inventory Item ID", "Product ID"))
            {
                var inventoryItemId = GetString(row, headers, "Inventory Item ID");
                if (string.IsNullOrEmpty(inventoryItemId))
                {
                    var productId = GetString(row, headers, "Product ID");
                    if (!string.IsNullOrEmpty(productId))
                    {
                        var existingInv = data.Inventory.FirstOrDefault(inv => inv.ProductId == productId);
                        if (existingInv != null)
                        {
                            inventoryItemId = existingInv.Id;
                        }
                        else if (options?.AutoCreateMissingReferences == true)
                        {
                            var newInv = new InventoryItem
                            {
                                Id = new IdGenerator(data).NextInventoryItemId(takenInventoryIds),
                                ProductId = productId,
                                InStock = GetDecimal(row, headers, "Total Qty")
                            };
                            data.Inventory.Add(newInv);
                            inventoryItemId = newInv.Id;
                        }
                    }
                }
                item.InventoryItemId = inventoryItemId;
            }

            if (Set("Daily Rate"))
                item.DailyRate = GetDecimal(row, headers, "Daily Rate");
            if (Set("Weekly Rate"))
                item.WeeklyRate = GetDecimal(row, headers, "Weekly Rate");
            if (Set("Monthly Rate"))
                item.MonthlyRate = GetDecimal(row, headers, "Monthly Rate");
            if (Set("Deposit"))
                item.SecurityDeposit = GetDecimal(row, headers, "Deposit");
            if (Set("Status"))
                item.Status = ParseEnum(GetString(row, headers, "Status"), EntityStatus.Active);

            if (existing == null)
                data.RentalInventory.Add(item);
            else if (options != null)
                options.UpdatedCount++;
        }
    }

    private void ImportRentalRecords(CompanyData data, List<string> headers, List<List<object?>> rows, ImportOptions? options = null)
    {
        // Group rows by ID to support multi-line-item rentals (same ID = multiple line items)
        var groupedRows = new Dictionary<string, List<List<object?>>>();
        foreach (var row in rows)
        {
            var id = GetString(row, headers, "ID");
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (!groupedRows.ContainsKey(id))
                groupedRows[id] = [];
            groupedRows[id].Add(row);
        }

        foreach (var (id, idRows) in groupedRows)
        {
            var existing = data.Rentals.FirstOrDefault(r => r.Id == id);
            if (options?.SkipExistingRecords == true && existing != null) { options.SkippedCount++; continue; }
            var record = existing ?? new RentalRecord();

            // Updating a rental changes only what the sheet has columns for; a new one takes every field.
            bool Set(params string[] columns) => existing == null || columns.Any(headers.Contains);

            record.Id = id;

            // Use first row for shared record fields
            var firstRow = idRows[0];
            if (Set("Customer ID"))
                record.CustomerId = GetString(firstRow, headers, "Customer ID");
            if (Set("Start Date"))
                record.StartDate = GetDateTime(firstRow, headers, "Start Date");
            if (Set("Due Date"))
                record.DueDate = GetDateTime(firstRow, headers, "Due Date");
            if (Set("Return Date"))
                record.ReturnDate = GetNullableDateTime(firstRow, headers, "Return Date");
            if (Set("Total Cost"))
            {
                var totalCost = GetDecimal(firstRow, headers, "Total Cost");
                record.TotalCost = totalCost == 0 ? null : totalCost;
            }
            if (Set("Status"))
                record.Status = ParseEnum(GetString(firstRow, headers, "Status"), RentalStatus.Active);
            if (Set("Paid"))
            {
                var paidStr = GetString(firstRow, headers, "Paid");
                record.Paid = paidStr.Equals("Yes", StringComparison.OrdinalIgnoreCase) || paidStr.Equals("True", StringComparison.OrdinalIgnoreCase);
            }

            // Build line items from all rows with this ID. Each row updates the stored line in the
            // same position, so a line keeps whatever the sheet has no column for.
            if (Set(RentalLineColumns))
            {
                var lines = new List<RentalLineItem>();
                for (int i = 0; i < idRows.Count; i++)
                {
                    var row = idRows[i];
                    var stored = i < record.LineItems.Count ? record.LineItems[i] : null;
                    bool SetLine(string column) => stored == null || headers.Contains(column);

                    var lineItem = stored ?? new RentalLineItem();
                    if (SetLine("Rental Item ID"))
                        lineItem.RentalItemId = GetString(row, headers, "Rental Item ID");
                    if (SetLine("Quantity"))
                        lineItem.Quantity = GetInt(row, headers, "Quantity");
                    if (SetLine("Rate Type"))
                        lineItem.RateType = ParseEnum(GetString(row, headers, "Rate Type"), RateType.Daily);
                    if (SetLine("Rate Amount"))
                        lineItem.RateAmount = GetDecimal(row, headers, "Rate Amount");
                    if (SetLine("Security Deposit"))
                        lineItem.SecurityDeposit = GetDecimal(row, headers, "Security Deposit");
                    lines.Add(lineItem);
                }
                record.LineItems = lines;

                // Set top-level backward-compat fields from first line item
                var firstLi = record.LineItems[0];
                record.RentalItemId = firstLi.RentalItemId;
                record.Quantity = record.LineItems.Sum(li => li.Quantity);
                record.RateType = firstLi.RateType;
                record.RateAmount = firstLi.RateAmount;
                record.SecurityDeposit = RentalBookings.TotalDeposit(record.LineItems);
            }

            if (existing == null)
                data.Rentals.Add(record);
            else if (options != null)
                options.UpdatedCount++;
        }
    }

    private void ImportCategories(CompanyData data, List<string> headers, List<List<object?>> rows, ImportOptions? options = null)
    {
        var takenIds = TakenIds(data.Categories.Select(c => c.Id), headers, rows, "ID");

        foreach (var row in rows)
        {
            var id = GetString(row, headers, "ID");
            var name = GetString(row, headers, "Name");

            // Skip fully-empty rows so trailing/blank template rows aren't imported as junk records.
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name))
                continue;

            var typeStr = GetString(row, headers, "Type");
            var categoryType = typeStr.ToLowerInvariant() switch
            {
                "revenue" or "sales" => CategoryType.Revenue,
                "expenses" or "purchase" => CategoryType.Expense,
                "rental" => CategoryType.Rental,
                _ => CategoryType.Revenue
            };

            // Blank ID: mint a unique one so distinct rows aren't collapsed into a single record.
            if (string.IsNullOrWhiteSpace(id))
                id = new IdGenerator(data).NextCategoryId(categoryType, takenIds);

            var existing = data.Categories.FirstOrDefault(c => c.Id == id);
            if (options?.SkipExistingRecords == true && existing != null) { options.SkippedCount++; continue; }

            var category = existing ?? new Category();

            // Updating a category changes only what the sheet has columns for; a new one takes every field.
            bool Set(params string[] columns) => existing == null || columns.Any(headers.Contains);

            category.Id = id;
            if (Set("Name"))
                category.Name = name;
            if (Set("Type"))
                category.Type = categoryType;
            if (Set("Parent ID"))
                category.ParentId = GetNullableString(row, headers, "Parent ID");
            if (Set("Description"))
                category.Description = GetNullableString(row, headers, "Description");
            if (Set("Icon"))
            {
                category.Icon = GetString(row, headers, "Icon");
                if (string.IsNullOrEmpty(category.Icon))
                    category.Icon = "📦";
            }

            if (existing == null)
                data.Categories.Add(category);
            else if (options != null)
                options.UpdatedCount++;
        }
    }

    private void ImportLocations(CompanyData data, List<string> headers, List<List<object?>> rows, ImportOptions? options = null)
    {
        var takenIds = TakenIds(data.Locations.Select(l => l.Id), headers, rows, "ID");

        foreach (var row in rows)
        {
            var id = GetString(row, headers, "ID");
            var name = GetString(row, headers, "Name");

            // Skip fully-empty rows so trailing/blank template rows aren't imported as junk records.
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name))
                continue;

            // Blank ID: mint a unique one so distinct rows aren't collapsed into a single record.
            if (string.IsNullOrWhiteSpace(id))
                id = new IdGenerator(data).NextLocationId(takenIds);

            var existing = data.Locations.FirstOrDefault(l => l.Id == id);
            if (options?.SkipExistingRecords == true && existing != null) { options.SkippedCount++; continue; }

            var location = existing ?? new Location();

            // Updating a location changes only what the sheet has columns for; a new one takes every field.
            bool Set(params string[] columns) => existing == null || columns.Any(headers.Contains);

            location.Id = id;
            if (Set("Name"))
                location.Name = name;
            if (Set("Contact Person"))
                location.ContactPerson = GetString(row, headers, "Contact Person");
            if (Set("Phone"))
                location.Phone = GetString(row, headers, "Phone");
            if (Set(AddressColumns))
            {
                location.Address = ReadAddress(location.Address, row, headers);
            }
            if (Set("Capacity"))
                location.Capacity = GetInt(row, headers, "Capacity");
            if (Set("Utilization"))
                location.CurrentUtilization = GetInt(row, headers, "Utilization");

            if (existing == null)
                data.Locations.Add(location);
            else if (options != null)
                options.UpdatedCount++;
        }
    }

    private void ImportRecurringInvoices(CompanyData data, List<string> headers, List<List<object?>> rows, ImportOptions? options = null)
    {
        var takenIds = TakenIds(data.RecurringInvoices.Select(r => r.Id), headers, rows, "ID");

        foreach (var row in rows)
        {
            var id = GetString(row, headers, "ID");
            var customerId = GetString(row, headers, "Customer ID");
            var amount = GetDecimal(row, headers, "Amount");
            var description = GetString(row, headers, "Description");

            // Skip fully-empty rows (no id, customer, amount, or description).
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(customerId)
                && amount == 0 && string.IsNullOrWhiteSpace(description))
                continue;

            // Blank ID: mint a unique one so distinct rows aren't collapsed into a single record.
            if (string.IsNullOrWhiteSpace(id))
                id = new IdGenerator(data).NextRecurringInvoiceId(takenIds);

            var existing = data.RecurringInvoices.FirstOrDefault(r => r.Id == id);
            if (options?.SkipExistingRecords == true && existing != null) { options.SkippedCount++; continue; }

            var recurring = existing ?? new RecurringInvoice();

            // Updating a recurring invoice changes only what the sheet has columns for; a new one takes every field.
            bool Set(params string[] columns) => existing == null || columns.Any(headers.Contains);

            recurring.Id = id;
            if (Set("Customer ID"))
                recurring.CustomerId = customerId;
            if (Set("Amount"))
                recurring.Amount = amount;
            if (Set("Description"))
                recurring.Description = description;
            if (Set("Frequency"))
                recurring.Frequency = ParseEnum(GetString(row, headers, "Frequency"), Frequency.Monthly);
            if (Set("Next Date"))
                recurring.NextInvoiceDate = GetDateTime(row, headers, "Next Date");
            if (Set("Status"))
                recurring.Status = ParseEnum(GetString(row, headers, "Status"), RecurringInvoiceStatus.Active);

            if (recurring.Status == default)
                recurring.Status = RecurringInvoiceStatus.Active;

            if (existing == null)
                data.RecurringInvoices.Add(recurring);
            else if (options != null)
                options.UpdatedCount++;
        }
    }

    private void ImportStockAdjustments(CompanyData data, List<string> headers, List<List<object?>> rows, ImportOptions? options = null)
    {
        var takenIds = TakenIds(data.StockAdjustments.Select(s => s.Id), headers, rows, "ID");

        foreach (var row in rows)
        {
            var id = GetString(row, headers, "ID");
            var inventoryItemId = GetString(row, headers, "Inventory Item ID");

            // Skip fully-empty rows (no id and no inventory item reference).
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(inventoryItemId))
                continue;

            // Blank ID: mint a unique one so distinct rows aren't collapsed into a single record.
            if (string.IsNullOrWhiteSpace(id))
                id = new IdGenerator(data).NextStockAdjustmentId(takenIds);

            var existing = data.StockAdjustments.FirstOrDefault(s => s.Id == id);
            if (options?.SkipExistingRecords == true && existing != null) { options.SkippedCount++; continue; }

            var adjustment = existing ?? new StockAdjustment();

            // Updating an adjustment changes only what the sheet has columns for; a new one takes every field.
            bool Set(params string[] columns) => existing == null || columns.Any(headers.Contains);

            adjustment.Id = id;
            if (Set("Inventory Item ID"))
                adjustment.InventoryItemId = inventoryItemId;
            if (Set("Type"))
                adjustment.AdjustmentType = ParseEnum(GetString(row, headers, "Type"), AdjustmentType.Set);
            if (Set("Quantity"))
                adjustment.Quantity = GetDecimal(row, headers, "Quantity");
            if (Set("Previous Stock"))
                adjustment.PreviousStock = GetDecimal(row, headers, "Previous Stock");
            if (Set("New Stock"))
                adjustment.NewStock = GetDecimal(row, headers, "New Stock");
            if (Set("Reason"))
                adjustment.Reason = GetString(row, headers, "Reason");
            if (Set("Reference Number"))
            {
                var refNum = GetString(row, headers, "Reference Number");
                adjustment.ReferenceNumber = string.IsNullOrEmpty(refNum) ? null : refNum;
            }
            if (Set("User ID"))
            {
                var userId = GetString(row, headers, "User ID");
                adjustment.UserId = string.IsNullOrEmpty(userId) ? null : userId;
            }
            if (Set("Timestamp"))
            {
                adjustment.Timestamp = GetDateTime(row, headers, "Timestamp");
                if (adjustment.Timestamp == DateTime.MinValue)
                    adjustment.Timestamp = DateTime.UtcNow;
            }
            var autoGenStr = GetString(row, headers, "Auto Generated");
            if (!string.IsNullOrEmpty(autoGenStr))
                adjustment.IsAutoGenerated = bool.TryParse(autoGenStr, out var ag) && ag;

            if (existing == null)
                data.StockAdjustments.Add(adjustment);
            else if (options != null)
                options.UpdatedCount++;
        }
    }

    private void ImportPurchaseOrders(CompanyData data, List<string> headers, List<List<object?>> rows, ImportOptions? options = null)
    {
        var takenIds = TakenIds(data.PurchaseOrders.SelectMany(p => new[] { p.Id, p.PoNumber }), headers, rows, "ID");

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var id = GetString(row, headers, "ID");
            var supplierId = GetString(row, headers, "Supplier ID");
            var orderDate = GetDateTime(row, headers, "Order Date");
            var total = GetDecimal(row, headers, "Total");

            // Skip fully-empty rows (no id, supplier, date, or amount).
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(supplierId)
                && orderDate == DateTime.MinValue && total == 0)
                continue;

            // Blank ID: mint a unique one so distinct rows aren't collapsed into a single record.
            if (string.IsNullOrWhiteSpace(id))
                id = new IdGenerator(data).NextPurchaseOrderId(takenIds);

            var existing = data.PurchaseOrders.FirstOrDefault(p => p.Id == id);
            if (options?.SkipExistingRecords == true && existing != null) { options.SkippedCount++; continue; }

            var po = existing ?? new PurchaseOrder();

            // Updating an order changes only what the sheet has columns for; a new one takes every field.
            bool Set(params string[] columns) => existing == null || columns.Any(headers.Contains);

            po.Id = id;
            if (Set("Supplier ID"))
                po.SupplierId = supplierId;
            if (Set("Order Date"))
                po.OrderDate = orderDate;
            if (Set("Expected Date"))
                po.ExpectedDeliveryDate = GetDateTime(row, headers, "Expected Date");
            if (Set("Total"))
                po.Total = total;
            if (Set("Status"))
                po.Status = ParseEnum(GetString(row, headers, "Status"), PurchaseOrderStatus.Draft);

            // Per-row currency detected from the amount cells, else the record's own when updating,
            // else the company currency. Left as it is when nothing it is priced from changed.
            if (Set("Order Date", "Total", "Currency"))
                ApplyPurchaseOrderCurrency(po, rowIndex, data, existing?.OriginalCurrency);

            if (existing == null)
                data.PurchaseOrders.Add(po);
            else if (options != null)
                options.UpdatedCount++;
        }
    }

    /// <summary>
    /// Puts the lines back on their invoices.
    ///
    /// Mirrors <see cref="ImportPurchaseOrderLineItems"/>, including the part that reads oddly:
    /// the whole sheet is grouped first and each invoice's lines are then REPLACED in one go,
    /// rather than appended row by row. Appending would double every line on a second import of
    /// the same file, which is the normal way people re-run an import after fixing something.
    ///
    /// The line's Amount column is deliberately not read. It is quantity times price less
    /// discount plus tax, all four of which are in the sheet, so recomputing it means a hand
    /// edit to one of the parts cannot leave a total that contradicts them.
    /// </summary>
    private void ImportInvoiceLineItems(CompanyData data, List<string> headers, List<List<object?>> rows, ImportOptions? options = null)
    {
        var lineItemsByInvoice = new Dictionary<string, List<LineItem>>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var invoiceId = GetString(row, headers, "Invoice ID");
            if (string.IsNullOrEmpty(invoiceId)) continue;

            var lineItem = new LineItem
            {
                ProductId = GetNullableString(row, headers, "Product ID"),
                Description = GetString(row, headers, "Description"),
                Quantity = GetDecimal(row, headers, "Quantity"),
                UnitPrice = GetDecimal(row, headers, "Unit Price"),
                TaxRate = GetDecimal(row, headers, "Tax Rate"),
                Discount = GetDecimal(row, headers, "Discount")
            };

            if (!lineItemsByInvoice.ContainsKey(invoiceId))
                lineItemsByInvoice[invoiceId] = [];

            lineItemsByInvoice[invoiceId].Add(lineItem);
        }

        foreach (var (invoiceId, lineItems) in lineItemsByInvoice)
        {
            // Match on either column, because either can identify an invoice on the sheet it
            // came from. See the fallback in ImportInvoices.
            var invoice = data.Invoices.FirstOrDefault(i => i.Id == invoiceId)
                          ?? data.Invoices.FirstOrDefault(i => i.InvoiceNumber == invoiceId);

            // No matching invoice: leave these rows unassigned (counted as unimported by the caller).
            if (invoice == null) continue;

            if (options?.SkipExistingRecords == true && invoice.LineItems.Count > 0)
            {
                options.SkippedCount += lineItems.Count;
                continue;
            }

            bool hadLineItems = invoice.LineItems.Count > 0;
            invoice.LineItems = lineItems;

            // The invoice's own totals are NOT recalculated from these lines. Tax, discounts,
            // shipping, deposits and a custom fee all sit on the invoice rather than on its
            // lines, and the Invoices sheet already carries the figures the customer was billed.
            // Deriving them here from lines alone would quietly restate what was sent out.

            if (options != null)
            {
                if (hadLineItems) options.UpdatedCount += lineItems.Count;
                else options.InsertedCount += lineItems.Count;
            }
        }
    }

    private void ImportPurchaseOrderLineItems(CompanyData data, List<string> headers, List<List<object?>> rows, ImportOptions? options = null)
    {
        // Group line items by purchase order ID
        var lineItemsByPo = new Dictionary<string, List<PurchaseOrderLineItem>>();

        foreach (var row in rows)
        {
            var poId = GetString(row, headers, "PO ID");
            if (string.IsNullOrEmpty(poId)) continue;

            var lineItem = new PurchaseOrderLineItem
            {
                ProductId = GetString(row, headers, "Product ID"),
                Quantity = GetDecimal(row, headers, "Quantity"),
                UnitCost = GetDecimal(row, headers, "Unit Cost"),
                QuantityReceived = GetDecimal(row, headers, "Quantity Received")
            };

            if (!lineItemsByPo.ContainsKey(poId))
                lineItemsByPo[poId] = [];

            lineItemsByPo[poId].Add(lineItem);
        }

        // Assign line items to purchase orders
        foreach (var (poId, lineItems) in lineItemsByPo)
        {
            var po = data.PurchaseOrders.FirstOrDefault(p => p.Id == poId);
            // No matching order: leave these rows unassigned (counted as unimported by the caller).
            if (po == null) continue;

            if (options?.SkipExistingRecords == true && po.LineItems.Count > 0)
            {
                options.SkippedCount += lineItems.Count;
                continue;
            }

            // An order that already had line items is being replaced (an update); one that had
            // none is a fresh insert. Count per line-item row so the per-sheet result is accurate,
            // because line items don't grow a top-level collection the way other entities do.
            bool hadLineItems = po.LineItems.Count > 0;
            po.LineItems = lineItems;
            // Calculate subtotal from line items
            po.Subtotal = lineItems.Sum(li => li.Total);

            if (options != null)
            {
                if (hadLineItems) options.UpdatedCount += lineItems.Count;
                else options.InsertedCount += lineItems.Count;
            }
        }
    }

    private void ImportReturns(CompanyData data, List<string> headers, List<List<object?>> rows, ImportOptions? options = null)
    {
        var takenIds = TakenIds(data.Returns.Select(r => r.Id), headers, rows, "ID");

        foreach (var row in rows)
        {
            var id = GetString(row, headers, "ID");
            var originalTransactionId = GetString(row, headers, "Original Transaction ID");
            var refundAmount = GetDecimal(row, headers, "Refund Amount");

            // Skip fully-empty rows (no id, original transaction, or refund).
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(originalTransactionId) && refundAmount == 0)
                continue;

            // Blank ID: mint a unique one so distinct rows aren't collapsed into a single record.
            if (string.IsNullOrWhiteSpace(id))
                id = new IdGenerator(data).NextReturnId(takenIds);

            var existing = data.Returns.FirstOrDefault(r => r.Id == id);
            if (options?.SkipExistingRecords == true && existing != null) { options.SkippedCount++; continue; }

            var returnRecord = existing ?? new Return();

            // Updating a return changes only what the sheet has columns for; a new one takes every field.
            bool Set(params string[] columns) => existing == null || columns.Any(headers.Contains);

            returnRecord.Id = id;
            if (Set("Original Transaction ID"))
                returnRecord.OriginalTransactionId = originalTransactionId;
            if (Set("Return Type"))
            {
                returnRecord.ReturnType = GetString(row, headers, "Return Type");
                if (string.IsNullOrEmpty(returnRecord.ReturnType))
                    returnRecord.ReturnType = "Customer";
            }
            if (Set("Customer ID"))
                returnRecord.CustomerId = GetString(row, headers, "Customer ID");
            if (Set("Supplier ID"))
                returnRecord.SupplierId = GetString(row, headers, "Supplier ID");
            if (Set("Return Date"))
                returnRecord.ReturnDate = GetDateTime(row, headers, "Return Date");
            if (Set("Refund Amount"))
                returnRecord.RefundAmount = refundAmount;
            if (Set("Restocking Fee"))
                returnRecord.RestockingFee = GetDecimal(row, headers, "Restocking Fee");
            if (Set("Status"))
                returnRecord.Status = ParseEnum(GetString(row, headers, "Status"), ReturnStatus.Pending);
            if (Set("Notes"))
                returnRecord.Notes = GetString(row, headers, "Notes");
            if (Set("Processed By"))
                returnRecord.ProcessedBy = GetNullableString(row, headers, "Processed By");

            // Handle items - simple single product per return row
            var productId = GetNullableString(row, headers, "Product ID");
            var productName = GetNullableString(row, headers, "Product");
            var quantity = GetInt(row, headers, "Quantity");
            var reason = GetString(row, headers, "Reason");

            if (!string.IsNullOrEmpty(productId) || !string.IsNullOrEmpty(productName))
            {
                // Look up product by name if ID not provided
                if (string.IsNullOrEmpty(productId) && !string.IsNullOrEmpty(productName))
                {
                    var product = data.Products.FirstOrDefault(p =>
                        string.Equals(p.Name, productName, StringComparison.OrdinalIgnoreCase));
                    productId = product?.Id ?? "";
                }

                returnRecord.Items =
                [
                    new ReturnItem
                    {
                        ProductId = productId ?? "",
                        Quantity = quantity > 0 ? quantity : 1,
                        Reason = reason
                    }
                ];
            }

            if (existing == null)
                data.Returns.Add(returnRecord);
            else if (options != null)
                options.UpdatedCount++;
        }
    }

    private void ImportLostDamaged(CompanyData data, List<string> headers, List<List<object?>> rows, ImportOptions? options = null)
    {
        var takenIds = TakenIds(data.LostDamaged.Select(ld => ld.Id), headers, rows, "ID");

        foreach (var row in rows)
        {
            var id = GetString(row, headers, "ID");

            // Skip fully-empty rows (no id and no product/inventory reference).
            if (string.IsNullOrWhiteSpace(id)
                && string.IsNullOrEmpty(GetNullableString(row, headers, "Product ID"))
                && string.IsNullOrEmpty(GetNullableString(row, headers, "Product"))
                && string.IsNullOrEmpty(GetNullableString(row, headers, "Inventory Item ID")))
                continue;

            // Blank ID: mint a unique one so distinct rows aren't collapsed into a single record.
            if (string.IsNullOrWhiteSpace(id))
                id = new IdGenerator(data).NextLostDamagedId(takenIds);

            var existing = data.LostDamaged.FirstOrDefault(ld => ld.Id == id);
            if (options?.SkipExistingRecords == true && existing != null) { options.SkippedCount++; continue; }

            var lostDamaged = existing ?? new LostDamaged();

            // Updating a loss changes only what the sheet has columns for; a new one takes every field.
            bool Set(params string[] columns) => existing == null || columns.Any(headers.Contains);

            lostDamaged.Id = id;

            // Handle product - prefer ID, fall back to name lookup
            if (Set("Product ID", "Product"))
            {
                var productId = GetNullableString(row, headers, "Product ID");
                if (string.IsNullOrEmpty(productId))
                {
                    var productName = GetNullableString(row, headers, "Product");
                    if (!string.IsNullOrEmpty(productName))
                    {
                        var product = data.Products.FirstOrDefault(p =>
                            string.Equals(p.Name, productName, StringComparison.OrdinalIgnoreCase));
                        productId = product?.Id;
                    }
                }
                lostDamaged.ProductId = productId ?? "";
            }

            if (Set("Inventory Item ID"))
                lostDamaged.InventoryItemId = GetNullableString(row, headers, "Inventory Item ID");
            if (Set("Quantity"))
            {
                lostDamaged.Quantity = GetInt(row, headers, "Quantity");
                if (lostDamaged.Quantity == 0)
                    lostDamaged.Quantity = 1;
            }
            if (Set("Reason"))
                lostDamaged.Reason = ParseEnum(GetString(row, headers, "Reason"), LostDamagedReason.Damaged);
            if (Set("Date Discovered", "Date"))
            {
                lostDamaged.DateDiscovered = GetDateTime(row, headers, "Date Discovered");
                if (lostDamaged.DateDiscovered == DateTime.MinValue)
                    lostDamaged.DateDiscovered = GetDateTime(row, headers, "Date");
            }
            if (Set("Value Lost"))
                lostDamaged.ValueLost = GetDecimal(row, headers, "Value Lost");
            if (Set("Notes"))
                lostDamaged.Notes = GetString(row, headers, "Notes");
            if (Set("Insurance Claim"))
            {
                var insuranceClaim = GetString(row, headers, "Insurance Claim");
                lostDamaged.InsuranceClaim = insuranceClaim.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
                                              insuranceClaim.Equals("True", StringComparison.OrdinalIgnoreCase);
            }

            if (existing == null)
                data.LostDamaged.Add(lostDamaged);
            else if (options != null)
                options.UpdatedCount++;
        }
    }

    #endregion

    #region ID Counter Update

    /// <summary>
    /// Raises each counter past the highest id now present. Only ever raises: a counter already
    /// ahead of every present id is ahead because a record was deleted, and lowering it would
    /// hand that deleted id to the next new record. Printed numbers (Invoice #, PO #) don't move a
    /// counter: IdGenerator already skips a number whose printed form is taken, and a printed
    /// number from another system (INV-20260315) would throw the counter far ahead.
    /// </summary>
    private static void UpdateIdCounters(CompanyData data)
    {
        foreach (var type in Enum.GetValues<SpreadsheetSheetType>())
            RaiseIdCounter(data, type, GetExistingEntityIds(data, type));

        var c = data.IdCounters;
        c.Quote = Math.Max(c.Quote, IdGenerator.HighestNumber(data.Quotes.Select(q => q.Id), "QUO-"));
    }

    /// <summary>
    /// Raises the counter a record type is numbered from past the highest number among
    /// <paramref name="ids"/> written in that type's own format, whatever their width, so a new id
    /// never repeats a number already used (ADJ-00001 beside ADJ-001). See
    /// <see cref="IdGenerator.HighestNumber"/> for which ids count.
    /// </summary>
    private static void RaiseIdCounter(CompanyData data, SpreadsheetSheetType type, IEnumerable<string?> ids)
    {
        var c = data.IdCounters;
        switch (type)
        {
            case SpreadsheetSheetType.Customers: c.Customer = Math.Max(c.Customer, IdGenerator.HighestNumber(ids, "CUS-")); break;
            case SpreadsheetSheetType.Products: c.Product = Math.Max(c.Product, IdGenerator.HighestNumber(ids, "PRD-", "PRD-IMP-")); break;
            case SpreadsheetSheetType.Suppliers: c.Supplier = Math.Max(c.Supplier, IdGenerator.HighestNumber(ids, "SUP-")); break;
            // Older files also have CAT-SAL- and CAT-PUR- ids. All types share one counter.
            case SpreadsheetSheetType.Categories: c.Category = Math.Max(c.Category, IdGenerator.HighestNumber(ids,
                "CAT-REV-", "CAT-EXP-", "CAT-RNT-", "CAT-GEN-", "CAT-SAL-", "CAT-PUR-")); break;
            case SpreadsheetSheetType.Locations: c.Location = Math.Max(c.Location, IdGenerator.HighestNumber(ids, "LOC-")); break;
            // Older files have SAL- revenue ids.
            case SpreadsheetSheetType.Revenue: c.Revenue = Math.Max(c.Revenue, IdGenerator.HighestNumber(ids, "REV-", "SAL-")); break;
            case SpreadsheetSheetType.Expenses: c.Expense = Math.Max(c.Expense, IdGenerator.HighestNumber(ids, "PUR-")); break;
            case SpreadsheetSheetType.Invoices: c.Invoice = Math.Max(c.Invoice, IdGenerator.HighestNumber(ids, "INV-")); break;
            case SpreadsheetSheetType.Payments: c.Payment = Math.Max(c.Payment, IdGenerator.HighestNumber(ids, "PAY-")); break;
            case SpreadsheetSheetType.RecurringInvoices: c.RecurringInvoice = Math.Max(c.RecurringInvoice, IdGenerator.HighestNumber(ids, "REC-INV-")); break;
            case SpreadsheetSheetType.Inventory: c.InventoryItem = Math.Max(c.InventoryItem, IdGenerator.HighestNumber(ids, "INV-ITM-")); break;
            case SpreadsheetSheetType.StockAdjustments: c.StockAdjustment = Math.Max(c.StockAdjustment, IdGenerator.HighestNumber(ids, "ADJ-")); break;
            case SpreadsheetSheetType.PurchaseOrders: c.PurchaseOrder = Math.Max(c.PurchaseOrder, IdGenerator.HighestNumber(ids, "PO-")); break;
            case SpreadsheetSheetType.RentalInventory: c.RentalItem = Math.Max(c.RentalItem, IdGenerator.HighestNumber(ids, "RNT-ITM-")); break;
            case SpreadsheetSheetType.RentalRecords: c.Rental = Math.Max(c.Rental, IdGenerator.HighestNumber(ids, "RNT-")); break;
            case SpreadsheetSheetType.Returns: c.Return = Math.Max(c.Return, IdGenerator.HighestNumber(ids, "RET-")); break;
            case SpreadsheetSheetType.LostDamaged: c.LostDamaged = Math.Max(c.LostDamaged, IdGenerator.HighestNumber(ids, "LOST-")); break;
        }
    }

    /// <summary>
    /// Columns on any sheet that name a record of another type. A reference can make that record
    /// (a placeholder customer named after the id), so its number is reserved like an id column's.
    /// </summary>
    private static readonly (string Column, SpreadsheetSheetType Type)[] ReferenceColumns =
    [
        ("Customer ID", SpreadsheetSheetType.Customers),
        ("Supplier ID", SpreadsheetSheetType.Suppliers),
        ("Product ID", SpreadsheetSheetType.Products),
        ("Category ID", SpreadsheetSheetType.Categories),
        ("Parent ID", SpreadsheetSheetType.Categories),
        ("Location ID", SpreadsheetSheetType.Locations),
        ("Invoice ID", SpreadsheetSheetType.Invoices),
        ("Inventory Item ID", SpreadsheetSheetType.Inventory),
        ("Rental Item ID", SpreadsheetSheetType.RentalInventory),
        ("PO ID", SpreadsheetSheetType.PurchaseOrders),
        ("Original Transaction ID", SpreadsheetSheetType.Revenue),
        ("Original Transaction ID", SpreadsheetSheetType.Expenses),
    ];

    /// <summary>
    /// Brings every counter past the highest number the company or any sheet of this import
    /// already uses, in an id column or a reference to another record, before a single id is
    /// minted. Sheets are read one after another, so without this a blank row could be numbered
    /// into an id a later sheet brings in or refers to, or into the same number as a sheet's id
    /// written in an older width.
    /// </summary>
    private static void ReserveIdNumbers(CompanyData data, IEnumerable<ImportSheet> sheets)
    {
        UpdateIdCounters(data);
        foreach (var sheet in sheets)
        {
            RaiseIdCounter(data, sheet.Type, ColumnValues(sheet, "ID"));
            foreach (var (column, type) in ReferenceColumns)
                RaiseIdCounter(data, type, ColumnValues(sheet, column));
        }
    }

    private static IEnumerable<string?> ColumnValues(ImportSheet sheet, string column) =>
        sheet.Rows.Select(row => GetString(row, sheet.Headers, column));

    #endregion
}

/// <summary>
/// A JsonConverterFactory that handles enum values leniently: strips spaces,
/// hyphens, and underscores before attempting case-insensitive enum parsing.
/// Falls back to the first enum value if parsing fails entirely.
/// </summary>
internal class LenientEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsEnum || (Nullable.GetUnderlyingType(typeToConvert)?.IsEnum == true);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        // For a nullable enum (e.g. BookRecordType?) the converter MUST handle the exact
        // Nullable<T> type, not the underlying enum, otherwise System.Text.Json throws a
        // "handles type X but asked to convert Y" mismatch.
        var underlying = Nullable.GetUnderlyingType(typeToConvert);
        if (underlying != null)
        {
            var nullableType = typeof(LenientNullableEnumConverter<>).MakeGenericType(underlying);
            return (JsonConverter)Activator.CreateInstance(nullableType)!;
        }

        var converterType = typeof(LenientEnumConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

/// <summary>
/// Shared lenient enum parsing used by both the value and nullable converters.
/// </summary>
internal static class LenientEnumParser
{
    public static T Parse<T>(ref Utf8JsonReader reader) where T : struct, Enum
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            var intValue = reader.GetInt32();
            if (Enum.IsDefined(typeof(T), intValue))
                return (T)Enum.ToObject(typeof(T), intValue);
            return default;
        }

        var value = reader.GetString();
        if (string.IsNullOrEmpty(value))
            return default;

        // Try exact match first
        if (Enum.TryParse<T>(value, ignoreCase: true, out var result))
            return result;

        // Strip spaces, hyphens, underscores and try again
        var normalized = value.Replace(" ", "").Replace("-", "").Replace("_", "");
        if (Enum.TryParse<T>(normalized, ignoreCase: true, out result))
            return result;

        // Fall back to default enum value
        return default;
    }
}

internal class LenientEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => LenientEnumParser.Parse<T>(ref reader);

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

internal class LenientNullableEnumConverter<T> : JsonConverter<T?> where T : struct, Enum
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        return LenientEnumParser.Parse<T>(ref reader);
    }

    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(value.Value.ToString());
        else
            writer.WriteNullValue();
    }
}
