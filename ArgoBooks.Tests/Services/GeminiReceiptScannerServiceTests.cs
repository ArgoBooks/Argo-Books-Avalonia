using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the GeminiReceiptScannerService class.
/// </summary>
public class GeminiReceiptScannerServiceTests
{
    #region IsConfigured Tests

    [Fact]
    public void IsConfigured_WithoutApiAuth_ReturnsFalse()
    {
        var service = new GeminiReceiptScannerService("https://example.com");

        Assert.False(service.IsConfigured);
    }

    #endregion

    #region ParseResponse Tests

    [Fact]
    public void ParseResponse_ValidJson_ReturnsCorrectResult()
    {
        var json = """
        {
          "supplierName": "Walmart",
          "transactionDate": "2026-03-15",
          "subtotal": 42.50,
          "taxes": [{"name": "State Tax", "amount": 2.10}, {"name": "County Tax", "amount": 1.30}],
          "discount": 5.00,
          "totalAmount": 40.90,
          "currencyCode": "USD",
          "paymentMethod": "Credit Card",
          "confidence": 0.95,
          "lineItems": [
            {"description": "Bread White", "quantity": 2, "unitPrice": 3.50, "totalPrice": 7.00, "confidence": 0.9},
            {"description": "Milk 2%", "quantity": 1, "unitPrice": 4.99, "totalPrice": 4.99, "confidence": 0.85}
          ]
        }
        """;

        var result = GeminiReceiptScannerService.ParseResponse(json);

        Assert.True(result.IsSuccess);
        Assert.Equal("Walmart", result.SupplierName);
        Assert.Equal(new DateTime(2026, 3, 15), result.TransactionDate);
        Assert.Equal(42.50m, result.Subtotal);
        Assert.Equal(3.40m, result.TaxAmount);
        Assert.Equal(5.00m, result.Discount);
        Assert.Equal(40.90m, result.TotalAmount);
        Assert.Equal("USD", result.CurrencyCode);
        Assert.Equal("Credit Card", result.PaymentMethod);
        Assert.Equal(0.95, result.Confidence);
        Assert.Equal(2, result.LineItems.Count);
        Assert.Equal("Bread White", result.LineItems[0].Description);
        Assert.Equal(2, result.LineItems[0].Quantity);
        Assert.Equal(3.50m, result.LineItems[0].UnitPrice);
        Assert.Equal(7.00m, result.LineItems[0].TotalPrice);
        Assert.Equal("Milk 2%", result.LineItems[1].Description);
    }

    [Fact]
    public void ParseResponse_WithMarkdownCodeBlock_StripsAndParses()
    {
        var json = """
        ```json
        {
          "supplierName": "Target",
          "transactionDate": "2026-01-10",
          "subtotal": 15.00,
          "taxAmount": 1.20,
          "totalAmount": 16.20,
          "confidence": 0.88,
          "lineItems": []
        }
        ```
        """;

        var result = GeminiReceiptScannerService.ParseResponse(json);

        Assert.True(result.IsSuccess);
        Assert.Equal("Target", result.SupplierName);
        Assert.Equal(16.20m, result.TotalAmount);
    }

    [Fact]
    public void ParseResponse_ErrorField_ReturnsFailedResult()
    {
        var json = """
        {
          "error": "Not a valid receipt",
          "confidence": 0.0
        }
        """;

        var result = GeminiReceiptScannerService.ParseResponse(json);

        Assert.False(result.IsSuccess);
        Assert.Equal("Not a valid receipt", result.ErrorMessage);
    }

    [Fact]
    public void ParseResponse_MalformedJson_ReturnsFailedResult()
    {
        var result = GeminiReceiptScannerService.ParseResponse("this is not json at all");

        Assert.False(result.IsSuccess);
        Assert.Contains("Failed to parse", result.ErrorMessage);
    }

