using ArgoBooks.Utilities;
using Xunit;

namespace ArgoBooks.Tests.Utilities;

/// <summary>
/// Tests for the PaginationTextHelper class.
/// </summary>
public class PaginationTextHelperTests
{
    #region FormatPaginationText Tests

    [Fact]
    public void FormatPaginationText_ZeroItems_ReturnsZeroPlural()
    {
        var result = PaginationTextHelper.FormatPaginationText(0, 1, 10, 0, "customer", "customers");

        Assert.Equal("0 customers", result);
    }

    [Fact]
    public void FormatPaginationText_SingleItem_ReturnsSingular()
    {
        var result = PaginationTextHelper.FormatPaginationText(1, 1, 10, 1, "customer", "customers");

        Assert.Equal("1 customer", result);
    }

    [Fact]
    public void FormatPaginationText_MultipleItemsSinglePage_ReturnsCount()
    {
        var result = PaginationTextHelper.FormatPaginationText(5, 1, 10, 1, "customer", "customers");

        Assert.Equal("5 customers", result);
    }

    [Fact]
    public void FormatPaginationText_FirstPage_ReturnsCorrectRange()
    {
        var result = PaginationTextHelper.FormatPaginationText(25, 1, 10, 3, "customer", "customers");

        Assert.Equal("1-10 of 25 customers", result);
    }

    [Fact]
    public void FormatPaginationText_MiddlePage_ReturnsCorrectRange()
    {
        var result = PaginationTextHelper.FormatPaginationText(25, 2, 10, 3, "customer", "customers");

        Assert.Equal("11-20 of 25 customers", result);
    }

    [Fact]
    public void FormatPaginationText_LastPage_ReturnsCorrectRange()
    {
        var result = PaginationTextHelper.FormatPaginationText(25, 3, 10, 3, "customer", "customers");

        Assert.Equal("21-25 of 25 customers", result);
    }

    [Fact]
    public void FormatPaginationText_NullPlural_AddsS()
    {
        var result = PaginationTextHelper.FormatPaginationText(5, 1, 10, 1, "item");

        Assert.Equal("5 items", result);
    }

    [Fact]
    public void FormatPaginationText_NullPluralSingularCase_UsesSingular()
    {
        var result = PaginationTextHelper.FormatPaginationText(1, 1, 10, 1, "item");

        Assert.Equal("1 item", result);
    }

    [Theory]
    [InlineData(100, 1, 25, 4, "invoice", "invoices", "1-25 of 100 invoices")]
    [InlineData(100, 2, 25, 4, "invoice", "invoices", "26-50 of 100 invoices")]
    [InlineData(100, 3, 25, 4, "invoice", "invoices", "51-75 of 100 invoices")]
    [InlineData(100, 4, 25, 4, "invoice", "invoices", "76-100 of 100 invoices")]
    public void FormatPaginationText_VariousPages_ReturnsCorrectRange(
        int totalCount, int currentPage, int pageSize, int totalPages,
        string singular, string plural, string expected)
    {
        var result = PaginationTextHelper.FormatPaginationText(
            totalCount, currentPage, pageSize, totalPages, singular, plural);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatPaginationText_PartialLastPage_ReturnsCorrectEnd()
    {
        // 47 items, 10 per page, page 5 should show 41-47
        var result = PaginationTextHelper.FormatPaginationText(47, 5, 10, 5, "product", "products");

        Assert.Equal("41-47 of 47 products", result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void FormatPaginationText_SinglePageWithExactPageSize_ReturnsSimpleCount()
    {
        // 10 items on a page of 10 = 1 page total
        var result = PaginationTextHelper.FormatPaginationText(10, 1, 10, 1, "revenue", "revenues");

        Assert.Equal("10 revenues", result);
    }

    [Fact]
    public void FormatPaginationText_LargeNumbers_FormatsCorrectly()
    {
        var result = PaginationTextHelper.FormatPaginationText(
            10000, 50, 100, 100, "transaction", "transactions");

        Assert.Equal("4901-5000 of 10000 transactions", result);
    }

    [Fact]
    public void FormatPaginationText_PageSizeOf1_WorksCorrectly()
    {
        var result = PaginationTextHelper.FormatPaginationText(5, 3, 1, 5, "item", "items");

        Assert.Equal("3-3 of 5 items", result);
    }

    #endregion
}
