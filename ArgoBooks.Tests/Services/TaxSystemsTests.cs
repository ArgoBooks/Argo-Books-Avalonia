using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Which sales tax system a company's country uses, which names the tax on its invoices and in its reports.
/// </summary>
public class TaxSystemsTests
{
    [Theory]
    [InlineData("Canada", TaxSystem.GstHst)]
    [InlineData("United States", TaxSystem.SalesTax)]
    [InlineData("Puerto Rico", TaxSystem.SalesTax)]
    [InlineData("United Kingdom", TaxSystem.VAT)]
    [InlineData("Germany", TaxSystem.VAT)]
    [InlineData("Mexico", TaxSystem.VAT)]
    [InlineData("Australia", TaxSystem.GST)]
    [InlineData("India", TaxSystem.GST)]
    [InlineData("Japan", TaxSystem.JCT)]
    [InlineData("Atlantis", TaxSystem.Tax)]
    [InlineData("", TaxSystem.Tax)]
    [InlineData(null, TaxSystem.Tax)]
    public void For_NamesTheCountrysTaxSystem(string? country, TaxSystem expected)
    {
        Assert.Equal(expected, TaxSystems.For(country));
    }

    [Theory]
    [InlineData("canada")]
    [InlineData("  CANADA  ")]
    public void For_IgnoresCaseAndSurroundingSpaces(string country)
    {
        Assert.Equal(TaxSystem.GstHst, TaxSystems.For(country));
    }

    [Theory]
    [InlineData(TaxSystem.VAT, "VAT")]
    [InlineData(TaxSystem.GstHst, "GST/HST")]
    [InlineData(TaxSystem.GST, "GST")]
    [InlineData(TaxSystem.JCT, "Consumption Tax")]
    [InlineData(TaxSystem.SalesTax, "Sales Tax")]
    [InlineData(TaxSystem.Tax, "Tax")]
    public void InvoiceLabel_IsTheTaxsShortName(TaxSystem system, string expected)
    {
        Assert.Equal(expected, TaxSystems.InvoiceLabel(system));
    }

    [Theory]
    [InlineData("Canada")]
    [InlineData("United States")]
    [InlineData("United Kingdom")]
    [InlineData("Australia")]
    [InlineData("Japan")]
    public void TaxSummary_NamesTheTaxAsTheInvoicesDo(string country)
    {
        var data = new CompanyData();
        data.Settings.Company.Country = country;

        var report = new AccountingReportDataService(data, new ReportFilters()).GetReportData(AccountingReportType.TaxSummary);

        var netTax = report.Rows.Single(r => r.RowType == AccountingRowType.GrandTotalRow).Label;
        Assert.Contains(TaxSystems.InvoiceLabel(TaxSystems.For(country)), netTax, StringComparison.OrdinalIgnoreCase);
    }
}
