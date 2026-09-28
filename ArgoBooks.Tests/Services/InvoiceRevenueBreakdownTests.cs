using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The revenue made from an invoice stores its discount and fee from InvoiceMath, so a discount larger
/// than the subtotal is worth only the subtotal there too (docs/Calculations.md §4).
/// </summary>
public class InvoiceRevenueBreakdownTests
{
    [Theory]
    [InlineData(150, false, 100)]
    [InlineData(150, true, 100)]
    [InlineData(25, true, 25)]
    public void Discount_IsCappedAtTheSubtotal(decimal amount, bool isPercent, decimal expected)
    {
        var data = new CompanyData();
        var invoice = new Invoice
        {
            Id = "INV-1", InvoiceNumber = "INV-1", OriginalCurrency = "USD", IssueDate = new DateTime(2026, 3, 1),
            Subtotal = 100m, DiscountAmount = amount, DiscountIsPercent = isPercent,
            Total = 100m - expected, TotalUSD = 100m - expected, Status = InvoiceStatus.Sent
        };

        InvoiceModalsViewModel.CreateRevenueFromInvoice(invoice, data);

        Assert.Equal(expected, Assert.Single(data.Revenues).Discount);
    }
}
