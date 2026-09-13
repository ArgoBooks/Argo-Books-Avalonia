using ArgoBooks.Core.Models.Invoices;

namespace ArgoBooks.Core.Services.InvoiceTemplates;

/// <summary>
/// Contains base HTML templates for invoice emails.
/// All templates use email-safe HTML (table-based layouts, inline CSS).
/// </summary>
public static class InvoiceHtmlTemplates
{
    /// <summary>
    /// Professional template: Corporate look with colored header banner.
    /// </summary>
    public const string Professional = """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <meta name="color-scheme" content="light">
    <title>Invoice {{InvoiceNumber}}</title>
</head>
<body style="margin: 0; padding: 0; font-family: {{FontFamily}}; background-color: #f3f4f6;">
    <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background-color: #f3f4f6;">
        <tr>
            <td align="center" style="padding: 40px 20px;">
                <table role="presentation" cellpadding="0" cellspacing="0" width="600" style="background-color: {{BackgroundColor}}; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,0.1);">
                    <!-- Header -->
                    <tr>
                        <td style="background-color: {{PrimaryColor}}; padding: 30px 40px; border-radius: 8px 8px 0 0;">
                            <table role="presentation" cellpadding="0" cellspacing="0" width="100%">
                                <tr>
                                    <td>
                                        {{#ShowLogo}}
                                        <img src="{{LogoSrc}}" alt="Company Logo" data-logo="1" style="display: inline-block; vertical-align: middle; max-width: {{LogoWidth}}px; max-height: 60px; width: auto; height: auto; object-fit: contain; margin-right: 14px;">
                                        {{/ShowLogo}}
                                        <span data-logo-slot="1" style="display: inline-block; vertical-align: middle; font-size: 24px; font-weight: bold; color: #ffffff;">{{CompanyName}}</span>
                                    </td>
                                    <td align="right" style="vertical-align: top;">
                                        <span style="font-size: 28px; font-weight: bold; color: #ffffff; letter-spacing: 1px;">{{HeaderText}}</span>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    <!-- Invoice Info -->
                    <tr>
                        <td style="padding: 30px 40px 20px 40px;">
                            <table role="presentation" cellpadding="0" cellspacing="0" width="100%">
                                <tr>
                                    <td style="vertical-align: top; width: 50%;">
                                        <p style="margin: 0 0 5px 0; font-size: 12px; color: #6b7280; text-transform: uppercase; letter-spacing: 0.5px;">Bill To</p>
                                        <p style="margin: 0 0 5px 0; font-size: 16px; font-weight: 600; color: {{TextColor}};"><span data-field="customer">{{CustomerName}}</span></p>
                                        {{#CustomerAddress}}
                                        <p style="margin: 0; font-size: 14px; color: #6b7280; line-height: 1.5;">{{CustomerAddress}}</p>
                                        {{/CustomerAddress}}
                                        {{#CustomerEmail}}
                                        <p style="margin: 5px 0 0 0; font-size: 14px; color: #6b7280;">{{CustomerEmail}}</p>
                                        {{/CustomerEmail}}
                                    </td>
                                    <td style="vertical-align: top; text-align: right;">
                                        <table role="presentation" cellpadding="0" cellspacing="0" style="margin-left: auto;">
                                            <tr>
                                                <td style="padding: 4px 15px 4px 0; font-size: 13px; color: #6b7280;">Invoice #</td>
                                                <td style="padding: 4px 0; font-size: 13px; font-weight: 600; color: {{TextColor}};">{{InvoiceNumber}}</td>
                                            </tr>
                                            <tr>
                                                <td style="padding: 4px 15px 4px 0; font-size: 13px; color: #6b7280;">Issue Date</td>
                                                <td style="padding: 4px 0; font-size: 13px; color: {{TextColor}};"><span data-field="issueDate" data-iso="{{IssueDateIso}}">{{IssueDate}}</span></td>
                                            </tr>
                                            <tr>
                                                <td style="padding: 4px 15px 4px 0; font-size: 13px; color: #6b7280;">Due Date</td>
                                                <td style="padding: 4px 0; font-size: 13px; {{#IsOverdue}}color: #dc2626; font-weight: 600;{{/IsOverdue}}{{^IsOverdue}}color: {{TextColor}};{{/IsOverdue}}"><span data-field="dueDate" data-iso="{{DueDateIso}}">{{DueDate}}</span></td>
                                            </tr>
                                            {{#ShowDueDateProminent}}
                                            <tr>
                                                <td colspan="2" style="padding-top: 10px;">
                                                    <span style="display: inline-block; background-color: {{#IsOverdue}}#fef2f2{{/IsOverdue}}{{^IsOverdue}}#f0fdf4{{/IsOverdue}}; color: {{#IsOverdue}}#dc2626{{/IsOverdue}}{{^IsOverdue}}{{AccentColor}}{{/IsOverdue}}; padding: 6px 12px; border-radius: 4px; font-size: 12px; font-weight: 600;">
                                                        {{#IsOverdue}}OVERDUE{{/IsOverdue}}{{^IsOverdue}}DUE: {{DueDate}}{{/IsOverdue}}
                                                    </span>
                                                </td>
                                            </tr>
                                            {{/ShowDueDateProminent}}
                                        </table>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    <!-- Line Items -->
                    <tr>
                        <td style="padding: 0 40px;">
                            <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="border-collapse: collapse;">
                                <tr style="background-color: {{SecondaryColor}};">
                                    <td style="padding: 12px 15px; font-size: 12px; font-weight: 600; color: #374151; text-transform: uppercase; border-radius: 4px 0 0 4px;">Description</td>
                                    <td style="padding: 12px 15px; font-size: 12px; font-weight: 600; color: #374151; text-transform: uppercase; text-align: center; width: 80px;">Qty</td>
                                    <td style="padding: 12px 15px; font-size: 12px; font-weight: 600; color: #374151; text-transform: uppercase; text-align: right; width: 100px;">Price</td>
                                    <td style="padding: 12px 15px; font-size: 12px; font-weight: 600; color: #374151; text-transform: uppercase; text-align: right; width: 100px; border-radius: 0 4px 4px 0;">Amount</td>
                                </tr>
                                {{#LineItems}}
                                <tr>
                                    <td style="padding: 15px; font-size: 14px; color: {{TextColor}}; border-bottom: 1px solid {{SecondaryColor}};">
                                        <span data-field="description" data-line-index="{{Index}}">{{Description}}</span>
                                        {{#ShowItemDescriptions}}{{#ItemDescription}}<br><span style="font-size: 12px; color: #6b7280;">{{ItemDescription}}</span>{{/ItemDescription}}{{/ShowItemDescriptions}}
                                    </td>
                                    <td style="padding: 15px; font-size: 14px; color: #6b7280; text-align: center; border-bottom: 1px solid {{SecondaryColor}};"><span data-field="quantity" data-line-index="{{Index}}">{{Quantity}}</span></td>
                                    <td style="padding: 15px; font-size: 14px; color: #6b7280; text-align: right; border-bottom: 1px solid {{SecondaryColor}};"><span data-field="rate" data-line-index="{{Index}}">{{UnitPrice}}</span></td>
                                    <td style="padding: 15px; font-size: 14px; font-weight: 500; color: {{TextColor}}; text-align: right; border-bottom: 1px solid {{SecondaryColor}};"><span data-out="lineAmount" data-line-index="{{Index}}" data-line-discount="{{LineDiscountRaw}}">{{Amount}}</span></td>
                                </tr>
                                {{/LineItems}}
                            </table>
                        </td>
                    </tr>

                    <!-- Totals -->
                    <tr>
                        <td style="padding: 20px 40px;">
                            <table role="presentation" cellpadding="0" cellspacing="0" width="280" style="margin-left: auto;">
                                <tr>
                                    <td style="padding: 8px 0; font-size: 14px; color: #6b7280;">Subtotal</td>
                                    <td style="padding: 8px 0; font-size: 14px; color: {{TextColor}}; text-align: right;"><span data-out="subtotal">{{Subtotal}}</span></td>
                                </tr>
                                {{#ShowTaxRow}}
                                <tr>
                                    <td style="padding: 8px 0; font-size: 14px; color: #6b7280;">{{TaxLabel}}<span data-out="taxRateLabel">{{TaxRateLabel}}</span></td>
                                    <td style="padding: 8px 0; font-size: 14px; color: {{TextColor}}; text-align: right;"><span data-total="tax" data-total-raw="{{TaxRateRaw}}" data-total-mode="{{TaxModeRaw}}" data-total-symbol="{{CurrencySymbol}}">{{TaxAmount}}</span></td>
                                </tr>
                                {{/ShowTaxRow}}
                                {{#ShowShipping}}
                                <tr>
                                    <td style="padding: 8px 0; font-size: 14px; color: #6b7280;">Shipping</td>
                                    <td style="padding: 8px 0; font-size: 14px; color: {{TextColor}}; text-align: right;"><span data-total="shipping" data-total-raw="{{ShippingRaw}}" data-total-symbol="{{CurrencySymbol}}">{{ShippingAmount}}</span></td>
                                </tr>
                                {{/ShowShipping}}
                                {{#ShowSecurityDeposit}}
                                <tr>
                                    <td style="padding: 8px 0; font-size: 14px; color: #6b7280;">Security Deposit</td>
                                    <td style="padding: 8px 0; font-size: 14px; color: {{TextColor}}; text-align: right;">{{SecurityDeposit}}</td>
                                </tr>
                                {{/ShowSecurityDeposit}}
                                {{#ShowCustomFee}}
                                <tr>
                                    <td style="padding: 8px 0; font-size: 14px; color: #6b7280;">{{CustomFeeLabel}}</td>
                                    <td style="padding: 8px 0; font-size: 14px; color: {{TextColor}}; text-align: right;"><span data-total="fee" data-total-raw="{{CustomFeeRaw}}" data-total-mode="{{FeeModeRaw}}" data-total-symbol="{{CurrencySymbol}}">{{CustomFeeAmount}}</span></td>
                                </tr>
                                {{/ShowCustomFee}}
                                {{#ShowDiscount}}
                                <tr>
                                    <td style="padding: 8px 0; font-size: 14px; color: #6b7280;">Discount</td>
                                    <td style="padding: 8px 0; font-size: 14px; color: {{TextColor}}; text-align: right;"><span data-total="discount" data-total-raw="{{DiscountRaw}}" data-total-mode="{{DiscountModeRaw}}" data-total-symbol="{{CurrencySymbol}}">{{DiscountAmount}}</span></td>
                                </tr>
                                {{/ShowDiscount}}
                                <tr>
                                    <td colspan="2" style="border-top: 2px solid {{SecondaryColor}}; padding-top: 12px;"></td>
                                </tr>
                                {{#AmountPaid}}
                                <tr>
                                    <td style="padding: 8px 0; font-size: 14px; color: {{AccentColor}};">Amount Paid</td>
                                    <td style="padding: 8px 0; font-size: 14px; color: {{AccentColor}}; text-align: right;">-{{AmountPaid}}</td>
                                </tr>
                                {{/AmountPaid}}
                                {{#ShowProcessingFee}}
                                <tr>
                                    <td style="padding: 8px 0; font-size: 14px; color: #6b7280;">{{ProcessingFeeLabel}}</td>
                                    <td style="padding: 8px 0; font-size: 14px; color: {{TextColor}}; text-align: right;"><span data-out="processingFee">{{ProcessingFeeAmount}}</span></td>
                                </tr>
                                {{/ShowProcessingFee}}
                                {{#ShowAmountToPay}}
                                <tr>
                                    <td style="padding: 8px 0; font-size: 18px; font-weight: 700; color: {{TextColor}};">Amount to Pay</td>
                                    <td style="padding: 8px 0; font-size: 18px; font-weight: 700; color: {{HeaderColor}}; text-align: right;"><span data-out="amountToPay">{{AmountToPay}}</span></td>
                                </tr>
                                {{/ShowAmountToPay}}
                            </table>
                        </td>
                    </tr>

                    {{#ShowPaymentInstructions}}
                    {{#PaymentInstructions}}
                    <!-- Payment Instructions -->
                    <tr>
                        <td style="padding: 0 40px 20px 40px;">
                            <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background-color: #eff6ff; border-radius: 6px; border-left: 4px solid {{PrimaryColor}};">
                                <tr>
                                    <td style="padding: 15px 20px;">
                                        <p style="margin: 0 0 5px 0; font-size: 12px; font-weight: 600; color: {{HeaderColor}}; text-transform: uppercase;">Payment Instructions</p>
                                        <p style="margin: 0; font-size: 14px; color: {{TextColor}}; line-height: 1.5; white-space: pre-line;">{{PaymentInstructions}}</p>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>
                    {{/PaymentInstructions}}
                    {{/ShowPaymentInstructions}}

                    <!-- Footer -->
                    <tr>
                        <td style="padding: 25px 40px; background-color: #f9fafb; border-radius: 0 0 8px 8px; text-align: center;">
                            <p style="margin: 0 0 10px 0; font-size: 14px; color: {{TextColor}};"><span data-field="notes">{{FooterOrNotes}}</span></p>
                            <p style="margin: 0; font-size: 12px; color: #9ca3af;">
                                {{CompanyName}}{{#ShowCompanyAddress}}{{#CompanyAddress}} • {{CompanyAddress}}{{/CompanyAddress}}{{/ShowCompanyAddress}}{{#ShowCompanyCity}}{{#CompanyCity}} • {{CompanyCity}}{{/CompanyCity}}{{/ShowCompanyCity}}{{#ShowCompanyProvinceState}}{{#CompanyProvinceState}} • {{CompanyProvinceState}}{{/CompanyProvinceState}}{{/ShowCompanyProvinceState}}{{#ShowCompanyCountry}}{{#CompanyCountry}} • {{CompanyCountry}}{{/CompanyCountry}}{{/ShowCompanyCountry}}
                            </p>
                            {{#CompanyEmail}}<p style="margin: 5px 0 0 0; font-size: 12px; color: #9ca3af;">{{CompanyEmail}}{{#ShowCompanyPhone}}{{#CompanyPhone}} • {{CompanyPhone}}{{/CompanyPhone}}{{/ShowCompanyPhone}}</p>{{/CompanyEmail}}
                            {{^CompanyEmail}}{{#ShowCompanyPhone}}{{#CompanyPhone}}<p style="margin: 5px 0 0 0; font-size: 12px; color: #9ca3af;">{{CompanyPhone}}</p>{{/CompanyPhone}}{{/ShowCompanyPhone}}{{/CompanyEmail}}
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>
""";

