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

    [Fact]
    public void SetRecentCompanies_KeepsTheFiveMostRecent()
    {
        _viewModel.SetRecentCompanies(Enumerable.Range(1, 7).Select(i => Company($"C{i}")));

        Assert.Equal(["C1", "C2", "C3", "C4", "C5"], _viewModel.RecentCompanies.Select(c => c.Name));
    }
}
