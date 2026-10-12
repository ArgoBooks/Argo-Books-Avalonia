using System.Globalization;
using ArgoBooks.Converters;
using Xunit;

namespace ArgoBooks.Tests.Converters;

/// <summary>
/// Tests for the NullableDecimalConverter class.
/// </summary>
public class NullableDecimalConverterTests
{
    private readonly NullableDecimalConverter _converter = NullableDecimalConverter.Instance;

    #region Convert Tests

    [Fact]
    public void Convert_DecimalValue_ToStringTarget_ReturnsFormattedString()
    {
        var result = _converter.Convert(123.45m, typeof(string), null, null!);

        Assert.NotNull(result);
        Assert.Contains("123", result.ToString()!);
    }

    [Fact]
    public void Convert_DecimalValue_ToDecimalTarget_ReturnsDecimal()
    {
        var result = _converter.Convert(123.45m, typeof(decimal), null, null!);

        Assert.Equal(123.45m, result);
    }

    [Fact]
    public void Convert_NullableDecimalTarget_ReturnsDecimal()
    {
        var result = _converter.Convert(50.0m, typeof(decimal?), null, null!);

        Assert.Equal(50.0m, result);
    }

    #endregion

    /// <summary>
    /// A quantity box asks for three places and a money box for two, so the format the binding
    /// passes decides how many the reader sees. Without it every box would show money's two.
    /// </summary>
    [Theory]
    [InlineData(1.5, "0.###", "1.5")]
    [InlineData(2, "0.###", "2")]
    [InlineData(17500.555, "0.###", "17500.555")]
    [InlineData(1234.5, "N2", "1,234.50")]
    [InlineData(12.3, null, "12.30")]
    public void Convert_WithAFormat_UsesIt(decimal value, string? format, string shown)
    {
        var result = _converter.Convert(value, typeof(string), format, CultureInfo.InvariantCulture);

        Assert.Equal(shown, result);
    }

    [Theory]
    [InlineData("0.###", "0")]
    [InlineData("N2", "0.00")]
    [InlineData(null, "0.00")]
    public void Convert_NothingAtAll_ShowsZeroInThatFormat(string? format, string shown)
    {
        var result = _converter.Convert(null, typeof(string), format, CultureInfo.InvariantCulture);

        Assert.Equal(shown, result);
    }

    /// <summary>A price shown with a thousands separator has to read back as the same number.</summary>
    [Fact]
    public void ConvertBack_APriceWithASeparator_ReadsAsTheSameNumber()
    {
        var result = _converter.ConvertBack("1,234.56", typeof(decimal?), "N2", CultureInfo.InvariantCulture);

        Assert.Equal(1234.56m, result);
    }

    #region ConvertBack Tests

    [Fact]
    public void ConvertBack_ValidDecimalString_ReturnsDecimal()
    {
        var result = _converter.ConvertBack("123.45", typeof(decimal), null, null!);

        Assert.Equal(123.45m, result);
    }

    [Fact]
    public void ConvertBack_EmptyString_ReturnsZero()
    {
        var result = _converter.ConvertBack("", typeof(decimal), null, null!);

        Assert.Equal(0m, result);
    }

    [Fact]
    public void ConvertBack_WhitespaceString_ReturnsZero()
    {
        var result = _converter.ConvertBack("   ", typeof(decimal), null, null!);

        Assert.Equal(0m, result);
    }

    [Fact]
    public void ConvertBack_NullValue_ReturnsZero()
    {
        var result = _converter.ConvertBack(null, typeof(decimal), null, null!);

        Assert.Equal(0m, result);
    }

    [Fact]
    public void ConvertBack_DecimalValue_ReturnsAsIs()
    {
        var result = _converter.ConvertBack(99.99m, typeof(decimal), null, null!);

        Assert.Equal(99.99m, result);
    }

    [Fact]
    public void ConvertBack_InvalidString_ReturnsZero()
    {
        var result = _converter.ConvertBack("not a number", typeof(decimal), null, null!);

        Assert.Equal(0m, result);
    }

    [Fact]
    public void ConvertBack_IntegerString_ReturnsDecimal()
    {
        var result = _converter.ConvertBack("100", typeof(decimal), null, null!);

        Assert.Equal(100m, result);
    }

    [Fact]
    public void ConvertBack_NegativeString_ReturnsNegativeDecimal()
    {
        var result = _converter.ConvertBack("-50.5", typeof(decimal), null, null!);

        Assert.Equal(-50.5m, result);
    }

    #endregion

    #region Singleton Tests

    [Fact]
    public void Instance_ReturnsSameInstance()
    {
        var instance1 = NullableDecimalConverter.Instance;
        var instance2 = NullableDecimalConverter.Instance;

        Assert.Same(instance1, instance2);
    }

    #endregion
}
