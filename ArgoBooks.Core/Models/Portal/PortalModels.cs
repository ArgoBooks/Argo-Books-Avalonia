namespace ArgoBooks.Core.Models.Portal;

/// <summary>
/// Response from publishing an invoice to the portal.
/// </summary>
public class PortalPublishResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// The unique token for directly accessing this invoice on the portal.
    /// </summary>
    [JsonPropertyName("invoiceToken")]
    public string? InvoiceToken { get; set; }

    /// <summary>
    /// The unique token for the customer's portal (shows all their invoices).
    /// </summary>
    [JsonPropertyName("customerToken")]
    public string? CustomerToken { get; set; }

    /// <summary>
    /// The full URL to the invoice on the portal.
    /// </summary>
    [JsonPropertyName("invoiceUrl")]
    public string? InvoiceUrl { get; set; }

    /// <summary>
    /// The full URL to the customer's portal.
    /// </summary>
    [JsonPropertyName("portalUrl")]
    public string? PortalUrl { get; set; }

    /// <summary>
    /// Whether the server sent an email notification to the customer.
    /// </summary>
    [JsonPropertyName("emailSent")]
    public bool EmailSent { get; set; }

    /// <summary>
    /// The currently connected payment methods (e.g. ["stripe", "square"]).
    /// Returned by the server so the desktop app can stay in sync.
    /// </summary>
    [JsonPropertyName("payment_methods")]
    public List<string>? PaymentMethods { get; set; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    /// <summary>
    /// A failed publish that leaves open whether the portal saved the invoice and emailed the
    /// customer: a timeout, a dropped connection, a server fault. Not part of the response body.
    /// </summary>
    [JsonIgnore]
    public bool MayHavePublished { get; set; }
}

/// <summary>
/// Response from disconnecting a payment provider.
/// </summary>
public class PortalDisconnectResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// The updated connected provider state after disconnection.
    /// </summary>
    [JsonPropertyName("connectedProviders")]
    public ConnectedPaymentAccounts? ConnectedProviders { get; set; }

    /// <summary>
    /// The currently connected payment methods (e.g. ["paypal", "square"]).
    /// </summary>
    [JsonPropertyName("payment_methods")]
    public List<string>? PaymentMethods { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }
}

/// <summary>
/// A payment record returned from the portal during sync.
/// </summary>
public class PortalPaymentRecord
{
    /// <summary>
    /// The server-side payment ID (used to prevent duplicate syncs).
    /// </summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>
    /// The invoice ID this payment is for (matches Argo Books invoice ID).
    /// </summary>
    [JsonPropertyName("invoiceId")]
    public string InvoiceId { get; set; } = string.Empty;

    /// <summary>
    /// The customer name (for display/verification).
    /// </summary>
    [JsonPropertyName("customerName")]
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>
    /// The payment amount (total charged including any processing fee).
    /// </summary>
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    /// <summary>
    /// The processing fee portion of the payment (0 if fees not passed to customer).
    /// </summary>
    [JsonPropertyName("processingFee")]
    public decimal ProcessingFee { get; set; }

    /// <summary>
    /// The currency code (e.g., "USD", "CAD").
    /// </summary>
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// The payment provider used: "stripe", "paypal", or "square".
    /// </summary>
    [JsonPropertyName("paymentMethod")]
    public string PaymentMethod { get; set; } = string.Empty;

    /// <summary>
    /// The provider's transaction/payment ID.
    /// </summary>
    [JsonPropertyName("providerTransactionId")]
    public string? ProviderTransactionId { get; set; }

    /// <summary>
    /// The provider's payment intent / order / capture ID: the value the desktop
    /// passes back to the server's /refunds/request endpoint as
    /// <c>provider_payment_id</c> when issuing a refund. Populated by sync.
    /// </summary>
    [JsonPropertyName("providerPaymentId")]
    public string? ProviderPaymentId { get; set; }

    /// <summary>
    /// Human-readable confirmation/reference number.
    /// </summary>
    [JsonPropertyName("referenceNumber")]
    public string ReferenceNumber { get; set; } = string.Empty;

    /// <summary>
    /// When the payment was made.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// True when this row represents a refund rather than an incoming payment.
    /// Refund rows have negative <see cref="Amount"/> and status "refunded".
    /// </summary>
    [JsonPropertyName("isRefund")]
    public bool IsRefund { get; set; }

    /// <summary>
    /// For refund rows, the provider's payment ID (e.g. Stripe payment_intent)
    /// that this refund offsets. The desktop uses this to link the refund
    /// Payment row back to its source Payment via PortalPaymentId lookup.
    /// </summary>
    [JsonPropertyName("refundedProviderPaymentId")]
    public string? RefundedProviderPaymentId { get; set; }

