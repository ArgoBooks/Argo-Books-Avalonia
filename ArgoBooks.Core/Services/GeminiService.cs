using System.Diagnostics;
using System.Net.Http.Headers;
using ArgoBooks.Core.Models.AI;

using ArgoBooks.Core.Models.Telemetry;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Gemini API service for AI-powered supplier and category suggestions.
/// Routes requests through the argorobots.com server proxy.
/// </summary>
public class GeminiService : IGeminiService, IDisposable
{
    private const string DefaultModel = "gemini-2.5-flash";
    private readonly HttpClient _httpClient;
    private readonly AiProxyClient _proxy;
    private readonly IErrorLogger? _errorLogger;
    private readonly ITelemetryManager? _telemetryManager;

    /// <summary>
    /// Creates a new instance of the Gemini service.
    /// </summary>
    public GeminiService(IErrorLogger? errorLogger = null, ITelemetryManager? telemetryManager = null)
    {
        _httpClient = new HttpClient();
        _proxy = new AiProxyClient(_httpClient, ApiConfig.BaseUrl, new LicenseApiAuth());
        _errorLogger = errorLogger;
        _telemetryManager = telemetryManager;
        ConfigureHttpClient();
    }

    /// <inheritdoc />
    public bool IsConfigured => LicenseAuthHelper.IsConfigured;