    [Fact]
    public void ParseResponse_WithDiscount_ExtractsDiscount()
    {
        var json = """
        {
          "supplierName": "CVS Pharmacy",
          "subtotal": 25.00,
          "taxAmount": 2.00,
          "discount": 3.50,
          "totalAmount": 23.50,
          "confidence": 0.92,
          "lineItems": [
            {"description": "Shampoo", "quantity": 1, "unitPrice": 12.50, "totalPrice": 12.50, "confidence": 0.9},
            {"description": "Coupon Discount", "quantity": 1, "unitPrice": -3.50, "totalPrice": -3.50, "confidence": 0.85}
          ]
        }
        """;

        var result = GeminiReceiptScannerService.ParseResponse(json);

        Assert.True(result.IsSuccess);
        // Discount sums the "discount" field (3.50) + negative line item (3.50)
        Assert.Equal(7.00m, result.Discount);
        Assert.Equal(23.50m, result.TotalAmount);
        // Negative line items are moved to discount, so only 1 product line item remains
        Assert.Single(result.LineItems);
    }

    [Fact]
    public void ParseResponse_NullFields_HandlesGracefully()
    {
        var json = """
        {
          "supplierName": null,
          "transactionDate": null,
          "subtotal": 10.00,
          "totalAmount": 10.00,
          "confidence": 0.5,
          "lineItems": []
        }
        """;

        var result = GeminiReceiptScannerService.ParseResponse(json);

        Assert.True(result.IsSuccess);
        Assert.Null(result.SupplierName);
        Assert.Null(result.TransactionDate);
        Assert.Null(result.Discount);
        Assert.Equal(10.00m, result.TotalAmount);
    }

