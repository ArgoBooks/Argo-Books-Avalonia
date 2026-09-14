using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Transactions;
using Xunit;

namespace ArgoBooks.Tests.Data;

/// <summary>
/// Tests for the IdGenerator class.
/// </summary>
public class IdGeneratorTests
{
    private CompanyData CreateCompanyData()
    {
        return new CompanyData();
    }

    #region Category ID Generation Tests

    [Theory]
    [InlineData(CategoryType.Revenue, "CAT-REV-001")]
    [InlineData(CategoryType.Expense, "CAT-EXP-001")]
    [InlineData(CategoryType.Rental, "CAT-RNT-001")]
    public void NextCategoryId_GeneratesCorrectPrefixForType(CategoryType type, string expectedId)
    {
        var companyData = CreateCompanyData();
        var generator = new IdGenerator(companyData);

        var id = generator.NextCategoryId(type);

        Assert.Equal(expectedId, id);
    }

    [Fact]
    public void NextCategoryId_SharesCounterAcrossTypes()
    {
        var companyData = CreateCompanyData();
        var generator = new IdGenerator(companyData);

        var salesId = generator.NextCategoryId(CategoryType.Revenue);
        var purchaseId = generator.NextCategoryId(CategoryType.Expense);
        var rentalId = generator.NextCategoryId(CategoryType.Rental);

        Assert.Equal("CAT-REV-001", salesId);
        Assert.Equal("CAT-EXP-002", purchaseId);
        Assert.Equal("CAT-RNT-003", rentalId);
    }

    [Fact]
    public void NextCategoryId_SkipsAnIdACategoryAlreadyHas()
    {
        var companyData = CreateCompanyData();
        companyData.Categories.Add(new Category { Id = "CAT-EXP-001", Name = "Typed by hand", Type = CategoryType.Expense });

        var id = new IdGenerator(companyData).NextCategoryId(CategoryType.Expense);

        Assert.Equal("CAT-EXP-002", id);
    }

    #endregion

    #region Revenue and Expense ID Generation Tests

    [Fact]
    public void NextRevenueId_TakesTheYearFromTheRevenuesDate()
    {
        var id = new IdGenerator(CreateCompanyData()).NextRevenueId(new DateTime(2019, 12, 31));

        Assert.Equal("REV-2019-00001", id);
    }

    [Fact]
    public void NextExpenseId_TakesTheYearFromTheExpensesDate()
    {
        var id = new IdGenerator(CreateCompanyData()).NextExpenseId(new DateTime(2021, 1, 1));

        Assert.Equal("PUR-2021-00001", id);
    }

    [Fact]
    public void NextRevenueAndExpenseId_SkipIdsAlreadyTaken()
    {
        var companyData = CreateCompanyData();
        companyData.Revenues.Add(new Revenue { Id = "REV-2024-00001" });
        companyData.Expenses.Add(new Expense { Id = "PUR-2024-00001" });
        var generator = new IdGenerator(companyData);

        Assert.Equal("REV-2024-00002", generator.NextRevenueId(new DateTime(2024, 6, 1)));
        Assert.Equal("PUR-2024-00002", generator.NextExpenseId(new DateTime(2024, 6, 1)));
        Assert.Equal(2, companyData.IdCounters.Revenue);
        Assert.Equal(2, companyData.IdCounters.Expense);
    }

    #endregion

    #region Invoice ID Generation Tests

    [Fact]
    public void NextInvoiceId_IncludesYear()
    {
        var companyData = CreateCompanyData();
        var generator = new IdGenerator(companyData);

        var id = generator.NextInvoiceId();

        Assert.Contains(DateTime.UtcNow.Year.ToString(), id);
        Assert.Matches(@"^INV-\d{4}-\d{5}$", id);
    }

    [Fact]
    public void NextInvoiceNumber_ReturnsDisplayFormat()
    {
        var companyData = CreateCompanyData();
        var generator = new IdGenerator(companyData);

        // First generate an invoice ID to increment counter
        generator.NextInvoiceId();

        var number = generator.NextInvoiceNumber();

        Assert.StartsWith("#INV-", number);
        Assert.Contains(DateTime.UtcNow.Year.ToString(), number);
    }

    #endregion

    #region Counter Persistence Tests

    [Fact]
    public void Counters_ArePersistedInCompanyData()
    {
        var companyData = CreateCompanyData();
        var generator = new IdGenerator(companyData);

        generator.NextInvoiceId();
        generator.NextInvoiceId();

        Assert.Equal(2, companyData.IdCounters.Invoice);
    }

    [Fact]
    public void NewGenerator_UsesExistingCounters()
    {
        var companyData = CreateCompanyData();
        companyData.IdCounters.Invoice = 50;

        var generator = new IdGenerator(companyData);
        var id = generator.NextInvoiceId();

        Assert.Contains("00051", id);
    }

    [Fact]
    public void DifferentEntityTypes_HaveIndependentCounters()
    {
        var companyData = CreateCompanyData();
        var generator = new IdGenerator(companyData);

        generator.NextInvoiceId();
        generator.NextInvoiceId();
        generator.NextInvoiceId();

        var categoryId = generator.NextCategoryId(CategoryType.Revenue);

        Assert.Equal("CAT-REV-001", categoryId);
        Assert.Equal(3, companyData.IdCounters.Invoice);
        Assert.Equal(1, companyData.IdCounters.Category);
    }

    #endregion
}
