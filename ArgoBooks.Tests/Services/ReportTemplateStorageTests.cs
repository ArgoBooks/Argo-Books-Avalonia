using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the ReportTemplateStorage class.
/// </summary>
public class ReportTemplateStorageTests : IDisposable
{
    private readonly string _testDir;
    private readonly ReportTemplateStorage _storage;

    public ReportTemplateStorageTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "ArgoBooks_Test_Templates_" + Guid.NewGuid().ToString("N")[..8]);
        _storage = new ReportTemplateStorage(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_CustomDirectory_SetsTemplatesDirectory()
    {
        Assert.Equal(_testDir, _storage.TemplatesDirectory);
    }

    #endregion

    #region GetSavedTemplateNames Tests

    [Fact]
    public void GetSavedTemplateNames_EmptyDirectory_ReturnsEmptyList()
    {
        var result = _storage.GetSavedTemplateNames();

        Assert.Empty(result);
    }

    #endregion

    #region TemplateExists Tests

    [Fact]
    public void TemplateExists_NoTemplates_ReturnsFalse()
    {
        var result = _storage.TemplateExists("NonExistent");

        Assert.False(result);
    }

    #endregion

    #region SaveTemplateAsync Tests

    [Fact]
    public async Task SaveTemplateAsync_ValidConfig_ReturnsTrue()
    {
        var config = new ReportConfiguration();

        var result = await _storage.SaveTemplateAsync(config, "Test Template");

        Assert.True(result);
    }

    [Fact]
    public async Task SaveTemplateAsync_CreatesDirectory()
    {
        var config = new ReportConfiguration();

        await _storage.SaveTemplateAsync(config, "Test Template");

        Assert.True(Directory.Exists(_testDir));
    }

    [Fact]
    public async Task SaveTemplateAsync_TemplateExistsAfterSave()
    {
        var config = new ReportConfiguration();
        await _storage.SaveTemplateAsync(config, "Test Template");

        var exists = _storage.TemplateExists("Test Template");

        Assert.True(exists);
    }

    #endregion

    #region LoadTemplateAsync Tests

    [Fact]
    public async Task LoadTemplateAsync_ExistingTemplate_ReturnsConfig()
    {
        var config = new ReportConfiguration { Title = "Test Report" };
        await _storage.SaveTemplateAsync(config, "Load Test");

        var loaded = await _storage.LoadTemplateAsync("Load Test");

        Assert.NotNull(loaded);
        Assert.Equal("Test Report", loaded.Title);
    }

    [Fact]
    public async Task LoadTemplateAsync_NonExistent_ReturnsNull()
    {
        var loaded = await _storage.LoadTemplateAsync("Does Not Exist");

        Assert.Null(loaded);
    }

    #endregion

    #region DeleteTemplate Tests

    [Fact]
    public async Task DeleteTemplate_ExistingTemplate_ReturnsTrue()
    {
        var config = new ReportConfiguration();
        await _storage.SaveTemplateAsync(config, "Delete Test");

        var result = _storage.DeleteTemplate("Delete Test");

        Assert.True(result);
    }

    [Fact]
    public void DeleteTemplate_NonExistent_ReturnsFalse()
    {
        var result = _storage.DeleteTemplate("Does Not Exist");

        Assert.False(result);
    }

    [Fact]
    public async Task DeleteTemplate_TemplateNoLongerExists()
    {
        var config = new ReportConfiguration();
        await _storage.SaveTemplateAsync(config, "Delete Test 2");
        _storage.DeleteTemplate("Delete Test 2");

        var exists = _storage.TemplateExists("Delete Test 2");

        Assert.False(exists);
    }

    #endregion

    #region GetImagesDirectory Tests

    [Fact]
    public void GetImagesDirectory_ReturnsSubdirectory()
    {
        var imagesDir = _storage.GetImagesDirectory();

        Assert.Contains("images", imagesDir.ToLower());
    }

    #endregion
}
