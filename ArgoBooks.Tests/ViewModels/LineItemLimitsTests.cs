using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// A quantity and a price are typed into a plain box, which accepts any number that fits in it, so
/// the line item itself has to refuse a figure too large to be a real one. Without that, a slipped
/// key turns one line into a total nobody meant to enter.
/// </summary>
public class LineItemLimitsTests
{
    [Theory]
    [InlineData(1000000, 999999)]
    [InlineData(9999999999, 999999)]
    public void AQuantityAboveTheCeiling_IsHeldAtIt(decimal typed, decimal kept)
    {
        var line = new ExpenseLineItem { Quantity = typed };

        Assert.Equal(kept, line.Quantity);
    }

    [Theory]
    [InlineData(1000000000, 999999999)]
    [InlineData(99999999999999, 999999999)]
    public void APriceAboveTheCeiling_IsHeldAtIt(decimal typed, decimal kept)
    {
        var line = new ExpenseLineItem { UnitPrice = typed };

        Assert.Equal(kept, line.UnitPrice);
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(1)]
    [InlineData(17500.555)]
    [InlineData(999999)]
    public void AQuantityWithinRange_IsKeptExactly(decimal typed)
    {
        var line = new ExpenseLineItem { Quantity = typed };

        Assert.Equal(typed, line.Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12.34)]
    [InlineData(999999999)]
    public void APriceWithinRange_IsKeptExactly(decimal typed)
    {
        var line = new ExpenseLineItem { UnitPrice = typed };

        Assert.Equal(typed, line.UnitPrice);
    }

    /// <summary>Holding a figure at the ceiling still has to leave the line's amount right.</summary>
    [Fact]
    public void HoldingAQuantityAtTheCeiling_LeavesTheAmountAgreeingWithIt()
    {
        var line = new ExpenseLineItem { UnitPrice = 2m, Quantity = 1000000m };

        Assert.Equal(999999m, line.Quantity);
        Assert.Equal(1999998m, line.Amount);
    }
}