    /// <summary>
    /// Modern template: Contemporary design with sidebar accent and clean layout.
    /// </summary>
    public const string Modern = """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <meta name="color-scheme" content="light">
    <title>Invoice {{InvoiceNumber}}</title>
</head>
<body style="margin: 0; padding: 0; font-family: {{FontFamily}}; background-color: #f0f4f8;">
    <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background-color: #f0f4f8;">
        <tr>
            <td align="center" style="padding: 40px 20px;">
                <table role="presentation" cellpadding="0" cellspacing="0" width="600" style="background-color: {{BackgroundColor}}; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 20px rgba(0,0,0,0.08);">
                    <!-- Header with accent bar -->
                    <tr>
                        <td>
                            <table role="presentation" cellpadding="0" cellspacing="0" width="100%">
                                <tr>
                                    <td style="width: 8px; background-color: {{PrimaryColor}};"></td>
                                    <td style="padding: 30px 35px; background-color: {{SecondaryColor}};">
                                        <table role="presentation" cellpadding="0" cellspacing="0" width="100%">
                                            <tr>
                                                <td style="vertical-align: middle;">
                                                    {{#ShowLogo}}
                                                    <img src="{{LogoSrc}}" alt="Company Logo" data-logo="1" style="display: inline-block; vertical-align: middle; max-width: {{LogoWidth}}px; max-height: 55px; width: auto; height: auto; object-fit: contain; margin-right: 14px;">
                                                    {{/ShowLogo}}
                                                    <span data-logo-slot="1" style="display: inline-block; vertical-align: middle; font-size: 22px; font-weight: 700; color: {{TextColor}};">{{CompanyName}}</span>
                                                </td>
                                                <td style="text-align: right; vertical-align: middle;">
                                                    <p style="margin: 0 0 4px 0; font-size: 24px; font-weight: 700; color: {{HeaderColor}}; letter-spacing: -0.5px;">{{HeaderText}}</p>
                                                    <p style="margin: 0; font-size: 14px; color: #6b7280;">{{InvoiceNumber}}</p>
                                                </td>
                                            </tr>
                                        </table>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    <!-- Invoice Details -->
                    <tr>
                        <td style="padding: 30px 35px;">
                            <table role="presentation" cellpadding="0" cellspacing="0" width="100%">
                                <tr>
                                    <td style="width: 55%; vertical-align: top; padding-right: 20px;">
                                        <p style="margin: 0 0 8px 0; font-size: 11px; text-transform: uppercase; letter-spacing: 1.5px; color: {{HeaderColor}}; font-weight: 600;">Bill To</p>
                                        <p style="margin: 0 0 4px 0; font-size: 16px; font-weight: 600; color: {{TextColor}};"><span data-field="customer">{{CustomerName}}</span></p>
                                        {{#CustomerAddress}}<p style="margin: 0 0 2px 0; font-size: 13px; color: #6b7280; line-height: 1.5;">{{CustomerAddress}}</p>{{/CustomerAddress}}
                                        {{#CustomerEmail}}<p style="margin: 4px 0 0 0; font-size: 13px; color: #6b7280;">{{CustomerEmail}}</p>{{/CustomerEmail}}
                                    </td>
                                    <td style="width: 45%; vertical-align: top;">
                                        <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background-color: {{SecondaryColor}}; border-radius: 6px;">
                                            <tr>
                                                <td style="padding: 15px;">
                                                    <table role="presentation" cellpadding="0" cellspacing="0" width="100%">
                                                        <tr>
                                                            <td style="padding: 4px 0; font-size: 12px; color: #6b7280;">Issue Date</td>
                                                            <td style="padding: 4px 0; font-size: 13px; font-weight: 500; color: {{TextColor}}; text-align: right;"><span data-field="issueDate" data-iso="{{IssueDateIso}}">{{IssueDate}}</span></td>
                                                        </tr>
                                                        <tr>
                                                            <td style="padding: 4px 0; font-size: 12px; color: #6b7280;">Due Date</td>
                                                            <td style="padding: 4px 0; font-size: 13px; font-weight: 500; text-align: right; {{#IsOverdue}}color: #dc2626;{{/IsOverdue}}{{^IsOverdue}}color: {{TextColor}};{{/IsOverdue}}"><span data-field="dueDate" data-iso="{{DueDateIso}}">{{DueDate}}</span></td>
                                                        </tr>
                                                        {{#ShowDueDateProminent}}
                                                        <tr>
                                                            <td colspan="2" style="padding-top: 10px;">
                                                                <span style="display: inline-block; width: 100%; text-align: center; background-color: {{#IsOverdue}}#fef2f2{{/IsOverdue}}{{^IsOverdue}}{{BackgroundColor}}{{/IsOverdue}}; color: {{#IsOverdue}}#dc2626{{/IsOverdue}}{{^IsOverdue}}{{PrimaryColor}}{{/IsOverdue}}; padding: 8px 0; border-radius: 4px; font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.5px;">
                                                                    {{#IsOverdue}}⚠ Overdue{{/IsOverdue}}{{^IsOverdue}}Due: {{DueDate}}{{/IsOverdue}}
                                                                </span>
                                                            </td>
                                                        </tr>
                                                        {{/ShowDueDateProminent}}
                                                    </table>
                                                </td>
                                            </tr>
                                        </table>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    <!-- Line Items -->
                    <tr>
                        <td style="padding: 0 35px;">
                            <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="border-collapse: collapse;">
                                <tr style="background-color: {{PrimaryColor}};">
                                    <td style="padding: 12px 15px; font-size: 11px; font-weight: 600; color: #ffffff; text-transform: uppercase; letter-spacing: 0.5px; border-radius: 6px 0 0 6px;">Description</td>
                                    <td style="padding: 12px 10px; font-size: 11px; font-weight: 600; color: #ffffff; text-transform: uppercase; letter-spacing: 0.5px; text-align: center; width: 70px;">Qty</td>
                                    <td style="padding: 12px 10px; font-size: 11px; font-weight: 600; color: #ffffff; text-transform: uppercase; letter-spacing: 0.5px; text-align: right; width: 90px;">Rate</td>
                                    <td style="padding: 12px 15px; font-size: 11px; font-weight: 600; color: #ffffff; text-transform: uppercase; letter-spacing: 0.5px; text-align: right; width: 100px; border-radius: 0 6px 6px 0;">Amount</td>
                                </tr>
                                {{#LineItems}}
                                <tr>
                                    <td style="padding: 16px 15px; font-size: 14px; color: {{TextColor}}; border-bottom: 1px solid {{SecondaryColor}};">
                                        <span data-field="description" data-line-index="{{Index}}">{{Description}}</span>
                                        {{#ShowItemDescriptions}}{{#ItemDescription}}<br><span style="font-size: 12px; color: #9ca3af;">{{ItemDescription}}</span>{{/ItemDescription}}{{/ShowItemDescriptions}}
                                    </td>
                                    <td style="padding: 16px 10px; font-size: 14px; color: #6b7280; text-align: center; border-bottom: 1px solid {{SecondaryColor}};"><span data-field="quantity" data-line-index="{{Index}}">{{Quantity}}</span></td>
                                    <td style="padding: 16px 10px; font-size: 14px; color: #6b7280; text-align: right; border-bottom: 1px solid {{SecondaryColor}};"><span data-field="rate" data-line-index="{{Index}}">{{UnitPrice}}</span></td>
                                    <td style="padding: 16px 15px; font-size: 14px; font-weight: 600; color: {{TextColor}}; text-align: right; border-bottom: 1px solid {{SecondaryColor}};"><span data-out="lineAmount" data-line-index="{{Index}}" data-line-discount="{{LineDiscountRaw}}">{{Amount}}</span></td>
                                </tr>
                                {{/LineItems}}
                            </table>
                        </td>
                    </tr>

                    <!-- Totals and Notes Row -->
                    <tr>
                        <td style="padding: 25px 35px;">
                            <table role="presentation" cellpadding="0" cellspacing="0" width="100%">
                                <tr>
                                    <td style="width: 55%; vertical-align: top; padding-right: 30px;">
                                    </td>
                                    <td style="width: 45%;">
                                        <table role="presentation" cellpadding="0" cellspacing="0" width="100%">
                                            <tr>
                                                <td style="padding: 8px 0; font-size: 13px; color: #6b7280;">Subtotal</td>
                                                <td style="padding: 8px 0; font-size: 13px; color: {{TextColor}}; text-align: right;"><span data-out="subtotal">{{Subtotal}}</span></td>
                                            </tr>
                                            {{#ShowTaxRow}}
                                            <tr>
                                                <td style="padding: 8px 0; font-size: 13px; color: #6b7280;">{{TaxLabel}}<span data-out="taxRateLabel">{{TaxRateLabel}}</span></td>
                                                <td style="padding: 8px 0; font-size: 13px; color: {{TextColor}}; text-align: right;"><span data-total="tax" data-total-raw="{{TaxRateRaw}}" data-total-mode="{{TaxModeRaw}}" data-total-symbol="{{CurrencySymbol}}">{{TaxAmount}}</span></td>
                                            </tr>
                                            {{/ShowTaxRow}}
                                            {{#ShowShipping}}
                                            <tr>
                                                <td style="padding: 8px 0; font-size: 13px; color: #6b7280;">Shipping</td>
                                                <td style="padding: 8px 0; font-size: 13px; color: {{TextColor}}; text-align: right;"><span data-total="shipping" data-total-raw="{{ShippingRaw}}" data-total-symbol="{{CurrencySymbol}}">{{ShippingAmount}}</span></td>
                                            </tr>
                                            {{/ShowShipping}}
                                            {{#ShowSecurityDeposit}}
                                            <tr>
                                                <td style="padding: 8px 0; font-size: 13px; color: #6b7280;">Security Deposit</td>
                                                <td style="padding: 8px 0; font-size: 13px; color: {{TextColor}}; text-align: right;">{{SecurityDeposit}}</td>
                                            </tr>
                                            {{/ShowSecurityDeposit}}
                                            {{#ShowCustomFee}}
                                            <tr>
                                                <td style="padding: 8px 0; font-size: 13px; color: #6b7280;">{{CustomFeeLabel}}</td>
                                                <td style="padding: 8px 0; font-size: 13px; color: {{TextColor}}; text-align: right;"><span data-total="fee" data-total-raw="{{CustomFeeRaw}}" data-total-mode="{{FeeModeRaw}}" data-total-symbol="{{CurrencySymbol}}">{{CustomFeeAmount}}</span></td>
                                            </tr>
                                            {{/ShowCustomFee}}
                                            {{#ShowDiscount}}
                                            <tr>
                                                <td style="padding: 8px 0; font-size: 13px; color: #6b7280;">Discount</td>
                                                <td style="padding: 8px 0; font-size: 13px; color: {{TextColor}}; text-align: right;"><span data-total="discount" data-total-raw="{{DiscountRaw}}" data-total-mode="{{DiscountModeRaw}}" data-total-symbol="{{CurrencySymbol}}">{{DiscountAmount}}</span></td>
                                            </tr>
                                            {{/ShowDiscount}}
                                            <tr>
                                                <td colspan="2" style="padding-top: 12px; border-top: 2px solid {{PrimaryColor}};"></td>
                                            </tr>
                                            {{#AmountPaid}}
                                            <tr>
                                                <td style="padding: 6px 0; font-size: 13px; color: {{AccentColor}};">Paid</td>
                                                <td style="padding: 6px 0; font-size: 13px; color: {{AccentColor}}; text-align: right;">-{{AmountPaid}}</td>
                                            </tr>
                                            {{/AmountPaid}}
                                            {{#ShowProcessingFee}}
                                            <tr>
                                                <td style="padding: 8px 0; font-size: 13px; color: #6b7280;">{{ProcessingFeeLabel}}</td>
                                                <td style="padding: 8px 0; font-size: 13px; color: {{TextColor}}; text-align: right;"><span data-out="processingFee">{{ProcessingFeeAmount}}</span></td>
                                            </tr>
                                            {{/ShowProcessingFee}}
                                            {{#ShowAmountToPay}}
                                            <tr>
                                                <td style="padding: 8px 0; font-size: 16px; font-weight: 700; color: {{TextColor}};">Amount to Pay</td>
                                                <td style="padding: 8px 0; font-size: 20px; font-weight: 700; color: {{HeaderColor}}; text-align: right;"><span data-out="amountToPay">{{AmountToPay}}</span></td>
                                            </tr>
                                            {{/ShowAmountToPay}}
                                        </table>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    {{#ShowPaymentInstructions}}
                    {{#PaymentInstructions}}
                    <!-- Payment Instructions -->
                    <tr>
                        <td style="padding: 0 35px 25px 35px;">
                            <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background-color: {{SecondaryColor}}; border-radius: 6px; border-left: 4px solid {{AccentColor}};">
                                <tr>
                                    <td style="padding: 18px 20px;">
                                        <p style="margin: 0 0 8px 0; font-size: 11px; text-transform: uppercase; letter-spacing: 1px; color: {{AccentColor}}; font-weight: 600;">Payment Instructions</p>
                                        <p style="margin: 0; font-size: 13px; color: {{TextColor}}; line-height: 1.6; white-space: pre-line;">{{PaymentInstructions}}</p>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>
                    {{/PaymentInstructions}}
                    {{/ShowPaymentInstructions}}

                    <!-- Footer -->
                    <tr>
                        <td>
                            <table role="presentation" cellpadding="0" cellspacing="0" width="100%">
                                <tr>
                                    <td style="width: 8px; background-color: {{PrimaryColor}};"></td>
                                    <td style="padding: 20px 35px; background-color: {{SecondaryColor}}; text-align: center;">
                                        <p style="margin: 0 0 6px 0; font-size: 13px; color: {{TextColor}};"><span data-field="notes">{{FooterOrNotes}}</span></p>
                                        <p style="margin: 0; font-size: 11px; color: #9ca3af;">
                                            {{CompanyName}}{{#ShowCompanyAddress}}{{#CompanyAddress}} • {{CompanyAddress}}{{/CompanyAddress}}{{/ShowCompanyAddress}}{{#ShowCompanyCity}}{{#CompanyCity}} • {{CompanyCity}}{{/CompanyCity}}{{/ShowCompanyCity}}{{#ShowCompanyProvinceState}}{{#CompanyProvinceState}} • {{CompanyProvinceState}}{{/CompanyProvinceState}}{{/ShowCompanyProvinceState}}{{#ShowCompanyCountry}}{{#CompanyCountry}} • {{CompanyCountry}}{{/CompanyCountry}}{{/ShowCompanyCountry}}{{#ShowCompanyPhone}}{{#CompanyPhone}} • {{CompanyPhone}}{{/CompanyPhone}}{{/ShowCompanyPhone}}
                                        </p>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>
""";

