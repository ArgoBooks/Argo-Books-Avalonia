using System.Collections.ObjectModel;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

public class BankCategoryRuleRowTests
{
    // The category picker is a SearchableDropdown that shows its SearchText.
    [Fact]
    public void Constructor_WithResolvableCategory_SeedsSelectedCategoryAndSearchText()
    {
        var category = new Category { Id = "CAT-PUR-001", Name = "Office Supplies", Type = CategoryType.Expense };
        var allCategories = new ObservableCollection<Category> { category };
        var rule = new BankCategoryRule { Id = "R1", Pattern = "staples", CategoryId = "CAT-PUR-001" };

        var row = new BankCategoryRuleRow(rule, allCategories);

        Assert.Same(category, row.SelectedCategory);
        Assert.Equal("Office Supplies", row.CategorySearchText);
    }

    [Fact]
    public void Constructor_WithNoCategory_LeavesSelectionAndSearchTextEmpty()
    {
        var allCategories = new ObservableCollection<Category>();
        var rule = new BankCategoryRule { Id = "R2", Pattern = "atm withdrawal", CategoryId = "" };

        var row = new BankCategoryRuleRow(rule, allCategories);

        Assert.Null(row.SelectedCategory);
        Assert.True(string.IsNullOrEmpty(row.CategorySearchText));
    }
}
