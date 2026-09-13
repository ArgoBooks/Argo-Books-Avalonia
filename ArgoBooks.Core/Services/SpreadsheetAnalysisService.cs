using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Models.Telemetry;
using ClosedXML.Excel;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Service that uses an LLM to analyze spreadsheet/CSV files and produce
/// column mappings and entity type detection for import.
/// </summary>
public class SpreadsheetAnalysisService(
    IGeminiService geminiService,
    IErrorLogger? errorLogger = null,
    string? country = null)
{
    private const int SampleFirstRows = 5;
    private const int SampleLastRows = 3;
    private const int SampleRandomRows = 5;
    private const int Tier2ChunkSize = 100;
    private const int MaxConcurrentChunks = 10;

    // A chunk whose call got no reply is tried this many times in all, pausing ChunkRetryDelay,
    // then twice that, between tries.
    private const int MaxChunkAttempts = 3;

    internal TimeSpan ChunkRetryDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Minimum confidence score for a sheet to be considered a supported entity type.
    /// Sheets below this threshold are marked unsupported and excluded from import.
    /// </summary>
    private const double MinTypeConfidence = 0.5;

    // Cap the columns analyzed per LLM call so its JSON response can never exceed the
    // model's output token budget and get truncated (which fails to parse).
    private const int MaxColumnsPerAnalysisBatch = 40;
    private const int MaxConcurrentAnalysisBatches = 5;

    // How many times to attempt a batch's classification. Classification can wobble run-to-run
    // for structurally ambiguous sheets (e.g. cross-tabs) whose confidence lands near the
    // threshold; a bounded retry re-rolls that wobble instead of rejecting the sheet outright.
    private const int MaxAnalysisAttempts = 2;

    // The rescue classify call only needs enough rows to judge shape/type, not the whole sheet
    // (the full sheet is sent later during extraction). Bounding it keeps the classify response small.
    private const int RescueClassifySampleRows = 40;

    #region Analysis Phase

    /// <summary>
    /// Analyzes an Excel file and returns sheet type detection + column mappings.
    /// </summary>
    public async Task<SpreadsheetAnalysisResult?> AnalyzeAsync(
        string filePath,
        CancellationToken cancellationToken = default,
        IProgress<(string detail, double percent)>? progress = null)
    {
        try
        {
            // Report initial progress so the UI shows the loading overlay immediately
            progress?.Report(("Reading file...", 0));
            await Task.Yield();

            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var workbook = new XLWorkbook(fileStream);

            var sheetsData = new List<(string Name, List<string> Headers, List<List<string>> SampleRows, int TotalRows)>();

            foreach (var worksheet in workbook.Worksheets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var headerRow = FindHeaderRow(worksheet);
                var headers = GetHeaders(worksheet, headerRow);
                if (headers.Count == 0) continue;

                var totalRows = (worksheet.LastRowUsed()?.RowNumber() ?? headerRow) - headerRow; // exclude header and rows above it
                var sampleRows = GetSampleRows(worksheet, headers.Count, totalRows);
                sheetsData.Add((worksheet.Name, headers, sampleRows, totalRows));
            }

            if (sheetsData.Count == 0)
                return null;

            return await AnalyzeWithLlmAsync(
                Path.GetFileName(filePath), sheetsData, cancellationToken, progress);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            errorLogger?.LogError(ex, ErrorCategory.Import, "Failed to analyze spreadsheet with AI");
            return null;
        }
    }

    /// <summary>
    /// Analyzes a CSV file and returns entity type detection + column mappings.
    /// </summary>
    public async Task<SpreadsheetAnalysisResult?> AnalyzeCsvAsync(
        string filePath,
        CancellationToken cancellationToken = default,
        IProgress<(string detail, double percent)>? progress = null)
    {
        try
        {
            // Report initial progress so the UI shows the loading overlay immediately
            progress?.Report(("Reading file...", 0));
            await Task.Yield();

            var allDataRows = CsvReader.ReadAllRows(filePath, out var headers);
            if (headers.Count == 0)
                return null;

            // Require at least one data row
            if (allDataRows.Count == 0)
                return null;

            var totalRows = allDataRows.Count;
            var sampleRows = GetSampleFromList(allDataRows, totalRows);
            var sheetName = Path.GetFileNameWithoutExtension(filePath);
            var sheetsData = new List<(string Name, List<string> Headers, List<List<string>> SampleRows, int TotalRows)>
            {
                (sheetName, headers, sampleRows, totalRows)
            };

            return await AnalyzeWithLlmAsync(
                Path.GetFileName(filePath), sheetsData, cancellationToken, progress);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            errorLogger?.LogError(ex, ErrorCategory.Import, "Failed to analyze CSV with AI");
            return null;
        }
    }

    private async Task<SpreadsheetAnalysisResult?> AnalyzeWithLlmAsync(
        string fileName,
        List<(string Name, List<string> Headers, List<List<string>> SampleRows, int TotalRows)> sheetsData,
        CancellationToken cancellationToken,
        IProgress<(string detail, double percent)>? progress = null)
    {
        // Split sheets into batches so a single LLM call never has to map so many columns
        // that its JSON response exceeds the model's output token budget and gets truncated.
        // A truncated response fails to parse and would otherwise look like an unreadable file.
        var batches = SplitIntoAnalysisBatches(sheetsData);

        // The visible progress bar is driven by the UI layer from the learned duration estimate
        // (see EstimatedProgressTicker), so this only reports the status text; percent -1 signals
        // "no real fraction here" so nothing shows a misleading number.
        progress?.Report(("Analyzing...", -1));

        // Analyze batches concurrently; each batch is an independent LLM call.
        using var semaphore = new SemaphoreSlim(MaxConcurrentAnalysisBatches);
        var tasks = batches.Select(batch => Task.Run(async () =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await AnalyzeBatchWithRetryAsync(batch, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        }, cancellationToken)).ToArray();

        var batchResults = await Task.WhenAll(tasks);

        // Merge successful batches. A batch is null only when its LLM call failed or its
        // response could not be parsed (both already logged inside AnalyzeBatchAsync).
        var succeeded = batchResults.Where(r => r != null).ToList();
        if (succeeded.Count == 0)
            return null;

        var merged = new SpreadsheetAnalysisResult { FileName = fileName };
        foreach (var batchResult in succeeded)
            merged.Sheets.AddRange(batchResult!.Sheets);

        // If some batches failed, flag the import as partial so the user is told some sheets
        // were skipped instead of silently importing only a subset.
        var failedBatches = batchResults.Length - succeeded.Count;
        if (failedBatches > 0)
            merged.PartialAnalysisWarning =
                $"{failedBatches} of {batchResults.Length} sheet group(s) could not be analyzed and were skipped, so those sheets were not imported.";

        // Populate row counts from our data
        foreach (var sheet in merged.Sheets)
        {
            var data = sheetsData.FirstOrDefault(s => s.Name == sheet.SourceSheetName);
            if (data != default)
                sheet.RowCount = data.TotalRows;
        }

        return merged;
    }

    /// <summary>
    /// Splits sheets into batches whose combined column count stays within
    /// <see cref="MaxColumnsPerAnalysisBatch"/>, so each analysis call produces a response
    /// that fits comfortably inside the model's output token budget. A single sheet wider
    /// than the limit gets its own batch.
    /// </summary>
    private static List<List<(string Name, List<string> Headers, List<List<string>> SampleRows, int TotalRows)>>
        SplitIntoAnalysisBatches(
            List<(string Name, List<string> Headers, List<List<string>> SampleRows, int TotalRows)> sheetsData)
    {
        var batches = new List<List<(string Name, List<string> Headers, List<List<string>> SampleRows, int TotalRows)>>();
        var current = new List<(string Name, List<string> Headers, List<List<string>> SampleRows, int TotalRows)>();
        var currentColumns = 0;

        foreach (var sheet in sheetsData)
        {
            var columns = sheet.Headers.Count;
            if (current.Count > 0 && currentColumns + columns > MaxColumnsPerAnalysisBatch)
            {
                batches.Add(current);
                current = [];
                currentColumns = 0;
            }

            current.Add(sheet);
            currentColumns += columns;
        }

        if (current.Count > 0)
            batches.Add(current);

        return batches;
    }

    /// <summary>
    /// Runs <see cref="AnalyzeBatchAsync"/> with a bounded retry. A retry is attempted only when
    /// the call/parse failed, or a sheet was classified as a known type but landed just under the
    /// confidence threshold (the wobble that makes an ambiguous sheet flip between importable and
    /// "cannot import" across runs). A confident "Unknown" is NOT retried, so genuinely
    /// unsupported sheets (notes, summaries) don't cost extra calls. The attempt with the fewest
    /// unsupported sheets wins.
    /// </summary>
    private async Task<SpreadsheetAnalysisResult?> AnalyzeBatchWithRetryAsync(
        List<(string Name, List<string> Headers, List<List<string>> SampleRows, int TotalRows)> batch,
        CancellationToken cancellationToken)
    {
        SpreadsheetAnalysisResult? best = null;
        var bestUnsupported = int.MaxValue;

        for (int attempt = 1; attempt <= MaxAnalysisAttempts; attempt++)
        {
            var result = await AnalyzeBatchAsync(batch, cancellationToken);

            if (result != null)
            {
                var unsupported = result.Sheets.Count(s => s.UnsupportedReason != null);
                if (unsupported < bestUnsupported)
                {
                    best = result;
                    bestUnsupported = unsupported;
                }

                // Only a known-type-but-low-confidence sheet is worth re-rolling. If none remain,
                // this result is as good as it gets (any leftover unsupported sheets are confidently
                // Unknown), so stop.
                var hasBorderlineKnown = result.Sheets.Any(s =>
                    s.DetectedType != SpreadsheetSheetType.Unknown && s.Confidence < MinTypeConfidence);
                if (!hasBorderlineKnown)
                    break;
            }
        }

        return best;
    }

    /// <summary>
    /// Runs one batch of sheets through the LLM and parses the response.
    /// Returns null if the call failed or the response could not be parsed.
    /// </summary>
    private async Task<SpreadsheetAnalysisResult?> AnalyzeBatchAsync(
        List<(string Name, List<string> Headers, List<List<string>> SampleRows, int TotalRows)> batch,
        CancellationToken cancellationToken)
    {
        var systemPrompt = BuildAnalysisSystemPrompt();
        var userPrompt = BuildAnalysisUserPrompt(batch);

        // The response needs one mapping object per source column, so scale the token budget
        // by total columns rather than sheet count. gemini-2.5-flash also spends part of this
        // budget on thinking tokens, so keep generous headroom above the raw mapping size.
        var totalColumns = batch.Sum(s => s.Headers.Count);
        var maxTokens = Math.Max(4000, totalColumns * 200 + batch.Count * 400);

        var response = await geminiService.SendChatAsync(
            systemPrompt, userPrompt, maxTokens: maxTokens, temperature: 0.0, cancellationToken,
            operation: OperationKind.SpreadsheetAnalysis, sizeFeature: totalColumns);

        if (string.IsNullOrEmpty(response))
        {
            errorLogger?.LogWarning(
                $"AI analysis returned an empty response for a batch of {batch.Count} sheet(s).",
                "Spreadsheet analysis");
            return null;
        }

        var result = ParseAnalysisResponse(response);
        if (result == null)
        {
            // A non-empty response that fails to parse is almost always truncated JSON:
            // the response ran past the model's output token budget mid-object.
            errorLogger?.LogError(
                $"AI analysis response could not be parsed (length {response.Length}, " +
                $"{batch.Count} sheet(s), {totalColumns} columns, maxTokens {maxTokens}); response likely truncated.",
                ErrorCategory.Parsing, "Spreadsheet analysis");
        }

        return result;
    }

    #endregion

    #region Tier 2 Processing

    /// <summary>
    /// Processes a chunk of rows through the LLM to normalize them into entity JSON.
    /// </summary>
    public async Task<LlmProcessedData?> ProcessChunkAsync(
        List<string> headers,
        List<List<string>> rows,
        SpreadsheetSheetType entityType,
        CancellationToken cancellationToken = default)
        => (await TryProcessChunkAsync(headers, rows, entityType, cancellationToken)).Result;

    /// <summary>
    /// Runs <see cref="TryProcessChunkAsync"/>, retrying with a growing pause when no reply came
    /// back at all (the server's rate limit, a timeout, a dropped connection).
    /// </summary>
    private async Task<LlmProcessedData?> ProcessChunkWithRetryAsync(
        List<string> headers,
        List<List<string>> rows,
        SpreadsheetSheetType entityType,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var (result, answered) = await TryProcessChunkAsync(headers, rows, entityType, cancellationToken);

            // An unreadable reply isn't retried: the same rows at temperature 0 would most likely
            // come back the same, and every answered call counts against the server's rate limit.
            if (result != null || answered || attempt >= MaxChunkAttempts)
                return result;

            await Task.Delay(ChunkRetryDelay * attempt, cancellationToken);
        }
    }

    /// <summary>
    /// One LLM call for a chunk. <c>Answered</c> is false when no reply came back, the transient
    /// case worth retrying.
    /// </summary>
    private async Task<(LlmProcessedData? Result, bool Answered)> TryProcessChunkAsync(
        List<string> headers,
        List<List<string>> rows,
        SpreadsheetSheetType entityType,
        CancellationToken cancellationToken)
    {
        var schema = ImportSchemaDefinition.GetSchemaForType(entityType, country);
        if (schema == null)
            return (null, true);

        var systemPrompt = BuildTier2SystemPrompt(entityType, schema);
        var userPrompt = BuildTier2UserPrompt(headers, rows);

        string? response;
        try
        {
            response = await geminiService.SendChatAsync(
                systemPrompt, userPrompt, maxTokens: 16000, temperature: 0.0, cancellationToken,
                operation: OperationKind.SpreadsheetProcess, sizeFeature: rows.Count);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a cancellation. Left to propagate, one slow
            // chunk ended the whole import as though the user had pressed Cancel.
            response = null;
        }

        if (string.IsNullOrEmpty(response))
            return (null, false);

        return (ParseTier2Response(response, entityType, rows.Count), true);
    }

    /// <summary>
    /// Processes all rows of a sheet through LLM in chunks, reporting progress.
    /// </summary>
    public async Task<List<LlmProcessedData>> ProcessAllChunksAsync(
        string filePath,
        SheetAnalysis sheetAnalysis,
        IProgress<(int processed, int total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        List<string> headers;
        List<List<string>> allRows;

        if (filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            allRows = CsvReader.ReadAllRows(filePath, out headers);
        }
        else
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var workbook = new XLWorkbook(fileStream);
            var worksheet = workbook.Worksheets.FirstOrDefault(w => w.Name == sheetAnalysis.SourceSheetName);
            if (worksheet == null)
                return [];

            headers = GetHeaders(worksheet);
            allRows = GetAllRowsAsStrings(worksheet, headers.Count);
        }

        return await ProcessAllChunksAsync(headers, allRows, sheetAnalysis, progress, cancellationToken);
    }

    /// <summary>
    /// Reads all sheet data from a file for the given sheets, returning headers and rows per sheet.
    /// Use this to pre-read the file once before calling ProcessAllChunksAsync with pre-read data.
    /// </summary>
    public static async Task<Dictionary<string, (List<string> Headers, List<List<string>> Rows)>> ReadSheetDataAsync(
        string filePath,
        List<SheetAnalysis> sheets,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, (List<string> Headers, List<List<string>> Rows)>();

        if (filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            var rows = CsvReader.ReadAllRows(filePath, out var headers);
            if (headers.Count == 0) return result;
            foreach (var sheet in sheets)
                result[sheet.SourceSheetName] = (headers, rows);
        }
        else
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var wb = new XLWorkbook(fs);
            foreach (var sheet in sheets)
            {
                var ws = wb.Worksheets.FirstOrDefault(w => w.Name == sheet.SourceSheetName);
                if (ws == null) continue;
                var headers = GetHeaders(ws);
                var rows = GetAllRowsAsStrings(ws, headers.Count);
                result[sheet.SourceSheetName] = (headers, rows);
            }
        }

        return result;
    }

    /// <summary>
    /// Processes pre-read rows through LLM in chunks, reporting progress.
    /// Use this overload to avoid re-reading the file for each sheet.
    /// </summary>
    public async Task<List<LlmProcessedData>> ProcessAllChunksAsync(
        List<string> headers,
        List<List<string>> allRows,
        SheetAnalysis sheetAnalysis,
        IProgress<(int processed, int total)>? progress = null,
        CancellationToken cancellationToken = default,
        int chunkSize = Tier2ChunkSize)
    {
        var total = allRows.Count;

        // Build all chunks upfront
        var chunks = new List<(int Index, List<List<string>> Rows)>();
        for (int i = 0; i < total; i += chunkSize)
            chunks.Add((i, allRows.Skip(i).Take(chunkSize).ToList()));

        // Process chunks in parallel with concurrency limit
        using var semaphore = new SemaphoreSlim(MaxConcurrentChunks);
        var processedCount = 0;
        var chunkResults = new LlmProcessedData?[chunks.Count];

        var tasks = chunks.Select((chunk, idx) => Task.Run(async () =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                chunkResults[idx] = await ProcessChunkWithRetryAsync(headers, chunk.Rows, sheetAnalysis.DetectedType, cancellationToken);
            }
            finally
            {
                semaphore.Release();
                var done = Interlocked.Add(ref processedCount, chunk.Rows.Count);
                progress?.Report((Math.Min(done, total), total));
            }
        }, cancellationToken)).ToArray();

        await Task.WhenAll(tasks);

        var results = new List<LlmProcessedData>();
        var failedRows = 0;
        for (int i = 0; i < chunkResults.Length; i++)
        {
            if (chunkResults[i] != null)
            {
                results.Add(chunkResults[i]!);
                continue;
            }

            failedRows += chunks[i].Rows.Count;
            results.Add(new LlmProcessedData
            {
                EntityType = sheetAnalysis.DetectedType,
                FailedRows = chunks[i].Rows.Select(row => string.Join(" | ", row)).ToList()
            });
        }

        if (failedRows > 0)
        {
            errorLogger?.LogWarning(
                $"AI processing: {failedRows} of {total} rows failed to process for sheet '{sheetAnalysis.SourceSheetName}'");
        }

        return results;
    }

    #endregion

    #region Prompt Building

    private static string BuildAnalysisSystemPrompt()
    {
        return @"You are an expert data analyst for a bookkeeping application called Argo Books. Your task is to analyze spreadsheet data and determine:
1. What type of business entity each sheet represents
2. How source columns map to the expected Argo Books schema
3. Whether simple column mapping (Tier 1) suffices, or if complex row transformation (Tier 2) is needed

Use Tier 2 ONLY when:
- Multiple entity types are mixed in one sheet
- Rows need grouping (e.g., line-item-per-row that must become one invoice)
- The structure is fundamentally different from a simple table (e.g., pivot tables, cross-tabs)
- Data requires splitting/combining columns in non-trivial ways

For everything else (renamed columns, different terminology, minor format differences), use Tier 1.

Respond with valid JSON only, no markdown code blocks.";
    }

    private string BuildAnalysisUserPrompt(
        List<(string Name, List<string> Headers, List<List<string>> SampleRows, int TotalRows)> sheetsData)
    {
        var sb = new StringBuilder();

        sb.AppendLine("## Target Schema");
        sb.AppendLine(ImportSchemaDefinition.FormatSchemaForPrompt(country));

        sb.AppendLine("## Source Data");
        sb.AppendLine();

        foreach (var (name, headers, sampleRows, totalRows) in sheetsData)
        {
            sb.AppendLine($"### Sheet: \"{name}\" ({totalRows} data rows)");
            sb.AppendLine();

            // Headers
            sb.Append("| ");
            sb.Append(string.Join(" | ", headers));
            sb.AppendLine(" |");

            sb.Append("| ");
            sb.Append(string.Join(" | ", headers.Select(_ => "---")));
            sb.AppendLine(" |");

            foreach (var row in sampleRows)
            {
                sb.Append("| ");
                // Pad row to match header count
                var cells = new List<string>(row);
                while (cells.Count < headers.Count)
                    cells.Add("");
                sb.Append(string.Join(" | ", cells.Select(c => c.Replace("|", "\\|"))));
                sb.AppendLine(" |");
            }
            sb.AppendLine();

            // Column profiles: inferred types + basic stats computed from the sample rows.
            // These give the model stronger signal for classification and column mapping.
            if (sampleRows.Count > 0)
            {
                var profiles = ColumnProfiler.Profile(headers, sampleRows);
                sb.AppendLine("#### Column profiles");
                foreach (var p in profiles)
                {
                    var examples = p.Examples.Count > 0
                        ? $", examples: {string.Join(", ", p.Examples)}"
                        : "";
                    sb.AppendLine($"- {p.Header} ({p.InferredType}, distinct={p.DistinctCount}, empty={p.EmptyCount}{examples})");
                }
                sb.AppendLine();

                var relationships = ColumnProfiler.DetectRelationships(headers, sampleRows);
                if (relationships.Count > 0)
                {
                    sb.AppendLine("#### Detected relationships");
                    foreach (var rel in relationships)
                        sb.AppendLine($"- {rel.Description}");
                    sb.AppendLine();
                }
            }
        }

        sb.AppendLine(@"## Response Format
{
  ""sheets"": [
    {
      ""sourceSheetName"": ""<exact sheet name>"",
      ""detectedType"": ""<one of: Customers, Suppliers, Products, Categories, Locations, Invoices, Expenses, Inventory, Payments, Revenue, RentalInventory, RentalRecords, RecurringInvoices, StockAdjustments, PurchaseOrders, PurchaseOrderLineItems, Returns, LostDamaged, Unknown>"",
      ""confidence"": 0.95,
      ""tier"": ""Tier1_Mapping"",
      ""tierReason"": """",
      ""columnMappings"": [
        { ""sourceColumn"": ""<source col>"", ""targetColumn"": ""<target col from schema>"", ""confidence"": 0.98, ""transformHint"": null }
      ],
      ""unmappedSourceColumns"": [""<columns that don't map to any target>""],
      ""unmappedTargetColumns"": [""<target columns with no source match>""]
    }
  ]
}

IMPORTANT:
- sourceSheetName must EXACTLY match the original sheet name
- targetColumn must EXACTLY match a column name from the target schema above
- detectedType must be one of the listed entity types
- Only include mappings where you are reasonably confident (>0.5)
- Set tier to ""Tier2_LlmProcessing"" only when simple column mapping cannot work");

        return sb.ToString();
    }

    private static string BuildTier2SystemPrompt(SpreadsheetSheetType entityType, List<SchemaColumn> schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are converting raw spreadsheet data into normalized {entityType} records for Argo Books.");
        sb.AppendLine();
        sb.AppendLine("Target JSON schema (use these exact property names as JSON keys):");

        // Collect columns with JsonName, deduplicating by JsonName (some columns map to the same property)
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var col in schema)
        {
            var jsonName = col.JsonName ?? col.Name;
            if (!seen.Add(jsonName)) continue;

            var req = col.Required ? " (REQUIRED)" : "";
            sb.AppendLine($"- {jsonName} ({col.Type}): {col.Description}{req}");
        }

        // If any columns use dotted names (e.g., address.street), explain nesting
        if (schema.Any(c => c.JsonName?.Contains('.') == true))
        {
            sb.AppendLine();
            sb.AppendLine("For dotted property names like 'address.street', nest them as JSON objects:");
            sb.AppendLine("  { \"address\": { \"street\": \"value\", \"city\": \"value\" } }");
        }

        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- Output a JSON array of objects using the exact JSON property names listed above");
        sb.AppendLine("- Generate reasonable IDs if none exist (e.g., CUS-001, INV-2024-001)");
        sb.AppendLine("- Parse dates to ISO 8601 format (yyyy-MM-dd or yyyy-MM-ddTHH:mm:ss)");
        sb.AppendLine("- Parse decimal amounts (remove currency symbols, handle comma/dot separators)");
        sb.AppendLine("- Skip rows that are clearly subtotals, headers, or empty");
        sb.AppendLine("- If multiple source rows represent one entity, group them");
        sb.AppendLine("- Respond with JSON array only, no markdown");
        sb.AppendLine("- Cell values containing pipe characters appear as \\| in the table, use | (without backslash) in your JSON output");

        // Product-specific instructions for category generation
        if (entityType == SpreadsheetSheetType.Products)
        {
            sb.AppendLine();
            sb.AppendLine("Product-specific rules:");
            sb.AppendLine("- ALWAYS provide a categoryName for every product, even if the source data has no category column");
            sb.AppendLine("- If the source data has a category, use it as categoryName");
            sb.AppendLine("- If no category exists in source data, infer an appropriate category name from the product name and description (e.g., 'Industrial Drill Press' → 'Power Tools', 'Monthly Bookkeeping' → 'Bookkeeping Services', 'Copper Pipe' → 'Plumbing')");
            sb.AppendLine("- Set type to 'Expense' for products/services that are typically purchased or expensed (e.g., office supplies, bookkeeping, equipment rental), and 'Revenue' for items typically sold to customers");
        }

        var prompt = sb.ToString();
        return prompt;
    }

    private static string BuildTier2UserPrompt(List<string> headers, List<List<string>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Convert these rows:");
        sb.AppendLine();

        sb.Append("| ");
        sb.Append(string.Join(" | ", headers));
        sb.AppendLine(" |");

        sb.Append("| ");
        sb.Append(string.Join(" | ", headers.Select(_ => "---")));
        sb.AppendLine(" |");

        foreach (var row in rows)
        {
            sb.Append("| ");
            var cells = new List<string>(row);
            while (cells.Count < headers.Count)
                cells.Add("");
            sb.Append(string.Join(" | ", cells.Select(c => c.Replace("|", "\\|"))));
            sb.AppendLine(" |");
        }

        return sb.ToString();
    }

    #endregion

    #region Response Parsing

    internal static SpreadsheetAnalysisResult? ParseAnalysisResponse(string response)
    {
        try
        {
            var cleanResponse = CleanJsonResponse(response);
            using var doc = JsonDocument.Parse(cleanResponse);
            var root = doc.RootElement;

            var result = new SpreadsheetAnalysisResult();

            if (root.TryGetProperty("sheets", out var sheetsArray))
            {
                foreach (var sheetEl in sheetsArray.EnumerateArray())
                {
                    var sheet = new SheetAnalysis
                    {
                        // Safe TryGetProperty-based helpers, not the throwing GetProperty: one element
                        // missing sourceSheetName/detectedType must not throw and discard the WHOLE
                        // batch (every other field already uses these helpers).
                        SourceSheetName = GetString(sheetEl, "sourceSheetName"),
                        Confidence = GetDouble(sheetEl, "confidence"),
                    };

                    // Parse detected type
                    var typeStr = GetString(sheetEl, "detectedType");
                    sheet.DetectedType = Enum.TryParse<SpreadsheetSheetType>(typeStr, ignoreCase: true, out var parsed)
                        ? parsed
                        : SpreadsheetSheetType.Unknown;

                    // Parse tier
                    var tierStr = GetString(sheetEl, "tier");
                    sheet.Tier = tierStr.Contains("Tier2", StringComparison.OrdinalIgnoreCase)
                        ? ProcessingTier.Tier2_LlmProcessing
                        : ProcessingTier.Tier1_Mapping;
                    sheet.TierReason = GetString(sheetEl, "tierReason");

                    // Parse column mappings
                    if (sheetEl.TryGetProperty("columnMappings", out var mappingsArray))
                    {
                        foreach (var mapEl in mappingsArray.EnumerateArray())
                        {
                            sheet.ColumnMappings.Add(new ColumnMapping
                            {
                                SourceColumn = GetString(mapEl, "sourceColumn"),
                                TargetColumn = GetString(mapEl, "targetColumn"),
                                Confidence = GetDouble(mapEl, "confidence"),
                                TransformHint = mapEl.TryGetProperty("transformHint", out var hint) && hint.ValueKind != JsonValueKind.Null
                                    ? hint.GetString() : null,
                            });
                        }
                    }

                    // Parse unmapped columns
                    if (sheetEl.TryGetProperty("unmappedSourceColumns", out var unmappedSrc))
                    {
                        foreach (var col in unmappedSrc.EnumerateArray())
                            sheet.UnmappedSourceColumns.Add(col.GetString() ?? "");
                    }

                    if (sheetEl.TryGetProperty("unmappedTargetColumns", out var unmappedTgt))
                    {
                        foreach (var col in unmappedTgt.EnumerateArray())
                            sheet.UnmappedTargetColumns.Add(col.GetString() ?? "");
                    }

                    // Mark sheets that cannot be imported: Unknown type or below the confidence threshold.
                    if (sheet.DetectedType == SpreadsheetSheetType.Unknown || sheet.Confidence < MinTypeConfidence)
                    {
                        sheet.UnsupportedReason = $"This sheet ('{sheet.SourceSheetName}') does not match a data type Argo Books can import.";
                        sheet.IsIncluded = false;
                    }

                    result.Sheets.Add(sheet);
                }
            }

            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static LlmProcessedData? ParseTier2Response(string response, SpreadsheetSheetType entityType, int sourceRowCount)
    {
        try
        {
            var cleanResponse = CleanJsonResponse(response);
            using var doc = JsonDocument.Parse(cleanResponse);

            var result = new LlmProcessedData
            {
                EntityType = entityType,
                SourceRowsProcessed = sourceRowCount,
            };

            // Response should be an array of entity objects, or an object wrapping one under
            // "entities". Any other shape is unrecognized: return null so the caller counts the chunk
            // as failed instead of silently importing zero rows with no warning.
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var entity in doc.RootElement.EnumerateArray())
                {
                    result.Entities.Add(entity.Clone());
                }
            }
            else if (doc.RootElement.TryGetProperty("entities", out var entitiesArray)
                     && entitiesArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var entity in entitiesArray.EnumerateArray())
                {
                    result.Entities.Add(entity.Clone());
                }
            }
            else
            {
                return null;
            }

            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string CleanJsonResponse(string response) =>
        JsonResponseHelper.StripMarkdownCodeBlock(response);

    private static string GetString(JsonElement el, string prop)
    {
        return el.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.String
            ? val.GetString() ?? "" : "";
    }

    private static double GetDouble(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var val)) return 0;
        return val.ValueKind == JsonValueKind.Number ? val.GetDouble() : 0;
    }

    #endregion

    #region Data Extraction Helpers

    /// <summary>
    /// Finds the header row by scanning for the first row with at least 2 non-empty cells.
    /// Falls back to row 1 if no such row is found within the first 10 rows.
    /// </summary>
    private static int FindHeaderRow(IXLWorksheet worksheet)
    {
        var lastRow = Math.Min(worksheet.LastRowUsed()?.RowNumber() ?? 1, 10);
        var colCount = worksheet.ColumnsUsed().Count();

        for (int rowNum = 1; rowNum <= lastRow; rowNum++)
        {
            var row = worksheet.Row(rowNum);
            int nonEmpty = 0;
            for (int col = 1; col <= colCount; col++)
            {
                if (!row.Cell(col).IsEmpty()) nonEmpty++;
                if (nonEmpty >= 2) return rowNum;
            }
        }

        return 1;
    }

    internal static List<string> GetHeaders(IXLWorksheet worksheet)
    {
        return GetHeaders(worksheet, FindHeaderRow(worksheet));
    }

    internal static List<string> GetHeaders(IXLWorksheet worksheet, int headerRow)
    {
        var headers = new List<string>();
        var row = worksheet.Row(headerRow);
        var colCount = worksheet.ColumnsUsed().Count();
        var trailingEmpty = 0;
        for (int col = 1; col <= colCount; col++)
        {
            var cell = row.Cell(col);
            if (cell.IsEmpty())
            {
                // Track consecutive trailing empties, add placeholder for gaps
                headers.Add($"Column{col}");
                trailingEmpty++;
            }
            else
            {
                trailingEmpty = 0;
                headers.Add(cell.GetString().Trim());
            }
        }
        // Remove trailing empty placeholders (only gap columns in the middle matter)
        if (trailingEmpty > 0)
            headers.RemoveRange(headers.Count - trailingEmpty, trailingEmpty);
        return headers;
    }

    private static List<List<string>> GetSampleRows(IXLWorksheet worksheet, int columnCount, int totalRows)
    {
        if (totalRows <= 0) return [];

        var headerRow = FindHeaderRow(worksheet);
        var indices = GetSampleIndices(totalRows);
        var result = new List<List<string>>();

        foreach (var rowIdx in indices)
        {
            var xlRow = worksheet.Row(rowIdx + headerRow + 1); // +headerRow+1: skip header, data starts after
            var rowData = new List<string>();
            for (int col = 1; col <= columnCount; col++)
            {
                var cell = xlRow.Cell(col);
                rowData.Add(cell.IsEmpty() ? "" : CellToString(cell));
            }
            result.Add(rowData);
        }

        return result;
    }

    internal static List<List<string>> GetAllRowsAsStrings(IXLWorksheet worksheet, int columnCount)
    {
        var headerRow = FindHeaderRow(worksheet);
        var rows = new List<List<string>>();
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;

        for (int rowNum = headerRow + 1; rowNum <= lastRow; rowNum++)
        {
            var xlRow = worksheet.Row(rowNum);
            var rowData = new List<string>();
            bool isEmpty = true;

            for (int col = 1; col <= columnCount; col++)
            {
                var cell = xlRow.Cell(col);
                var val = cell.IsEmpty() ? "" : CellToString(cell);
                if (!string.IsNullOrEmpty(val)) isEmpty = false;
                rowData.Add(val);
            }

            if (!isEmpty)
                rows.Add(rowData);
        }

        return rows;
    }

    private static string CellToString(IXLCell cell)
    {
        if (cell.IsEmpty()) return "";
        return cell.DataType switch
        {
            XLDataType.DateTime => cell.GetDateTime().TimeOfDay == TimeSpan.Zero
                ? cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : cell.GetDateTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            XLDataType.Number => cell.GetDouble().ToString(CultureInfo.InvariantCulture),
            XLDataType.Boolean => cell.GetBoolean().ToString(),
            _ => cell.GetString()
        };
    }

    /// <summary>
    /// Gets sample row indices: first N, last M, and P random from the middle.
    /// </summary>
    internal static List<int> GetSampleIndices(int totalRows)
    {
        if (totalRows <= SampleFirstRows + SampleLastRows + SampleRandomRows)
        {
            // Return all rows if the total is small enough
            return Enumerable.Range(0, totalRows).ToList();
        }

        var indices = new HashSet<int>();

        for (int i = 0; i < SampleFirstRows; i++)
            indices.Add(i);

        for (int i = totalRows - SampleLastRows; i < totalRows; i++)
            indices.Add(i);

        // Random from middle
        var rng = new Random(42); // deterministic seed for reproducibility
        var middleStart = SampleFirstRows;
        var middleEnd = totalRows - SampleLastRows;
        var attempts = 0;
        while (indices.Count < SampleFirstRows + SampleLastRows + SampleRandomRows && attempts < 50)
        {
            indices.Add(rng.Next(middleStart, middleEnd));
            attempts++;
        }

        return indices.OrderBy(i => i).ToList();
    }

    private static List<List<string>> GetSampleFromList(List<List<string>> allRows, int totalRows)
    {
        var indices = GetSampleIndices(totalRows);
        return indices.Where(i => i < allRows.Count).Select(i => allRows[i]).ToList();
    }

    #endregion

    #region CSV Helpers

    internal static char DetectCsvDelimiter(string headerLine)
    {
        char[] candidates = [',', '\t', ';', '|'];
        var maxCount = 0;
        var bestDelimiter = ',';

        foreach (var delimiter in candidates)
        {
            // Count delimiter occurrences outside quoted fields
            var count = 0;
            var inQuotes = false;
            foreach (var c in headerLine)
            {
                if (c == '"') inQuotes = !inQuotes;
                else if (c == delimiter && !inQuotes) count++;
            }
            if (count > maxCount)
            {
                maxCount = count;
                bestDelimiter = delimiter;
            }
        }

        return bestDelimiter;
    }

    internal static List<string> ParseCsvLine(string line, char delimiter)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++; // skip escaped quote
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString().Trim());
        return fields;
    }

    #endregion

    #region Rescue Fallback

    /// <summary>
    /// Whole-file rescue used when normal analysis could not classify a file. Asks the LLM, per sheet,
    /// to either classify it into a supported entity type or return a fixed rejection reason code.
    /// </summary>
    public async Task<RescueClassification> ClassifyOrRejectAsync(
        List<string> headers,
        List<List<string>> rows,
        string sheetName,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = BuildRescueClassifySystemPrompt();
        // Sample across the whole sheet (beginning, middle, end) so a mixed report's later
        // Expenses section is visible to the classifier, not just the leading Income rows.
        var sample = rows.Count > RescueClassifySampleRows
            ? SpreadSample(rows, RescueClassifySampleRows)
            : rows;
        var userPrompt = BuildRescueClassifyUserPrompt(sheetName, headers, sample, rows.Count);

        var response = await geminiService.SendChatAsync(
            systemPrompt, userPrompt, maxTokens: 1000, temperature: 0.0, cancellationToken,
            operation: OperationKind.SpreadsheetAnalysis, sizeFeature: headers.Count);

        if (string.IsNullOrEmpty(response))
            return new RescueClassification { Reason = ImportRescueRejectionReason.UnsupportedStructure };

        return ParseRescueClassification(response);
    }

    // Evenly spaced sample across all rows (always includes the first and last row),
    // so classification sees every section of a long report, not just the top.
    internal static List<List<string>> SpreadSample(List<List<string>> rows, int count)
    {
        if (rows.Count <= count) return rows;
        var picked = new List<List<string>>(count);
        for (int i = 0; i < count; i++)
        {
            var idx = (int)((long)i * (rows.Count - 1) / (count - 1));
            picked.Add(rows[idx]);
        }
        return picked;
    }

    private static string BuildRescueClassifySystemPrompt()
    {
        return @"You are a data import assistant for a bookkeeping app called Argo Books. The normal importer could not recognize this sheet. Decide ONE of the following:

1. EXTRACT - the sheet is a list of individual records (one record per row) that matches an Argo Books data type. Respond:
   {""action"":""extract"",""entityType"":""<one of: Customers, Suppliers, Products, Categories, Locations, Invoices, Expenses, Inventory, Payments, Revenue, RentalInventory, RentalRecords, RecurringInvoices, StockAdjustments, PurchaseOrders, PurchaseOrderLineItems, Returns, LostDamaged, BankStatement>""}

2. MIXED - the sheet is a single report that lists individual transactions under BOTH an Income section and an Expenses section (for example a Profit and Loss Detail). Respond:
   {""action"":""mixed""}

3. REJECT - the sheet cannot be imported as individual records. Respond:
   {""action"":""reject"",""reason"":""<one of the reason codes below>""}

Reason codes:
- SummaryOrReport: a summary or report with category totals and subtotals (e.g. a Profit and Loss statement), not individual records.
- NotArgoData: the content is unrelated to anything a bookkeeping app tracks.
- UnsupportedStructure: it looks like records but the layout (pivoted, cross-tab, multiple stacked tables) cannot be read as one record per row.
- EmptyOrUnreadable: there is no usable data.

Choose EXTRACT only when you are confident real per-row records are present. When unsure, REJECT with the closest reason. Respond with a single JSON object only, no markdown.";
    }

    private string BuildRescueClassifyUserPrompt(
        string sheetName, List<string> headers, List<List<string>> sampleRows, int totalRows)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Sheet name: \"{sheetName}\" ({totalRows} data rows). Sample rows:");
        sb.AppendLine();

        sb.Append("| ").Append(string.Join(" | ", headers)).AppendLine(" |");
        sb.Append("| ").Append(string.Join(" | ", headers.Select(_ => "---"))).AppendLine(" |");
        foreach (var row in sampleRows)
        {
            var cells = new List<string>(row);
            while (cells.Count < headers.Count) cells.Add("");
            sb.Append("| ").Append(string.Join(" | ", cells.Select(c => c.Replace("|", "\\|")))).AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("Argo Books data types and their fields:");
        sb.AppendLine(ImportSchemaDefinition.FormatSchemaForPrompt(country));
        return sb.ToString();
    }

    internal static RescueClassification ParseRescueClassification(string response)
    {
        try
        {
            var clean = CleanJsonResponse(response);
            using var doc = JsonDocument.Parse(clean);
            var root = doc.RootElement;

            var action = GetString(root, "action");
            if (string.Equals(action, "mixed", StringComparison.OrdinalIgnoreCase))
            {
                return new RescueClassification { IsMixedIncomeExpense = true };
            }

            if (string.Equals(action, "extract", StringComparison.OrdinalIgnoreCase))
            {
                var typeStr = GetString(root, "entityType");
                if (Enum.TryParse<SpreadsheetSheetType>(typeStr, ignoreCase: true, out var type)
                    && type != SpreadsheetSheetType.Unknown)
                {
                    return new RescueClassification { EntityType = type };
                }

                // "extract" without a usable type is contradictory: treat as unmappable.
                return new RescueClassification { Reason = ImportRescueRejectionReason.UnsupportedStructure };
            }

            // Any non-extract action is a rejection. Unknown/blank reason codes default to UnsupportedStructure.
            var reasonStr = GetString(root, "reason");
            var reason = Enum.TryParse<ImportRescueRejectionReason>(reasonStr, ignoreCase: true, out var parsed)
                ? parsed
                : ImportRescueRejectionReason.UnsupportedStructure;
            return new RescueClassification { Reason = reason };
        }
        catch (Exception)
        {
            // Unparseable/empty AI output must not crash the rescue; surface a clean, safe reason.
            return new RescueClassification { Reason = ImportRescueRejectionReason.UnsupportedStructure };
        }
    }

    internal static List<MixedRowMarker> ParseMixedOutline(string response)
    {
        var markers = new List<MixedRowMarker>();
        try
        {
            var clean = CleanJsonResponse(response);
            using var doc = JsonDocument.Parse(clean);
            if (!doc.RootElement.TryGetProperty("markers", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return markers;

            foreach (var el in arr.EnumerateArray())
            {
                if (!el.TryGetProperty("row", out var rowEl) || rowEl.ValueKind != JsonValueKind.Number)
                    continue;
                var kindStr = GetString(el, "kind");
                if (!Enum.TryParse<MixedRowKind>(kindStr, ignoreCase: true, out var kind))
                    continue;
                markers.Add(new MixedRowMarker(rowEl.GetInt32(), kind, GetString(el, "text")));
            }
        }
        catch (Exception)
        {
            // A mid-parse exception must not surface a partial outline: return nothing so the
            // caller treats this the same as a fully unparseable response.
            return [];
        }
        return markers;
    }

    // Assigns every non-structural row to a (type, category) group using the outline markers.
    // Type comes from the nearest preceding section; category from the nearest preceding Category
    // header within that section (falling back to the section name). Structural rows (sections,
    // categories, subtotals) and any rows before the first section are excluded.
    internal static Dictionary<(SpreadsheetSheetType Type, string Category), List<int>> BucketMixedRows(
        int rowCount, IReadOnlyList<MixedRowMarker> markers)
    {
        // The outline is untrusted model output, so a repeated row index must not throw here;
        // last marker for a given row wins.
        var byIndex = markers.GroupBy(m => m.RowIndex).ToDictionary(g => g.Key, g => g.Last());
        var buckets = new Dictionary<(SpreadsheetSheetType, string), List<int>>();

        SpreadsheetSheetType? currentType = null;
        string? currentSection = null;
        string? currentCategory = null;

        for (int i = 0; i < rowCount; i++)
        {
            if (byIndex.TryGetValue(i, out var marker))
            {
                switch (marker.Kind)
                {
                    case MixedRowKind.IncomeSection:
                        currentType = SpreadsheetSheetType.Revenue;
                        currentSection = marker.Text;
                        currentCategory = null;
                        break;
                    case MixedRowKind.ExpenseSection:
                        currentType = SpreadsheetSheetType.Expenses;
                        currentSection = marker.Text;
                        currentCategory = null;
                        break;
                    case MixedRowKind.Category:
                        currentCategory = marker.Text;
                        break;
                    case MixedRowKind.Subtotal:
                        break; // skip; category context is unchanged
                }
                continue; // structural rows are never data
            }

            if (currentType is not { } type) continue; // before any section

            var category = !string.IsNullOrWhiteSpace(currentCategory)
                ? currentCategory
                : (currentSection ?? "Uncategorized");

            var key = (type, category);
            if (!buckets.TryGetValue(key, out var list))
                buckets[key] = list = [];
            list.Add(i);
        }

        return buckets;
    }

    private async Task<List<MixedRowMarker>> OutlineMixedReportAsync(
        List<List<string>> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are outlining a Profit and Loss Detail report. Identify only the structural rows:");
        sb.AppendLine("- IncomeSection: the header that begins the income section (e.g. \"Income\")");
        sb.AppendLine("- ExpenseSection: the header that begins the expenses section (e.g. \"Expenses\")");
        sb.AppendLine("- Category: a category/group header (e.g. \"Design income\", \"Advertising\")");
        sb.AppendLine("- Subtotal: a total/subtotal row (e.g. \"Total for ...\")");
        sb.AppendLine("Transaction rows are NOT structural; do not list them.");
        sb.AppendLine("Respond with JSON only: {\"markers\":[{\"row\":<index>,\"kind\":\"IncomeSection|ExpenseSection|Category|Subtotal\",\"text\":\"...\"}]}");
        sb.AppendLine();
        sb.AppendLine("Rows (index: first non-empty cell):");
        for (int i = 0; i < rows.Count; i++)
        {
            var first = rows[i].FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? "";
            sb.AppendLine($"{i}: {first}");
        }

        var response = await geminiService.SendChatAsync(
            "You outline the structural rows of a financial report.", sb.ToString(),
            maxTokens: 8000, temperature: 0.0, cancellationToken,
            operation: OperationKind.SpreadsheetAnalysis, sizeFeature: rows.Count);

        return string.IsNullOrEmpty(response) ? [] : ParseMixedOutline(response);
    }

    /// <summary>
    /// Extracts a mixed income/expense report into Revenue and Expense entities. Outlines the report
    /// structure, buckets rows into (type, category) groups, normalizes each group with the existing
    /// single-type extraction, and injects the report category onto every entity.
    /// </summary>
    public async Task<List<LlmProcessedData>> ExtractMixedAsync(
        List<string> headers,
        List<List<string>> rows,
        IProgress<(int processed, int total)>? progress,
        CancellationToken cancellationToken)
    {
        var markers = await OutlineMixedReportAsync(rows, cancellationToken);
        var buckets = BucketMixedRows(rows.Count, markers);
        if (buckets.Count == 0)
            return [];

        var results = new List<LlmProcessedData>();
        foreach (var ((type, category), rowIndices) in buckets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var groupRows = rowIndices.Select(i => rows[i]).ToList();
            if (groupRows.Count == 0) continue;

            var sheetAnalysis = new SheetAnalysis { SourceSheetName = category, DetectedType = type };
            var processed = await ProcessAllChunksAsync(
                headers, groupRows, sheetAnalysis, progress, cancellationToken, RescueChunkSize(headers.Count));

            foreach (var chunk in processed)
            {
                var withCategory = new LlmProcessedData
                {
                    EntityType = chunk.EntityType,
                    SourceRowsProcessed = chunk.SourceRowsProcessed,
                    FailedRows = chunk.FailedRows
                };
                foreach (var entity in chunk.Entities)
                    withCategory.Entities.Add(InjectCategoryName(entity, category));
                results.Add(withCategory);
            }
        }
        return results;
    }

    // Returns a copy of the entity JSON with categoryName set (rescue mixed-report grouping).
    private static JsonElement InjectCategoryName(JsonElement entity, string categoryName)
    {
        var node = JsonNode.Parse(entity.GetRawText());
        if (node is JsonObject obj)
        {
            obj["categoryName"] = categoryName;
            return JsonSerializer.SerializeToElement(obj);
        }
        return entity;
    }

    /// <summary>
    /// Backup import path: when normal analysis could not recognize a file, read every sheet raw and,
    /// per sheet, either extract records into a supported type or record a rejection reason. Reuses the
    /// Tier-2 extraction (ProcessAllChunksAsync) and returns an aggregate outcome for the whole file.
    /// </summary>
    public async Task<ImportRescueResult> RescueAsync(
        string filePath,
        bool isCsv,
        IProgress<(int processed, int total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        List<(string Name, List<string> Headers, List<List<string>> Rows)> sheets;
        try
        {
            sheets = ReadAllSheetsRaw(filePath, isCsv);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Deliberate UX choice: a read failure here (corrupt/locked/unreadable file) surfaces as a
            // friendly rejection instead of throwing and crashing the import flow. The error is still
            // logged via errorLogger so it's not silently swallowed.
            errorLogger?.LogError(ex, ErrorCategory.Import, "Rescue import: failed to read the file");
            return new ImportRescueResult
            {
                Outcome = ImportRescueOutcome.Rejected,
                ReasonCode = ImportRescueRejectionReason.EmptyOrUnreadable
            };
        }

        // Hard cap: past this many rows the rescue would mean too many AI calls / too long a wait.
        // Reject up front without making a single AI call so the user gets an instant, clear answer.
        var totalRows = sheets.Sum(s => s.Rows.Count);
        if (totalRows > RescueMaxTotalRows)
            return new ImportRescueResult
            {
                Outcome = ImportRescueOutcome.Rejected,
                ReasonCode = ImportRescueRejectionReason.TooLarge
            };

        // Report the total once up front so the UI can warn on large (but allowed) files before the AI runs.
        progress?.Report((0, totalRows));

        var perSheet = new List<RescueSheetResult>();
        foreach (var (name, headers, rows) in sheets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (headers.Count == 0 || rows.Count == 0)
            {
                perSheet.Add(new RescueSheetResult { SheetName = name, Reason = ImportRescueRejectionReason.EmptyOrUnreadable });
                continue;
            }

            var classification = await ClassifyOrRejectAsync(headers, rows, name, cancellationToken);

            if (classification.IsMixedIncomeExpense)
            {
                var mixed = await ExtractMixedAsync(headers, rows, progress, cancellationToken);
                var mixedCount = mixed.Sum(p => p.Entities.Count);
                perSheet.Add(mixedCount > 0
                    ? new RescueSheetResult { SheetName = name, ProcessedData = mixed }
                    : new RescueSheetResult { SheetName = name, Reason = ImportRescueRejectionReason.UnsupportedStructure });
                continue;
            }

            if (classification.EntityType is not { } type)
            {
                perSheet.Add(new RescueSheetResult
                {
                    SheetName = name,
                    Reason = classification.Reason ?? ImportRescueRejectionReason.UnsupportedStructure
                });
                continue;
            }

            var sheetAnalysis = new SheetAnalysis
            {
                SourceSheetName = name,
                DetectedType = type,
                Tier = ProcessingTier.Tier2_LlmProcessing
            };

            var processed = await ProcessAllChunksAsync(
                headers, rows, sheetAnalysis, progress, cancellationToken, RescueChunkSize(headers.Count));
            var entityCount = processed.Sum(p => p.Entities.Count);

            perSheet.Add(entityCount > 0
                ? new RescueSheetResult { SheetName = name, ProcessedData = processed }
                : new RescueSheetResult { SheetName = name, Reason = ImportRescueRejectionReason.UnsupportedStructure });
        }

        return ImportRescueResult.Aggregate(perSheet);
    }

    /// <summary>
    /// Reads headers + all data rows for every sheet (or the single CSV) without any analysis,
    /// for the rescue path. Mirrors the readers used by ProcessAllChunksAsync.
    /// </summary>
    private static List<(string Name, List<string> Headers, List<List<string>> Rows)> ReadAllSheetsRaw(
        string filePath, bool isCsv)
    {
        var result = new List<(string Name, List<string> Headers, List<List<string>> Rows)>();

        if (isCsv)
        {
            var rows = CsvReader.ReadAllRows(filePath, out var headers);
            var name = Path.GetFileNameWithoutExtension(filePath);
            result.Add((name, headers, rows));
            return result;
        }

        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(fileStream);
        foreach (var worksheet in workbook.Worksheets)
        {
            var headers = GetHeaders(worksheet);
            if (headers.Count == 0) continue;
            var rows = GetAllRowsAsStrings(worksheet, headers.Count);
            result.Add((worksheet.Name, headers, rows));
        }

        return result;
    }

    // Operational limits for the rescue path (time and number of AI calls), NOT model limits:
    // rows are processed in batches, so a larger file just means more calls.
    public const int RescueMaxTotalRows = 10_000;
    public const int RescueLargeFileWarnRows = 1_000;

    /// <summary>
    /// Batch size for rescue extraction. Wide sheets emit more JSON per row, so shrink the batch as the
    /// column count grows to keep an AI response from being truncated. Targets ~2,500 cells per batch,
    /// clamped to [20, 100]. A zero/negative column count falls back to the default 100.
    /// </summary>
    public static int RescueChunkSize(int columnCount)
    {
        if (columnCount <= 0) return 100;
        return Math.Clamp(2500 / columnCount, 20, 100);
    }

    #endregion
}
