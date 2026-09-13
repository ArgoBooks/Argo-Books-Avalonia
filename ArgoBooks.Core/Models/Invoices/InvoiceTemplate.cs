namespace ArgoBooks.Core.Models.Invoices;

/// <summary>
/// Represents a customizable invoice template.
/// </summary>
public class InvoiceTemplate
{
    /// <summary>
    /// Unique identifier for this template.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display name for this template.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The base template type to use.
    /// </summary>
    [JsonPropertyName("baseTemplate")]
    public InvoiceTemplateType BaseTemplate { get; set; } = InvoiceTemplateType.Professional;

    /// <summary>
    /// Whether this is the default template for new invoices.
    /// </summary>
    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; }

    /// <summary>
    /// Primary brand color (header background, accents).
    /// </summary>
    [JsonPropertyName("primaryColor")]
    public string PrimaryColor { get; set; } = AppColors.PrimaryHover;

    /// <summary>
    /// Secondary color (borders, subtle backgrounds).
    /// </summary>
    [JsonPropertyName("secondaryColor")]
    public string SecondaryColor { get; set; } = AppColors.ChartGrid;

    /// <summary>
    /// Accent color for highlights and important text.
    /// </summary>
    [JsonPropertyName("accentColor")]
    public string AccentColor { get; set; } = AppColors.EmeraldHover;

    /// <summary>
    /// Header/title color. Falls back to PrimaryColor if empty.
    /// </summary>
    [JsonPropertyName("headerColor")]
    public string HeaderColor { get; set; } = string.Empty;

    /// <summary>
    /// Text color for primary content.
    /// </summary>
    [JsonPropertyName("textColor")]
    public string TextColor { get; set; } = AppColors.TextLight;

    /// <summary>
    /// Background color for the invoice.
    /// </summary>
    [JsonPropertyName("backgroundColor")]
    public string BackgroundColor { get; set; } = AppColors.White;

    /// <summary>
    /// Font family to use. Must be web-safe for email compatibility.
    /// </summary>
    [JsonPropertyName("fontFamily")]
    public string FontFamily { get; set; } = "Arial, Helvetica, sans-serif";

    /// <summary>
    /// Company logo as base64-encoded image data.
    /// </summary>
    [JsonPropertyName("logoBase64")]
    public string? LogoBase64 { get; set; }

    /// <summary>
    /// Logo width in pixels (height auto-scales).
    /// </summary>
    [JsonPropertyName("logoWidth")]
    public int LogoWidth { get; set; } = 150;

    /// <summary>
    /// Custom header text (replaces "INVOICE" if set).
    /// </summary>
    [JsonPropertyName("headerText")]
    public string HeaderText { get; set; } = "INVOICE";

    /// <summary>
    /// Custom footer text (e.g., "Thank you for your business!").
    /// </summary>
    [JsonPropertyName("footerText")]
    public string FooterText { get; set; } = "Thank you for your business!";

    /// <summary>
    /// Payment terms text displayed on the invoice.
    /// </summary>
    [JsonPropertyName("paymentTermsText")]
    public string PaymentTermsText { get; set; } = string.Empty;

    /// <summary>
    /// Bank details or payment instructions.
    /// </summary>
    [JsonPropertyName("paymentInstructions")]
    public string PaymentInstructions { get; set; } = string.Empty;

    /// <summary>
    /// Default notes text pre-filled when creating invoices with this template.
    /// </summary>
    [JsonPropertyName("defaultNotes")]
    public string DefaultNotes { get; set; } = string.Empty;

    /// <summary>
    /// Whether to show the company logo.
    /// </summary>
    [JsonPropertyName("showLogo")]
    public bool ShowLogo { get; set; } = true;

    /// <summary>
    /// Whether to show the company address.
    /// </summary>
    [JsonPropertyName("showCompanyAddress")]
    public bool ShowCompanyAddress { get; set; } = true;

    /// <summary>
    /// Whether to show the company phone number.
    /// </summary>
    [JsonPropertyName("showCompanyPhone")]
    public bool ShowCompanyPhone { get; set; } = true;

    /// <summary>
    /// Whether to show the company city.
    /// </summary>
    [JsonPropertyName("showCompanyCity")]
    public bool ShowCompanyCity { get; set; } = true;

    /// <summary>
    /// Whether to show the company province/state.
    /// </summary>
    [JsonPropertyName("showCompanyProvinceState")]
    public bool ShowCompanyProvinceState { get; set; } = true;

    /// <summary>
    /// Whether to show the company country.
    /// </summary>
    [JsonPropertyName("showCompanyCountry")]
    public bool ShowCompanyCountry { get; set; } = true;

    /// <summary>
    /// Whether to show the tax breakdown.
    /// </summary>
    [JsonPropertyName("showTaxBreakdown")]
    public bool ShowTaxBreakdown { get; set; } = true;

    /// <summary>
    /// Whether to show product/item descriptions.
    /// </summary>
    [JsonPropertyName("showItemDescriptions")]
    public bool ShowItemDescriptions { get; set; } = true;

    // No "show notes" setting: the customer message renders in the invoice footer, always.

    /// <summary>
    /// Whether to show payment instructions.
    /// </summary>
    [JsonPropertyName("showPaymentInstructions")]
    public bool ShowPaymentInstructions { get; set; } = true;

    /// <summary>
    /// Whether to show the due date prominently.
    /// </summary>
    [JsonPropertyName("showDueDateProminent")]
    public bool ShowDueDateProminent { get; set; } = true;

    /// <summary>
    /// When this template was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this template was last modified.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Base64-encoded PNG thumbnail of the template preview.
    /// </summary>
    [JsonPropertyName("thumbnailBase64")]
    public string? ThumbnailBase64 { get; set; }

    /// <summary>
    /// Creates a deep copy of this template.
    /// </summary>
    public InvoiceTemplate Clone()
    {
        return new InvoiceTemplate
        {
            Id = $"custom-{Guid.NewGuid():N}",
            Name = Name,
            BaseTemplate = BaseTemplate,
            IsDefault = IsDefault,
            PrimaryColor = PrimaryColor,
            SecondaryColor = SecondaryColor,
            AccentColor = AccentColor,
            HeaderColor = HeaderColor,
            TextColor = TextColor,
            BackgroundColor = BackgroundColor,
            FontFamily = FontFamily,
            LogoBase64 = LogoBase64,
            LogoWidth = LogoWidth,
            HeaderText = HeaderText,
            FooterText = FooterText,
            PaymentTermsText = PaymentTermsText,
            PaymentInstructions = PaymentInstructions,
            DefaultNotes = DefaultNotes,
            ShowLogo = ShowLogo,
            ShowCompanyAddress = ShowCompanyAddress,
            ShowCompanyPhone = ShowCompanyPhone,
            ShowCompanyCity = ShowCompanyCity,
            ShowCompanyProvinceState = ShowCompanyProvinceState,
            ShowCompanyCountry = ShowCompanyCountry,
            ShowTaxBreakdown = ShowTaxBreakdown,
            ShowItemDescriptions = ShowItemDescriptions,
            ShowPaymentInstructions = ShowPaymentInstructions,
            ShowDueDateProminent = ShowDueDateProminent,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ThumbnailBase64 = ThumbnailBase64
        };
    }
}

/// <summary>
/// Base template types that define the overall layout structure.
/// </summary>
public enum InvoiceTemplateType
{
    /// <summary>
    /// Professional: Corporate look with header banner, structured layout.
    /// </summary>
    Professional,

    /// <summary>
    /// Modern: Clean, minimalist design with plenty of whitespace.
    /// </summary>
    Modern,

    /// <summary>
    /// Classic: Traditional invoice layout with borders and lines.
    /// </summary>
    Classic,

    /// <summary>
    /// Elegant: Sophisticated design with accent borders and refined typography.
    /// </summary>
    Elegant,

    /// <summary>
    /// Ribbon: Flowing design with decorative side wave ribbons and open layout.
    /// </summary>
    Ribbon
}