    /// <summary>
    /// Classic template: Traditional invoice layout with borders and structured sections.
    /// </summary>
    public const string Classic = """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <meta name="color-scheme" content="light">
    <title>Invoice {{InvoiceNumber}}</title>
</head>
<body style="margin: 0; padding: 0; font-family: {{FontFamily}}; background-color: #ffffff;">
    <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background-color: #ffffff;">
        <tr>
            <td align="center" style="padding: 30px 20px;">
                <table role="presentation" cellpadding="0" cellspacing="0" width="600" style="background-color: {{BackgroundColor}}; border: 2px solid {{PrimaryColor}};">
                    <!-- Header -->
                    <tr>
                        <td style="padding: 20px; border-bottom: 2px solid {{PrimaryColor}};">
                            <table role="presentation" cellpadding="0" cellspacing="0" width="100%">
                                <tr>
                                    <td style="width: 60%;">
                                        {{#ShowLogo}}
                                        <img src="{{LogoSrc}}" alt="Company Logo" data-logo="1" style="display: inline-block; vertical-align: middle; max-width: {{LogoWidth}}px; max-height: 60px; width: auto; height: auto; object-fit: contain; margin-right: 14px;">
                                        {{/ShowLogo}}
                                        <p data-logo-slot="1" style="display: inline-block; vertical-align: middle; margin: 0; font-size: 18px; font-weight: bold; color: {{TextColor}};">{{CompanyName}}</p>
                                        {{#ShowCompanyAddress}}{{#CompanyAddress}}<p style="margin: 5px 0 0 0; font-size: 12px; color: #666666; line-height: 1.5;">{{CompanyAddress}}</p>{{/CompanyAddress}}{{/ShowCompanyAddress}}
                                        {{#ShowCompanyCity}}{{#CompanyCity}}<p style="margin: 2px 0 0 0; font-size: 12px; color: #666666;">{{CompanyCity}}{{#ShowCompanyProvinceState}}{{#CompanyProvinceState}}, {{CompanyProvinceState}}{{/CompanyProvinceState}}{{/ShowCompanyProvinceState}}{{#ShowCompanyCountry}}{{#CompanyCountry}}, {{CompanyCountry}}{{/CompanyCountry}}{{/ShowCompanyCountry}}</p>{{/CompanyCity}}{{/ShowCompanyCity}}
                                        {{^ShowCompanyCity}}{{#ShowCompanyProvinceState}}{{#CompanyProvinceState}}<p style="margin: 2px 0 0 0; font-size: 12px; color: #666666;">{{CompanyProvinceState}}{{#ShowCompanyCountry}}{{#CompanyCountry}}, {{CompanyCountry}}{{/CompanyCountry}}{{/ShowCompanyCountry}}</p>{{/CompanyProvinceState}}{{/ShowCompanyProvinceState}}{{^ShowCompanyProvinceState}}{{#ShowCompanyCountry}}{{#CompanyCountry}}<p style="margin: 2px 0 0 0; font-size: 12px; color: #666666;">{{CompanyCountry}}</p>{{/CompanyCountry}}{{/ShowCompanyCountry}}{{/ShowCompanyProvinceState}}{{/ShowCompanyCity}}
                                        {{#CompanyEmail}}<p style="margin: 2px 0 0 0; font-size: 12px; color: #666666;">{{CompanyEmail}}</p>{{/CompanyEmail}}
                                        {{#ShowCompanyPhone}}{{#CompanyPhone}}<p style="margin: 2px 0 0 0; font-size: 12px; color: #666666;">{{CompanyPhone}}</p>{{/CompanyPhone}}{{/ShowCompanyPhone}}
                                    </td>
                                    <td style="width: 40%; text-align: right; vertical-align: top;">
                                        <p style="margin: 0; font-size: 28px; font-weight: bold; color: {{HeaderColor}};">{{HeaderText}}</p>
                                        <p style="margin: 10px 0 0 0; font-size: 14px; color: {{TextColor}};"><strong>Invoice #:</strong> {{InvoiceNumber}}</p>
                                        <p style="margin: 5px 0 0 0; font-size: 14px; color: {{TextColor}};"><strong>Date:</strong> <span data-field="issueDate" data-iso="{{IssueDateIso}}">{{IssueDate}}</span></p>
                                        <p style="margin: 5px 0 0 0; font-size: 14px; {{#IsOverdue}}color: #dc2626;{{/IsOverdue}}{{^IsOverdue}}color: {{TextColor}};{{/IsOverdue}}"><strong>Due:</strong> <span data-field="dueDate" data-iso="{{DueDateIso}}">{{DueDate}}</span></p>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    <!-- Bill To -->
                    <tr>
                        <td style="padding: 20px; background-color: #f8f9fa; border-bottom: 1px solid {{SecondaryColor}};">
                            <table role="presentation" cellpadding="0" cellspacing="0" width="100%">
                                <tr>
                                    <td>
                                        <p style="margin: 0 0 5px 0; font-size: 12px; font-weight: bold; color: {{HeaderColor}}; text-transform: uppercase;">Bill To:</p>
                                        <p style="margin: 0; font-size: 15px; font-weight: bold; color: {{TextColor}};"><span data-field="customer">{{CustomerName}}</span></p>
                                        {{#CustomerAddress}}<p style="margin: 3px 0 0 0; font-size: 13px; color: #666666;">{{CustomerAddress}}</p>{{/CustomerAddress}}
                                        {{#CustomerEmail}}<p style="margin: 3px 0 0 0; font-size: 13px; color: #666666;">{{CustomerEmail}}</p>{{/CustomerEmail}}
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    <!-- Line Items -->
                    <tr>
                        <td style="padding: 0;">
                            <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="border-collapse: collapse;">
                                <tr style="background-color: {{PrimaryColor}};">
                                    <td style="padding: 12px 15px; font-size: 12px; font-weight: bold; color: #ffffff; border: 1px solid {{PrimaryColor}};">DESCRIPTION</td>
                                    <td style="padding: 12px 10px; font-size: 12px; font-weight: bold; color: #ffffff; text-align: center; width: 60px; border: 1px solid {{PrimaryColor}};">QTY</td>
                                    <td style="padding: 12px 10px; font-size: 12px; font-weight: bold; color: #ffffff; text-align: right; width: 90px; border: 1px solid {{PrimaryColor}};">RATE</td>
                                    <td style="padding: 12px 15px; font-size: 12px; font-weight: bold; color: #ffffff; text-align: right; width: 90px; border: 1px solid {{PrimaryColor}};">AMOUNT</td>
                                </tr>
                                {{#LineItems}}
                                <tr>
                                    <td style="padding: 12px 15px; font-size: 13px; color: {{TextColor}}; border: 1px solid {{SecondaryColor}};">
                                        <span data-field="description" data-line-index="{{Index}}">{{Description}}</span>
                                        {{#ShowItemDescriptions}}{{#ItemDescription}}<br><span style="font-size: 11px; color: #888888;">{{ItemDescription}}</span>{{/ItemDescription}}{{/ShowItemDescriptions}}
                                    </td>
                                    <td style="padding: 12px 10px; font-size: 13px; color: {{TextColor}}; text-align: center; border: 1px solid {{SecondaryColor}};"><span data-field="quantity" data-line-index="{{Index}}">{{Quantity}}</span></td>
                                    <td style="padding: 12px 10px; font-size: 13px; color: {{TextColor}}; text-align: right; border: 1px solid {{SecondaryColor}};"><span data-field="rate" data-line-index="{{Index}}">{{UnitPrice}}</span></td>
                                    <td style="padding: 12px 15px; font-size: 13px; font-weight: bold; color: {{TextColor}}; text-align: right; border: 1px solid {{SecondaryColor}};"><span data-out="lineAmount" data-line-index="{{Index}}" data-line-discount="{{LineDiscountRaw}}">{{Amount}}</span></td>
                                </tr>
                                {{/LineItems}}
                            </table>
                        </td>
                    </tr>

                    <!-- Totals -->
                    <tr>
                        <td style="padding: 0;">
                            <table role="presentation" cellpadding="0" cellspacing="0" width="100%">
                                <tr>
                                    <td style="width: 60%; padding: 15px; vertical-align: top;">
                                    </td>
                                    <td style="width: 40%; border-left: 1px solid {{SecondaryColor}};">
                                        <table role="presentation" cellpadding="0" cellspacing="0" width="100%">
                                            <tr>
                                                <td style="padding: 10px 15px; font-size: 13px; color: #666666; border-bottom: 1px solid {{SecondaryColor}};">Subtotal</td>
                                                <td style="padding: 10px 15px; font-size: 13px; color: {{TextColor}}; text-align: right; border-bottom: 1px solid {{SecondaryColor}};"><span data-out="subtotal">{{Subtotal}}</span></td>
                                            </tr>
                                            {{#ShowTaxRow}}
                                            <tr>
                                                <td style="padding: 10px 15px; font-size: 13px; color: #666666; border-bottom: 1px solid {{SecondaryColor}};">{{TaxLabel}}<span data-out="taxRateLabel">{{TaxRateLabel}}</span></td>
                                                <td style="padding: 10px 15px; font-size: 13px; color: {{TextColor}}; text-align: right; border-bottom: 1px solid {{SecondaryColor}};"><span data-total="tax" data-total-raw="{{TaxRateRaw}}" data-total-mode="{{TaxModeRaw}}" data-total-symbol="{{CurrencySymbol}}">{{TaxAmount}}</span></td>
                                            </tr>
                                            {{/ShowTaxRow}}
                                            {{#ShowShipping}}
                                            <tr>
                                                <td style="padding: 10px 15px; font-size: 13px; color: #666666; border-bottom: 1px solid {{SecondaryColor}};">Shipping</td>
                                                <td style="padding: 10px 15px; font-size: 13px; color: {{TextColor}}; text-align: right; border-bottom: 1px solid {{SecondaryColor}};"><span data-total="shipping" data-total-raw="{{ShippingRaw}}" data-total-symbol="{{CurrencySymbol}}">{{ShippingAmount}}</span></td>
                                            </tr>
                                            {{/ShowShipping}}
                                            {{#ShowSecurityDeposit}}
                                            <tr>
                                                <td style="padding: 10px 15px; font-size: 13px; color: #666666; border-bottom: 1px solid {{SecondaryColor}};">Security Deposit</td>
                                                <td style="padding: 10px 15px; font-size: 13px; color: {{TextColor}}; text-align: right; border-bottom: 1px solid {{SecondaryColor}};">{{SecurityDeposit}}</td>
                                            </tr>
                                            {{/ShowSecurityDeposit}}
                                            {{#ShowCustomFee}}
                                            <tr>
                                                <td style="padding: 10px 15px; font-size: 13px; color: #666666; border-bottom: 1px solid {{SecondaryColor}};">{{CustomFeeLabel}}</td>
                                                <td style="padding: 10px 15px; font-size: 13px; color: {{TextColor}}; text-align: right; border-bottom: 1px solid {{SecondaryColor}};"><span data-total="fee" data-total-raw="{{CustomFeeRaw}}" data-total-mode="{{FeeModeRaw}}" data-total-symbol="{{CurrencySymbol}}">{{CustomFeeAmount}}</span></td>
                                            </tr>
                                            {{/ShowCustomFee}}
                                            {{#ShowDiscount}}
                                            <tr>
                                                <td style="padding: 10px 15px; font-size: 13px; color: #666666; border-bottom: 1px solid {{SecondaryColor}};">Discount</td>
                                                <td style="padding: 10px 15px; font-size: 13px; color: {{TextColor}}; text-align: right; border-bottom: 1px solid {{SecondaryColor}};"><span data-total="discount" data-total-raw="{{DiscountRaw}}" data-total-mode="{{DiscountModeRaw}}" data-total-symbol="{{CurrencySymbol}}">{{DiscountAmount}}</span></td>
                                            </tr>
                                            {{/ShowDiscount}}
                                            {{#AmountPaid}}
                                            <tr>
                                                <td style="padding: 10px 15px; font-size: 13px; color: {{AccentColor}}; border-bottom: 1px solid {{SecondaryColor}};">Amount Paid</td>
                                                <td style="padding: 10px 15px; font-size: 13px; color: {{AccentColor}}; text-align: right; border-bottom: 1px solid {{SecondaryColor}};">-{{AmountPaid}}</td>
                                            </tr>
                                            {{/AmountPaid}}
                                            {{#ShowProcessingFee}}
                                            <tr>
                                                <td style="padding: 10px 15px; font-size: 13px; color: #666666; border-bottom: 1px solid {{SecondaryColor}};">{{ProcessingFeeLabel}}</td>
                                                <td style="padding: 10px 15px; font-size: 13px; color: {{TextColor}}; text-align: right; border-bottom: 1px solid {{SecondaryColor}};"><span data-out="processingFee">{{ProcessingFeeAmount}}</span></td>
                                            </tr>
                                            {{/ShowProcessingFee}}
                                            {{#ShowAmountToPay}}
                                            <tr style="background-color: {{PrimaryColor}};">
                                                <td style="padding: 12px 15px; font-size: 14px; font-weight: bold; color: #ffffff;">AMOUNT TO PAY</td>
                                                <td style="padding: 12px 15px; font-size: 16px; font-weight: bold; color: #ffffff; text-align: right;"><span data-out="amountToPay">{{AmountToPay}}</span></td>
                                            </tr>
                                            {{/ShowAmountToPay}}
                                        </table>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    {{#ShowPaymentInstructions}}
                    {{#PaymentInstructions}}
                    <!-- Payment Instructions -->
                    <tr>
                        <td style="padding: 15px; border-top: 1px solid {{SecondaryColor}}; background-color: #f8f9fa;">
                            <p style="margin: 0 0 5px 0; font-size: 12px; font-weight: bold; color: {{HeaderColor}};">PAYMENT INSTRUCTIONS:</p>
                            <p style="margin: 0; font-size: 12px; color: {{TextColor}}; line-height: 1.5; white-space: pre-line;">{{PaymentInstructions}}</p>
                        </td>
                    </tr>
                    {{/PaymentInstructions}}
                    {{/ShowPaymentInstructions}}

                    <!-- Footer -->
                    <tr>
                        <td style="padding: 15px; background-color: {{PrimaryColor}}; text-align: center;">
                            <p style="margin: 0; font-size: 13px; color: #ffffff;"><span data-field="notes">{{FooterOrNotes}}</span></p>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>
""";

