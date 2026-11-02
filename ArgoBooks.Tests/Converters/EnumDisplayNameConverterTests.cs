using ArgoBooks.Converters;
using ArgoBooks.Core.Enums;
using Xunit;

namespace ArgoBooks.Tests.Converters;

/// <summary>
/// Tests for the EnumDisplayNameConverter and TransactionTypeSupportsReturnsConverter.
/// </summary>
public class EnumDisplayNameConverterTests
{
    private readonly EnumDisplayNameConverter _converter = new();

    #region TransactionType Display Names

    [Theory]
    [InlineData(TransactionType.Revenue, "Revenue")]
    [InlineData(TransactionType.Expenses, "Expenses")]
    [InlineData(TransactionType.Invoices, "Invoices")]
    [InlineData(TransactionType.Payments, "Payments")]
    [InlineData(TransactionType.Customers, "Customers")]
    [InlineData(TransactionType.Suppliers, "Suppliers")]
    [InlineData(TransactionType.Products, "Products")]
    public void Convert_TransactionType_ReturnsDisplayName(TransactionType type, string expected)
    {
        var result = _converter.Convert(type, typeof(string), null, null!);

        Assert.Equal(expected, result);
    }

    #endregion

    #region TableSortOrder Display Names

    [Theory]
    [InlineData(TableSortOrder.DateDescending, "Date descending")]
    [InlineData(TableSortOrder.DateAscending, "Date ascending")]
    [InlineData(TableSortOrder.AmountDescending, "Amount descending")]
    [InlineData(TableSortOrder.AmountAscending, "Amount ascending")]
    public void Convert_TableSortOrder_ReturnsDisplayName(TableSortOrder order, string expected)
    {
        var result = _converter.Convert(order, typeof(string), null, null!);

        Assert.Equal(expected, result);
    }

    #endregion

    #region AccountingReportType Display Names

    [Theory]
    [InlineData(AccountingReportType.IncomeStatement, "Income Statement")]
    [InlineData(AccountingReportType.BalanceSheet, "Balance Sheet")]
    [InlineData(AccountingReportType.CashFlowStatement, "Cash Flow Statement")]
    [InlineData(AccountingReportType.GeneralLedger, "General Ledger")]
    [InlineData(AccountingReportType.TaxSummary, "Tax Summary")]
    [InlineData(AccountingReportType.TrialBalance, "Trial Balance")]
    [InlineData(AccountingReportType.InventoryValuation, "Inventory Valuation")]
    [InlineData(AccountingReportType.OpenInvoices, "Open Invoices")]
    [InlineData(AccountingReportType.UnpaidBills, "Unpaid Bills")]
    [InlineData(AccountingReportType.SalesTaxLiability, "Sales Tax Liability")]
    public void Convert_AccountingReportType_ReturnsDisplayName(AccountingReportType type, string expected)
    {
        var result = _converter.Convert(type, typeof(string), null, null!);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(IncomeComparison.None, "No comparison")]
    [InlineData(IncomeComparison.LastYear, "Compare with last year")]
    [InlineData(IncomeComparison.ByMonth, "By month")]
    public void Convert_IncomeComparison_ReturnsDisplayName(IncomeComparison comparison, string expected)
    {
        var result = _converter.Convert(comparison, typeof(string), null, null!);

        Assert.Equal(expected, result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Convert_NullValue_ReturnsEmpty()
    {
        var result = _converter.Convert(null, typeof(string), null, null!);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Convert_NonEnumValue_ReturnsToString()
    {
        var result = _converter.Convert("hello", typeof(string), null, null!);

        Assert.Equal("hello", result);
    }

    #endregion

    #region TransactionTypeSupportsReturnsConverter Tests

    [Fact]
    public void SupportsReturns_Revenue_ReturnsTrue()
    {
        var converter = new TransactionTypeSupportsReturnsConverter();

        var result = converter.Convert(TransactionType.Revenue, typeof(bool), null, null!);

        Assert.Equal(true, result);
    }

    [Fact]
    public void SupportsReturns_Expenses_ReturnsTrue()
    {
        var converter = new TransactionTypeSupportsReturnsConverter();

        var result = converter.Convert(TransactionType.Expenses, typeof(bool), null, null!);

        Assert.Equal(true, result);
    }

    [Fact]
    public void SupportsReturns_Invoices_ReturnsFalse()
    {
        var converter = new TransactionTypeSupportsReturnsConverter();

        var result = converter.Convert(TransactionType.Invoices, typeof(bool), null, null!);

        Assert.Equal(false, result);
    }

    [Fact]
    public void SupportsReturns_Customers_ReturnsFalse()
    {
        var converter = new TransactionTypeSupportsReturnsConverter();

        var result = converter.Convert(TransactionType.Customers, typeof(bool), null, null!);

        Assert.Equal(false, result);
    }

    [Fact]
    public void SupportsReturns_NullValue_ReturnsFalse()
    {
        var converter = new TransactionTypeSupportsReturnsConverter();

        var result = converter.Convert(null, typeof(bool), null, null!);

        Assert.Equal(false, result);
    }

    #endregion
}
