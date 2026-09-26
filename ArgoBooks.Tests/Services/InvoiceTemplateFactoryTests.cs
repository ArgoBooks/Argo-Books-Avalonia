using ArgoBooks.Core.Models.Invoices;
using ArgoBooks.Core.Services.InvoiceTemplates;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the InvoiceTemplateFactory class.
/// </summary>
public class InvoiceTemplateFactoryTests
{
    #region CreateDefaultTemplates Tests

    [Fact]
    public void CreateDefaultTemplates_ReturnsMultipleTemplates()
    {
        var templates = InvoiceTemplateFactory.CreateDefaultTemplates();

        Assert.NotNull(templates);
        Assert.Equal(5, templates.Count);
    }

    [Fact]
    public void CreateDefaultTemplates_EachTemplateHasNonEmptyId()
    {
        var templates = InvoiceTemplateFactory.CreateDefaultTemplates();

        foreach (var template in templates)
        {
            Assert.False(string.IsNullOrEmpty(template.Id),
                $"Template '{template.Name}' has a null or empty Id");
        }
    }

    [Fact]
    public void CreateDefaultTemplates_EachTemplateHasNonEmptyName()
    {
        var templates = InvoiceTemplateFactory.CreateDefaultTemplates();

        foreach (var template in templates)
        {
            Assert.False(string.IsNullOrEmpty(template.Name),
                $"Template with Id '{template.Id}' has a null or empty Name");
        }
    }

