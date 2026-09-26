using ArgoBooks.Converters;
using Xunit;

namespace ArgoBooks.Tests.Converters;

/// <summary>
/// Tests for the FullscreenDimensionConverter class.
/// </summary>
public class FullscreenDimensionConverterTests
{
    #region Normal Mode Tests

    [Fact]
    public void Convert_NotFullscreen_WithDoubleParameter_ReturnsParameter()
    {
        var converter = new FullscreenDimensionConverter(800);

        var result = converter.Convert(false, typeof(double), 600.0, null!);

        Assert.Equal(600.0, result);
    }

    [Fact]
    public void Convert_NotFullscreen_WithStringParameter_ReturnsParsedValue()
    {
        var converter = new FullscreenDimensionConverter(800);

        var result = converter.Convert(false, typeof(double), "600", null!);

        Assert.Equal(600.0, result);
    }

    [Fact]
    public void Convert_NotFullscreen_NoParameter_ReturnsDefault()
    {
        var converter = new FullscreenDimensionConverter(800);

        var result = converter.Convert(false, typeof(double), null, null!);

        Assert.Equal(800.0, result);
    }

    #endregion

    #region Fullscreen Mode Tests

    [Fact]
    public void Convert_Fullscreen_WithSingleParameter_ReturnsNaN()
    {
        var converter = new FullscreenDimensionConverter(800);

        var result = converter.Convert(true, typeof(double), "600", null!);

        Assert.True(double.IsNaN((double)result));
    }

    [Fact]
    public void Convert_Fullscreen_WithDoubleParameter_ReturnsNaN()
    {
        var converter = new FullscreenDimensionConverter(800);

        var result = converter.Convert(true, typeof(double), 600.0, null!);

        Assert.True(double.IsNaN((double)result));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Convert_NonBoolValue_ReturnsDefault()
    {
        var converter = new FullscreenDimensionConverter(800);

        var result = converter.Convert("not bool", typeof(double), null, null!);

        Assert.Equal(800.0, result);
    }

    [Fact]
    public void Convert_NullValue_ReturnsDefault()
    {
        var converter = new FullscreenDimensionConverter(800);

        var result = converter.Convert(null, typeof(double), null, null!);

        Assert.Equal(800.0, result);
    }

    #endregion
}
