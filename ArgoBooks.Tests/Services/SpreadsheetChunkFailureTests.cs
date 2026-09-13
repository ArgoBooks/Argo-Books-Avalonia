using System.Text.Json;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tier 2 AI import sends a sheet to the model 100 rows at a time. A chunk whose call fails (rate
/// limit, timeout, unreadable reply) must not make its rows vanish from the import.
/// </summary>
public class SpreadsheetChunkFailureTests
{
    private static readonly List<string> Headers = ["Id", "Name"];

    private static readonly List<List<string>> Rows =
    [
        ["CUS-1", "Alpha"],
        ["CUS-2", "Beta"],
        ["CUS-3", "Gamma"],
        ["CUS-4", "Delta"]
    ];

    private static readonly SheetAnalysis CustomersSheet = new()
    {
        SourceSheetName = "Customers",
        DetectedType = SpreadsheetSheetType.Customers,
        Tier = ProcessingTier.Tier2_LlmProcessing
    };

    /// <summary>
    /// Answers each chunk with the customers whose names appear in its prompt. A chunk containing
    /// <see cref="FailingName"/> gets no answer for its first <see cref="FailuresBeforeSuccess"/> calls.
    /// </summary>
    private sealed class ChunkGemini : IGeminiService
    {
        private readonly object _lock = new();
        private int _failuresSoFar;

        public required string FailingName { get; init; }
        public int FailuresBeforeSuccess { get; init; } = int.MaxValue;

        /// <summary>Fail the way an HttpClient timeout does, instead of with no reply.</summary>
        public bool TimeOut { get; init; }

        public bool IsConfigured => true;

        public Task<string?> SendChatAsync(
            string systemPrompt, string userPrompt,
            int maxTokens = 4000, double temperature = 0.1,
            CancellationToken cancellationToken = default,
            OperationKind operation = OperationKind.Completion,
            long? sizeFeature = null)
        {
            if (userPrompt.Contains(FailingName, StringComparison.Ordinal))
            {
                lock (_lock)
                {
                    if (_failuresSoFar < FailuresBeforeSuccess)
                    {
                        _failuresSoFar++;
                        return TimeOut
                            ? Task.FromException<string?>(new TaskCanceledException("timed out", new TimeoutException()))
                            : Task.FromResult<string?>(null);
                    }
                }
            }

            var customers = Rows
                .Where(r => userPrompt.Contains(r[1], StringComparison.Ordinal))
                .Select(r => new { id = r[0], name = r[1] });
            return Task.FromResult<string?>(JsonSerializer.Serialize(customers));
        }
    }

    private static async Task<SheetImportResult> ImportAsync(SpreadsheetAnalysisService analysis)
    {
        analysis.ChunkRetryDelay = TimeSpan.Zero;
        var processed = await analysis.ProcessAllChunksAsync(Headers, Rows, CustomersSheet, chunkSize: 2);
        return new SpreadsheetImportService().ImportProcessedEntities(
            new CompanyData(), processed, CustomersSheet.SourceSheetName);
    }

    [Fact]
    public async Task ChunkThatKeepsFailing_ItsRowsAreReportedAsNotImported()
    {
        var analysis = new SpreadsheetAnalysisService(new ChunkGemini { FailingName = "Gamma" });

        var result = await ImportAsync(analysis);

        Assert.Equal(2, result.Inserted);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(2, result.UnimportedRows.Count);
        Assert.Contains(result.UnimportedRows, r => r.RawValue!.Contains("Gamma"));
        Assert.Contains(result.UnimportedRows, r => r.RawValue!.Contains("Delta"));
    }

    [Fact]
    public async Task ChunkThatFailsOnce_IsRetriedAndImported()
    {
        var analysis = new SpreadsheetAnalysisService(new ChunkGemini { FailingName = "Gamma", FailuresBeforeSuccess = 1 });

        var result = await ImportAsync(analysis);

        Assert.Equal(4, result.Inserted);
        Assert.Empty(result.UnimportedRows);
    }

    // HttpClient reports its own timeout as a cancellation. One slow chunk must not end the whole
    // import as though the user had pressed Cancel.
    [Fact]
    public async Task ChunkThatTimesOutOnce_IsRetriedAndImported()
    {
        var analysis = new SpreadsheetAnalysisService(new ChunkGemini { FailingName = "Gamma", FailuresBeforeSuccess = 1, TimeOut = true });

        var result = await ImportAsync(analysis);

        Assert.Equal(4, result.Inserted);
    }
}
