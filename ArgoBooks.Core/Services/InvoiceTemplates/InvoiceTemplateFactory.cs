using ArgoBooks.Core.Models.Invoices;

namespace ArgoBooks.Core.Services.InvoiceTemplates;

/// <summary>
/// Factory for creating default invoice templates.
/// </summary>
public static class InvoiceTemplateFactory
{
    /// <summary>
    /// Creates the default set of invoice templates.
    /// </summary>
    public static List<InvoiceTemplate> CreateDefaultTemplates()
    {
        return
        [
            CreateProfessionalTemplate(),
            CreateModernTemplate(),
            CreateClassicTemplate(),
            CreateElegantTemplate(),
            CreateRibbonTemplate()
        ];
    }

    /// <summary>
    /// Creates the Professional template with default settings.
    /// </summary>
    public static InvoiceTemplate CreateProfessionalTemplate()
    {
        return new InvoiceTemplate
        {
            Id = "default-professional",
            Name = "Professional",
            BaseTemplate = InvoiceTemplateType.Professional,
            IsDefault = true,
            PrimaryColor = AppColors.PrimaryHover,
            SecondaryColor = AppColors.ChartGrid,
            AccentColor = AppColors.EmeraldHover,
            HeaderColor = AppColors.PrimaryHover,
            TextColor = AppColors.TextLight,
            BackgroundColor = AppColors.White,
            FontFamily = "Arial, Helvetica, sans-serif",
            HeaderText = "INVOICE",
            FooterText = "Thank you for your business!",
            ShowLogo = true,
            ShowCompanyAddress = true,
            ShowTaxBreakdown = true,
            ShowItemDescriptions = true,
            ShowPaymentInstructions = true,
            ShowDueDateProminent = true
        };
    }

    /// <summary>
    /// Creates the Modern template with default settings.
    /// </summary>
    public static InvoiceTemplate CreateModernTemplate()
    {
        return new InvoiceTemplate
        {
            Id = "default-modern",
            Name = "Modern",
            BaseTemplate = InvoiceTemplateType.Modern,
            IsDefault = false,
            PrimaryColor = AppColors.SlateDark,
            SecondaryColor = AppColors.SlateLight,
            AccentColor = AppColors.SkyBlue,
            HeaderColor = AppColors.SlateDark,
            TextColor = AppColors.SlateDark,
            BackgroundColor = AppColors.White,
            FontFamily = "'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif",
            HeaderText = "Invoice",
            FooterText = "Thank you for your business.",
            ShowLogo = true,
            ShowCompanyAddress = true,
            ShowTaxBreakdown = true,
            ShowItemDescriptions = true,
            ShowPaymentInstructions = true,
            ShowDueDateProminent = false
        };
    }

    /// <summary>
    /// Creates the Classic template with default settings.
    /// </summary>
    public static InvoiceTemplate CreateClassicTemplate()
    {
        return new InvoiceTemplate
        {
            Id = "default-classic",
            Name = "Classic",
            BaseTemplate = InvoiceTemplateType.Classic,
            IsDefault = false,
            PrimaryColor = AppColors.PrimaryDarkBg,
            SecondaryColor = AppColors.GrayBorder,
            AccentColor = AppColors.EmeraldDark,
            HeaderColor = AppColors.PrimaryDarkBg,
            TextColor = AppColors.TextLightAlt,
            BackgroundColor = AppColors.White,
            FontFamily = "Georgia, 'Times New Roman', Times, serif",
            HeaderText = "INVOICE",
            FooterText = "Thank you for your business!",
            ShowLogo = true,
            ShowCompanyAddress = true,
            ShowTaxBreakdown = true,
            ShowItemDescriptions = true,
            ShowPaymentInstructions = true,
            ShowDueDateProminent = false
        };
    }

    /// <summary>
    /// Creates the Elegant template with default settings.
    /// </summary>
    public static InvoiceTemplate CreateElegantTemplate()
    {
        return new InvoiceTemplate
        {
            Id = "default-elegant",
            Name = "Elegant",
            BaseTemplate = InvoiceTemplateType.Elegant,
            IsDefault = false,
            PrimaryColor = AppColors.IndigoText,
            SecondaryColor = AppColors.GrayLightest,
            AccentColor = AppColors.TealHover,
            HeaderColor = AppColors.IndigoText,
            TextColor = AppColors.TextLight,
            BackgroundColor = AppColors.White,
            FontFamily = "Georgia, 'Times New Roman', Times, serif",
            HeaderText = "Invoice",
            FooterText = "Thank you for your business!",
            ShowLogo = true,
            ShowCompanyAddress = true,
            ShowTaxBreakdown = true,
            ShowItemDescriptions = true,
            ShowPaymentInstructions = true,
            ShowDueDateProminent = true
        };
    }

    /// <summary>
    /// Creates the Ribbon template with default settings.
    /// </summary>
    public static InvoiceTemplate CreateRibbonTemplate()
    {
        return new InvoiceTemplate
        {
            Id = "default-ribbon",
            Name = "Ribbon",
            BaseTemplate = InvoiceTemplateType.Ribbon,
            IsDefault = false,
            PrimaryColor = AppColors.LightBlue,
            SecondaryColor = AppColors.YellowBright,
            AccentColor = AppColors.LightGreen,
            HeaderColor = AppColors.NavyBlue,
            TextColor = AppColors.DarkGrayText,
            BackgroundColor = AppColors.White,
            FontFamily = "'Open Sans', 'Segoe UI', Arial, sans-serif",
            HeaderText = "SALES RECEIPT",
            FooterText = "Thank you for your business!",
            ShowLogo = true,
            ShowCompanyAddress = true,
            ShowTaxBreakdown = true,
            ShowItemDescriptions = true,
            ShowPaymentInstructions = true,
            ShowDueDateProminent = false
        };
    }

    /// <summary>
    /// Creates a custom template with the specified base type.
    /// </summary>
    public static InvoiceTemplate CreateCustomTemplate(string id, string name, InvoiceTemplateType baseType)
    {
        var baseTemplate = baseType switch
        {
            InvoiceTemplateType.Professional => CreateProfessionalTemplate(),
            InvoiceTemplateType.Modern => CreateModernTemplate(),
            InvoiceTemplateType.Classic => CreateClassicTemplate(),
            InvoiceTemplateType.Elegant => CreateElegantTemplate(),
            InvoiceTemplateType.Ribbon => CreateRibbonTemplate(),
            _ => CreateProfessionalTemplate()
        };

        baseTemplate.Id = id;
        baseTemplate.Name = name;
        baseTemplate.IsDefault = false;
        baseTemplate.CreatedAt = DateTime.UtcNow;
        baseTemplate.UpdatedAt = DateTime.UtcNow;

        return baseTemplate;
    }
}
