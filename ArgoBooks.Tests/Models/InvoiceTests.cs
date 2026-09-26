using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;
using Xunit;

namespace ArgoBooks.Tests.Models;

/// <summary>
/// Tests for the Invoice model properties.
/// </summary>
public class InvoiceTests
{
    #region IsOverdue Tests

    [Theory]
    [InlineData(InvoiceStatus.Pending)]
    [InlineData(InvoiceStatus.Sent)]
    [InlineData(InvoiceStatus.Viewed)]
    [InlineData(InvoiceStatus.Partial)]
    [InlineData(InvoiceStatus.Overdue)]
    public void IsOverdue_UnpaidStatuses_WhenPastDue_ReturnsTrue(InvoiceStatus status)
    {
        var invoice = new Invoice
        {
            Status = status,
            Total = 100m,
            Balance = 100m,
            DueDate = DateTime.Today.AddDays(-10)
        };

        Assert.True(invoice.IsOverdue);
    }

    /// <summary>
    /// A draft was never sent, so however late its due date nobody owes it yet.
    /// </summary>
    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Cancelled)]
    public void IsOverdue_DraftOrClosedStatuses_WhenPastDue_ReturnsFalse(InvoiceStatus status)
    {
        var invoice = new Invoice
        {
            Status = status,
            Total = 100m,
            Balance = 100m,
            DueDate = DateTime.Today.AddDays(-10)
        };

        Assert.False(invoice.IsOverdue);
    }

    [Fact]
    public void IsOverdue_NothingOwed_WhenPastDue_ReturnsFalse()
    {
        var invoice = new Invoice
        {
            Status = InvoiceStatus.Sent,
            Total = 0m,
            Balance = 0m,
            DueDate = DateTime.Today.AddDays(-10)
        };

        Assert.False(invoice.IsOverdue);
    }

    /// <summary>
    /// A spreadsheet import can save the Overdue status; it counts while something is owed.
    /// </summary>
    [Fact]
    public void IsOverdue_StoredOverdueStatus_BeforeDueDate_ReturnsTrue()
    {
        var invoice = new Invoice
        {
            Status = InvoiceStatus.Overdue,
            Total = 100m,
            Balance = 100m,
            DueDate = DateTime.Today.AddDays(10)
        };

        Assert.True(invoice.IsOverdue);
    }

    /// <summary>
    /// A refunded invoice was paid in full, so nothing is owed on it however late it is.
    /// </summary>
    [Theory]
    [InlineData(InvoiceStatus.Refunded, 100)]
    [InlineData(InvoiceStatus.PartiallyRefunded, 30)]
    public void IsOverdue_RefundedAfterBeingPaidInFull_WhenPastDue_ReturnsFalse(InvoiceStatus status, int refunded)
    {
        var invoice = new Invoice
        {
            Status = status,
            Total = 100m,
            AmountPaid = 100m,
            AmountRefunded = refunded,
            Balance = 0m,
            DueDate = DateTime.Today.AddDays(-10)
        };

        Assert.False(invoice.IsOverdue);
    }

    [Fact]
    public void IsOverdue_PartlyPaidThenPartlyRefunded_WhenPastDue_ReturnsTrue()
    {
        var invoice = new Invoice
        {
            Status = InvoiceStatus.PartiallyRefunded,
            Total = 100m,
            AmountPaid = 50m,
            AmountRefunded = 10m,
            Balance = 50m,
            DueDate = DateTime.Today.AddDays(-10)
        };

        Assert.True(invoice.IsOverdue);
    }

    [Fact]
    public void IsOverdue_DueToday_ReturnsFalse()
    {
        var invoice = new Invoice
        {
            Status = InvoiceStatus.Pending,
            Balance = 100m,
            DueDate = DateTime.Today
        };

        Assert.False(invoice.IsOverdue);
    }

    [Fact]
    public void IsOverdue_FutureDue_ReturnsFalse()
    {
        var invoice = new Invoice
        {
            Status = InvoiceStatus.Pending,
            Balance = 100m,
            DueDate = DateTime.Today.AddDays(30)
        };

        Assert.False(invoice.IsOverdue);
    }

    #endregion

    #region Balance and Payment Tests

    [Fact]
    public void Invoice_BalanceTracking_WorksCorrectly()
    {
        var invoice = new Invoice
        {
            Total = 1000m,
            AmountPaid = 400m,
            Balance = 600m
        };

        Assert.Equal(1000m, invoice.Total);
        Assert.Equal(400m, invoice.AmountPaid);
        Assert.Equal(600m, invoice.Balance);
    }

    [Fact]
    public void Invoice_FullyPaid_ZeroBalance()
    {
        var invoice = new Invoice
        {
            Total = 500m,
            AmountPaid = 500m,
            Balance = 0m
        };

        Assert.Equal(0m, invoice.Balance);
    }

    #endregion
}
