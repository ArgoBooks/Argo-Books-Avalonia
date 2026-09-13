using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Tests for the QuickActionsViewModel search ranking functionality.
/// </summary>
public class QuickActionsViewModelTests
{
    private readonly QuickActionsViewModel _viewModel = new();

    #region Top Results Tests

    [Fact]
    public void SearchQuery_ExactTitleMatch_AppearsInTopResults()
    {
        _viewModel.SearchQuery = "export";

        Assert.True(_viewModel.HasTopResults);
        Assert.Contains(_viewModel.TopResults, x => x.Title == "Export Data");
    }

    [Fact]
    public void SearchQuery_PrefixTitleMatch_AppearsInTopResults()
    {
        _viewModel.SearchQuery = "exp";

        Assert.True(_viewModel.HasTopResults);
        Assert.Contains(_viewModel.TopResults, x => x.Title == "Export Data");
    }

    [Fact]
    public void SearchQuery_WordStartMatch_AppearsInTopResults()
    {
        _viewModel.SearchQuery = "data";

        Assert.True(_viewModel.HasTopResults);
        // "Export Data" has "Data" as a word start
        Assert.Contains(_viewModel.TopResults, x => x.Title == "Export Data");
    }

    [Fact]
    public void SearchQuery_StrongTitleMatch_ExcludedFromNormalCategory()
    {
        _viewModel.SearchQuery = "export";

        // Export Data should be in TopResults
        Assert.Contains(_viewModel.TopResults, x => x.Title == "Export Data");
        // And NOT in ToolsItems (its normal category)
        Assert.DoesNotContain(_viewModel.ToolsItems, x => x.Title == "Export Data");
    }

    [Fact]
    public void SearchQuery_FuzzyOnlyMatch_DoesNotAppearInTopResults()
    {
        // "expense" fuzzy matches "export" but not strongly enough for TopResults
        _viewModel.SearchQuery = "export";

        // "New Expense" should NOT be in TopResults (fuzzy match on "expense")
        Assert.DoesNotContain(_viewModel.TopResults, x => x.Title == "New Expense");
    }

    #endregion

    #region Title vs Description Prioritization Tests

    [Fact]
    public void SearchQuery_TitleMatchRanksHigherThanDescriptionMatch()
    {
        _viewModel.SearchQuery = "export";

        // "Export Data" with title match should appear before items
        // that only match via description fuzzy matching
        Assert.True(_viewModel.TopResults.Count > 0);
        Assert.Equal("Export Data", _viewModel.TopResults[0].Title);
    }

    [Fact]
    public void SearchQuery_DescriptionOnlyFuzzyMatch_ReducedScore()
    {
        // "Scan Receipt" has "import" in description which fuzzy matches "export"
        // but this should be scored lower than title matches
        _viewModel.SearchQuery = "export";

        // Scan Receipt should not be in TopResults (description-only fuzzy match)
        Assert.DoesNotContain(_viewModel.TopResults, x => x.Title == "Scan Receipt");
    }

    #endregion

    #region Category Grouping Tests

    [Fact]
    public void SearchQuery_Empty_ShowsAllCategoriesNoTopResults()
    {
        _viewModel.SearchQuery = "";

        Assert.False(_viewModel.HasTopResults);
        Assert.True(_viewModel.HasQuickActions);
        Assert.True(_viewModel.HasNavigationItems);
        Assert.True(_viewModel.HasToolsItems);
    }

    [Fact]
    public void SearchQuery_Null_ShowsAllCategoriesNoTopResults()
    {
        _viewModel.SearchQuery = null;

        Assert.False(_viewModel.HasTopResults);
        Assert.True(_viewModel.HasQuickActions);
        Assert.True(_viewModel.HasNavigationItems);
        Assert.True(_viewModel.HasToolsItems);
    }

    [Fact]
    public void SearchQuery_NoMatch_ShowsNoResults()
    {
        _viewModel.SearchQuery = "xyznonexistent123";

        Assert.False(_viewModel.HasTopResults);
        Assert.False(_viewModel.HasQuickActions);
        Assert.False(_viewModel.HasNavigationItems);
        Assert.False(_viewModel.HasToolsItems);
        Assert.False(_viewModel.HasResults);
    }

    #endregion

    #region Multiple Strong Matches Tests

    [Fact]
    public void SearchQuery_MultipleStrongMatches_AllAppearInTopResults()
    {
        // "new" should match multiple items strongly
        _viewModel.SearchQuery = "new";

        Assert.True(_viewModel.HasTopResults);
        // Multiple items start with "New"
        Assert.True(_viewModel.TopResults.Count > 1);
        Assert.All(_viewModel.TopResults, x => Assert.StartsWith("New", x.Title));
    }

    [Fact]
    public void SearchQuery_TopResultsLimitedTo4()
    {
        // "new" matches many items
        _viewModel.SearchQuery = "new";

        Assert.True(_viewModel.TopResults.Count <= 4);
    }

    #endregion

    #region Real-World Scenario Tests

    [Fact]
    public void SearchQuery_Invoice_ShowsInvoiceRelatedItemsFirst()
    {
        _viewModel.SearchQuery = "invoice";

        Assert.True(_viewModel.HasTopResults);
        Assert.Contains(_viewModel.TopResults, x => x.Title.Contains("Invoice"));
    }

    [Fact]
    public void SearchQuery_Settings_ShowsSettingsFirst()
    {
        _viewModel.SearchQuery = "settings";

        Assert.True(_viewModel.HasTopResults);
        Assert.Contains(_viewModel.TopResults, x => x.Title == "Settings");
    }

    [Fact]
    public void SearchQuery_Import_ShowsImportDataFirst()
    {
        _viewModel.SearchQuery = "import";

        Assert.True(_viewModel.HasTopResults);
        Assert.Contains(_viewModel.TopResults, x => x.Title == "Import Data");
    }

    #endregion

    #region Keyboard Navigation Tests

    [Fact]
    public void ExecuteSelected_IncludesTopResultsInSelection()
    {
        _viewModel.SearchQuery = "export";

        // Move down should cycle through all items including TopResults
        _viewModel.MoveDownCommand.Execute(null);
        Assert.Equal(0, _viewModel.SelectedIndex);

        // Verify TopResults are counted in total
        Assert.True(_viewModel.TopResults.Count > 0);
    }

    #endregion
}
