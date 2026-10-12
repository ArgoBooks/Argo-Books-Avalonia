using ArgoBooks.Core.Models.Common;
using Xunit;

namespace ArgoBooks.Tests.Models;

/// <summary>
/// An amount sitting exactly halfway rounds to the larger number, so 6.305 is 6.31 and never 6.30.
/// C# rounds a half to whichever neighbour is even unless it is told otherwise, which would make
/// half of all halfway amounts a cent lower than the other half, and would disagree with payroll
/// and with the check for whether an invoice is paid in full. See Calculations.md §2.
/// </summary>
public class MoneyRoundingTests
{
    [Theory]
    // quantity, unit price, discount, expected subtotal. Each lands exactly on half a cent.
    [InlineData(3, 0.875, 0, 2.63)]   // 2.625, the even neighbour is 2.62
    [InlineData(1, 6.305, 0, 6.31)]   // 6.305, the even neighbour is 6.30
    [InlineData(1, 10.00, 7.875, 2.13)] // 2.125, the even neighbour is 2.12
    public void ALineSubtotalRoundsAHalfUp(decimal quantity, decimal unitPrice, decimal discount, decimal expected)
    {
        Assert.Equal(expected, LineItem.SubtotalOf(quantity, unitPrice, discount));
    }

    /// <summary>
    /// $48.50 at 13% is exactly 6.3050, the case a Canadian company hits on an ordinary invoice.
    /// </summary>
    [Fact]
    public void ALinesTaxRoundsAHalfUp()
    {
        var line = new LineItem { Quantity = 1, UnitPrice = 48.50m, TaxRate = 0.13m };

        Assert.Equal(48.50m, line.Subtotal);
        Assert.Equal(6.31m, line.TaxAmount);
        Assert.Equal(54.81m, line.Amount);
    }
}
