using ArgoBooks.Core.Models.AI;
using ArgoBooks.Core.Services;

namespace ArgoBooks.Tests.Importer;

/// <summary>
/// Track A fake: returns a canned response whose key substring appears in the user prompt.
/// Deterministic and order/parallelism independent (matches by content, not call order).
/// </summary>
public sealed class ScriptedGeminiService(IReadOnlyDictionary<string, string> responsesByKey) : IGeminiService
{
    private readonly object _lock = new();

    public bool IsConfigured => true;
    public int CallCount { get; private set; }
    public List<string> UnmatchedPrompts { get; } = [];

    public Task<SupplierCategorySuggestion?> GetSupplierCategorySuggestionAsync(
        ReceiptAnalysisRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult<SupplierCategorySuggestion?>(null);

    public Task<List<BankLineSuggestion>?> GetBankLineSuggestionsAsync(
        BankLineCategorizationRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult<List<BankLineSuggestion>?>(null);

    public Task<string?> SendChatAsync(
        string systemPrompt, string userPrompt,
        int maxTokens = 4000, double temperature = 0.1,
        CancellationToken cancellationToken = default,
        OperationKind operation = OperationKind.Completion,
        long? sizeFeature = null)
    {
        lock (_lock)
        {
            CallCount++;
            foreach (var (key, value) in responsesByKey)
            {
                if (userPrompt.Contains(key, StringComparison.Ordinal))
                    return Task.FromResult<string?>(value);
            }
            UnmatchedPrompts.Add(userPrompt);
            return Task.FromResult<string?>(null);
        }
    }

    public Task<string?> SendVisionChatAsync(
        string systemPrompt, string userPrompt, string base64Image, string mimeType,
        int maxTokens = 4000, double temperature = 0.1, string? model = null,
        CancellationToken cancellationToken = default,
        OperationKind operation = OperationKind.ReceiptScan)
        => Task.FromResult<string?>(null);
}
