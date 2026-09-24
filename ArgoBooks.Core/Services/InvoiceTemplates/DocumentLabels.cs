namespace ArgoBooks.Core.Services.InvoiceTemplates;

/// <summary>
/// The wording the shared templates print around a document's figures, so the same templates can
/// render an invoice or a quote. Every default describes an invoice, which is what the renderer
/// uses when a caller passes nothing: existing output is unchanged to the byte.
/// </summary>
public sealed record DocumentLabels
{
    /// <summary>The invoice wording, and the renderer's default.</summary>
    public static readonly DocumentLabels Invoice = new();

    /// <summary>Replaces the template's own header text. Null keeps whatever the template has.</summary>
    public string? HeaderText { get; init; }

    /// <summary>The document's name, used in the page title.</summary>
    public string DocumentName { get; init; } = "Invoice";

    /// <summary>Label on the document number.</summary>
    public string NumberLabel { get; init; } = "Invoice #";

    /// <summary>The number label on the sales-receipt template, which words it its own way.</summary>
    public string ReceiptNumberLabel { get; init; } = "Receipt #";

    /// <summary>Label on the date the document stops applying.</summary>
    public string DueDateLabel { get; init; } = "Due Date";

    /// <summary>The short form of <see cref="DueDateLabel"/>, printed inline before the date.</summary>
    public string DueLabelShort { get; init; } = "Due";

    /// <summary>The short form again, for the one template that prints it in capitals.</summary>
    public string DueLabelShortUpper { get; init; } = "DUE";

    /// <summary>
    /// Hides the amount-to-pay row, the processing-fee row and the payment instructions. A quote
    /// is not a request for money, and showing a customer what to pay before they have accepted
    /// invites them to pay it.
    /// </summary>
    public bool HidePaymentDetails { get; init; }

    /// <summary>
    /// The quote wording, with the header taking the casing the template already uses for its own
    /// heading, so "INVOICE" becomes "QUOTE" and "Invoice" becomes "Quote".
    /// </summary>
    public static DocumentLabels ForQuote(string? templateHeaderText) => new()
    {
        HeaderText = IsAllCaps(templateHeaderText) ? "QUOTE" : "Quote",
        DocumentName = "Quote",
        NumberLabel = "Quote #",
        ReceiptNumberLabel = "Quote #",
        DueDateLabel = "Valid Until",
        DueLabelShort = "Valid until",
        DueLabelShortUpper = "VALID UNTIL",
        HidePaymentDetails = true
    };

    private static bool IsAllCaps(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var letters = text.Where(char.IsLetter).ToList();
        return letters.Count > 0 && letters.All(char.IsUpper);
    }
}
