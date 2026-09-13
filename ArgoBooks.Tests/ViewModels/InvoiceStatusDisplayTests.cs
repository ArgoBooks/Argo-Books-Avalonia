using System.Reflection;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// The status the invoices list shows. It works the refund status out afresh rather than trusting the
/// stored one, so it has to use the same rule as InvoiceTotalsService (docs/Calculations.md §6).
/// </summary>
public class InvoiceStatusDisplayTests
{
    private static readonly MethodInfo GetStatusDisplay =
        typeof(InvoicesPageViewModel).GetMethod("GetStatusDisplay", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static string Display(Invoice invoice) => (string)GetStatusDisplay.Invoke(null, [invoice])!;

    private static Invoice Invoice(InvoiceStatus status, decimal paid, decimal refunded, int dueInDays) => new()
    {
        Status = status,
        Total = 100m,
        AmountPaid = paid,
        AmountRefunded = refunded,
        Balance = Math.Max(0m, 100m - paid),
        DueDate = DateTime.Today.AddDays(dueInDays)
    };

    [Theory]
    [InlineData(InvoiceStatus.Refunded, 100, "Refunded")]
    [InlineData(InvoiceStatus.PartiallyRefunded, 30, "Partially Refunded")]
    public void RefundedAfterBeingPaidInFull_PastDue_ShowsTheRefundStatus(InvoiceStatus status, int refunded, string expected)
    {
        Assert.Equal(expected, Display(Invoice(status, 100m, refunded, dueInDays: -10)));
    }

    [Fact]
    public void PaidRefundedInFullThenPaidAgain_ShowsPartiallyRefunded()
    {
        Assert.Equal("Partially Refunded", Display(Invoice(InvoiceStatus.PartiallyRefunded, 200m, 100m, dueInDays: 10)));
    }

    [Fact]
    public void PaidWithAFeeThenRefundedInFull_ShowsRefunded()
    {
        Assert.Equal("Refunded", Display(Invoice(InvoiceStatus.Refunded, 103m, 100m, dueInDays: 10)));
    }

    [Fact]
    public void PartlyPaidThenPartlyRefundedAndPastDue_ShowsOverdue()
    {
        Assert.Equal("Overdue", Display(Invoice(InvoiceStatus.PartiallyRefunded, 50m, 10m, dueInDays: -10)));
    }
}
