using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Tests for the EditCompanyModalViewModel.
/// </summary>
public class EditCompanyModalViewModelTests
{
    private readonly EditCompanyModalViewModel _viewModel = new();

    #region HasChanges Tests

    [Fact]
    public void HasChanges_WhenNoModifications_ReturnsFalse()
    {
        _viewModel.Open("Test Company", "LLC", "Technology");

        Assert.False(_viewModel.HasChanges);
    }

    [Fact]
    public void HasChanges_WhenCompanyNameModified_ReturnsTrue()
    {
        _viewModel.Open("Original Name");

        _viewModel.CompanyName = "Modified Name";

        Assert.True(_viewModel.HasChanges);
    }

    [Fact]
    public void HasChanges_WhenBusinessTypeModified_ReturnsTrue()
    {
        _viewModel.Open("Test Company", businessType: "LLC");

        _viewModel.BusinessType = "Corporation";

        Assert.True(_viewModel.HasChanges);
    }

    [Fact]
    public void HasChanges_WhenIndustryModified_ReturnsTrue()
    {
        _viewModel.Open("Test Company", industry: "Technology");

        _viewModel.Industry = "Healthcare";

        Assert.True(_viewModel.HasChanges);
    }

    [Fact]
    public void HasChanges_WhenCountryModified_ReturnsTrue()
    {
        _viewModel.Open("Test Company", country: "United States");

        _viewModel.Country = "Canada";

        Assert.True(_viewModel.HasChanges);
    }

    [Fact]
    public void HasChanges_WhenCityModified_ReturnsTrue()
    {
        _viewModel.Open("Test Company", city: "New York");

        _viewModel.City = "Los Angeles";

        Assert.True(_viewModel.HasChanges);
    }

    [Fact]
    public void HasChanges_WhenAddressModified_ReturnsTrue()
    {
        _viewModel.Open("Test Company", address: "123 Main St");

        _viewModel.Address = "456 Oak Ave";

        Assert.True(_viewModel.HasChanges);
    }

    [Fact]
    public void HasChanges_WhenEmailModified_ReturnsTrue()
    {
        _viewModel.Open("Test Company", email: "old@example.com");

        _viewModel.Email = "new@example.com";

        Assert.True(_viewModel.HasChanges);
    }

    [Fact]
    public void HasChanges_WhenRevertedToOriginal_ReturnsFalse()
    {
        _viewModel.Open("Test Company");

        _viewModel.CompanyName = "Modified";
        Assert.True(_viewModel.HasChanges);

        _viewModel.CompanyName = "Test Company";
        Assert.False(_viewModel.HasChanges);
    }

    #endregion

    #region Data Lists Tests

    [Fact]
    public void BusinessTypes_IsPopulated()
    {
        Assert.NotEmpty(EditCompanyModalViewModel.BusinessTypes);
    }

    [Fact]
    public void BusinessTypes_ContainsExpectedTypes()
    {
        Assert.Contains("Sole Proprietorship", EditCompanyModalViewModel.BusinessTypes);
        Assert.Contains("Corporation", EditCompanyModalViewModel.BusinessTypes);
        Assert.Contains("LLC", EditCompanyModalViewModel.BusinessTypes);
        Assert.Contains("Non-Profit", EditCompanyModalViewModel.BusinessTypes);
    }

    [Fact]
    public void Industries_IsPopulated()
    {
        Assert.NotEmpty(EditCompanyModalViewModel.Industries);
    }

    [Fact]
    public void Industries_ContainsExpectedIndustries()
    {
        Assert.Contains("Retail", EditCompanyModalViewModel.Industries);
        Assert.Contains("Technology", EditCompanyModalViewModel.Industries);
        Assert.Contains("Healthcare", EditCompanyModalViewModel.Industries);
        Assert.Contains("Manufacturing", EditCompanyModalViewModel.Industries);
    }

    #endregion

    #region Open Tests

    [Fact]
    public void Open_WithCompanyInfo_SetsIsOpenToTrue()
    {
        _viewModel.Open("Test Company");

        Assert.True(_viewModel.IsOpen);
    }

    [Fact]
    public void Open_WithCompanyInfo_SetsCompanyName()
    {
        _viewModel.Open("My Business", "LLC", "Technology");

        Assert.Equal("My Business", _viewModel.CompanyName);
        Assert.Equal("LLC", _viewModel.BusinessType);
        Assert.Equal("Technology", _viewModel.Industry);
    }

    [Fact]
    public void Open_WithAllParameters_SetsAllFields()
    {
        _viewModel.Open(
            companyName: "Test Co",
            businessType: "Corporation",
            industry: "Retail",
            logo: null,
            phone: null,
            country: "Canada",
            city: "Toronto",
            address: "100 King St",
            email: "info@test.co"
        );

        Assert.Equal("Test Co", _viewModel.CompanyName);
        Assert.Equal("Corporation", _viewModel.BusinessType);
        Assert.Equal("Retail", _viewModel.Industry);
        Assert.Equal("Canada", _viewModel.Country);
        Assert.Equal("Toronto", _viewModel.City);
        Assert.Equal("100 King St", _viewModel.Address);
        Assert.Equal("info@test.co", _viewModel.Email);
    }

    #endregion

    #region CanSave Tests

    [Fact]
    public void CanSave_WhenCompanyNameAndCountrySet_ReturnsTrue()
    {
        _viewModel.Open("Test Company", country: "US");

        Assert.True(_viewModel.CanSave);
    }

    [Fact]
    public void CanSave_WhenCompanyNameEmpty_ReturnsFalse()
    {
        _viewModel.Open("", country: "US");

        Assert.False(_viewModel.CanSave);
    }

    [Fact]
    public void CanSave_WhenCountryNull_ReturnsFalse()
    {
        _viewModel.Open("Test Company", country: null);

        Assert.False(_viewModel.CanSave);
    }

    #endregion
}
