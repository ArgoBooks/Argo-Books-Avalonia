using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Inventory;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Models.Rentals;
using Xunit;

namespace ArgoBooks.Tests.Models;

/// <summary>
/// Tests for which optional sidebar sections a company shows.
/// </summary>
public class FeatureVisibilityTests
{
    private static CompanySettings SettingsFor(string? industry) =>
        new() { Company = { Industry = industry } };

    [Fact]
    public void Services_HidesInventoryAndRentals()
    {
        var visible = FeatureVisibility.Resolve(SettingsFor(IndustryNames.Services), new CompanyData());

        Assert.False(visible.Inventory);
        Assert.False(visible.Rentals);
    }

    [Fact]
    public void Retail_ShowsInventoryButNotRentals()
    {
        var visible = FeatureVisibility.Resolve(SettingsFor(IndustryNames.Retail), new CompanyData());

        Assert.True(visible.Inventory);
        Assert.False(visible.Rentals);
    }

    [Fact]
    public void RealEstate_ShowsRentalsButNotInventory()
    {
        var visible = FeatureVisibility.Resolve(SettingsFor(IndustryNames.RealEstate), new CompanyData());

        Assert.True(visible.Rentals);
        Assert.False(visible.Inventory);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(IndustryNames.Other)]
    public void UnknownIndustry_ShowsEverything(string? industry)
    {
        var visible = FeatureVisibility.Resolve(SettingsFor(industry), new CompanyData());

        Assert.True(visible.Inventory);
        Assert.True(visible.Rentals);
        Assert.True(visible.Payroll);
    }

    [Fact]
    public void Payroll_StaysOnForEveryIndustry()
    {
        foreach (var industry in IndustryNames.All)
        {
            var visible = FeatureVisibility.Resolve(SettingsFor(industry), new CompanyData());
            Assert.True(visible.Payroll);
        }
    }

    [Fact]
    public void ExistingRecords_KeepASectionTheIndustryWouldHide()
    {
        var data = new CompanyData();
        data.Inventory.Add(new InventoryItem());
        data.RentalInventory.Add(new RentalItem());

        var visible = FeatureVisibility.Resolve(SettingsFor(IndustryNames.Services), data);

        Assert.True(visible.Inventory);
        Assert.True(visible.Rentals);
    }

    [Fact]
    public void ExplicitToggleOff_BeatsBothTheIndustryAndExistingRecords()
    {
        var data = new CompanyData();
        data.Inventory.Add(new InventoryItem());
        data.Employees.Add(new Employee());

        var settings = SettingsFor(IndustryNames.Retail);
        settings.Features.ShowInventory = false;
        settings.Features.ShowPayroll = false;

        var visible = FeatureVisibility.Resolve(settings, data);

        Assert.False(visible.Inventory);
        Assert.False(visible.Payroll);
    }

    [Fact]
    public void ExplicitToggleOn_BeatsTheIndustryDefault()
    {
        var settings = SettingsFor(IndustryNames.Services);
        settings.Features.ShowRentals = true;

        var visible = FeatureVisibility.Resolve(settings, new CompanyData());

        Assert.True(visible.Rentals);
    }

    [Fact]
    public void NoCompanyOpen_FallsBackToShowingEverything()
    {
        var visible = FeatureVisibility.Resolve(null, null);

        Assert.True(visible.Inventory);
        Assert.True(visible.Rentals);
        Assert.True(visible.Payroll);
    }
}