    /// <summary>
    /// Server's refund_requests.id when this refund was initiated through the
    /// Argo Books refund flow. Null for refunds created via the provider's own
    /// dashboard (which we received via webhook).
    /// </summary>
    [JsonPropertyName("refundRequestId")]
    public int? RefundRequestId { get; set; }

    /// <summary>
    /// User-supplied reason recorded at refund-request time.
    /// </summary>
    [JsonPropertyName("refundReason")]
    public string? RefundReason { get; set; }

    /// <summary>
    /// For a refund made in Argo Books, the part that gave back the invoice's security deposit.
    /// Null for refunds made in the provider's dashboard, and from servers that don't send it.
    /// </summary>
    [JsonPropertyName("depositAmount")]
    public decimal? DepositAmount { get; set; }
}

/// <summary>
/// Response from the payment sync endpoint.
/// </summary>
public class PortalSyncResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// New payment records since the last sync.
    /// </summary>
    [JsonPropertyName("payments")]
    public List<PortalPaymentRecord> Payments { get; set; } = [];

    /// <summary>
    /// Server timestamp for this sync (use as "since" for next sync).
    /// </summary>
    [JsonPropertyName("syncTimestamp")]
    public DateTime? SyncTimestamp { get; set; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }
}

/// <summary>
/// Response from the portal status check endpoint.
/// </summary>
public class PortalStatusResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("connected")]
    public bool Connected { get; set; }

    [JsonPropertyName("portalUrl")]
    public string? PortalUrl { get; set; }

    /// <summary>
    /// Company info returned by the server, including logo URL.
    /// </summary>
    [JsonPropertyName("company")]
    public PortalCompanyInfo? Company { get; set; }

    /// <summary>
    /// Which payment providers the business has connected.
    /// </summary>
    [JsonPropertyName("connectedProviders")]
    public ConnectedPaymentAccounts? ConnectedProviders { get; set; }

    /// <summary>
    /// Server-side email preferences. The server is authoritative for these
    /// because the reminder cron and the payment webhooks read them while this
    /// app is closed, so the local copy converges on whatever arrives here.
    /// Null when talking to a server that predates the preferences block.
    /// </summary>
    [JsonPropertyName("preferences")]
    public PortalPreferences? Preferences { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// Per-company email preferences held on the server.
/// </summary>
public class PortalPreferences
{
    /// <summary>Whether the server chases unpaid invoices at 3/7/14 days past due.</summary>
    [JsonPropertyName("sendPaymentReminders")]
    public bool SendPaymentReminders { get; set; }

    /// <summary>
    /// When reminders were last switched on. Only invoices due after this are
    /// ever chased, so enabling never releases a backlog.
    /// </summary>
    [JsonPropertyName("remindersEnabledAt")]
    public DateTime? RemindersEnabledAt { get; set; }

    /// <summary>Whether the server emails the owner when a customer pays.</summary>
    [JsonPropertyName("emailOwnerOnPayment")]
    public bool EmailOwnerOnPayment { get; set; }

    /// <summary>
    /// Whether the owner's email has been verified. The server refuses to send
    /// payment notifications without this, so the UI has to surface it or the
    /// toggle looks broken.
    /// </summary>
    [JsonPropertyName("ownerEmailVerified")]
    public bool OwnerEmailVerified { get; set; }
}

/// <summary>
/// Response from the portal preferences update endpoint.
/// </summary>
public class PortalPreferencesResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>The resolved server-side state, including any cutoff just armed.</summary>
    [JsonPropertyName("preferences")]
    public PortalPreferences? Preferences { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }
}

/// <summary>
/// One invoice's locally-known payment state, pushed to the server so it can
/// tell which invoices are genuinely still unpaid.
/// </summary>
public class PortalBalanceSyncItem
{
    /// <summary>Matches portal_invoices.invoice_id, which is the local Invoice.Id.</summary>
    [JsonPropertyName("invoiceId")]
    public string InvoiceId { get; set; } = string.Empty;

    [JsonPropertyName("totalAmount")]
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// Total recorded against this invoice OUTSIDE the portal (cash, cheque,
    /// bank transfer). Portal payments are excluded: the server already owns
    /// those and counting them here would double-apply them.
    /// </summary>
    [JsonPropertyName("externalPaid")]
    public decimal ExternalPaid { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("dueDate")]
    public string? DueDate { get; set; }

    [JsonPropertyName("cancelled")]
    public bool Cancelled { get; set; }

