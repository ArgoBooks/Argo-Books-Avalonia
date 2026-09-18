namespace ArgoBooks.Core.Services;

/// <summary>
/// Service interface for Gemini API interactions.
/// </summary>
public interface IGeminiService
{
    /// <summary>
    /// Whether the service is configured with valid API credentials.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Sends a generic chat completion request to the Gemini API.
    /// </summary>
    /// <param name="systemPrompt">The system prompt.</param>
    /// <param name="userPrompt">The user prompt.</param>
    /// <param name="maxTokens">Maximum tokens in the response.</param>
    /// <param name="temperature">Sampling temperature (0.0 = deterministic, 1.0 = creative).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The model's response text, or null on failure.</returns>
    Task<string?> SendChatAsync(
        string systemPrompt,
        string userPrompt,
        int maxTokens = 4000,
        double temperature = 0.1,
        CancellationToken cancellationToken = default,
        OperationKind operation = OperationKind.Completion,
        long? sizeFeature = null);

    /// <summary>
    /// <see cref="SendChatAsync"/>, also saying whether a null reply was the Argo server's own
    /// rate limit. A caller that retries failures must not retry that one: every retry is
    /// another request against the same exhausted budget.
    /// </summary>
    async Task<AiChatResult> SendChatWithStatusAsync(
        string systemPrompt,
        string userPrompt,
        int maxTokens = 4000,
        double temperature = 0.1,
        CancellationToken cancellationToken = default,
        OperationKind operation = OperationKind.Completion,
        long? sizeFeature = null)
        => new(await SendChatAsync(systemPrompt, userPrompt, maxTokens, temperature, cancellationToken, operation, sizeFeature), false);
}

/// <summary>A chat reply, or null, and whether the null was a rate limit.</summary>
public readonly record struct AiChatResult(string? Content, bool RateLimited);
