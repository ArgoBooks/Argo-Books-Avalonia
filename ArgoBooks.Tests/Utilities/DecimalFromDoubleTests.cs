using ArgoBooks.Core.Utilities;
using Xunit;

namespace ArgoBooks.Tests.Utilities;

public class DecimalFromDoubleTests
{
    [Theory]
    [InlineData(-1284.9, "-1284.9")]
    [InlineData(0.05, "0.05")]
    [InlineData(1234.56, "1234.56")]
    [InlineData(0.1 + 0.2, "0.3")]
    [InlineData(1.23e-5, "0.0000123")]
    [InlineData(0, "0")]
    public void KeepsTheNumberAsWritten(double value, string expected) =>
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), DecimalFromDouble.Convert(value));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e30)]
    public void RefusesWhatADecimalCannotHold(double value) =>
        Assert.Throws<OverflowException>(() => DecimalFromDouble.Convert(value));
}
