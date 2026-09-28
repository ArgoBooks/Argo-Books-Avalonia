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
    private readonly StubGlobalSettingsService _settingsService = new();

    public TutorialServiceTests()
    {
        _service = new TutorialService();
        // Set FirstLaunchDate so the user is not considered a first-time user by default
        _settingsService.GetSettings().Tutorial.FirstLaunchDate = DateTime.UtcNow;
        _service.SetGlobalSettingsService(_settingsService);
    }

    #region Checklist Tests

    [Fact]
    public void AreAllChecklistItemsCompleted_DefaultIsFalse()
    {
        Assert.False(_service.AreAllChecklistItemsCompleted());
    }

    [Fact]
    public void MigrateLegacyChecklist_FinishedOldList_CreditsImportStep()
    {
        CompleteItems(
            TutorialService.ChecklistItems.ScanReceipt,
            TutorialService.ChecklistItems.RecordExpense,
            TutorialService.ChecklistItems.VisitAnalytics);

        _service.MigrateLegacyChecklist();

        Assert.True(IsChecklistItemCompleted(TutorialService.ChecklistItems.ImportData));
        Assert.True(_service.AreAllChecklistItemsCompleted());
    }

    [Fact]
    public void MigrateLegacyChecklist_PartialOldList_DoesNotCreditImportStep()
    {
        CompleteItems(
            TutorialService.ChecklistItems.RecordExpense,
            TutorialService.ChecklistItems.VisitAnalytics);

        _service.MigrateLegacyChecklist();

        Assert.False(IsChecklistItemCompleted(TutorialService.ChecklistItems.ImportData));
    }

    [Fact]
    public void MigrateLegacyChecklist_NoLegacyItem_CreditsNothing()
    {
        CompleteItems(
            TutorialService.ChecklistItems.ScanReceipt,
            TutorialService.ChecklistItems.RecordExpense);

        _service.MigrateLegacyChecklist();

        Assert.False(IsChecklistItemCompleted(TutorialService.ChecklistItems.ImportData));
    }

    private bool IsChecklistItemCompleted(string itemId) =>
        _settingsService.GetSettings().Tutorial.CompletedChecklistItems.Contains(itemId);

    private void CompleteItems(params string[] itemIds)
    {
        foreach (var id in itemIds)
            _service.CompleteChecklistItem(id);
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

    #region Event Tests

    [Fact]
    public void CompleteChecklistItem_RaisesEvent()
    {
        var eventRaised = false;
        _service.ChecklistItemCompleted += (_, _) => eventRaised = true;

        _service.CompleteChecklistItem(TutorialService.ChecklistItems.ScanReceipt);

        Assert.True(eventRaised);
    }

    #endregion
}
