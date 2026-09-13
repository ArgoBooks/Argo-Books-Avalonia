using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.Sync;

namespace ArgoBooks.Shared.Mobile;

/// <summary>
/// One line item on the review screen: mirrors <see cref="ScannedLineItem"/> plus the
/// auto-suggested/edited <see cref="ProductName"/>. Mutable so the review UI can update
/// <see cref="ProductName"/>/<see cref="IsMatched"/> in place when the user picks a different
/// product, without re-running the mapper.
/// </summary>
public sealed class ReviewLineItem
{
    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; } = 1;

    public decimal UnitPrice { get; set; }

    public decimal Total { get; set; }

    /// <summary>
    /// Always non-empty: either a product matched from the snapshot's product list, or a
    /// best-guess suggestion (the line's own description) when nothing matched. The review screen
    /// never shows an empty "assign a product" state.
    /// </summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>True when <see cref="ProductName"/> was matched against an existing snapshot product
    /// rather than guessed from the line description.</summary>
    public bool IsMatched { get; set; }
}

/// <summary>
/// The editable review screen's model: a <see cref="ReceiptScanResult"/> reshaped with the
/// snapshot's suppliers/products auto-matched in, ready for the user to edit before
/// <see cref="ReviewModelMapper.BuildCapturedTransaction"/> turns it into a <see cref="CapturedTransaction"/>.
/// </summary>
public sealed class ReviewModel
{
    public CapturedTransactionType Type { get; set; } = CapturedTransactionType.Expense;

    public string SupplierOrCustomer { get; set; } = string.Empty;

    public DateTime Date { get; set; }

    public decimal Total { get; set; }

    public decimal Tax { get; set; }

    public List<ReviewLineItem> LineItems { get; set; } = [];
}

/// <summary>
/// Pure mapping logic for the review screen: turns a <see cref="ReceiptScanResult"/> (raw AI
/// scan output) plus the active <see cref="MobileSnapshot"/> (for supplier/product auto-suggest)
/// into a <see cref="ReviewModel"/> the UI can bind to and edit, then turns the (possibly edited)
/// <see cref="ReviewModel"/> back into a <see cref="CapturedTransaction"/> to
/// encrypt+push. No UI/device dependency, so it's fully unit-tested
/// (see ArgoBooks.Tests/Mobile/ReviewModelMapperTests.cs).
/// </summary>
public static class ReviewModelMapper
{
    /// <summary>
    /// Maps a scan result into a review model. Supplier name is matched case-insensitively against
    /// the snapshot's supplier list (falling back to the raw scanned name if nothing matches); each
    /// line item's description is matched (exact, then a loose substring match) against the
    /// snapshot's product list, falling back to a best-guess suggestion (the line's own description)
    /// so a product suggestion is always present.
    /// </summary>
    public static ReviewModel Map(ReceiptScanResult result, MobileSnapshot? snapshot)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        var suppliers = snapshot?.Suppliers ?? [];
        var products = snapshot?.Products ?? [];

        var rawSupplier = string.IsNullOrWhiteSpace(result.SupplierName) ? string.Empty : result.SupplierName.Trim();
        var matchedSupplier = FindMatch(suppliers, rawSupplier);

        var reviewModel = new ReviewModel
        {
            Type = CapturedTransactionType.Expense,
            SupplierOrCustomer = matchedSupplier ?? rawSupplier,
            Date = result.TransactionDate ?? DateTime.Today,
            Total = result.TotalAmount ?? 0m,
            Tax = result.TaxAmount ?? 0m,
        };

        foreach (var line in result.LineItems)
        {
            var matchedProduct = FindMatch(products, line.Description);
            var suggested = matchedProduct
                ?? (string.IsNullOrWhiteSpace(line.Description) ? "New product" : line.Description.Trim());

            reviewModel.LineItems.Add(new ReviewLineItem
            {
                Description = line.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Total = line.TotalPrice,
                ProductName = suggested,
                IsMatched = matchedProduct != null,
            });
        }

        return reviewModel;
    }

    /// <summary>
    /// Builds the wire DTO Task 5 encrypts and pushes to the sync queue: an idempotency key
    /// (<see cref="CapturedTransaction.ScanUid"/>), the (possibly edited) header fields, each line
    /// item carrying its chosen product name, and the receipt image if provided.
    /// </summary>
    /// <param name="scanUid">
    /// The idempotency key the desktop de-duplicates on. Pass a stable id (e.g. the offline outbox's
    /// queue id) so a retry after a lost push response re-sends the same key instead of creating a
    /// duplicate transaction; pass null for a one-shot push (the interactive review path) to mint a
    /// fresh key.
    /// </param>
    public static CapturedTransaction BuildCapturedTransaction(ReviewModel reviewModel, string? imageBase64, string? scanUid = null)
    {
        if (reviewModel == null)
        {
            throw new ArgumentNullException(nameof(reviewModel));
        }

        return new CapturedTransaction
        {
            ScanUid = string.IsNullOrEmpty(scanUid) ? Guid.NewGuid().ToString("N") : scanUid,
            Type = reviewModel.Type,
            SupplierOrCustomer = reviewModel.SupplierOrCustomer,
            Date = reviewModel.Date,
            Total = reviewModel.Total,
            Tax = reviewModel.Tax,
            ImageBase64 = imageBase64,
            LineItems = reviewModel.LineItems.Select(li => new CapturedLineItem
            {
                Description = li.Description,
                Quantity = li.Quantity,
                UnitPrice = li.UnitPrice,
                Total = li.Total,
                ProductName = li.ProductName,
            }).ToList(),
        };
    }

    /// <summary>Exact case-insensitive match first, then a loose substring match either direction
    /// (handles e.g. a scanned "2x4 Lumber - 8ft" line against a snapshot product named "Lumber").</summary>
    private static string? FindMatch(List<RowDto> candidates, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();

        var exact = candidates.FirstOrDefault(c => string.Equals(c.Title, trimmed, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
        {
            return exact.Title;
        }

        var fuzzy = candidates.FirstOrDefault(c =>
            !string.IsNullOrWhiteSpace(c.Title) &&
            (trimmed.Contains(c.Title, StringComparison.OrdinalIgnoreCase) ||
             c.Title.Contains(trimmed, StringComparison.OrdinalIgnoreCase)));

        return fuzzy?.Title;
    }
}
