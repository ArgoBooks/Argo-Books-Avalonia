using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The Paid check matches on substrings, and "unpaid" contains "paid", so negated statuses have to be
/// read first. Getting this wrong counts unpaid sales as collected revenue.
/// </summary>
public class SpreadsheetImportPaymentStatusTests
{
    [Theory]
    [InlineData("Unpaid")]
    [InlineData("Not paid")]
    [InlineData("unsettled")]
    [InlineData("Uncollected")]
    [InlineData("Not received")]
    [InlineData("Incomplete")]
    public void NegatedStatus_IsUnpaid(string text) =>
        Assert.Equal(RevenuePaymentStatus.Unpaid, SpreadsheetImportService.NormalizePaymentStatus(text));

    [Theory]
    [InlineData("Paid", RevenuePaymentStatus.Paid)]
    [InlineData("Settled", RevenuePaymentStatus.Paid)]
    [InlineData("Partially paid", RevenuePaymentStatus.Partial)]
    [InlineData("Overdue", RevenuePaymentStatus.Overdue)]
    [InlineData("Past due", RevenuePaymentStatus.Overdue)]
    [InlineData("Pending", RevenuePaymentStatus.Pending)]
    [InlineData("Outstanding", RevenuePaymentStatus.Unpaid)]
    public void OtherStatuses_Unchanged(string text, RevenuePaymentStatus expected) =>
        Assert.Equal(expected, SpreadsheetImportService.NormalizePaymentStatus(text));

    [Fact]
    public void EveryStatusName_RoundTripsThroughExportText()
    {
        foreach (var status in Enum.GetValues<RevenuePaymentStatus>())
        {
            var expected = status == RevenuePaymentStatus.Complete ? RevenuePaymentStatus.Paid : status;
            Assert.Equal(expected, SpreadsheetImportService.NormalizePaymentStatus(status.ToString()));
        }
    }
}