    [Fact]
    public void CreateDefaultTemplates_AllIdsAreUnique()
    {
        var templates = InvoiceTemplateFactory.CreateDefaultTemplates();
        var ids = templates.Select(t => t.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void CreateDefaultTemplates_AllNamesAreUnique()
    {
        var templates = InvoiceTemplateFactory.CreateDefaultTemplates();
        var names = templates.Select(t => t.Name).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void CreateDefaultTemplates_ExactlyOneIsDefault()
    {
        var templates = InvoiceTemplateFactory.CreateDefaultTemplates();

        var defaultCount = templates.Count(t => t.IsDefault);

        Assert.Equal(1, defaultCount);
    }

    [Fact]
    public void CreateDefaultTemplates_ContainsAllExpectedTypes()
    {
        var templates = InvoiceTemplateFactory.CreateDefaultTemplates();
        var baseTypes = templates.Select(t => t.BaseTemplate).ToHashSet();

        Assert.Contains(InvoiceTemplateType.Professional, baseTypes);
        Assert.Contains(InvoiceTemplateType.Modern, baseTypes);
        Assert.Contains(InvoiceTemplateType.Classic, baseTypes);
        Assert.Contains(InvoiceTemplateType.Elegant, baseTypes);
        Assert.Contains(InvoiceTemplateType.Ribbon, baseTypes);
    }

    #endregion

    #region Template Properties Tests

    [Theory]
    [InlineData("default-professional", "Professional")]
    [InlineData("default-modern", "Modern")]
    [InlineData("default-classic", "Classic")]
    [InlineData("default-elegant", "Elegant")]
    [InlineData("default-ribbon", "Ribbon")]
    public void CreateDefaultTemplates_TemplateHasExpectedIdAndName(string expectedId, string expectedName)
    {
        var templates = InvoiceTemplateFactory.CreateDefaultTemplates();

        var template = templates.FirstOrDefault(t => t.Id == expectedId);

        Assert.NotNull(template);
        Assert.Equal(expectedName, template.Name);
    }

    [Fact]
    public void CreateDefaultTemplates_AllTemplatesHaveColorSettings()
    {
        var templates = InvoiceTemplateFactory.CreateDefaultTemplates();

        foreach (var template in templates)
        {
            Assert.False(string.IsNullOrEmpty(template.PrimaryColor),
                $"Template '{template.Name}' has empty PrimaryColor");
            Assert.False(string.IsNullOrEmpty(template.SecondaryColor),
                $"Template '{template.Name}' has empty SecondaryColor");
            Assert.False(string.IsNullOrEmpty(template.AccentColor),
                $"Template '{template.Name}' has empty AccentColor");
            Assert.False(string.IsNullOrEmpty(template.HeaderColor),
                $"Template '{template.Name}' has empty HeaderColor");
            Assert.False(string.IsNullOrEmpty(template.TextColor),
                $"Template '{template.Name}' has empty TextColor");
            Assert.False(string.IsNullOrEmpty(template.BackgroundColor),
                $"Template '{template.Name}' has empty BackgroundColor");
        }
    }

    [Fact]
    public void CreateDefaultTemplates_AllTemplatesHaveFontFamily()
    {
        var templates = InvoiceTemplateFactory.CreateDefaultTemplates();

        foreach (var template in templates)
        {
            Assert.False(string.IsNullOrEmpty(template.FontFamily),
                $"Template '{template.Name}' has empty FontFamily");
        }
    }

    [Fact]
    public void CreateDefaultTemplates_AllTemplatesHaveHeaderText()
    {
        var templates = InvoiceTemplateFactory.CreateDefaultTemplates();

        foreach (var template in templates)
        {
            Assert.False(string.IsNullOrEmpty(template.HeaderText),
                $"Template '{template.Name}' has empty HeaderText");
        }
    }

    [Fact]
    public void CreateDefaultTemplates_AllTemplatesHaveFooterText()
    {
        var templates = InvoiceTemplateFactory.CreateDefaultTemplates();

        foreach (var template in templates)
        {
            Assert.False(string.IsNullOrEmpty(template.FooterText),
                $"Template '{template.Name}' has empty FooterText");
        }
    }

    [Fact]
    public void CreateDefaultTemplates_AllColorsAreValidHexFormat()
    {
        var templates = InvoiceTemplateFactory.CreateDefaultTemplates();

        foreach (var template in templates)
        {
            AssertIsHexColor(template.PrimaryColor, $"{template.Name}.PrimaryColor");
            AssertIsHexColor(template.SecondaryColor, $"{template.Name}.SecondaryColor");
            AssertIsHexColor(template.AccentColor, $"{template.Name}.AccentColor");
            AssertIsHexColor(template.TextColor, $"{template.Name}.TextColor");
            AssertIsHexColor(template.BackgroundColor, $"{template.Name}.BackgroundColor");
        }
    }

    #endregion

    #region Individual Template Creation Tests

    [Fact]
    public void CreateProfessionalTemplate_ReturnsCorrectTemplate()
    {
        var template = InvoiceTemplateFactory.CreateProfessionalTemplate();

        Assert.Equal("default-professional", template.Id);
        Assert.Equal("Professional", template.Name);
        Assert.Equal(InvoiceTemplateType.Professional, template.BaseTemplate);
        Assert.True(template.IsDefault);
        Assert.True(template.ShowLogo);
        Assert.True(template.ShowCompanyAddress);
        Assert.True(template.ShowTaxBreakdown);
    }

    [Fact]
    public void CreateModernTemplate_ReturnsCorrectTemplate()
    {
        var template = InvoiceTemplateFactory.CreateModernTemplate();

        Assert.Equal("default-modern", template.Id);
        Assert.Equal("Modern", template.Name);
        Assert.Equal(InvoiceTemplateType.Modern, template.BaseTemplate);
        Assert.False(template.IsDefault);
    }

    [Fact]
    public void CreateClassicTemplate_ReturnsCorrectTemplate()
    {
        var template = InvoiceTemplateFactory.CreateClassicTemplate();

        Assert.Equal("default-classic", template.Id);
        Assert.Equal("Classic", template.Name);
        Assert.Equal(InvoiceTemplateType.Classic, template.BaseTemplate);
        Assert.False(template.IsDefault);
    }

    [Fact]
    public void CreateElegantTemplate_ReturnsCorrectTemplate()
    {
        var template = InvoiceTemplateFactory.CreateElegantTemplate();

        Assert.Equal("default-elegant", template.Id);
        Assert.Equal("Elegant", template.Name);
        Assert.Equal(InvoiceTemplateType.Elegant, template.BaseTemplate);
        Assert.False(template.IsDefault);
    }

    [Fact]
    public void CreateRibbonTemplate_ReturnsCorrectTemplate()
    {
        var template = InvoiceTemplateFactory.CreateRibbonTemplate();

        Assert.Equal("default-ribbon", template.Id);
        Assert.Equal("Ribbon", template.Name);
        Assert.Equal(InvoiceTemplateType.Ribbon, template.BaseTemplate);
        Assert.False(template.IsDefault);
    }

    #endregion

    #region Helper Methods

    private static void AssertIsHexColor(string color, string propertyName)
    {
        Assert.True(
            color.StartsWith('#') && (color.Length == 7 || color.Length == 4 || color.Length == 9),
            $"{propertyName} value '{color}' is not a valid hex color");
    }

    #endregion
}