    /// <summary>
    /// Elegant template: Sophisticated design with accent borders and refined typography.
    /// </summary>
    public const string Elegant = """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <meta name="color-scheme" content="light">
    <title>Invoice {{InvoiceNumber}}</title>
</head>
<body style="margin: 0; padding: 0; font-family: {{FontFamily}}; background-color: #f8f9fa;">
    <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background-color: #f8f9fa;">
        <tr>
            <td align="center" style="padding: 40px 20px;">
                <table role="presentation" cellpadding="0" cellspacing="0" width="600" style="background-color: {{BackgroundColor}}; border-radius: 4px; box-shadow: 0 1px 3px rgba(0,0,0,0.08);">
                    <!-- Accent Top Border -->
                    <tr>
                        <td style="height: 4px; background: linear-gradient(90deg, {{PrimaryColor}}, {{AccentColor}});"></td>
                    </tr>

                    <!-- Header -->
                    <tr>
                        <td style="padding: 35px 40px 25px 40px;">
                            <table role="presentation" cellpadding="0" cellspacing="0" width="100%">
                                <tr>
                                    <td style="width: 60%; vertical-align: top;">
                                        {{#ShowLogo}}
                                        <img src="{{LogoSrc}}" alt="Company Logo" data-logo="1" style="display: inline-block; vertical-align: middle; max-width: {{LogoWidth}}px; max-height: 55px; width: auto; height: auto; object-fit: contain; margin-right: 14px;">
                                        {{/ShowLogo}}
                                        <p data-logo-slot="1" style="display: inline-block; vertical-align: middle; margin: 0 0 15px 0; font-size: 20px; font-weight: 600; color: {{TextColor}}; letter-spacing: -0.5px;">{{CompanyName}}</p>
                                        {{#ShowCompanyAddress}}{{#CompanyAddress}}<p style="margin: 0 0 3px 0; font-size: 13px; color: #6b7280; line-height: 1.5;">{{CompanyAddress}}</p>{{/CompanyAddress}}{{/ShowCompanyAddress}}
                                        {{#ShowCompanyCity}}{{#CompanyCity}}<p style="margin: 0 0 3px 0; font-size: 13px; color: #6b7280;">{{CompanyCity}}{{#ShowCompanyProvinceState}}{{#CompanyProvinceState}}, {{CompanyProvinceState}}{{/CompanyProvinceState}}{{/ShowCompanyProvinceState}}{{#ShowCompanyCountry}}{{#CompanyCountry}}, {{CompanyCountry}}{{/CompanyCountry}}{{/ShowCompanyCountry}}</p>{{/CompanyCity}}{{/ShowCompanyCity}}
                                        {{^ShowCompanyCity}}{{#ShowCompanyProvinceState}}{{#CompanyProvinceState}}<p style="margin: 0 0 3px 0; font-size: 13px; color: #6b7280;">{{CompanyProvinceState}}{{#ShowCompanyCountry}}{{#CompanyCountry}}, {{CompanyCountry}}{{/CompanyCountry}}{{/ShowCompanyCountry}}</p>{{/CompanyProvinceState}}{{/ShowCompanyProvinceState}}{{^ShowCompanyProvinceState}}{{#ShowCompanyCountry}}{{#CompanyCountry}}<p style="margin: 0 0 3px 0; font-size: 13px; color: #6b7280;">{{CompanyCountry}}</p>{{/CompanyCountry}}{{/ShowCompanyCountry}}{{/ShowCompanyProvinceState}}{{/ShowCompanyCity}}
                                        {{#CompanyEmail}}<p style="margin: 0 0 3px 0; font-size: 13px; color: #6b7280;">{{CompanyEmail}}</p>{{/CompanyEmail}}
                                        {{#ShowCompanyPhone}}{{#CompanyPhone}}<p style="margin: 0; font-size: 13px; color: #6b7280;">{{CompanyPhone}}</p>{{/CompanyPhone}}{{/ShowCompanyPhone}}
                                    </td>
                                    <td style="width: 40%; text-align: right; vertical-align: top;">
                                        <p style="margin: 0 0 8px 0; font-size: 11px; text-transform: uppercase; letter-spacing: 2px; color: {{HeaderColor}}; font-weight: 600;">{{HeaderText}}</p>
                                        <p style="margin: 0 0 20px 0; font-size: 22px; font-weight: 300; color: {{TextColor}};">{{InvoiceNumber}}</p>
                                        <table role="presentation" cellpadding="0" cellspacing="0" style="margin-left: auto;">
                                            <tr>
                                                <td style="padding: 3px 12px 3px 0; font-size: 12px; color: #9ca3af; text-align: right;">Issued</td>
                                                <td style="padding: 3px 0; font-size: 13px; color: {{TextColor}}; text-align: right;"><span data-field="issueDate" data-iso="{{IssueDateIso}}">{{IssueDate}}</span></td>
                                            </tr>
                                            <tr>
                                                <td style="padding: 3px 12px 3px 0; font-size: 12px; color: #9ca3af; text-align: right;">Due</td>
                                                <td style="padding: 3px 0; font-size: 13px; text-align: right; {{#IsOverdue}}color: #dc2626; font-weight: 600;{{/IsOverdue}}{{^IsOverdue}}color: {{TextColor}};{{/IsOverdue}}"><span data-field="dueDate" data-iso="{{DueDateIso}}">{{DueDate}}</span></td>
                                            </tr>
                                        </table>
                                        {{#ShowDueDateProminent}}
                                        <p style="margin: 15px 0 0 0;">
                                            <span style="display: inline-block; background-color: {{#IsOverdue}}#fef2f2{{/IsOverdue}}{{^IsOverdue}}{{SecondaryColor}}{{/IsOverdue}}; color: {{#IsOverdue}}#dc2626{{/IsOverdue}}{{^IsOverdue}}{{PrimaryColor}}{{/IsOverdue}}; padding: 6px 14px; border-radius: 20px; font-size: 11px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;">
                                                {{#IsOverdue}}Overdue{{/IsOverdue}}{{^IsOverdue}}Due {{DueDate}}{{/IsOverdue}}
                                            </span>
                                        </p>
                                        {{/ShowDueDateProminent}}
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    <!-- Bill To Section -->
                    <tr>
                        <td style="padding: 0 40px;">
                            <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="border-left: 3px solid {{PrimaryColor}}; padding-left: 15px;">
                                <tr>
                                    <td>
                                        <p style="margin: 0 0 5px 0; font-size: 11px; text-transform: uppercase; letter-spacing: 1px; color: #9ca3af; font-weight: 500;">Bill To</p>
                                        <p style="margin: 0 0 3px 0; font-size: 16px; font-weight: 600; color: {{TextColor}};"><span data-field="customer">{{CustomerName}}</span></p>
                                        {{#CustomerAddress}}<p style="margin: 0 0 2px 0; font-size: 13px; color: #6b7280; line-height: 1.5;">{{CustomerAddress}}</p>{{/CustomerAddress}}
                                        {{#CustomerEmail}}<p style="margin: 0; font-size: 13px; color: #6b7280;">{{CustomerEmail}}</p>{{/CustomerEmail}}
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    <!-- Line Items -->
                    <tr>
                        <td style="padding: 30px 40px;">
                            <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="border-collapse: collapse;">
                                <tr>
                                    <td style="padding: 12px 0; font-size: 11px; font-weight: 600; color: #9ca3af; text-transform: uppercase; letter-spacing: 0.5px; border-bottom: 2px solid {{SecondaryColor}};">Description</td>
                                    <td style="padding: 12px 0; font-size: 11px; font-weight: 600; color: #9ca3af; text-transform: uppercase; letter-spacing: 0.5px; text-align: center; width: 70px; border-bottom: 2px solid {{SecondaryColor}};">Qty</td>
                                    <td style="padding: 12px 0; font-size: 11px; font-weight: 600; color: #9ca3af; text-transform: uppercase; letter-spacing: 0.5px; text-align: right; width: 90px; border-bottom: 2px solid {{SecondaryColor}};">Rate</td>
                                    <td style="padding: 12px 0; font-size: 11px; font-weight: 600; color: #9ca3af; text-transform: uppercase; letter-spacing: 0.5px; text-align: right; width: 100px; border-bottom: 2px solid {{SecondaryColor}};">Amount</td>
                                </tr>
                                {{#LineItems}}
                                <tr>
                                    <td style="padding: 16px 0; font-size: 14px; color: {{TextColor}}; border-bottom: 1px solid {{SecondaryColor}};">
                                        <span data-field="description" data-line-index="{{Index}}">{{Description}}</span>
                                        {{#ShowItemDescriptions}}{{#ItemDescription}}<br><span style="font-size: 12px; color: #9ca3af; font-style: italic;">{{ItemDescription}}</span>{{/ItemDescription}}{{/ShowItemDescriptions}}
                                    </td>
                                    <td style="padding: 16px 0; font-size: 14px; color: #6b7280; text-align: center; border-bottom: 1px solid {{SecondaryColor}};"><span data-field="quantity" data-line-index="{{Index}}">{{Quantity}}</span></td>
                                    <td style="padding: 16px 0; font-size: 14px; color: #6b7280; text-align: right; border-bottom: 1px solid {{SecondaryColor}};"><span data-field="rate" data-line-index="{{Index}}">{{UnitPrice}}</span></td>
                                    <td style="padding: 16px 0; font-size: 14px; font-weight: 500; color: {{TextColor}}; text-align: right; border-bottom: 1px solid {{SecondaryColor}};"><span data-out="lineAmount" data-line-index="{{Index}}" data-line-discount="{{LineDiscountRaw}}">{{Amount}}</span></td>
                                </tr>
                                {{/LineItems}}
                            </table>
                        </td>
                    </tr>

                    <!-- Totals -->
                    <tr>
                        <td style="padding: 0 40px 30px 40px;">
                            <table role="presentation" cellpadding="0" cellspacing="0" width="100%">
                                <tr>
                                    <td style="width: 55%; vertical-align: top; padding-right: 30px;">
                                    </td>
                                    <td style="width: 45%;">
                                        <table role="presentation" cellpadding="0" cellspacing="0" width="100%">
                                            <tr>
                                                <td style="padding: 8px 0; font-size: 13px; color: #6b7280;">Subtotal</td>
                                                <td style="padding: 8px 0; font-size: 13px; color: {{TextColor}}; text-align: right;"><span data-out="subtotal">{{Subtotal}}</span></td>
                                            </tr>
                                            {{#ShowTaxRow}}
                                            <tr>
                                                <td style="padding: 8px 0; font-size: 13px; color: #6b7280;">{{TaxLabel}}<span data-out="taxRateLabel">{{TaxRateLabel}}</span></td>
                                                <td style="padding: 8px 0; font-size: 13px; color: {{TextColor}}; text-align: right;"><span data-total="tax" data-total-raw="{{TaxRateRaw}}" data-total-mode="{{TaxModeRaw}}" data-total-symbol="{{CurrencySymbol}}">{{TaxAmount}}</span></td>
                                            </tr>
                                            {{/ShowTaxRow}}
                                            {{#ShowShipping}}
                                            <tr>
                                                <td style="padding: 8px 0; font-size: 13px; color: #6b7280;">Shipping</td>
                                                <td style="padding: 8px 0; font-size: 13px; color: {{TextColor}}; text-align: right;"><span data-total="shipping" data-total-raw="{{ShippingRaw}}" data-total-symbol="{{CurrencySymbol}}">{{ShippingAmount}}</span></td>
                                            </tr>
                                            {{/ShowShipping}}
                                            {{#ShowSecurityDeposit}}
                                            <tr>
                                                <td style="padding: 8px 0; font-size: 13px; color: #6b7280;">Security Deposit</td>
                                                <td style="padding: 8px 0; font-size: 13px; color: {{TextColor}}; text-align: right;">{{SecurityDeposit}}</td>
                                            </tr>
                                            {{/ShowSecurityDeposit}}
                                            {{#ShowCustomFee}}
                                            <tr>
                                                <td style="padding: 8px 0; font-size: 13px; color: #6b7280;">{{CustomFeeLabel}}</td>
                                                <td style="padding: 8px 0; font-size: 13px; color: {{TextColor}}; text-align: right;"><span data-total="fee" data-total-raw="{{CustomFeeRaw}}" data-total-mode="{{FeeModeRaw}}" data-total-symbol="{{CurrencySymbol}}">{{CustomFeeAmount}}</span></td>
                                            </tr>
                                            {{/ShowCustomFee}}
                                            {{#ShowDiscount}}
                                            <tr>
                                                <td style="padding: 8px 0; font-size: 13px; color: #6b7280;">Discount</td>
                                                <td style="padding: 8px 0; font-size: 13px; color: {{TextColor}}; text-align: right;"><span data-total="discount" data-total-raw="{{DiscountRaw}}" data-total-mode="{{DiscountModeRaw}}" data-total-symbol="{{CurrencySymbol}}">{{DiscountAmount}}</span></td>
                                            </tr>
                                            {{/ShowDiscount}}
                                            <tr>
                                                <td colspan="2" style="padding-top: 12px; border-top: 2px solid {{PrimaryColor}};"></td>
                                            </tr>
                                            {{#AmountPaid}}
                                            <tr>
                                                <td style="padding: 6px 0; font-size: 13px; color: {{AccentColor}};">Paid</td>
                                                <td style="padding: 6px 0; font-size: 13px; color: {{AccentColor}}; text-align: right;">-{{AmountPaid}}</td>
                                            </tr>
                                            {{/AmountPaid}}
                                            {{#ShowProcessingFee}}
                                            <tr>
                                                <td style="padding: 8px 0; font-size: 13px; color: #6b7280;">{{ProcessingFeeLabel}}</td>
                                                <td style="padding: 8px 0; font-size: 13px; color: {{TextColor}}; text-align: right;"><span data-out="processingFee">{{ProcessingFeeAmount}}</span></td>
                                            </tr>
                                            {{/ShowProcessingFee}}
                                            {{#ShowAmountToPay}}
                                            <tr>
                                                <td style="padding: 8px 0; font-size: 11px; text-transform: uppercase; letter-spacing: 1px; color: #9ca3af; font-weight: 500;">Amount to Pay</td>
                                                <td style="padding: 8px 0; font-size: 22px; font-weight: 600; color: {{HeaderColor}}; text-align: right;"><span data-out="amountToPay">{{AmountToPay}}</span></td>
                                            </tr>
                                            {{/ShowAmountToPay}}
                                        </table>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    {{#ShowPaymentInstructions}}
                    {{#PaymentInstructions}}
                    <!-- Payment Instructions -->
                    <tr>
                        <td style="padding: 0 40px 30px 40px;">
                            <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background-color: {{SecondaryColor}}; border-radius: 6px;">
                                <tr>
                                    <td style="padding: 20px 25px;">
                                        <p style="margin: 0 0 10px 0; font-size: 11px; text-transform: uppercase; letter-spacing: 1px; color: {{HeaderColor}}; font-weight: 600;">Payment Instructions</p>
                                        <p style="margin: 0; font-size: 13px; color: {{TextColor}}; line-height: 1.6; white-space: pre-line;">{{PaymentInstructions}}</p>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>
                    {{/PaymentInstructions}}
                    {{/ShowPaymentInstructions}}

                    <!-- Footer -->
                    <tr>
                        <td style="padding: 25px 40px; background-color: {{SecondaryColor}}; text-align: center; border-radius: 0 0 4px 4px;">
                            <p style="margin: 0; font-size: 13px; color: {{TextColor}};"><span data-field="notes">{{FooterOrNotes}}</span></p>
                            <p style="margin: 8px 0 0 0; font-size: 12px; color: #9ca3af;">
                                {{CompanyName}}{{#ShowCompanyAddress}}{{#CompanyAddress}} • {{CompanyAddress}}{{/CompanyAddress}}{{/ShowCompanyAddress}}{{#ShowCompanyCity}}{{#CompanyCity}} • {{CompanyCity}}{{/CompanyCity}}{{/ShowCompanyCity}}{{#ShowCompanyProvinceState}}{{#CompanyProvinceState}} • {{CompanyProvinceState}}{{/CompanyProvinceState}}{{/ShowCompanyProvinceState}}{{#ShowCompanyCountry}}{{#CompanyCountry}} • {{CompanyCountry}}{{/CompanyCountry}}{{/ShowCompanyCountry}}{{#CompanyEmail}} • {{CompanyEmail}}{{/CompanyEmail}}{{#ShowCompanyPhone}}{{#CompanyPhone}} • {{CompanyPhone}}{{/CompanyPhone}}{{/ShowCompanyPhone}}
                            </p>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>
""";

