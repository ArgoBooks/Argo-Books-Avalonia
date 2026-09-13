using ArgoBooks.Core.Models;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Stub implementation of IGlobalSettingsService for testing.
/// </summary>
internal class StubGlobalSettingsService : IGlobalSettingsService
{
    private GlobalSettings _settings = new();

    public GlobalSettings GetSettings() => _settings;
    public void SaveSettings(GlobalSettings settings) => _settings = settings;
    public Task<GlobalSettings> LoadAsync() => Task.FromResult(_settings);
    public Task SaveAsync(GlobalSettings settings) { _settings = settings; return Task.CompletedTask; }
    public IReadOnlyList<string> GetRecentCompanies() => _settings.RecentCompanies.AsReadOnly();
    public void AddRecentCompany(string filePath) => _settings.RecentCompanies.Add(filePath);
    public void RemoveRecentCompany(string filePath) => _settings.RecentCompanies.Remove(filePath);
}

/// <summary>
/// Tests for the TutorialService class.
/// </summary>
public class TutorialServiceTests
{
    private readonly TutorialService _service;

    public TutorialServiceTests()
    {
        _service = new TutorialService();
        var settingsService = new StubGlobalSettingsService();
        // Set FirstLaunchDate so the user is not considered a first-time user by default
        settingsService.GetSettings().Tutorial.FirstLaunchDate = DateTime.UtcNow;
        _service.SetGlobalSettingsService(settingsService);
    }

    #region Checklist Tests

    [Fact]
    public void GetTotalChecklistCount_ReturnsPositive()
    {
        Assert.True(_service.GetTotalChecklistCount() > 0);
    }

    [Fact]
    public void AreAllChecklistItemsCompleted_DefaultIsFalse()
    {
        Assert.False(_service.AreAllChecklistItemsCompleted());
    }

    [Fact]
    public void IsChecklistItemCompleted_UnknownItem_ReturnsFalse()
    {
        Assert.False(_service.IsChecklistItemCompleted("unknown_item"));
    }

    #endregion

    #region Page Visit Tests

    [Fact]
    public void HasVisitedPage_UnvisitedPage_ReturnsFalse()
    {
        Assert.False(_service.HasVisitedPage("SomePage"));
    }

    [Fact]
    public void MarkPageVisited_ThenHasVisitedPage_ReturnsTrue()
    {
        _service.MarkPageVisited("TestPage");

        Assert.True(_service.HasVisitedPage("TestPage"));
    }

    #endregion

    #region Tutorial State Tests

    [Fact]
    public void CompleteWelcomeTutorial_SetsFlag()
    {
        _service.CompleteWelcomeTutorial();

        Assert.True(_service.HasCompletedWelcomeTutorial);
    }

    [Fact]
    public void CompleteAppTour_SetsFlag()
    {
        _service.CompleteAppTour();

        Assert.True(_service.HasCompletedAppTour);
    }

    [Fact]
    public void DisableFirstVisitHints_ClearsFlag()
    {
        _service.DisableFirstVisitHints();

        Assert.False(_service.ShowFirstVisitHints);
    }

    #endregion

    #region Reset Tests

    [Fact]
    public void ResetAllTutorials_ResetsAllFlags()
    {
        _service.CompleteWelcomeTutorial();
        _service.CompleteAppTour();
        _service.MarkPageVisited("TestPage");

        _service.ResetAllTutorials();

        Assert.False(_service.HasCompletedWelcomeTutorial);
        Assert.False(_service.HasCompletedAppTour);
    }

    #endregion

    #region GetFirstVisitHint Tests

    [Fact]
    public void GetFirstVisitHint_KnownPage_ReturnsHint()
    {
        var hint = TutorialService.GetFirstVisitHint("Categories");

        // Should return a hint object or null depending on implementation
        Assert.NotNull(hint);
    }

    [Fact]
    public void GetFirstVisitHint_UnknownPage_ReturnsNull()
    {
        var hint = TutorialService.GetFirstVisitHint("NonExistentPage123");

        Assert.Null(hint);
    }

    #endregion

    #region Event Tests

    [Fact]
    public void CompleteChecklistItem_RaisesEvent()
    {
        var eventRaised = false;
        _service.ChecklistItemCompleted += (_, _) => eventRaised = true;

        _service.CompleteChecklistItem(TutorialService.ChecklistItems.ScanReceipt);

        Assert.True(eventRaised);
    }

    [Fact]
    public void MarkPageVisited_RaisesEvent()
    {
        var eventRaised = false;
        _service.PageFirstVisited += (_, _) => eventRaised = true;

        _service.MarkPageVisited("NewTestPage");

        Assert.True(eventRaised);
    }

    #endregion
}
