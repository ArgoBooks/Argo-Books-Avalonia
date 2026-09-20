using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Invoices;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services.InvoiceTemplates;

/// <summary>
/// Renders invoice HTML from templates and data.
/// Uses a simple mustache-like template syntax.
/// </summary>
public partial class InvoiceHtmlRenderer
{
    // All money amounts on an invoice are formatted with InvariantCulture so a non-US machine locale
    // can't render a hybrid like "$1.234,56" on a customer-facing document. The number of decimals
    // is the currency's, not the culture's: yen has no subunit, so "¥5,000.00" is wrong everywhere.
    private static string Money(decimal amount, int decimals) =>
        amount.ToString($"N{decimals}", System.Globalization.CultureInfo.InvariantCulture);

    private static int DecimalsFor(Invoice invoice) =>
        Models.Common.CurrencyInfo.GetByCode(
            string.IsNullOrEmpty(invoice.OriginalCurrency) ? "USD" : invoice.OriginalCurrency).DecimalPlaces;

    // Dates carry the same decision as the money: the labels around them are hardcoded English, so a
    // French or German machine must not print "14 août 2026" next to "Invoice Date".
    private static string InvoiceDate(DateTime date) =>
        date.ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);

    // Raw numbers for the editable paper's own fields. The browser parses these with parseFloat, so a
    // comma decimal separator from the machine locale would be read as a whole number.
    private static string Raw(decimal value) =>
        value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    // A stored figure that went negative (an older save, an imported sheet) is still not something to
    // put in front of a customer. The math that produces them cannot go below zero any more.
    private static decimal NonNegative(decimal amount) => Math.Max(0m, amount);

    // " CAD" style suffix appended to the amount-due rows so the currency is unambiguous.
    private static string CurrencyCodeSuffix(Invoice invoice) =>
        string.IsNullOrEmpty(invoice.OriginalCurrency) ? string.Empty : $" {invoice.OriginalCurrency}";

    /// <summary>
    /// Renders an invoice to HTML using the specified template.
    /// </summary>
    /// <param name="invoice">The invoice to render.</param>
    /// <param name="template">The template to use for rendering.</param>
    /// <param name="companyData">Company data for company info and customer lookup.</param>
    /// <param name="currencySymbol">Currency symbol to use (defaults to $).</param>
    /// <param name="editable">True when rendering for the in-app editor, which keeps the tax, shipping and discount rows and the on-paper input fields.</param>
    /// <param name="labels">The wording to print around the figures. Defaults to the invoice wording.</param>
    /// <returns>The rendered HTML string.</returns>
    public string RenderInvoice(
        Invoice invoice,
        InvoiceTemplate template,
        CompanyData companyData,
        string currencySymbol = "$",
        bool editable = false,
        DocumentLabels? labels = null)
    {
        var customer = companyData.GetCustomer(invoice.CustomerId);
        var companySettings = companyData.Settings;

        var html = InvoiceHtmlTemplates.GetTemplate(template.BaseTemplate);

        // Build the data context for template rendering
        var context = BuildContext(invoice, template, customer, companySettings, currencySymbol, lockAspectRatio: true, companyData.Payments, editable, labels);

        html = ProcessTemplate(html, context);

        return html;
    }

    /// <summary>
    /// Renders a quote to HTML with the same templates an invoice uses, so the customer sees the
    /// layout the business already chose. The quote is mapped onto a throwaway invoice because the
    /// templates take one; nothing about it is stored.
    /// </summary>
    public string RenderQuote(
        Quote quote,
        InvoiceTemplate template,
        CompanyData companyData,
        string currencySymbol = "$") =>
        RenderInvoice(quote.ToRenderableInvoice(), template, companyData, currencySymbol,
            editable: false, DocumentLabels.ForQuote(template.HeaderText));

    /// <summary>
    /// Renders a preview invoice (for template designer) with sample data.
    /// </summary>
    public string RenderPreview(InvoiceTemplate template, CompanySettings companySettings, bool lockAspectRatio = true)
    {
        // Create sample invoice data for preview
        var sampleInvoice = new Invoice
        {
            Id = "INV-0001",
            InvoiceNumber = "INV-0001",
            IssueDate = DateTime.Today,
            DueDate = DateTime.Today.AddDays(30),
            Subtotal = 1250.00m,
            TaxRate = 10m,
            TaxAmount = 125.00m,
            Total = 1375.00m,
            AmountPaid = 0m,
            Balance = 1375.00m,
            Notes = !string.IsNullOrWhiteSpace(template.DefaultNotes)
                ? template.DefaultNotes
                : "Example notes section. This area can contain additional information for your customer.",
            LineItems =
            [
                new() { Description = "Example Product 1", Quantity = 1, UnitPrice = 800.00m },
                new() { Description = "Example Product 2", Quantity = 1, UnitPrice = 250.00m },
                new() { Description = "Example Product 3", Quantity = 2, UnitPrice = 100.00m }
            ]
        };

        var sampleCustomer = new Models.Entities.Customer
        {
            Name = "Example Customer",
            Email = "customer@example.com",
            Address = new Models.Common.Address
            {
                Street = "123 Example Street",
                City = "Example City",
                State = "EX",
                ZipCode = "12345",
                Country = "Example Country"
            }
        };

        // Override company info with generic placeholders for the preview
        var previewCompanySettings = new CompanySettings
        {
            Company = new CompanyInfo
            {
                Name = "Your Company",
                Address = companySettings.Company.Address ?? "Your Address",
                Email = companySettings.Company.Email ?? "your@email.com",
                Phone = companySettings.Company.Phone ?? "Your Phone",
                City = companySettings.Company.City ?? "Your City",
                ProvinceState = companySettings.Company.ProvinceState ?? "Your Province",
                Country = companySettings.Company.Country ?? "Your Country"
            }
        };

        var html = InvoiceHtmlTemplates.GetTemplate(template.BaseTemplate);
        var context = BuildContext(sampleInvoice, template, sampleCustomer, previewCompanySettings, "$", lockAspectRatio, payments: null);
        return ProcessTemplate(html, context);
    }

    /// <summary>
    /// Generates plain text version of the invoice for email fallback.
    /// </summary>
    public string RenderPlainText(
        Invoice invoice,
        InvoiceTemplate template,
        CompanyData companyData,
        string currencySymbol = "$",
        DocumentLabels? labels = null)
    {
        labels ??= DocumentLabels.Invoice;
        var customer = companyData.GetCustomer(invoice.CustomerId);
        var companySettings = companyData.Settings;
        var decimals = DecimalsFor(invoice);
        var sb = new StringBuilder();

        // Header
        sb.AppendLine($"{labels.HeaderText ?? template.HeaderText}");
        sb.AppendLine(new string('=', 50));
        sb.AppendLine();

        // Company info
        sb.AppendLine(companySettings.Company.Name);
        if (!string.IsNullOrWhiteSpace(companySettings.Company.Address))
            sb.AppendLine(companySettings.Company.Address);
        if (!string.IsNullOrWhiteSpace(companySettings.Company.Email))
            sb.AppendLine(companySettings.Company.Email);
        sb.AppendLine();

        // Invoice details
        sb.AppendLine($"{labels.NumberLabel}: {invoice.InvoiceNumber}");
        sb.AppendLine($"Date: {InvoiceDate(invoice.IssueDate)}");
        sb.AppendLine($"{labels.DueDateLabel}: {InvoiceDate(invoice.DueDate)}");
        sb.AppendLine();

        // Bill to
        sb.AppendLine("BILL TO:");
        sb.AppendLine(customer?.Name ?? "Unknown Customer");
        var customerAddress = customer?.Address.ToString();
        if (!string.IsNullOrWhiteSpace(customerAddress))
            sb.AppendLine(customerAddress);
        if (!string.IsNullOrWhiteSpace(customer?.Email))
            sb.AppendLine(customer.Email);
        sb.AppendLine();

        // Line items
        sb.AppendLine(new string('-', 50));
        sb.AppendLine("ITEMS:");
        sb.AppendLine(new string('-', 50));

        foreach (var item in invoice.LineItems)
        {
            sb.AppendLine($"{item.Description}");
            sb.AppendLine($"  {item.Quantity} x {currencySymbol}{Money(item.UnitPrice, decimals)} = {currencySymbol}{Money(item.Subtotal, decimals)}");
        }

        sb.AppendLine(new string('-', 50));

        // Totals
        sb.AppendLine($"Subtotal: {currencySymbol}{Money(NonNegative(invoice.Subtotal), decimals)}");
        if (invoice.TaxRate > 0)
        {
            var taxLabel = GetTaxLabel(companySettings.Company.Country);
            // A fixed tax stores a dollar amount in TaxRate, so only a percent tax gets a "(x%)" suffix.
            var taxRateSuffix = invoice.TaxIsFixed ? "" : $" ({invoice.TaxRate}%)";
            sb.AppendLine($"{taxLabel}{taxRateSuffix}: {currencySymbol}{Money(NonNegative(invoice.TaxAmount), decimals)}");
        }
        // Shipping is part of the stored Total, so the breakdown must list it or it won't sum to TOTAL.
        if (invoice.ShippingAmount > 0)
            sb.AppendLine($"Shipping: {currencySymbol}{Money(invoice.ShippingAmount, decimals)}");
        if (invoice.SecurityDeposit > 0)
            sb.AppendLine($"Security Deposit: {currencySymbol}{Money(invoice.SecurityDeposit, decimals)}");
        if (invoice.CustomFeeAmount > 0)
            sb.AppendLine($"{BuildFeeLabel(invoice)}: {currencySymbol}{Money(CalculateCustomFee(invoice), decimals)}");
        if (invoice.DiscountAmount > 0)
        {
            var discountLabel = invoice.DiscountIsPercent ? $"Discount ({invoice.DiscountAmount}%)" : "Discount";
            sb.AppendLine($"{discountLabel}: -{currencySymbol}{Money(CalculateDiscount(invoice), decimals)}");
        }
        sb.AppendLine($"TOTAL: {currencySymbol}{Money(NonNegative(invoice.Total), decimals)}");

        if (invoice.AmountPaid > 0)
        {
            sb.AppendLine($"Amount Paid: -{currencySymbol}{Money(invoice.AmountPaid, decimals)}");
            sb.AppendLine($"Balance Due: {currencySymbol}{Money(invoice.Balance, decimals)}");
        }

        sb.AppendLine();

        // The customer message, which the HTML invoice always prints in its footer.
        if (!string.IsNullOrWhiteSpace(invoice.Notes))
        {
            sb.AppendLine("NOTES:");
            sb.AppendLine(invoice.Notes);
            sb.AppendLine();
        }

        if (!labels.HidePaymentDetails && template.ShowPaymentInstructions && !string.IsNullOrWhiteSpace(template.PaymentInstructions))
        {
            sb.AppendLine("PAYMENT INSTRUCTIONS:");
            sb.AppendLine(template.PaymentInstructions);
            sb.AppendLine();
        }

        // Footer
        sb.AppendLine(new string('=', 50));
        sb.AppendLine(template.FooterText);

        return sb.ToString();
    }

    private Dictionary<string, object?> BuildContext(
        Invoice invoice,
        InvoiceTemplate template,
        Models.Entities.Customer? customer,
        CompanySettings companySettings,
        string currencySymbol,
        bool lockAspectRatio,
        IEnumerable<Payment>? payments,
        bool editable = false,
        DocumentLabels? labels = null)
    {
        labels ??= DocumentLabels.Invoice;
        var isOverdue = invoice.DueDate.Date < DateTime.UtcNow.Date &&
                        invoice.Balance > 0;
        var decimals = DecimalsFor(invoice);

        // Processing-fee row logic:
        //  - If the customer has already paid online with a fee (sum of
        //    Payment.ProcessingFee > 0), show the actual fee, even when
        //    Balance == 0. This is the user-visible record of what they
        //    were actually charged.
        //  - Otherwise, if the invoice is unpaid and the portal is
        //    configured, show the *estimated* fee they would pay if they
        //    chose to pay through the portal.
        // The "Amount to Pay" row (= Balance + estimated fee) only makes
        // sense when there is still a balance the customer can pay
        // online. For paid invoices we want the plain "Total" row.
        var invoiceCurrency = string.IsNullOrEmpty(invoice.OriginalCurrency)
            ? "USD" : invoice.OriginalCurrency;
        var actualProcessingFee = payments?
            .Where(p => p.InvoiceId == invoice.Id
                        && !p.IsRefund
                        && string.Equals(
                            string.IsNullOrEmpty(p.OriginalCurrency) ? "USD" : p.OriginalCurrency,
                            invoiceCurrency,
                            StringComparison.OrdinalIgnoreCase))
            .Sum(p => p.ProcessingFee) ?? 0m;

        var portalConfigured = IsPortalConfigured(companySettings);
        // Passing the fee to the customer is a per-invoice setting, defaulting to on.
        var passProcessingFee = invoice.PassProcessingFee ?? true;
        var feesActive = portalConfigured && passProcessingFee;
        var hasUnpaidBalance = invoice.Balance > 0;
        var estimatedProcessingFee = hasUnpaidBalance && feesActive
            ? CalculateProcessingFee(invoice.Balance)
            : 0m;
        // Fees already charged AND the estimate on what is still owed. The totals
        // column has to reconcile a gross Amount Paid against a forward-looking
        // Amount to Pay, so showing only one of the two would never add up.
        var displayProcessingFee = actualProcessingFee + estimatedProcessingFee;
        // In the editor, keep the fee row present whenever the fee applies so the
        // live recompute can fill it in as the user types (even from a $0 start).
        var showProcessingFeeRow = (displayProcessingFee > 0 || (editable && feesActive))
                                   && !labels.HidePaymentDetails;

        var context = new Dictionary<string, object?>
        {
            // Document wording. Invoice by default, so the templates read as they always did.
            ["DocumentName"] = labels.DocumentName,
            ["NumberLabel"] = labels.NumberLabel,
            ["ReceiptNumberLabel"] = labels.ReceiptNumberLabel,
            ["DueDateLabel"] = labels.DueDateLabel,
            ["DueLabelShort"] = labels.DueLabelShort,
            ["DueLabelShortUpper"] = labels.DueLabelShortUpper,

            // Template styling
            ["FontFamily"] = template.FontFamily,
            ["PrimaryColor"] = template.PrimaryColor,
            ["SecondaryColor"] = template.SecondaryColor,
            ["AccentColor"] = template.AccentColor,
            ["HeaderColor"] = !string.IsNullOrEmpty(template.HeaderColor) ? template.HeaderColor : template.PrimaryColor,
            ["TextColor"] = template.TextColor,
            ["BackgroundColor"] = template.BackgroundColor,

            // Template settings
            ["HeaderText"] = labels.HeaderText ?? template.HeaderText,
            ["FooterText"] = template.FooterText,
            ["PaymentInstructions"] = template.PaymentInstructions,
            // Per-invoice overrides win over the template setting when present (invoice.X ?? template.X).
            // "Show company address" hides the whole company location line (address + city/state/country).
            ["ShowLogo"] = template.ShowLogo && !string.IsNullOrEmpty(template.LogoBase64),
            ["ShowCompanyAddress"] = invoice.ShowCompanyAddress ?? template.ShowCompanyAddress,
            ["ShowCompanyPhone"] = invoice.ShowCompanyPhone ?? template.ShowCompanyPhone,
            ["ShowCompanyCity"] = invoice.ShowCompanyAddress ?? template.ShowCompanyCity,
            ["ShowCompanyProvinceState"] = invoice.ShowCompanyAddress ?? template.ShowCompanyProvinceState,
            ["ShowCompanyCountry"] = invoice.ShowCompanyAddress ?? template.ShowCompanyCountry,
            ["ShowTaxBreakdown"] = template.ShowTaxBreakdown && invoice.TaxAmount > 0,
            ["ShowItemDescriptions"] = template.ShowItemDescriptions,
            ["ShowPaymentInstructions"] = !labels.HidePaymentDetails && template.ShowPaymentInstructions && !string.IsNullOrWhiteSpace(template.PaymentInstructions),
            ["ShowDueDateProminent"] = invoice.ShowDueDateProminent ?? template.ShowDueDateProminent,

            // Logo
            ["LogoSrc"] = template.ShowLogo && !string.IsNullOrEmpty(template.LogoBase64)
                ? $"data:image/png;base64,{template.LogoBase64}"
                : "",
            ["LogoWidth"] = template.LogoWidth.ToString(),
            ["LockAspectRatio"] = lockAspectRatio,

            // Company info
            ["CompanyName"] = companySettings.Company.Name,
            ["CompanyAddress"] = FormatAddress(companySettings.Company.Address),
            ["CompanyEmail"] = companySettings.Company.Email,
            ["CompanyPhone"] = companySettings.Company.Phone,
            ["CompanyCity"] = companySettings.Company.City,
            ["CompanyProvinceState"] = companySettings.Company.ProvinceState,
            ["CompanyCountry"] = companySettings.Company.Country,

            // Customer info
            // Empty (not "Unknown Customer") when no customer is picked yet, so the editable field on
            // the paper reads as a blank, clickable placeholder rather than a bogus name.
            ["CustomerName"] = customer?.Name ?? string.Empty,
            ["CustomerAddress"] = FormatAddress(customer?.Address),
            ["CustomerEmail"] = customer?.Email,

            // Invoice details
            ["InvoiceNumber"] = invoice.InvoiceNumber,
            ["IssueDate"] = InvoiceDate(invoice.IssueDate),
            ["DueDate"] = InvoiceDate(invoice.DueDate),
            ["IsOverdue"] = isOverdue,

            // Financial
            ["Subtotal"] = $"{currencySymbol}{Money(NonNegative(invoice.Subtotal), decimals)}",
            ["TaxRate"] = Raw(invoice.TaxRate),
            ["TaxLabel"] = GetTaxLabel(companySettings.Company.Country),
            // The "(13%)" suffix on the tax label only makes sense in percent mode.
            ["TaxRateLabel"] = invoice.TaxIsFixed ? "" : $" ({Raw(invoice.TaxRate)}%)",
            ["TaxAmount"] = $"{currencySymbol}{Money(NonNegative(invoice.TaxAmount), decimals)}",
            // The tax row is always shown in the editor so it can be edited, even if the template
            // normally hides the breakdown.
            ["ShowTaxRow"] = template.ShowTaxBreakdown || editable,
            ["ShowSecurityDeposit"] = invoice.SecurityDeposit > 0,
            ["SecurityDeposit"] = $"{currencySymbol}{Money(invoice.SecurityDeposit, decimals)}",
            ["ShowShipping"] = invoice.ShippingAmount > 0 || editable,
            ["ShippingAmount"] = $"{currencySymbol}{Money(invoice.ShippingAmount, decimals)}",
            // The custom fee carries a user-defined label, so only surface it once one is actually set
            // (it stays editable on the paper when present); tax/shipping/discount are always editable.
            ["ShowCustomFee"] = invoice.CustomFeeAmount > 0,
            ["CustomFeeLabel"] = BuildFeeLabel(invoice),
            ["CustomFeeAmount"] = $"{currencySymbol}{Money(CalculateCustomFee(invoice), decimals)}",
            ["ShowDiscount"] = invoice.DiscountAmount > 0 || editable,
            ["DiscountAmount"] = $"-{currencySymbol}{Money(CalculateDiscount(invoice), decimals)}",
            // Raw values + modes for the on-paper totals editors (the editing script builds the
            // input + %/fixed swap from these; the customer view just shows the computed amounts above).
            ["Editable"] = editable,
            ["CurrencySymbol"] = currencySymbol,
            ["TaxRateRaw"] = Raw(invoice.TaxRate),
            ["TaxModeRaw"] = invoice.TaxIsFixed ? "fixed" : "percent",
            ["ShippingRaw"] = Raw(invoice.ShippingAmount),
            ["CustomFeeRaw"] = Raw(invoice.CustomFeeAmount),
            ["FeeModeRaw"] = invoice.CustomFeeIsPercent ? "percent" : "fixed",
            ["DiscountRaw"] = Raw(invoice.DiscountAmount),
            ["DiscountModeRaw"] = invoice.DiscountIsPercent ? "percent" : "fixed",
            ["Total"] = $"{currencySymbol}{Money(NonNegative(invoice.Total), decimals)}{CurrencyCodeSuffix(invoice)}",
            // Already gross: portal payments are stored at the amount the customer
            // was actually charged, fee included (see PaymentPortalService). Adding
            // the fee again here double-counts it.
            ["AmountPaid"] = invoice.AmountPaid > 0 ? $"{currencySymbol}{Money(invoice.AmountPaid, decimals)}" : null,
            ["Balance"] = $"{currencySymbol}{Money(NonNegative(invoice.Balance), decimals)}{CurrencyCodeSuffix(invoice)}",

            // Every fee touching this invoice: already charged, plus the estimate
            // on what is still owed. Both belong in the column, the first to offset
            // the gross Amount Paid above and the second to reach Amount to Pay.
            ["ShowProcessingFee"] = showProcessingFeeRow,
            ["ProcessingFeeLabel"] = BuildProcessingFeeLabel(companySettings),
            ["ProcessingFeeAmount"] = showProcessingFeeRow
                ? $"{currencySymbol}{Money(displayProcessingFee, decimals)}"
                : "",
            // The only headline figure on the invoice, so it always renders, including
            // the 0.00 on a settled invoice. A quote asks for no money and hides it.
            ["ShowAmountToPay"] = !labels.HidePaymentDetails,
            ["AmountToPay"] = $"{currencySymbol}{Money(NonNegative(invoice.Balance) + estimatedProcessingFee, decimals)}{CurrencyCodeSuffix(invoice)}",

            // The footer is where the customer message lives: the invoice's Notes, falling back to the
            // template's default footer text when the user hasn't set notes. Editable on the paper.
            ["FooterOrNotes"] = !string.IsNullOrEmpty(invoice.Notes) ? invoice.Notes : (template.FooterText),
            // ISO dates so the paper's date editor (an <input type=date>) can pre-fill.
            ["IssueDateIso"] = invoice.IssueDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["DueDateIso"] = invoice.DueDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),

            // Line items (as a list of dictionaries). Index drives the editor's data-line-index so
            // an edit on the paper can be routed back to the right row.
            ["LineItems"] = invoice.LineItems.Select((item, i) => new Dictionary<string, object?>
            {
                ["Index"] = i,
                ["Description"] = item.Description,
                ["ItemDescription"] = null, // Can be extended for product descriptions
                ["Quantity"] = Raw(item.Quantity),
                ["UnitPrice"] = $"{currencySymbol}{Money(item.UnitPrice, decimals)}",
                // LineItem.Subtotal, which is quantity x price LESS the discount and is what the
                // invoice Subtotal is summed from. Printing quantity x price put an Amount on the
                // line that did not add up to the total beneath it on a discounted invoice.
                ["Amount"] = $"{currencySymbol}{Money(item.Subtotal, decimals)}",
                // Carried onto the paper so the browser's live recompute can subtract it too; it has
                // no editor of its own, and without it an edit anywhere restated the line at full price.
                ["LineDiscountRaw"] = Raw(item.Discount)
            }).ToList()
        };

        return context;
    }

    private string ProcessTemplate(string template, Dictionary<string, object?> context)
    {
        var result = template;

        // Process sections ({{#Section}}...{{/Section}} and {{^Section}}...{{/Section}})
        result = ProcessSections(result, context);

        // Process loops ({{#ListName}}...{{/ListName}})
        result = ProcessLoops(result, context);

        // Process simple variables ({{VariableName}})
        result = ProcessVariables(result, context);

        return result;
    }

    private string ProcessSections(string template, Dictionary<string, object?> context)
    {
        var positiveSectionRegex = PositiveSectionRegex();
        var negativeSectionRegex = NegativeSectionRegex();

        // Process sections in a loop to handle nested sections
        string previous;
        do
        {
            previous = template;

            // Process positive sections {{#Name}}content{{/Name}}
            template = positiveSectionRegex.Replace(template, match =>
            {
                var name = match.Groups[1].Value;
                var content = match.Groups[2].Value;

                if (context.TryGetValue(name, out var value))
                {
                    // If it's a list, skip here (handled by ProcessLoops)
                    if (value is IEnumerable<Dictionary<string, object?>>)
                        return match.Value;

                    // If it's a truthy value, include the content
                    if (IsTruthy(value))
                        return content;
                }

                return string.Empty;
            });

            // Process negative sections {{^Name}}content{{/Name}}
            template = negativeSectionRegex.Replace(template, match =>
            {
                var name = match.Groups[1].Value;
                var content = match.Groups[2].Value;

                if (context.TryGetValue(name, out var value))
                {
                    // If it's a falsy value, include the content
                    if (!IsTruthy(value))
                        return content;
                }
                else
                {
                    // Variable not found, treat as falsy
                    return content;
                }

                return string.Empty;
            });
        }
        while (template != previous); // Continue until no more changes

        return template;
    }

    private string ProcessLoops(string template, Dictionary<string, object?> context)
    {
        var loopRegex = LoopRegex();

        return loopRegex.Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            var itemTemplate = match.Groups[2].Value;

            if (context.TryGetValue(name, out var value) &&
                value is IEnumerable<Dictionary<string, object?>> items)
            {
                var sb = new StringBuilder();
                foreach (var item in items)
                {
                    // Merge item context with parent context
                    var itemContext = new Dictionary<string, object?>(context);
                    foreach (var kvp in item)
                    {
                        itemContext[kvp.Key] = kvp.Value;
                    }

                    var processedItem = ProcessSections(itemTemplate, itemContext);
                    processedItem = ProcessVariables(processedItem, itemContext);
                    sb.Append(processedItem);
                }
                return sb.ToString();
            }

            return string.Empty;
        });
    }

    // Fields whose values already contain safe HTML (e.g. <br> from FormatAddress)
    private static readonly HashSet<string> RawHtmlFields = new(StringComparer.Ordinal)
    {
        "CustomerAddress", "CompanyAddress"
    };

    private static string ProcessVariables(string template, Dictionary<string, object?> context)
    {
        var variableRegex = VariableRegex();

        return variableRegex.Replace(template, match =>
        {
            var name = match.Groups[1].Value;

            if (context.TryGetValue(name, out var value) && value != null)
            {
                // Skip encoding for fields that already contain safe HTML (e.g. <br> tags)
                if (RawHtmlFields.Contains(name))
                    return value.ToString() ?? string.Empty;

                // HTML-encode all other values to prevent XSS when rendering in browser
                return WebUtility.HtmlEncode(value.ToString()) ?? string.Empty;
            }

            return string.Empty;
        });
    }

    private static bool IsTruthy(object? value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            string s => !string.IsNullOrWhiteSpace(s),
            int i => i != 0,
            decimal d => d != 0,
            IEnumerable<object> list => list.Any(),
            _ => true
        };
    }

    private static string? FormatAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;

        // Encode each line individually then join with <br> for HTML
        return string.Join("<br>", address.Replace("\r", "").Split('\n')
            .Select(WebUtility.HtmlEncode));
    }

    private static string? FormatAddress(Models.Common.Address? address)
    {
        if (address == null)
            return null;

        var formatted = address.ToString();
        if (string.IsNullOrWhiteSpace(formatted))
            return null;

        // Encode each part individually then join with <br> for HTML display
        return string.Join("<br>", formatted.Split(", ")
            .Select(WebUtility.HtmlEncode));
    }

    private static decimal CalculateCustomFee(Invoice invoice) =>
        InvoiceMath.CustomFee(NonNegative(invoice.Subtotal), invoice.CustomFeeAmount, invoice.CustomFeeIsPercent);

    // Capped at the subtotal it comes off, so the rows the customer adds up can't come to less than nothing.
    private static decimal CalculateDiscount(Invoice invoice) =>
        InvoiceMath.Discount(NonNegative(invoice.Subtotal), invoice.DiscountAmount, invoice.DiscountIsPercent);

    private static string BuildFeeLabel(Invoice invoice)
    {
        var label = !string.IsNullOrWhiteSpace(invoice.CustomFeeLabel) ? invoice.CustomFeeLabel : "Fee";
        return invoice.CustomFeeIsPercent ? $"{label} ({invoice.CustomFeeAmount}%)" : label;
    }

    private static string BuildProcessingFeeLabel(CompanySettings companySettings)
    {
        var providers = new List<string>();
        var accounts = companySettings.PaymentPortal.ConnectedAccounts;
        if (accounts.StripeConnected) providers.Add("Stripe");
        if (accounts.PaypalConnected) providers.Add("PayPal");
        if (accounts.SquareConnected) providers.Add("Square");

        var providerName = providers.Count > 0 ? string.Join(" / ", providers) : "Payment";
        return $"{providerName} processing fee";
    }

    /// <summary>
    /// True when at least one online payment provider is connected, i.e.
    /// the customer can actually pay through the portal, which is when a
    /// processing fee would be charged on top of the invoice balance.
    /// </summary>
    private static bool IsPortalConfigured(CompanySettings companySettings)
    {
        var accounts = companySettings.PaymentPortal.ConnectedAccounts;
        return accounts.StripeConnected || accounts.PaypalConnected || accounts.SquareConnected;
    }

    /// <summary>
    /// Calculate the payment processing fee (2.90% + $0.30 flat).
    /// The $0.30 is applied in the invoice's currency here; the server-side
    /// calculate_invoice_processing_fee() in config/pricing.php converts from CAD.
    /// </summary>
    private static decimal CalculateProcessingFee(decimal amount)
    {
        if (amount <= 0) return 0m;
        const decimal percent = 2.90m;
        const decimal fixedFee = 0.30m;
        return Math.Round(amount * percent / 100m + fixedFee, 2);
    }

    /// <summary>
    /// Returns the country-appropriate tax label (e.g., "VAT", "GST/HST", "GST", "Tax", "Sales Tax")
    /// for use on invoices.
    /// </summary>
    private static string GetTaxLabel(string? country)
    {
        var normalized = country?.Trim().ToUpperInvariant() ?? "";
        return normalized switch
        {
            "UNITED KINGDOM" or "FRANCE" or "GERMANY" or "ITALY" or "SPAIN" or "NETHERLANDS"
            or "BELGIUM" or "AUSTRIA" or "SWEDEN" or "NORWAY" or "DENMARK" or "FINLAND"
            or "IRELAND" or "PORTUGAL" or "GREECE" or "SWITZERLAND" or "POLAND"
            or "CZECH REPUBLIC" or "CZECHIA" or "HUNGARY" or "ROMANIA" or "BULGARIA" or "CROATIA"
            or "SLOVAKIA" or "SLOVENIA" or "LITHUANIA" or "LATVIA" or "ESTONIA"
            or "LUXEMBOURG" or "MALTA" or "CYPRUS" or "SOUTH AFRICA" or "KENYA"
            or "NIGERIA" or "GHANA" or "ZIMBABWE" or "BOTSWANA"
            or "BANGLADESH" or "SRI LANKA" or "JAMAICA" or "TRINIDAD AND TOBAGO"
            or "TURKEY" or "RUSSIA" or "UKRAINE" or "BRAZIL"
            or "ARGENTINA" or "CHILE" or "COLOMBIA" or "MEXICO" or "PERU"
            or "ISRAEL" or "UNITED ARAB EMIRATES" or "SAUDI ARABIA" or "THAILAND"
            or "VIETNAM" or "INDONESIA" or "PHILIPPINES" or "SOUTH KOREA"
            or "CHINA" or "TAIWAN" or "ICELAND" => "VAT",
            "CANADA" => "GST/HST",
            "INDIA" or "SINGAPORE" or "MALAYSIA" or "AUSTRALIA" or "NEW ZEALAND"
            or "PAKISTAN" => "GST",
            "JAPAN" => "Consumption Tax",
            "UNITED STATES" or "PUERTO RICO" => "Sales Tax",
            _ => "Tax"
        };
    }

    [GeneratedRegex(@"\{\{#(\w+)\}\}([\s\S]*?)\{\{/\1\}\}", RegexOptions.Compiled)]
    private static partial Regex PositiveSectionRegex();

    [GeneratedRegex(@"\{\{\^(\w+)\}\}([\s\S]*?)\{\{/\1\}\}", RegexOptions.Compiled)]
    private static partial Regex NegativeSectionRegex();

    [GeneratedRegex(@"\{\{#(\w+)\}\}([\s\S]*?)\{\{/\1\}\}", RegexOptions.Compiled)]
    private static partial Regex LoopRegex();

    [GeneratedRegex(@"\{\{(\w+)\}\}", RegexOptions.Compiled)]
    private static partial Regex VariableRegex();
}
