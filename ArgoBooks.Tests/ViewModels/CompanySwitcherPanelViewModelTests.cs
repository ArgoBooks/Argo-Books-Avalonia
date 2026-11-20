using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// The recent companies the company switcher offers.
/// </summary>
public class CompanySwitcherPanelViewModelTests
{
    private readonly CompanySwitcherPanelViewModel _viewModel = new();

    private static CompanyItem Company(string name) => new() { Name = name, FilePath = $"/{name}.argo" };

    [Fact]
    public void SetRecentCompanies_KeepsTheMostRecentFirst()
    {
        _viewModel.SetRecentCompanies([Company("Newest"), Company("Middle"), Company("Oldest")]);

        Assert.Equal(["Newest", "Middle", "Oldest"], _viewModel.RecentCompanies.Select(c => c.Name));
        Assert.Equal(["Newest", "Middle", "Oldest"], _viewModel.FilteredRecentCompanies.Select(c => c.Name));
    }

    /// <summary>
    /// The list on screen is worked out from the recent companies, so it has to be told when one
    /// goes, such as a company whose file was deleted.
    /// </summary>
    [Fact]
    public void RemovingARecentCompany_UpdatesTheListOnScreen()
    {
        _viewModel.SetRecentCompanies([Company("Kept"), Company("Deleted")]);
        var changed = new List<string?>();
        _viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _viewModel.RecentCompanies.Remove(_viewModel.RecentCompanies[1]);

        Assert.Contains(nameof(CompanySwitcherPanelViewModel.FilteredRecentCompanies), changed);
        Assert.Contains(nameof(CompanySwitcherPanelViewModel.HasFilteredRecentCompanies), changed);
    }

    [Fact]
    public void SetRecentCompanies_KeepsTheFiveMostRecent()
    {
        _viewModel.SetRecentCompanies(Enumerable.Range(1, 7).Select(i => Company($"C{i}")));

        Assert.Equal(["C1", "C2", "C3", "C4", "C5"], _viewModel.RecentCompanies.Select(c => c.Name));
    }
}