    /// <inheritdoc />
    public async Task<SupplierCategorySuggestion?> GetSupplierCategorySuggestionAsync(
        ReceiptAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return null;

        var stopwatch = Stopwatch.StartNew();
        var model = DefaultModel;
        var success = false;

        try
        {
            var prompt = BuildPrompt(request);
            // gemini-2.5-flash spends hidden "thinking" tokens out of maxOutputTokens; 500 was too
            // small and the budget was exhausted before the JSON answer (finishReason=MAX_TOKENS,
            // truncated content), so the suggestion silently failed. The output itself is tiny, so a
            // generous budget just covers thinking (billing is per token used, not the ceiling).
            var response = await SendApiRequestAsync(
                "You are a helpful assistant that categorizes business expenses. Always respond with valid JSON only, no markdown.",
                prompt,
                4000,
                0.3,
                cancellationToken: cancellationToken,
                operation: OperationKind.SupplierCategory);

            if (string.IsNullOrEmpty(response))
                return null;

            success = true;
            return ParseResponse(response, request);
        }
        catch (Exception ex)
        {
            _errorLogger?.LogError(ex, ErrorCategory.Api, "Gemini API call failed");
            return null;
        }
        finally
        {
            stopwatch.Stop();
            _ = _telemetryManager?.TrackApiCallAsync(
                ApiName.Gemini,
                stopwatch.ElapsedMilliseconds,
                success,
                model,
                cancellationToken: cancellationToken);
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// Max statement lines per AI request. One request with every line can exceed the model's
    /// output budget (finishReason=MAX_TOKENS -> empty response), so large statements are split
    /// into batches whose results are merged by line index. 40 keeps each batch's budget
    /// (4000 + 40*250 = 14000) comfortably under the 16000 cap including hidden "thinking" tokens.
    /// </summary>
    private const int BankLineBatchSize = 40;

    public async Task<List<BankLineSuggestion>?> GetBankLineSuggestionsAsync(
        BankLineCategorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || request.Lines.Count == 0)
            return null;

        if (request.Lines.Count <= BankLineBatchSize)
            return await GetBankLineSuggestionsBatchAsync(request, request.Lines, cancellationToken);

        // Split a large statement into batches and merge. Suggestions carry the original line
        // Index, so a plain concat maps back correctly. A failed batch leaves its lines blank
        // (the user fills them in manually); return whatever the successful batches produced.
        var merged = new List<BankLineSuggestion>();
        var anySucceeded = false;
        for (int i = 0; i < request.Lines.Count; i += BankLineBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = request.Lines.GetRange(i, Math.Min(BankLineBatchSize, request.Lines.Count - i));
            var part = await GetBankLineSuggestionsBatchAsync(request, batch, cancellationToken);
            if (part != null)
            {
                merged.AddRange(part);
                anySucceeded = true;
            }
        }
        return anySucceeded ? merged : null;
    }

    /// <summary>
    /// Categorizes a single batch of lines, reusing <paramref name="baseRequest"/>'s context
    /// (products/categories/counterparties) with just <paramref name="lines"/> as the work set.
    /// </summary>
    private async Task<List<BankLineSuggestion>?> GetBankLineSuggestionsBatchAsync(
        BankLineCategorizationRequest baseRequest,
        List<BankLineToCategorize> lines,
        CancellationToken cancellationToken)
    {
        var batchRequest = new BankLineCategorizationRequest
        {
            Lines = lines,
            ExistingProducts = baseRequest.ExistingProducts,
            ExistingExpenseCategories = baseRequest.ExistingExpenseCategories,
            ExistingRevenueCategories = baseRequest.ExistingRevenueCategories,
            ExistingSuppliers = baseRequest.ExistingSuppliers,
            ExistingCustomers = baseRequest.ExistingCustomers,
        };

        var stopwatch = Stopwatch.StartNew();
        var model = DefaultModel;
        var success = false;

        try
        {
            var prompt = BuildBankLinePrompt(batchRequest);
            // gemini-2.5-flash spends hidden "thinking" tokens out of maxOutputTokens, so the
            // budget must comfortably cover thinking plus the JSON output or the response comes
            // back empty (finishReason=MAX_TOKENS). Be generous; the model only uses what it needs.
            var maxTokens = Math.Min(16000, 4000 + lines.Count * 250);
            var response = await SendApiRequestAsync(
                "You categorize business bank statement lines. Always respond with valid JSON only, no markdown.",
                prompt,
                maxTokens,
                0.2,
                cancellationToken: cancellationToken,
                operation: OperationKind.BankCategorize,
                sizeFeature: lines.Count);

            if (string.IsNullOrEmpty(response))
                return null;

            success = true;
            return ParseBankLineResponse(response);
        }
        catch (Exception ex)
        {
            _errorLogger?.LogError(ex, ErrorCategory.Api, "Gemini bank-line categorization failed");
            return null;
        }
        finally
        {
            stopwatch.Stop();
            _ = _telemetryManager?.TrackApiCallAsync(
                ApiName.Gemini, stopwatch.ElapsedMilliseconds, success, model, cancellationToken: cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<string?> SendChatAsync(
        string systemPrompt,
        string userPrompt,
        int maxTokens = 4000,
        double temperature = 0.1,
        CancellationToken cancellationToken = default,
        OperationKind operation = OperationKind.Completion,
        long? sizeFeature = null)
    {
        if (!IsConfigured)
            return null;

        var stopwatch = Stopwatch.StartNew();
        var model = DefaultModel;
        var success = false;

        try
        {
            var response = await SendApiRequestAsync(systemPrompt, userPrompt, maxTokens, temperature, cancellationToken: cancellationToken, operation: operation, sizeFeature: sizeFeature);
            if (!string.IsNullOrEmpty(response))
                success = true;
            return response;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _errorLogger?.LogError(ex, ErrorCategory.Api, "Gemini API call failed");
            return null;
        }
        finally
        {
            stopwatch.Stop();
            _ = _telemetryManager?.TrackApiCallAsync(
                ApiName.Gemini,
                stopwatch.ElapsedMilliseconds,
                success,
                model,
                cancellationToken: cancellationToken);
        }
    }

    private void ConfigureHttpClient()
    {
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static string BuildPrompt(ReceiptAnalysisRequest request)
    {
        var suppliersJson = JsonSerializer.Serialize(request.ExistingSuppliers.Select(s => new { s.Id, s.Name }));
        var categoriesJson = JsonSerializer.Serialize(request.ExistingCategories.Select(c => new { c.Id, c.Name, c.Description }));
        var lineItemsText = request.LineItemDescriptions.Count > 0
            ? string.Join(", ", request.LineItemDescriptions.Take(10))
            : "N/A";

        return $@"You are an AI assistant helping categorize business expenses. Analyze the receipt data and suggest the best matching supplier and category.

## Receipt Data
- Supplier Name: ""{request.SupplierName}""
- Line Items: {lineItemsText}
- Total Amount: {request.TotalAmount:F2}

## Existing Suppliers
{suppliersJson}

## Existing Categories (Expense type)
{categoriesJson}

## Instructions
1. SUPPLIER: Find the best matching supplier from the existing list. Consider:
   - Exact name matches (highest confidence)
   - Partial matches (e.g., ""Walmart"" matches ""Walmart Inc."")
   - Common abbreviations and variations
   - If no good match exists (confidence < 0.6), set shouldCreateNew=true and suggest a clean supplier name

2. CATEGORY: Find the best matching category based on:
   - Line item descriptions (most important, use these to determine what was actually purchased)
   - What the supplier typically sells
   - Common business expense categories
   - If no good match exists (confidence < 0.6), set shouldCreateNew=true and suggest a SPECIFIC category name
   - IMPORTANT: Be specific! Use descriptive names based on the actual items (e.g., ""Groceries"", ""Cooking Ingredients"", ""Office Supplies"", ""Cleaning Products""). NEVER use vague or generic names like ""Purchases"", ""General"", ""General Expenses"", ""Miscellaneous"", ""Expenses"", or any combination of these words

## Response Format (JSON only, no markdown code blocks)
{{
  ""supplier"": {{
    ""matchedId"": ""<supplier-id or null>"",
    ""matchedName"": ""<supplier-name or null>"",
    ""confidence"": <0.0-1.0>,
    ""shouldCreateNew"": <true/false>,
    ""newName"": ""<suggested name if shouldCreateNew>"",
    ""newNotes"": ""<optional notes>""
  }},
  ""category"": {{
    ""matchedId"": ""<category-id or null>"",
    ""matchedName"": ""<category-name or null>"",
    ""confidence"": <0.0-1.0>,
    ""shouldCreateNew"": <true/false>,
    ""newName"": ""<suggested name if shouldCreateNew>"",
    ""newDescription"": ""<optional description>""
  }}
}}

Respond with JSON only.";
    }

    private async Task<string?> SendApiRequestAsync(
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        double temperature,
        CancellationToken cancellationToken,
        OperationKind operation,
        long? sizeFeature = null)
    {
        var response = await _proxy.SendAsync(new AiProxyRequest
        {
            Operation = operation.ToServerTag(),
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt,
            Model = DefaultModel,
            MaxTokens = maxTokens,
            Temperature = temperature,
            SizeFeature = sizeFeature,
        }, cancellationToken);

        if (!response.IsHttpSuccess)
        {
            _errorLogger?.LogError(
                $"AI proxy error {response.StatusCode} ({response.Code ?? "no code"}): {response.Message ?? "no message"}",
                ErrorCategory.Api, "AI chat completion");
            return null;
        }

        // Feeds the server-measured time into the shared timing service so progress estimates
        // self-calibrate; the wall clock minus server time trains the upload-speed estimate.
        if (response.Timing is { } timing)
        {
            OperationTimingService.Instance?.RecordResult(
                operation, timing.ServerMs, response.WallClockMs, response.UploadBytes, timing.LoadFactor);
        }

        if (!response.Succeeded)
        {
            if (response.Message != null || response.Code != null)
            {
                _errorLogger?.LogError(
                    $"AI proxy returned success=false ({response.Code ?? "no code"}): {response.Message ?? "no message"}",
                    ErrorCategory.Api, "AI chat completion");
            }
            return null;
        }

        return response.Content;
    }

    private SupplierCategorySuggestion? ParseResponse(string response, ReceiptAnalysisRequest request)
    {
        try
        {
            var cleanResponse = JsonResponseHelper.StripMarkdownCodeBlock(response);

            using var doc = JsonDocument.Parse(cleanResponse);
            var root = doc.RootElement;

            var result = new SupplierCategorySuggestion();

            // Parse supplier
            if (root.TryGetProperty("supplier", out var supplier))
            {
                if (supplier.TryGetProperty("matchedId", out var matchedId) && matchedId.ValueKind != JsonValueKind.Null)
                {
                    result.MatchedSupplierId = matchedId.GetString();
                }

                if (supplier.TryGetProperty("matchedName", out var matchedName) && matchedName.ValueKind != JsonValueKind.Null)
                {
                    result.MatchedSupplierName = matchedName.GetString();
                }

                if (supplier.TryGetProperty("confidence", out var confidence))
                {
                    result.SupplierConfidence = confidence.GetDouble();
                }

                if (supplier.TryGetProperty("shouldCreateNew", out var shouldCreateNew))
                {
                    result.ShouldCreateNewSupplier = shouldCreateNew.GetBoolean();
                }

                if (result.ShouldCreateNewSupplier)
                {
                    result.NewSupplier = new NewSupplierSuggestion();

                    if (supplier.TryGetProperty("newName", out var newName) && newName.ValueKind != JsonValueKind.Null)
                    {
                        result.NewSupplier.Name = newName.GetString() ?? request.SupplierName;
                    }
                    else
                    {
                        result.NewSupplier.Name = request.SupplierName;
                    }

                    if (supplier.TryGetProperty("newNotes", out var newNotes) && newNotes.ValueKind != JsonValueKind.Null)
                    {
                        result.NewSupplier.Notes = newNotes.GetString();
                    }
                }
            }

            // Parse category
            if (root.TryGetProperty("category", out var category))
            {
                if (category.TryGetProperty("matchedId", out var matchedId) && matchedId.ValueKind != JsonValueKind.Null)
                {
                    result.MatchedCategoryId = matchedId.GetString();
                }

                if (category.TryGetProperty("matchedName", out var matchedName) && matchedName.ValueKind != JsonValueKind.Null)
                {
                    result.MatchedCategoryName = matchedName.GetString();
                }

                if (category.TryGetProperty("confidence", out var confidence))
                {
                    result.CategoryConfidence = confidence.GetDouble();
                }

                if (category.TryGetProperty("shouldCreateNew", out var shouldCreateNew))
                {
                    result.ShouldCreateNewCategory = shouldCreateNew.GetBoolean();
                }

                if (result.ShouldCreateNewCategory)
                {
                    result.NewCategory = new NewCategorySuggestion();

                    var suggestedName = "General";
                    if (category.TryGetProperty("newName", out var newName) && newName.ValueKind != JsonValueKind.Null)
                    {
                        suggestedName = newName.GetString() ?? "General";
                    }

                    // Reject vague category names, the AI sometimes suggests these
                    var vagueName = IsVagueCategoryName(suggestedName);
                    if (vagueName && category.TryGetProperty("newDescription", out var descFallback)
                        && descFallback.ValueKind != JsonValueKind.Null
                        && !string.IsNullOrWhiteSpace(descFallback.GetString()))
                    {
                        // Use the description as the name if it's more specific
                        var desc = descFallback.GetString()!;
                        if (!IsVagueCategoryName(desc) && desc.Length <= 40)
                            suggestedName = desc;
                    }

                    result.NewCategory.Name = suggestedName;

                    if (category.TryGetProperty("newDescription", out var newDesc) && newDesc.ValueKind != JsonValueKind.Null)
                    {
                        result.NewCategory.Description = newDesc.GetString();
                    }

                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _errorLogger?.LogError(ex, ErrorCategory.Parsing, "Failed to parse Gemini response");
            return null;
        }
    }

    private static bool IsVagueCategoryName(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        var vagueExact = new[] { "purchases", "general", "miscellaneous", "expenses", "other", "various", "items", "goods" };
        if (vagueExact.Contains(normalized))
            return true;

        // Catch compound vague names like "general expenses", "other purchases", "miscellaneous items"
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 1 && words.All(w => vagueExact.Contains(w));
    }

    private static string BuildBankLinePrompt(BankLineCategorizationRequest request)
    {
        var products = JsonSerializer.Serialize(request.ExistingProducts.Select(p => new { p.Id, p.Name, category = p.CategoryName, type = p.IsRevenue ? "revenue" : "expense" }));
        var expenseCats = JsonSerializer.Serialize(request.ExistingExpenseCategories.Select(c => new { c.Id, c.Name }));
        var revenueCats = JsonSerializer.Serialize(request.ExistingRevenueCategories.Select(c => new { c.Id, c.Name }));
        var suppliers = JsonSerializer.Serialize(request.ExistingSuppliers.Select(s => new { s.Id, s.Name }));
        var customers = JsonSerializer.Serialize(request.ExistingCustomers.Select(c => new { c.Id, c.Name }));
        var lines = JsonSerializer.Serialize(request.Lines.Select(l => new { l.Index, l.Description, amount = l.Amount, type = l.IsRevenue ? "revenue" : "expense" }));

        return $@"Categorize each bank statement line. For every line assign a PRODUCT (which carries its category) and a SUPPLIER (expense lines) or CUSTOMER (revenue lines).

## Lines
{lines}

## Existing Products
{products}

## Existing Expense Categories
{expenseCats}

## Existing Revenue Categories
{revenueCats}

## Existing Suppliers (for expense lines)
{suppliers}

## Existing Customers (for revenue lines)
{customers}

## Rules
For each line:
1. PRODUCT: prefer an existing product of the matching type and return its id in ""productId"". Otherwise set ""productId"" to null and propose a short, specific ""newProductName"" describing the spend (e.g. ""Paint Supplies"", ""Fuel"", ""Bank Fees"", ""Consulting Revenue""). NEVER use vague names like ""Purchases"", ""General"", ""Miscellaneous"", ""Expenses"", or ""Other"".
2. CATEGORY (only when proposing a new product): prefer an existing category of the matching type and return its id in ""categoryId"". Otherwise set ""categoryId"" to null and propose a broader ""newCategoryName"" (e.g. ""Materials"", ""Utilities"", ""Sales""). Avoid vague names.
3. COUNTERPARTY: for expense lines pick a supplier, for revenue lines pick a customer. If a good match exists return its id in ""counterpartyId"". Otherwise set ""counterpartyId"" to null and propose a clean ""newCounterpartyName"" from the description. If the description has no identifiable party (e.g. ""ATM WITHDRAWAL"", ""MONTHLY FEE"", ""INTERAC E-TRANSFER""), set both counterparty fields to null.

## Response Format (JSON array only, no markdown)
[
  {{ ""index"": <line index>, ""productId"": ""<id or null>"", ""newProductName"": ""<name or null>"", ""categoryId"": ""<id or null>"", ""newCategoryName"": ""<name or null>"", ""counterpartyId"": ""<id or null>"", ""newCounterpartyName"": ""<name or null>"" }}
]

Respond with the JSON array only.";
    }

    private List<BankLineSuggestion>? ParseBankLineResponse(string response)
    {
        try
        {
            var clean = JsonResponseHelper.StripMarkdownCodeBlock(response);
            using var doc = JsonDocument.Parse(clean);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
                return null;

            var results = new List<BankLineSuggestion>();
            foreach (var el in root.EnumerateArray())
            {
                var s = new BankLineSuggestion
                {
                    Index = GetJsonInt(el, "index"),
                    ProductId = GetJsonString(el, "productId"),
                    NewProductName = GetJsonString(el, "newProductName"),
                    ProductCategoryId = GetJsonString(el, "categoryId"),
                    NewProductCategoryName = GetJsonString(el, "newCategoryName"),
                    CounterpartyId = GetJsonString(el, "counterpartyId"),
                    NewCounterpartyName = GetJsonString(el, "newCounterpartyName")
                };

                // Reject vague AI-proposed names so we never create a "General" product/category.
                if (s.NewProductName != null && IsVagueCategoryName(s.NewProductName)) s.NewProductName = null;
                if (s.NewProductCategoryName != null && IsVagueCategoryName(s.NewProductCategoryName)) s.NewProductCategoryName = null;

                results.Add(s);
            }
            return results;
        }
        catch (Exception ex)
        {
            _errorLogger?.LogError(ex, ErrorCategory.Parsing, "Failed to parse bank-line suggestions");
            return null;
        }
    }

    private static string? GetJsonString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.GetString())
            ? p.GetString()
            : null;

    private static int GetJsonInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p)) return -1;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v)) return v;
        if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var sv)) return sv;
        return -1;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