    /// <summary>
    /// Freshly rendered invoice HTML, sent only when the stored copy would be
    /// wrong. The portal shows a snapshot taken when the invoice was published,
    /// so once anything has been paid it keeps showing the original totals with
    /// no Amount Paid row. Null leaves the stored copy untouched.
    /// </summary>
    [JsonPropertyName("customInvoiceHtml")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CustomInvoiceHtml { get; set; }
}

/// <summary>
/// Response from the invoice balance reconciliation endpoint.
/// </summary>
public class PortalBalanceSyncResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("updated")]
    public int Updated { get; set; }

    [JsonPropertyName("notFound")]
    public int NotFound { get; set; }

    [JsonPropertyName("rejected")]
    public int Rejected { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// Company info returned by the portal status endpoint.
/// </summary>
public class PortalCompanyInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("logo_url")]
    public string? LogoUrl { get; set; }

    /// <summary>
    /// The authoritative portal owner email as the server currently has it.
    /// The desktop mirrors this into Settings.Company.Email so a server-side
    /// change (e.g. the email-change revert link) reaches this device.
    /// </summary>
    [JsonPropertyName("owner_email")]
    public string? OwnerEmail { get; set; }
}

/// <summary>
/// Request payload for publishing an invoice to the portal.
/// </summary>
public class PortalPublishRequest
{
    [JsonPropertyName("invoiceId")]
    public string InvoiceId { get; set; } = string.Empty;

    [JsonPropertyName("invoiceNumber")]
    public string InvoiceNumber { get; set; } = string.Empty;

    [JsonPropertyName("customerId")]
    public string CustomerId { get; set; } = string.Empty;

    [JsonPropertyName("customerName")]
    public string CustomerName { get; set; } = string.Empty;

    [JsonPropertyName("customerEmail")]
    public string? CustomerEmail { get; set; }

    [JsonPropertyName("companyName")]
    public string CompanyName { get; set; } = string.Empty;

    [JsonPropertyName("issueDate")]
    public DateTime IssueDate { get; set; }

    [JsonPropertyName("dueDate")]
    public DateTime DueDate { get; set; }

    [JsonPropertyName("lineItems")]
    public List<PortalLineItem> LineItems { get; set; } = [];

    [JsonPropertyName("subtotal")]
    public decimal Subtotal { get; set; }

    [JsonPropertyName("taxRate")]
    public decimal TaxRate { get; set; }

    [JsonPropertyName("taxAmount")]
    public decimal TaxAmount { get; set; }

    [JsonPropertyName("securityDeposit")]
    public decimal SecurityDeposit { get; set; }

    [JsonPropertyName("totalAmount")]
    public decimal Total { get; set; }

    [JsonPropertyName("amountPaid")]
    public decimal AmountPaid { get; set; }

    [JsonPropertyName("balanceDue")]
    public decimal Balance { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "USD";

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "sent";

    /// <summary>
    /// When true, the server sends a notification email to the customer
    /// with a summary and "View &amp; Pay Invoice" link.
    /// </summary>
    [JsonPropertyName("sendEmail")]
    public bool SendEmail { get; set; }

    /// <summary>
    /// Pre-rendered invoice HTML from the desktop app's template engine.
    /// When present, the portal displays this in a sandboxed iframe instead of the generic template.
    /// </summary>
    [JsonPropertyName("customInvoiceHtml")]
    public string? CustomInvoiceHtml { get; set; }

    /// <summary>
    /// Whether to pass payment processing fees to customers.
    /// When true, the portal adds a card processing fee to online payments.
    /// </summary>
    [JsonPropertyName("passProcessingFee")]
    public bool PassProcessingFee { get; set; } = true;
}

/// <summary>
/// A line item in the portal publish request.
/// </summary>
public class PortalLineItem
{
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    [JsonPropertyName("unitPrice")]
    public decimal UnitPrice { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }
}

/// <summary>
/// Request to confirm synced payments.
/// </summary>
public class PortalSyncConfirmRequest
{
    /// <summary>
    /// IDs of payments that were successfully synced to Argo Books.
    /// </summary>
    [JsonPropertyName("paymentIds")]
    public List<int> PaymentIds { get; set; } = [];
}

/// <summary>
/// Response from OAuth connect flow initiation.
/// </summary>
public class PortalOAuthResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// The URL to redirect the user to for OAuth authorization.
    /// </summary>
    [JsonPropertyName("authUrl")]
    public string? AuthUrl { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }
}

/// <summary>
/// Response from the portal company registration endpoint.
/// </summary>
public class PortalRegisterResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("api_key")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("company_id")]
    public int? CompanyId { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    /// True when the server has issued an email verification code that the
    /// owner must enter before refunds become available. The desktop should
    /// surface a verification prompt and call /account/verify-email/confirm.php.
    /// </summary>
    [JsonPropertyName("email_verification_required")]
    public bool EmailVerificationRequired { get; set; }
}

/// <summary>
/// Response from logo upload/delete endpoints.
/// </summary>
public class PortalLogoResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("logo_url")]
    public string? LogoUrl { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }
}

/// <summary>
/// Response from updating the company name on the portal.
/// </summary>
public class PortalCompanyNameResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("companyName")]
    public string? CompanyName { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
