using ArgoBooks.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the ChartImageExportService static helpers.
/// </summary>
public class ChartImageExportServiceTests
{
    #region CreateSafeFileName Tests

    [Fact]
    public void CreateSafeFileName_NormalString_ReturnsSame()
    {
        var result = ChartImageExportService.CreateSafeFileName("Revenue Chart");

        var expected = $"Revenue-Chart-{DateTime.Now:yyyy-MM-dd}";
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CreateSafeFileName_WithSlashes_RemovesThem()
    {
        var result = ChartImageExportService.CreateSafeFileName("Q1/Q2 Report");

        Assert.DoesNotContain("/", result);
    }

    [Fact]
    public void CreateSafeFileName_WithSpecialChars_RemovesThem()
    {
        var result = ChartImageExportService.CreateSafeFileName("Chart: Revenue (2024)");

        Assert.DoesNotContain(":", result);
    }

    [Fact]
    public void CreateSafeFileName_EmptyString_FallsBackToChart()
    {
        var result = ChartImageExportService.CreateSafeFileName("");

        var expected = $"Chart-{DateTime.Now:yyyy-MM-dd}";
        Assert.Equal(expected, result);
    }

    #endregion
}
