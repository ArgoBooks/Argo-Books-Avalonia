using Avalonia.Data;
using ArgoBooks.Converters;
using Xunit;

namespace ArgoBooks.Tests.Converters;

/// <summary>
/// Tests for the EqualityConverter class.
/// </summary>
public class EqualityConverterTests
{
    #region EqualityConverter Convert Tests

    [Fact]
    public void EqualityConverter_Convert_BothNull_ReturnsTrue()
    {
        var converter = new EqualityConverter();

        var result = converter.Convert(null, typeof(bool), null, null!);

        Assert.Equal(true, result);
    }

    [Fact]
    public void EqualityConverter_Convert_ValueNullParameterNotNull_ReturnsFalse()
    {
        var converter = new EqualityConverter();

        var result = converter.Convert(null, typeof(bool), "something", null!);

        Assert.Equal(false, result);
    }

    [Fact]
    public void EqualityConverter_Convert_ValueNotNullParameterNull_ReturnsFalse()
    {
        var converter = new EqualityConverter();

        var result = converter.Convert("something", typeof(bool), null, null!);

        Assert.Equal(false, result);
    }

    [Fact]
    public void EqualityConverter_Convert_EqualValues_ReturnsTrue()
    {
        var converter = new EqualityConverter();

        var result = converter.Convert("test", typeof(bool), "test", null!);

        Assert.Equal(true, result);
    }

    [Fact]
    public void EqualityConverter_Convert_DifferentValues_ReturnsFalse()
    {
        var converter = new EqualityConverter();

        var result = converter.Convert("test", typeof(bool), "other", null!);

        Assert.Equal(false, result);
    }

    [Fact]
    public void EqualityConverter_Convert_EqualIntegers_ReturnsTrue()
    {
        var converter = new EqualityConverter();

        var result = converter.Convert(42, typeof(bool), 42, null!);

        Assert.Equal(true, result);
    }

    #endregion

    #region EqualityConverter ConvertBack Tests

    [Fact]
    public void EqualityConverter_ConvertBack_TrueValue_ReturnsParameter()
    {
        var converter = new EqualityConverter();

        var result = converter.ConvertBack(true, typeof(object), "myParam", null!);

        Assert.Equal("myParam", result);
    }

    [Fact]
    public void EqualityConverter_ConvertBack_FalseValue_ReturnsDoNothing()
    {
        var converter = new EqualityConverter();

        var result = converter.ConvertBack(false, typeof(object), "myParam", null!);

        Assert.Equal(BindingOperations.DoNothing, result);
    }

    [Fact]
    public void EqualityConverter_ConvertBack_NonBoolValue_ReturnsDoNothing()
    {
        var converter = new EqualityConverter();

        var result = converter.ConvertBack("not bool", typeof(object), "myParam", null!);

        Assert.Equal(BindingOperations.DoNothing, result);
    }

    #endregion

    #region EqualityConverter Int Parameter Tests

    [Fact]
    public void EqualityConverter_Convert_IntWithMatchingStringParameter_ReturnsTrue()
    {
        var converter = new EqualityConverter();

        var result = converter.Convert(42, typeof(bool), "42", null!);

        Assert.Equal(true, result);
    }

    [Fact]
    public void EqualityConverter_Convert_IntWithDifferentStringParameter_ReturnsFalse()
    {
        var converter = new EqualityConverter();

        var result = converter.Convert(42, typeof(bool), "43", null!);

        Assert.Equal(false, result);
    }

    [Fact]
    public void EqualityConverter_Convert_IntWithNonNumericStringParameter_ReturnsFalse()
    {
        var converter = new EqualityConverter();

        var result = converter.Convert(42, typeof(bool), "abc", null!);

        Assert.Equal(false, result);
    }

    [Fact]
    public void EqualityConverter_Convert_DifferentStrings_ReturnsFalse()
    {
        var converter = new EqualityConverter();

        var result = converter.Convert("hello", typeof(bool), "world", null!);

        Assert.Equal(false, result);
    }

    #endregion
}