    [Fact]
    public void ParseResponse_EmptyLineItems_ReturnsEmptyList()
    {
        var json = """
        {
          "supplierName": "Gas Station",
          "totalAmount": 55.00,
          "confidence": 0.9,
          "lineItems": []
        }
        """;

        var result = GeminiReceiptScannerService.ParseResponse(json);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.LineItems);
    }

    [Fact]
    public void ParseResponse_MultipleTaxLines_SumsCorrectly()
    {
        var json = """
        {
          "supplierName": "Independent",
          "subtotal": 178.68,
          "taxes": [
            {"name": "GST", "amount": 1.22},
            {"name": "PST", "amount": 1.47}
          ],
          "totalAmount": 181.37,
          "confidence": 0.9,
          "lineItems": []
        }
        """;

        var result = GeminiReceiptScannerService.ParseResponse(json);

        Assert.True(result.IsSuccess);
        Assert.Equal(2.69m, result.TaxAmount);
    }

    [Fact]
    public void ParseResponse_SingleTaxAmountFallback_StillWorks()
    {
        var json = """
        {
          "supplierName": "Store",
          "taxAmount": 5.50,
          "totalAmount": 55.50,
          "confidence": 0.9,
          "lineItems": []
        }
        """;

        var result = GeminiReceiptScannerService.ParseResponse(json);

        Assert.True(result.IsSuccess);
        Assert.Equal(5.50m, result.TaxAmount);
    }

    [Fact]
    public void ParseResponse_MultipleDiscountLines_SumsCorrectly()
    {
        var json = """
        {
          "supplierName": "Grocery Store",
          "subtotal": 50.00,
          "discounts": [
            {"name": "Member Discount", "amount": 2.49},
            {"name": "Coupon", "amount": 1.00},
            {"name": "Loyalty Points", "amount": 0.50}
          ],
          "totalAmount": 46.01,
          "confidence": 0.9,
          "lineItems": []
        }
        """;

        var result = GeminiReceiptScannerService.ParseResponse(json);

        Assert.True(result.IsSuccess);
        Assert.Equal(3.99m, result.Discount);
    }

    [Fact]
    public void ParseResponse_SingleDiscountFallback_StillWorks()
    {
        var json = """
        {
          "supplierName": "Store",
          "discount": 5.00,
          "totalAmount": 45.00,
          "confidence": 0.9,
          "lineItems": []
        }
        """;

        var result = GeminiReceiptScannerService.ParseResponse(json);

        Assert.True(result.IsSuccess);
        Assert.Equal(5.00m, result.Discount);
    }

    #endregion

    #region ShouldRunVerification Tests

    private static ScannedLineItem Item(decimal totalPrice) => new() { TotalPrice = totalPrice };

    private static ReceiptScanResult Result(
        decimal? total,
        double confidence,
        decimal? tax = null,
        decimal? discount = null,
        params decimal[] itemTotals)
        => new()
        {
            IsSuccess = true,
            TotalAmount = total,
            TaxAmount = tax,
            Discount = discount,
            Confidence = confidence,
            LineItems = itemTotals.Select(Item).ToList()
        };

    [Fact]
    public void ShouldRunVerification_BalancedLongReceipt_SkipsVerification()
    {
        // 20 items summing exactly to the total. The old heuristic verified any
        // receipt with 15+ items; reconciliation lets this fast path skip it.
        var items = Enumerable.Repeat(1.50m, 20).ToArray();
        var result = Result(total: 30.00m, confidence: 0.95, itemTotals: items);

        Assert.False(GeminiReceiptScannerService.ShouldRunVerification(result));
    }

    [Fact]
    public void ShouldRunVerification_BalancedWithTaxAndDiscount_SkipsVerification()
    {
        // sum(items) 50 - discount 5 + tax 4 = 49 == total
        var result = Result(total: 49.00m, confidence: 0.9, tax: 4.00m, discount: 5.00m,
            itemTotals: [20.00m, 30.00m]);

        Assert.False(GeminiReceiptScannerService.ShouldRunVerification(result));
    }

    [Fact]
    public void ShouldRunVerification_MissingItemDiscrepancy_Verifies()
    {
        // Items sum to 27 but the receipt total is 30: a ~$3 item was missed.
        var result = Result(total: 30.00m, confidence: 0.9, itemTotals: [12.00m, 15.00m]);

        Assert.True(GeminiReceiptScannerService.ShouldRunVerification(result));
    }

    [Fact]
    public void ShouldRunVerification_LowConfidence_VerifiesEvenWhenBalanced()
    {
        var result = Result(total: 30.00m, confidence: 0.7, itemTotals: [10.00m, 20.00m]);

        Assert.True(GeminiReceiptScannerService.ShouldRunVerification(result));
    }

    [Fact]
    public void ShouldRunVerification_NoTotal_LongReceipt_Verifies()
    {
        // No total to reconcile against: fall back to the size heuristic (15+ items).
        var items = Enumerable.Repeat(1.00m, 15).ToArray();
        var result = Result(total: null, confidence: 0.9, itemTotals: items);

        Assert.True(GeminiReceiptScannerService.ShouldRunVerification(result));
    }

    [Fact]
    public void ShouldRunVerification_NoTotal_ShortReceipt_SkipsVerification()
    {
        var result = Result(total: null, confidence: 0.9, itemTotals: [1.00m, 2.00m, 3.00m]);

        Assert.False(GeminiReceiptScannerService.ShouldRunVerification(result));
    }

    [Fact]
    public void ShouldRunVerification_WithinTolerance_SkipsVerification()
    {
        // total 100 -> tolerance = max(0.05, 0.50) = 0.50; off by 0.40, still balances.
        var result = Result(total: 100.00m, confidence: 0.9, itemTotals: [100.40m]);

        Assert.False(GeminiReceiptScannerService.ShouldRunVerification(result));
    }

    [Fact]
    public void ShouldRunVerification_JustOutsideTolerance_Verifies()
    {
        // total 100 -> tolerance 0.50; off by 0.60, does not balance.
        var result = Result(total: 100.00m, confidence: 0.9, itemTotals: [100.60m]);

        Assert.True(GeminiReceiptScannerService.ShouldRunVerification(result));
    }

    [Fact]
    public void ShouldRunVerification_SmallReceipt_UsesFiveCentFloor()
    {
        // total 8 -> 0.5% is 0.04, so the 0.05 floor applies. Off by 0.06 -> verify.
        var result = Result(total: 8.00m, confidence: 0.9, itemTotals: [8.06m]);

        Assert.True(GeminiReceiptScannerService.ShouldRunVerification(result));
    }

    #endregion

    #region ParseResponse date culture

    [Fact]
    public void ParseResponse_AmbiguousDate_UsesInvariantCultureRegardlessOfLocale()
    {
        // The receipt date must be culture-independent. "02/03/2023" is February 3 under
        // InvariantCulture (month-first) but March 2 under a day-first locale like en-GB.
        const string json = """{"transactionDate":"02/03/2023"}""";

        ReceiptScanResult result = null!;
        var thread = new Thread(() =>
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-GB");
            result = GeminiReceiptScannerService.ParseResponse(json);
        });
        thread.Start();
        thread.Join();

        Assert.Equal(new DateTime(2023, 2, 3), result.TransactionDate);
    }

    #endregion
}