    /// <summary>
    /// Ribbon template: Flowing design with decorative SVG wave ribbons on the left side,
    /// open layout with bold header, and terms section.
    /// </summary>
    public const string Ribbon = """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <meta name="color-scheme" content="light">
    <title>{{HeaderText}} {{InvoiceNumber}}</title>
</head>
<body style="margin: 0; padding: 0; font-family: {{FontFamily}}; font-weight: 600; background-color: #f5f5f5; min-height: 100vh; display: flex; justify-content: center; align-items: center; padding: 40px 20px;">
    <div style="width: 800px; min-height: 1100px; background: {{BackgroundColor}}; position: relative; box-shadow: 0 25px 80px rgba(0, 0, 0, 0.15); overflow: hidden;">
        <!-- Side Decoration: SVG Wave Ribbons -->
        <div style="position: absolute; left: 0; top: 0; width: 280px; height: 100%;">
            <svg viewBox="0 0 280 1100" preserveAspectRatio="none" style="position: absolute; left: 0; top: 0; width: 280px; height: 100%;">
                <defs>
                    <linearGradient id="ribbon1Grad" x1="0%" y1="0%" x2="0%" y2="100%">
                        <stop offset="0%" stop-color="{{AccentColor}}" stop-opacity="0.85"/>
                        <stop offset="25%" stop-color="{{AccentColor}}" stop-opacity="1"/>
                        <stop offset="50%" stop-color="{{AccentColor}}" stop-opacity="0.55"/>
                        <stop offset="75%" stop-color="{{AccentColor}}" stop-opacity="1"/>
                        <stop offset="100%" stop-color="{{AccentColor}}" stop-opacity="0.7"/>
                    </linearGradient>
                    <linearGradient id="ribbon2Grad" x1="0%" y1="0%" x2="0%" y2="100%">
                        <stop offset="0%" stop-color="{{PrimaryColor}}" stop-opacity="0.7"/>
                        <stop offset="25%" stop-color="{{PrimaryColor}}" stop-opacity="0.9"/>
                        <stop offset="50%" stop-color="{{PrimaryColor}}" stop-opacity="0.5"/>
                        <stop offset="75%" stop-color="{{PrimaryColor}}" stop-opacity="0.9"/>
                        <stop offset="100%" stop-color="{{PrimaryColor}}" stop-opacity="0.85"/>
                    </linearGradient>
                    <linearGradient id="ribbon3Grad" x1="0%" y1="0%" x2="0%" y2="100%">
                        <stop offset="0%" stop-color="{{SecondaryColor}}" stop-opacity="0.85"/>
                        <stop offset="25%" stop-color="{{SecondaryColor}}" stop-opacity="1"/>
                        <stop offset="50%" stop-color="{{SecondaryColor}}" stop-opacity="0.6"/>
                        <stop offset="75%" stop-color="{{SecondaryColor}}" stop-opacity="1"/>
                        <stop offset="100%" stop-color="{{SecondaryColor}}" stop-opacity="0.75"/>
                    </linearGradient>
                    <path id="waveRibbon" d="
                        M140,0
                        C170,80 190,160 190,250
                        C190,340 150,420 150,510
                        C150,600 190,680 190,770
                        C190,860 150,940 150,1030
                        C150,1120 190,1200 190,1290
                        C190,1380 150,1460 150,1550
                        C150,1640 190,1720 190,1810
                        C190,1900 150,1980 150,2070
                        L10,2070
                        C10,1980 50,1900 50,1810
                        C50,1720 10,1640 10,1550
                        C10,1460 50,1380 50,1290
                        C50,1200 10,1120 10,1030
                        C10,940 50,860 50,770
                        C50,680 10,600 10,510
                        C10,420 50,340 50,250
                        C50,160 30,80 0,0
                        Z
                    "/>
                </defs>
                <use href="#waveRibbon" fill="url(#ribbon1Grad)" opacity="0.225" transform="translate(-50, 0)"/>
                <use href="#waveRibbon" fill="url(#ribbon2Grad)" opacity="0.225" transform="translate(-10, -130)"/>
                <use href="#waveRibbon" fill="url(#ribbon3Grad)" opacity="0.225" transform="translate(30, -260)"/>
            </svg>
        </div>

        <!-- Main Content -->
        <div style="padding: 50px 50px 50px 140px; position: relative; z-index: 1;">
            <!-- Header Title -->
            <h1 style="font-family: 'Oswald', {{FontFamily}}; font-size: 52px; font-weight: bold; color: {{HeaderColor}}; letter-spacing: 2px; margin: 0 0 25px 0; text-transform: uppercase;">{{HeaderText}}</h1>

            <!-- Company Info -->
            <div style="margin-bottom: 30px;">
                {{#ShowLogo}}
                <img src="{{LogoSrc}}" alt="Company Logo" data-logo="1" style="display: inline-block; vertical-align: middle; max-width: {{LogoWidth}}px; max-height: 60px; width: auto; height: auto; object-fit: contain; margin-right: 14px;">
                {{/ShowLogo}}
                <div data-logo-slot="1" style="display: inline-block; vertical-align: middle; font-weight: bold; color: {{HeaderColor}}; font-size: 15px;">{{CompanyName}}</div>
                {{#ShowCompanyAddress}}{{#CompanyAddress}}<div style="color: #555; font-size: 14px; line-height: 1.6;">{{CompanyAddress}}</div>{{/CompanyAddress}}{{/ShowCompanyAddress}}
                {{#ShowCompanyCity}}{{#CompanyCity}}<div style="color: #555; font-size: 14px; line-height: 1.6;">{{CompanyCity}}{{#ShowCompanyProvinceState}}{{#CompanyProvinceState}}, {{CompanyProvinceState}}{{/CompanyProvinceState}}{{/ShowCompanyProvinceState}}{{#ShowCompanyCountry}}{{#CompanyCountry}}, {{CompanyCountry}}{{/CompanyCountry}}{{/ShowCompanyCountry}}</div>{{/CompanyCity}}{{/ShowCompanyCity}}
                {{^ShowCompanyCity}}{{#ShowCompanyProvinceState}}{{#CompanyProvinceState}}<div style="color: #555; font-size: 14px; line-height: 1.6;">{{CompanyProvinceState}}{{#ShowCompanyCountry}}{{#CompanyCountry}}, {{CompanyCountry}}{{/CompanyCountry}}{{/ShowCompanyCountry}}</div>{{/CompanyProvinceState}}{{/ShowCompanyProvinceState}}{{/ShowCompanyCity}}
                {{#CompanyEmail}}<div style="color: #555; font-size: 14px; line-height: 1.6;">{{CompanyEmail}}</div>{{/CompanyEmail}}
                {{#ShowCompanyPhone}}{{#CompanyPhone}}<div style="color: #555; font-size: 14px; line-height: 1.6;">{{CompanyPhone}}</div>{{/CompanyPhone}}{{/ShowCompanyPhone}}
            </div>

            <!-- Info Section: Sold To + Receipt Details -->
            <div style="display: flex; justify-content: space-between; margin-bottom: 35px; gap: 20px;">
                <div style="flex: 1;">
                    <div style="font-weight: bold; color: {{HeaderColor}}; font-size: 16px; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 8px;">Sold To</div>
                    <div style="color: {{TextColor}}; font-size: 14px; line-height: 1.6;">
                        <strong><span data-field="customer">{{CustomerName}}</span></strong><br>
                        {{#CustomerAddress}}{{CustomerAddress}}<br>{{/CustomerAddress}}
                        {{#CustomerEmail}}{{CustomerEmail}}{{/CustomerEmail}}
                    </div>
                </div>
                <div style="flex: 0.2;"></div>
                <div style="flex: 1.4;">
                    <div style="display: grid; grid-template-columns: auto 1fr; gap: 5px 20px;">
                        <span style="font-weight: bold; color: {{HeaderColor}}; font-size: 16px; text-transform: uppercase; white-space: nowrap;">Receipt #</span>
                        <span style="text-align: right; color: {{TextColor}}; font-size: 14px; white-space: nowrap;">{{InvoiceNumber}}</span>
                        <span style="font-weight: bold; color: {{HeaderColor}}; font-size: 16px; text-transform: uppercase; white-space: nowrap;">Receipt Date</span>
                        <span style="text-align: right; color: {{TextColor}}; font-size: 14px; white-space: nowrap;"><span data-field="issueDate" data-iso="{{IssueDateIso}}">{{IssueDate}}</span></span>
                        <span style="font-weight: bold; color: {{HeaderColor}}; font-size: 16px; text-transform: uppercase; white-space: nowrap;">Due Date</span>
                        <span style="text-align: right; font-size: 14px; white-space: nowrap; {{#IsOverdue}}color: #dc2626; font-weight: bold;{{/IsOverdue}}{{^IsOverdue}}color: {{TextColor}};{{/IsOverdue}}"><span data-field="dueDate" data-iso="{{DueDateIso}}">{{DueDate}}</span></span>
                    </div>
                    {{#ShowDueDateProminent}}
                    <div style="text-align: right; margin-top: 10px;">
                        <span style="display: inline-block; background-color: {{#IsOverdue}}#fef2f2{{/IsOverdue}}{{^IsOverdue}}#f0fdf4{{/IsOverdue}}; color: {{#IsOverdue}}#dc2626{{/IsOverdue}}{{^IsOverdue}}{{AccentColor}}{{/IsOverdue}}; padding: 6px 12px; border-radius: 4px; font-size: 12px; font-weight: 600;">
                            {{#IsOverdue}}OVERDUE{{/IsOverdue}}{{^IsOverdue}}DUE: {{DueDate}}{{/IsOverdue}}
                        </span>
                    </div>
                    {{/ShowDueDateProminent}}
                </div>
            </div>

            <!-- Items Table -->
            <table style="width: 100%; border-collapse: collapse; margin-bottom: 30px; font-family: {{FontFamily}}; font-weight: 600;">
                <thead style="border-top: 2px solid {{HeaderColor}}; border-bottom: 2px solid {{HeaderColor}};">
                    <tr>
                        <th style="color: {{HeaderColor}}; font-weight: bold; font-size: 16px; text-transform: uppercase; letter-spacing: 0.5px; padding: 14px 15px; text-align: left; width: 60px; background: transparent;">QTY</th>
                        <th style="color: {{HeaderColor}}; font-weight: bold; font-size: 16px; text-transform: uppercase; letter-spacing: 0.5px; padding: 14px 15px; text-align: left; background: transparent;">Description</th>
                        <th style="color: {{HeaderColor}}; font-weight: bold; font-size: 16px; text-transform: uppercase; letter-spacing: 0.5px; padding: 14px 15px; text-align: right; background: transparent;">Unit Price</th>
                        <th style="color: {{HeaderColor}}; font-weight: bold; font-size: 16px; text-transform: uppercase; letter-spacing: 0.5px; padding: 14px 15px; text-align: right; background: transparent;">Amount</th>
                    </tr>
                </thead>
                <tbody>
                    {{#LineItems}}
                    <tr>
                        <td style="padding: 16px 15px; border-bottom: 1px solid #e8e8e8; font-size: 14px; text-align: center; font-weight: 600; color: {{HeaderColor}};"><span data-field="quantity" data-line-index="{{Index}}">{{Quantity}}</span></td>
                        <td style="padding: 16px 15px; border-bottom: 1px solid #e8e8e8; font-size: 14px; color: {{TextColor}};">
                            <span data-field="description" data-line-index="{{Index}}">{{Description}}</span>
                            {{#ShowItemDescriptions}}{{#ItemDescription}}<br><span style="font-size: 12px; color: #888;">{{ItemDescription}}</span>{{/ItemDescription}}{{/ShowItemDescriptions}}
                        </td>
                        <td style="padding: 16px 15px; border-bottom: 1px solid #e8e8e8; font-size: 14px; color: {{TextColor}}; text-align: right;"><span data-field="rate" data-line-index="{{Index}}">{{UnitPrice}}</span></td>
                        <td style="padding: 16px 15px; border-bottom: 1px solid #e8e8e8; font-size: 14px; color: {{TextColor}}; text-align: right;"><span data-out="lineAmount" data-line-index="{{Index}}" data-line-discount="{{LineDiscountRaw}}">{{Amount}}</span></td>
                    </tr>
                    {{/LineItems}}
                </tbody>
            </table>

            <!-- Totals Section -->
            <div style="display: flex; justify-content: flex-end; margin-bottom: 50px;">
                <div style="width: 280px;">
                    <div style="display: flex; justify-content: space-between; padding: 10px 0; font-size: 14px; color: {{TextColor}}; border-top: 1px solid #e0e0e0;">
                        <span>Subtotal</span>
                        <span><span data-out="subtotal">{{Subtotal}}</span></span>
                    </div>
                    {{#ShowTaxRow}}
                    <div style="display: flex; justify-content: space-between; padding: 10px 0; font-size: 14px; color: {{TextColor}};">
                        <span>{{TaxLabel}}<span data-out="taxRateLabel">{{TaxRateLabel}}</span></span>
                        <span><span data-total="tax" data-total-raw="{{TaxRateRaw}}" data-total-mode="{{TaxModeRaw}}" data-total-symbol="{{CurrencySymbol}}">{{TaxAmount}}</span></span>
                    </div>
                    {{/ShowTaxRow}}
                    {{#ShowShipping}}
                    <div style="display: flex; justify-content: space-between; padding: 10px 0; font-size: 14px; color: {{TextColor}};">
                        <span>Shipping</span>
                        <span data-total="shipping" data-total-raw="{{ShippingRaw}}" data-total-symbol="{{CurrencySymbol}}">{{ShippingAmount}}</span>
                    </div>
                    {{/ShowShipping}}
                    {{#ShowSecurityDeposit}}
                    <div style="display: flex; justify-content: space-between; padding: 10px 0; font-size: 14px; color: {{TextColor}};">
                        <span>Security Deposit</span>
                        <span>{{SecurityDeposit}}</span>
                    </div>
                    {{/ShowSecurityDeposit}}
                    {{#ShowCustomFee}}
                    <div style="display: flex; justify-content: space-between; padding: 10px 0; font-size: 14px; color: {{TextColor}};">
                        <span>{{CustomFeeLabel}}</span>
                        <span><span data-total="fee" data-total-raw="{{CustomFeeRaw}}" data-total-mode="{{FeeModeRaw}}" data-total-symbol="{{CurrencySymbol}}">{{CustomFeeAmount}}</span></span>
                    </div>
                    {{/ShowCustomFee}}
                    {{#ShowDiscount}}
                    <div style="display: flex; justify-content: space-between; padding: 10px 0; font-size: 14px; color: {{TextColor}};">
                        <span>Discount</span>
                        <span><span data-total="discount" data-total-raw="{{DiscountRaw}}" data-total-mode="{{DiscountModeRaw}}" data-total-symbol="{{CurrencySymbol}}">{{DiscountAmount}}</span></span>
                    </div>
                    {{/ShowDiscount}}
                    {{#AmountPaid}}
                    <div style="display: flex; justify-content: space-between; padding: 6px 0; font-size: 14px; color: {{AccentColor}};">
                        <span>Amount Paid</span>
                        <span>-{{AmountPaid}}</span>
                    </div>
                    {{/AmountPaid}}
                    {{#ShowProcessingFee}}
                    <div style="display: flex; justify-content: space-between; padding: 10px 0; font-size: 14px; color: {{TextColor}};">
                        <span>{{ProcessingFeeLabel}}</span>
                        <span><span data-out="processingFee">{{ProcessingFeeAmount}}</span></span>
                    </div>
                    {{/ShowProcessingFee}}
                    {{#ShowAmountToPay}}
                    <div style="display: flex; justify-content: space-between; padding: 15px 0 8px 0; font-size: 14px; border-top: 2px solid {{HeaderColor}}; margin-top: 10px;">
                        <span style="font-weight: bold; color: {{HeaderColor}}; font-size: 16px; text-transform: uppercase;">Amount to Pay</span>
                        <span style="font-weight: bold; color: {{HeaderColor}}; font-size: 22px;"><span data-out="amountToPay">{{AmountToPay}}</span></span>
                    </div>
                    {{/ShowAmountToPay}}
                </div>
            </div>

            {{#ShowPaymentInstructions}}
            {{#PaymentInstructions}}
            <!-- Terms & Conditions -->
            <div style="border-top: 3px solid {{HeaderColor}}; padding-top: 20px;">
                <div style="font-weight: bold; color: {{HeaderColor}}; font-size: 14px; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 12px;">Terms &amp; Conditions</div>
                <div style="color: {{TextColor}}; font-size: 13px; line-height: 1.8; white-space: pre-line;">{{PaymentInstructions}}</div>
            </div>
            {{/PaymentInstructions}}
            {{/ShowPaymentInstructions}}

            <!-- Footer -->
            <div style="text-align: center; margin-top: 30px; padding-top: 20px; border-top: 1px solid #e0e0e0;">
                <div style="font-size: 13px; color: {{TextColor}}; margin-bottom: 6px;"><span data-field="notes">{{FooterOrNotes}}</span></div>
                <div style="font-size: 12px; color: #9ca3af;">
                    {{CompanyName}}{{#ShowCompanyAddress}}{{#CompanyAddress}} &bull; {{CompanyAddress}}{{/CompanyAddress}}{{/ShowCompanyAddress}}{{#ShowCompanyCity}}{{#CompanyCity}} &bull; {{CompanyCity}}{{/CompanyCity}}{{/ShowCompanyCity}}{{#ShowCompanyProvinceState}}{{#CompanyProvinceState}} &bull; {{CompanyProvinceState}}{{/CompanyProvinceState}}{{/ShowCompanyProvinceState}}{{#ShowCompanyCountry}}{{#CompanyCountry}} &bull; {{CompanyCountry}}{{/CompanyCountry}}{{/ShowCompanyCountry}}{{#CompanyEmail}} &bull; {{CompanyEmail}}{{/CompanyEmail}}{{#ShowCompanyPhone}}{{#CompanyPhone}} &bull; {{CompanyPhone}}{{/CompanyPhone}}{{/ShowCompanyPhone}}
                </div>
            </div>
        </div>
    </div>
</body>
</html>
""";

    /// <summary>
    /// Gets the HTML template for the specified template type.
    /// </summary>
    public static string GetTemplate(InvoiceTemplateType templateType)
    {
        return templateType switch
        {
            InvoiceTemplateType.Professional => Professional,
            InvoiceTemplateType.Modern => Modern,
            InvoiceTemplateType.Classic => Classic,
            InvoiceTemplateType.Elegant => Elegant,
            InvoiceTemplateType.Ribbon => Ribbon,
            _ => Professional
        };
    }
}
