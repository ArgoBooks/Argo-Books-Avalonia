using System.Diagnostics;
using System.Text.Json;
using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Shared.Telemetry;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Receipt scanning service using Gemini 2.5 Flash vision through the argorobots.com proxy.
/// Lives in <c>ArgoBooks.Shared</c> so both the desktop and the mobile app can reuse the exact
/// same scan/parse logic. Talks to <c>{baseUrl}/api/ai/completions.php</c> directly (rather than
/// going through the desktop-only <c>GeminiService</c>, which also carries bank-categorization and
/// supplier-suggestion features the phone doesn't need). Auth and telemetry are both seams: the
/// desktop passes <c>LicenseApiAuth</c> (wrapping <c>LicenseAuthHelper</c>) and its real
/// <c>ITelemetryManager</c>; the mobile app passes its own device-token auth adapter and null
/// telemetry (both optional).
/// </summary>
public class GeminiReceiptScannerService(
    string baseUrl,
    IApiAuth? apiAuth = null,
    IErrorLogger? errorLogger = null,
    ITelemetryManager? telemetryManager = null,
    HttpClient? httpClient = null,
    Action<double, double, long, double?>? onTimingRecorded = null)
    : IReceiptScannerService, IDisposable
{
    /// <summary>
    /// Receipt extraction is the slowest call the app makes: a vision model reading a full
    /// photo, occasionally followed by a second verification pass. Observed successful scans
    /// reach 85 seconds, so HttpClient's 100-second default sat barely above the working
    /// range and gave up on calls that would have completed. Every other client in the app
    /// picks its own timeout.
    /// </summary>
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(180);

    private readonly HttpClient _httpClient = httpClient ?? new HttpClient { Timeout = ScanTimeout };
    private readonly bool _ownsHttpClient = httpClient is null;

    private const string DefaultModel = "gemini-2.5-flash";

    private const string SystemPrompt = @"You are a receipt data extraction system. You must extract EVERY item and ALL data from the receipt image into structured JSON. Be thorough, missing items is unacceptable.

Return JSON only (no markdown code blocks), with this exact format:
{
  ""supplierName"": ""Store or business name"",
  ""transactionDate"": ""YYYY-MM-DD"",
  ""subtotal"": 0.00,
  ""taxes"": [{""name"": ""GST"", ""amount"": 0.00}, {""name"": ""PST"", ""amount"": 0.00}],
  ""discounts"": [{""name"": ""Member Discount"", ""amount"": 0.00}],
  ""shipping"": 0.00,
  ""totalAmount"": 0.00,
  ""currencyCode"": ""USD"",
  ""paymentMethod"": ""Credit Card"",
  ""confidence"": 0.95,
  ""lineItems"": [
    {""description"": ""Product Name"", ""quantity"": 1, ""unitPrice"": 0.00, ""totalPrice"": 0.00, ""confidence"": 0.9}
  ]
}

Rules:
1. LINE ITEMS: Extract EVERY purchased item on the receipt. Scan the entire receipt top to bottom. Grocery receipts often have 20-40+ items, include ALL of them. Do not summarize or skip items. Each product line with a price is a line item. Return items in the same order they appear on the receipt.
2. TAX: Return EACH tax line separately in the ""taxes"" array. Do NOT sum them, list every individual tax with its label and amount. Common tax labels: GST, G-GST, PST, P-PST, HST, QST, TVQ, TPS, VAT, state tax, county tax, city tax, sales tax, excise tax. If there is only one tax line, still return it as a single-element array.
3. PRODUCT NAMES: Transcribe EXACTLY as printed on the receipt, character by character. Do NOT normalize, expand abbreviations, correct spelling, or rename items. Keep the original abbreviations and casing. If a character is hard to read, use your best guess but do not substitute a different word. ALWAYS remove SKU codes, barcodes, and internal item numbers that are not part of the product name, especially a leading code printed before the name such as ""6010-0272-0259-0062 Co Palm Refill"" (extract just ""Co Palm Refill"") or a long leading digit string. The description must start with the product name, never with a code.
4. MONETARY VALUES: All as numbers. Use 0.00 for missing values, null for unknown fields.
5. CONFIDENCE: Both the overall ""confidence"" and each line item's ""confidence"" must be 0.0-1.0. Be STRICT and CONSERVATIVE with line item confidence: if the text is blurry, smudged, faded, partially obscured, wrinkled, or if ANY digit or character in the description or price required guessing, the confidence MUST be below 0.85. Use 0.5-0.7 for items where you are genuinely unsure about the price or name. Only use 0.9+ when the text is crisp and completely unambiguous. Do NOT default to high confidence, earn it.
6. PRICES vs DISCOUNTS: When a product has two numbers near it (a price and a discount/savings below it), the product's line item should use the FULL PRICE (the larger, positive number), not the discounted price. The discount is a separate entry in the ""discounts"" array.
7. DISCOUNTS: ANY line on the receipt with a negative amount or a minus sign is a discount. This includes lines labeled ""Member Pricing"", ""Member Discount"", ""SAVE"", ""OFF"", ""DISCOUNT"", coupons, promos, loyalty savings, price reductions, markdowns, or any other negative adjustment. Return EACH one separately in the ""discounts"" array with the label and amount as a positive number. Do NOT include discounts as line items. They belong only in the ""discounts"" array. Do NOT skip or ignore negative amounts.
7b. SHIPPING: If the receipt has a shipping, delivery, freight, or postage charge, put its amount (a positive number) in the ""shipping"" field. This is a separate cost added to the total, NOT a line item and NOT a tax or discount. Use 0.00 if there is no shipping charge.
8. ERROR: If the image is not a receipt or is completely unreadable, return: {""error"": ""Not a valid receipt"", ""confidence"": 0.0}
9. DATE: YYYY-MM-DD format. Best guess if only partial date is visible.
10. CURRENCY: Infer the currency from location clues on the receipt: store address, city, province/state, country name, language, tax labels (e.g. GST/PST = CAD, VAT/TVA = EUR/GBP, IVA = EUR/MXN), and currency symbols ($ is ambiguous, £ = GBP, € = EUR, ¥ = JPY/CNY). Map the identified country to its ISO 4217 currency code. Default to ""USD"" only if there are genuinely no location or currency clues.
11. PAYMENT METHOD: One of ""Credit Card"", ""Debit Card"", ""Cash"", ""Check"", or null. ""MASTERCARD"", ""VISA"", ""AMEX"" = ""Credit Card"". ""INTERAC"", ""DEBIT"" = ""Debit Card"".
12. QUANTITY: Default to 1. For weighted/per-unit items (e.g. ""1.340 kg @ $1.92/kg  2.57""), the rate line is NOT a separate line item. Use the FINAL COMPUTED PRICE on the right (2.57) as both unitPrice and totalPrice, and set quantity to 1. Ignore the per-unit rate and weight, the user only cares about the amount paid. These rate lines often contain ""@"", ""/"", ""kg"", ""lb"", ""per"", or appear indented below the product name.
13. SUPPLIER - This is often the largest and boldest text on the receipt, and usually at the very top.
14. SPATIAL ALIGNMENT: Grocery receipts use a two-column layout: product name on the LEFT, its price on the RIGHT of the SAME row. Match each product name to the price that is horizontally aligned with it, NOT the price on the row above or below. Characters on the same printed line share the same vertical position even if there is a large horizontal gap between the name and the price. If a line has only a name with no price on its right, it is likely a description or category header, do not assign it a price from an adjacent row. IMPORTANT: The receipt photo may be tilted or at an angle. Mentally straighten the image first, then read each row. Two items at the same vertical position on a tilted receipt will appear at slightly different heights in the photo, follow the angle of the printed text lines, not strict horizontal.
15. CROSS-CHECK: After extracting all items, count the number of distinct price values visible on the right side of the receipt and compare to the number of line items you extracted. If you have fewer line items than prices, you missed an item, re-scan. Every price on the receipt must be accounted for as either a line item, a tax, a discount, or a total/subtotal.
16. DIGIT ACCURACY: Pay close attention to easily confused digits: 3↔8, 5↔6, 1↔7, 0↔6, swapped digits. When uncertain, look at the digit shape carefully before committing to a value.";

    /// <inheritdoc />
    public bool IsConfigured => apiAuth?.IsConfigured ?? false;

    /// <inheritdoc />
    public async Task<ReceiptScanResult> ScanReceiptAsync(byte[] imageData, string fileName, CancellationToken cancellationToken = default)
        => await ScanReceiptAsync(imageData, fileName, skipPreprocessing: false, cancellationToken);

    /// <summary>
    /// Scans a receipt image. Set <paramref name="skipPreprocessing"/> to true if the caller
    /// has already run <see cref="ReceiptImageHelper.PreprocessForOcr"/> on the image data.
    /// </summary>
    public async Task<ReceiptScanResult> ScanReceiptAsync(byte[] imageData, string fileName, bool skipPreprocessing, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var success = false;

        try
        {
            if (!IsConfigured)
            {
                return ReceiptScanResult.Failed("No active license key or device ID found.");
            }

            if (!skipPreprocessing)
            {
                // Preprocess image to improve OCR accuracy (contrast, sharpen). PDFs pass
                // through unchanged, and so does anything Skia cannot decode: HEIC from an
                // iPhone arrives here as its original bytes. Rename only on a real
                // conversion, or the content type below would describe the wrong format.
                imageData = ReceiptImageHelper.PreprocessForOcr(imageData, fileName, out var convertedToJpeg);
                if (convertedToJpeg)
                    fileName = Path.ChangeExtension(fileName, ".jpg");
            }

            // Validate file type
            var mimeType = ReceiptImageHelper.GetContentType(fileName);
            if (mimeType == null)
            {
                return ReceiptScanResult.Failed("Unsupported file type. Please use JPEG, PNG, WebP, HEIC, BMP, or PDF files.");
            }

            // Convert to base64 for vision API
            var base64Image = Convert.ToBase64String(imageData);

            // Call Gemini 2.5 Flash vision for receipt scanning
            var response = await SendVisionRequestAsync(
                SystemPrompt,
                "Extract all data from this receipt. Respond with JSON only.",
                base64Image,
                mimeType,
                cancellationToken);

            if (string.IsNullOrEmpty(response.Content))
            {
                // The server's own words when it gave any. It names the monthly allowance, the
                // reset date, and which limit was hit, none of which the caller could work out.
                _ = telemetryManager?.TrackFeatureAsync(
                    FeatureName.ReceiptScanFailed,
                    response.Code is { Length: > 0 } ? $"no-content:{response.Code}" : "no-content",
                    cancellationToken: cancellationToken);
                return ReceiptScanResult.Failed(
                    response.Message ?? "No response from the AI service. Please try again.",
                    response.Code);
            }

            var result = ParseResponse(response.Content);

            // A scan can come back unusable without anything throwing, which is why these
            // report themselves.
            if (!result.IsSuccess)
            {
                // Covers both "the model says this is not a receipt" and "the response would
                // not parse". The code groups them; the message says which, since it carries
                // the model's own words.
                errorLogger?.LogWarning(
                    $"Receipt scan returned no usable data: {result.ErrorMessage}",
                    "GeminiReceiptScannerService.ScanReceiptAsync",
                    ErrorCategory.Api,
                    "ReceiptScanRejected");
                _ = telemetryManager?.TrackFeatureAsync(
                    FeatureName.ReceiptScanFailed, "rejected", cancellationToken: cancellationToken);
            }
            else if (result.LineItems.Count == 0)
            {
                // Parsed cleanly and found nothing. Usually a blurred or cropped photo, but
                // a run of these is how a prompt or model regression would first show up.
                errorLogger?.LogWarning(
                    "Receipt scan parsed but extracted no line items",
                    "GeminiReceiptScannerService.ScanReceiptAsync",
                    ErrorCategory.Api,
                    "ReceiptScanNoLineItems");
                _ = telemetryManager?.TrackFeatureAsync(
                    FeatureName.ReceiptScanFailed, "no-line-items", cancellationToken: cancellationToken);
            }

            if (result.IsSuccess && result.LineItems.Count > 0)
            {
                // The verification pass is a second full-image round-trip (~15-30s),
                // so only spend it when there is evidence the first pass missed an item.
                if (ShouldRunVerification(result))
                {
                    result = await VerifyAndFillMissingItemsAsync(result, base64Image, mimeType, cancellationToken);
                }
                success = true;
                _ = telemetryManager?.TrackFeatureAsync(FeatureName.ReceiptScanned, cancellationToken: cancellationToken);
            }
            return result;
        }
        catch (TaskCanceledException)
        {
            // One exception type, two very different events. A user who pressed Cancel is
            // not a failure and must not be reported as one; only the client giving up on
            // its own is worth knowing about.
            if (cancellationToken.IsCancellationRequested)
            {
                return ReceiptScanResult.Failed("Scan cancelled.");
            }

            errorLogger?.LogWarning(
                $"Receipt scan timed out after {stopwatch.ElapsedMilliseconds} ms",
                "GeminiReceiptScannerService.ScanReceiptAsync",
                ErrorCategory.Api,
                "ReceiptScanTimeout");
            _ = telemetryManager?.TrackFeatureAsync(FeatureName.ReceiptScanFailed, "timeout");
            return ReceiptScanResult.Failed("The scan took too long to complete. Please try again.");
        }
        catch (HttpRequestException ex)
        {
            errorLogger?.LogError(ex, ErrorCategory.Api, "Receipt scan network error");
            _ = telemetryManager?.TrackFeatureAsync(FeatureName.ReceiptScanFailed, "network");
            return ReceiptScanResult.Failed("Network error: unable to reach the scanning service. Please check your internet connection.");
        }
        catch (Exception ex)
        {
            errorLogger?.LogError(ex, ErrorCategory.Api, "Receipt scan failed");
            _ = telemetryManager?.TrackFeatureAsync(FeatureName.ReceiptScanFailed, "exception");
            return ReceiptScanResult.Failed("Failed to scan receipt. Please try again.");
        }
        finally
        {
            stopwatch.Stop();
            // Tag as ReceiptScanProxy (not Gemini) so the admin app-stats dashboard's
            // Receipt Scanning charts see these calls; the generic Gemini bucket is for
            // non-receipt AI (spreadsheet analysis, bank categorize, etc.).
            _ = telemetryManager?.TrackApiCallAsync(
                ApiName.ReceiptScanProxy,
                stopwatch.ElapsedMilliseconds,
                success,
                cancellationToken: cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<ReceiptScanResult> ScanReceiptFromFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return ReceiptScanResult.Failed("File not found.");
            }

            var imageData = await SharedFileReader.ReadAllBytesAsync(filePath, cancellationToken);
            var fileName = Path.GetFileName(filePath);
            return await ScanReceiptAsync(imageData, fileName, cancellationToken);
        }
        catch (IOException ex)
        {
            return ReceiptScanResult.Failed($"Failed to read file: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<bool> ValidateConfigurationAsync()
    {
        return Task.FromResult(IsConfigured);
    }

    private const string VerificationPrompt = @"You previously extracted these line items from a receipt image. Look at the receipt again carefully and check if ANY items were missed.

Extracted items:
{0}

Look at EVERY price on the right side of the receipt. If any product with a price was NOT included in the list above, return ONLY the missing items in this JSON format. Use ""insertAfter"" to indicate where the item belongs: set it to the number of the item it should appear after (based on receipt order), or 0 if it should be first:
{{""missingItems"": [{{""insertAfter"": 3, ""description"": ""Product Name"", ""quantity"": 1, ""unitPrice"": 0.00, ""totalPrice"": 0.00, ""confidence"": 0.9}}]}}

If nothing was missed, return: {{""missingItems"": []}}";

    /// <summary>
    /// Decides whether the second verification pass is worth its latency (~15-30s).
    /// It runs only when there is evidence the first pass may have missed an item:
    /// the scan confidence is low, or the extracted amounts don't reconcile to the
    /// printed total. When the receipt has no usable total to check against, it falls
    /// back to the old heuristic (long receipts are where misses are most likely).
    /// Receipts whose math already balances skip the pass, which is the fast path.
    /// </summary>
    public static bool ShouldRunVerification(ReceiptScanResult result)
    {
        // Low-confidence scans always get a second look.
        if (result.Confidence < 0.8)
            return true;

        var total = result.TotalAmount ?? 0m;

        // No printed total to reconcile against: fall back to the size heuristic.
        if (total <= 0m)
            return result.LineItems.Count >= 15;

        // Reconcile the extracted amounts against the printed total. Line items are
        // stored as positive amounts (negatives are folded into Discount during
        // parsing), so: total == sum(items) - discount + tax + shipping.
        var itemsSum = result.LineItems.Sum(li => li.TotalPrice);
        var computedTotal = itemsSum - (result.Discount ?? 0m) + (result.TaxAmount ?? 0m) + (result.Shipping ?? 0m);

        // Tolerance is kept tight and biased toward verifying: a genuinely missed item
        // shifts the total by roughly its own price, which we want to catch. A few cents
        // (or 0.5% on larger receipts) absorbs ordinary per-line rounding and fees we
        // don't model. When the books don't balance, re-scan; otherwise trust the pass.
        var tolerance = Math.Max(0.05m, total * 0.005m);
        return Math.Abs(computedTotal - total) > tolerance;
    }

    /// <summary>
    /// Sends the receipt image back with the extracted items and asks the model to find anything missed.
    /// </summary>
    private async Task<ReceiptScanResult> VerifyAndFillMissingItemsAsync(
        ReceiptScanResult result, string base64Image, string mimeType, CancellationToken cancellationToken)
    {
        try
        {
            var itemList = string.Join("\n", result.LineItems.Select((li, i) =>
                $"{i + 1}. {li.Description}, {li.TotalPrice:F2}"));

            var prompt = string.Format(VerificationPrompt, itemList);

            // Sent as receipt_verify, NOT receipt_scan. The user asked for one scan; this is the
            // app choosing to look again because the arithmetic did not reconcile, and billing
            // them a second time for that decision meant ten receipts could quietly cost twelve
            // and the last one be refused. The server meters on the operation name, so this is
            // what stops the double charge.
            var verifyResponse = await SendVisionRequestAsync(
                "You are a receipt verification system. Check if any line items were missed. Return JSON only.",
                prompt,
                base64Image,
                mimeType,
                cancellationToken,
                operation: "receipt_verify");

            // Deliberately keeps the first pass's result rather than failing the scan. A refused
            // or failed verification means the extra look did not happen, not that the receipt
            // is unusable.
            if (string.IsNullOrEmpty(verifyResponse.Content))
                return result;

            var cleaned = JsonResponseHelper.StripMarkdownCodeBlock(verifyResponse.Content);
            cleaned = JsonResponseHelper.SanitizeJsonNumbers(cleaned);
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            if (!root.TryGetProperty("missingItems", out var missingItems) || missingItems.ValueKind != JsonValueKind.Array)
                return result;

            // Collect missing items with their insertion positions, then insert
            // in reverse order so earlier insertions don't shift later indices
            var toInsert = new List<(int position, ScannedLineItem item)>();

            foreach (var item in missingItems.EnumerateArray())
            {
                var lineItem = new ScannedLineItem();
                var hasData = false;

                if (item.TryGetProperty("description", out var desc) && desc.ValueKind != JsonValueKind.Null)
                {
                    lineItem.Description = ReceiptDescriptionCleaner.Clean(desc.GetString());
                    hasData = true;
                }

                if (item.TryGetProperty("quantity", out var qty) && qty.ValueKind == JsonValueKind.Number)
                    lineItem.Quantity = qty.GetDecimal();

                if (item.TryGetProperty("unitPrice", out var unitPrice) && unitPrice.ValueKind == JsonValueKind.Number)
                    lineItem.UnitPrice = unitPrice.GetDecimal();

                if (item.TryGetProperty("totalPrice", out var totalPrice) && totalPrice.ValueKind == JsonValueKind.Number)
                {
                    lineItem.TotalPrice = totalPrice.GetDecimal();
                    hasData = true;
                }

                if (item.TryGetProperty("confidence", out var conf) && conf.ValueKind == JsonValueKind.Number)
                    lineItem.Confidence = conf.GetDouble();

                if (!hasData || lineItem.TotalPrice < 0) continue;

                var insertAfter = 0;
                if (item.TryGetProperty("insertAfter", out var pos) && pos.ValueKind == JsonValueKind.Number)
                    insertAfter = pos.GetInt32();

                // Clamp to valid range
                var insertIndex = Math.Clamp(insertAfter, 0, result.LineItems.Count);
                toInsert.Add((insertIndex, lineItem));
            }

            // Insert in reverse order of position so indices stay stable
            foreach (var (position, lineItem) in toInsert.OrderByDescending(x => x.position))
            {
                result.LineItems.Insert(position, lineItem);
            }
        }
        catch
        {
            // Verification is best-effort, don't fail the whole scan
        }

        return result;
    }

    /// <summary>
    /// Posts a vision (image + prompt) request to the AI proxy. Content is null on failure, with
    /// the server's message and code when it gave them.
    /// </summary>
    private async Task<AiProxyResponse> SendVisionRequestAsync(
        string systemPrompt, string userPrompt, string base64Image, string mimeType,
        CancellationToken cancellationToken, string operation = "receipt_scan")
    {
        var response = await new AiProxyClient(_httpClient, baseUrl, apiAuth).SendAsync(new AiProxyRequest
        {
            Operation = operation,
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt,
            Model = DefaultModel,
            // No maxTokens: the server sets the receipt-scan output budget authoritatively
            // from RECEIPT_SCAN_MAX_OUTPUT_TOKENS (.env) and ignores any client value here.
            Temperature = 0.0,
            Base64Image = base64Image,
            MimeType = mimeType,
        }, cancellationToken);

        if (!response.IsHttpSuccess)
        {
            errorLogger?.LogError(
                $"AI proxy error {response.StatusCode} ({response.Code ?? "no code"}): {response.Message ?? "no message"}",
                ErrorCategory.Api,
                "Receipt scan completion");

            return response;
        }

        // Feeds the server-measured compute time (and load factor) back to the caller so
        // desktop-side progress estimates can self-calibrate (see App's OperationTimingService
        // wiring). Best-effort: no-op when the response has no "timing" block.
        if (response.Timing is { } timing)
        {
            onTimingRecorded?.Invoke(timing.ServerMs, response.WallClockMs, response.UploadBytes, timing.LoadFactor);
        }

        if (!response.Succeeded)
        {
            errorLogger?.LogError(
                $"AI proxy returned success=false ({response.Code ?? "no code"}): {response.Message ?? "no message"}",
                ErrorCategory.Api,
                "Receipt scan completion");
        }

        return response;
    }

    // IMPORTANT: Make this internal so tests can call it
    public static ReceiptScanResult ParseResponse(string response)
    {
        try
        {
            var cleanResponse = JsonResponseHelper.StripMarkdownCodeBlock(response);
            cleanResponse = JsonResponseHelper.SanitizeJsonNumbers(cleanResponse);

            using var doc = JsonDocument.Parse(cleanResponse);
            var root = doc.RootElement;

            // Check for error response
            if (root.TryGetProperty("error", out var errorProp) && errorProp.ValueKind == JsonValueKind.String)
            {
                return ReceiptScanResult.Failed(errorProp.GetString() ?? "Scan failed");
            }

            var result = new ReceiptScanResult
            {
                IsSuccess = true,
                LineItems = []
            };

            if (root.TryGetProperty("supplierName", out var supplier) && supplier.ValueKind != JsonValueKind.Null)
                result.SupplierName = supplier.GetString();

            if (root.TryGetProperty("transactionDate", out var date) && date.ValueKind != JsonValueKind.Null)
            {
                // InvariantCulture (like every other date parse in the codebase) so an ambiguous date
                // from the scan doesn't read as the wrong day on a non-US machine locale.
                if (DateTime.TryParse(date.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDate))
                    result.TransactionDate = parsedDate;
            }

            if (root.TryGetProperty("subtotal", out var subtotal) && subtotal.ValueKind == JsonValueKind.Number)
                result.Subtotal = subtotal.GetDecimal();

            // Sum individual tax lines returned by the LLM
            if (root.TryGetProperty("taxes", out var taxes) && taxes.ValueKind == JsonValueKind.Array)
            {
                decimal taxTotal = 0;
                foreach (var taxLine in taxes.EnumerateArray())
                {
                    if (taxLine.TryGetProperty("amount", out var amt) && amt.ValueKind == JsonValueKind.Number)
                        taxTotal += amt.GetDecimal();
                }
                result.TaxAmount = taxTotal;
            }
            else if (root.TryGetProperty("taxAmount", out var tax) && tax.ValueKind == JsonValueKind.Number)
            {
                // Fallback for single taxAmount field
                result.TaxAmount = tax.GetDecimal();
            }

            if (root.TryGetProperty("totalAmount", out var total) && total.ValueKind == JsonValueKind.Number)
                result.TotalAmount = total.GetDecimal();

            // Sum individual discount lines returned by the LLM
            if (root.TryGetProperty("discounts", out var discounts) && discounts.ValueKind == JsonValueKind.Array)
            {
                decimal discountTotal = 0;
                foreach (var discountLine in discounts.EnumerateArray())
                {
                    if (discountLine.TryGetProperty("amount", out var amt) && amt.ValueKind == JsonValueKind.Number)
                        discountTotal += amt.GetDecimal();
                }
                result.Discount = discountTotal;
            }
            else if (root.TryGetProperty("discount", out var discount) && discount.ValueKind == JsonValueKind.Number)
            {
                // Fallback for single discount field
                result.Discount = discount.GetDecimal();
            }

            if (root.TryGetProperty("shipping", out var shipping) && shipping.ValueKind == JsonValueKind.Number)
                result.Shipping = shipping.GetDecimal();

            if (root.TryGetProperty("currencyCode", out var currency) && currency.ValueKind != JsonValueKind.Null)
                result.CurrencyCode = currency.GetString();

            if (root.TryGetProperty("confidence", out var confidence) && confidence.ValueKind == JsonValueKind.Number)
                result.Confidence = confidence.GetDouble();

            if (root.TryGetProperty("rawText", out var rawText) && rawText.ValueKind != JsonValueKind.Null)
                result.RawText = rawText.GetString();

            if (root.TryGetProperty("paymentMethod", out var paymentMethod) && paymentMethod.ValueKind != JsonValueKind.Null)
                result.PaymentMethod = paymentMethod.GetString();

            if (root.TryGetProperty("lineItems", out var lineItems) && lineItems.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in lineItems.EnumerateArray())
                {
                    if (!ScannedLineItemParser.TryParse(item, out var lineItem))
                        continue;

                    // Negative line items are discounts, add to discount total, not line items
                    if (lineItem.TotalPrice < 0)
                    {
                        result.Discount = (result.Discount ?? 0) + Math.Abs(lineItem.TotalPrice);
                    }
                    else
                    {
                        result.LineItems.Add(lineItem);
                    }
                }
            }

            return result;
        }
        catch (JsonException ex)
        {
            return ReceiptScanResult.Failed($"Failed to parse AI response: {ex.Message}");
        }
        catch (Exception)
        {
            return ReceiptScanResult.Failed("Failed to process the scan result.");
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
