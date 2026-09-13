using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Portal;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// A security deposit is held for the customer, not earned (docs/Calculations.md §4).
/// </summary>
public class SecurityDepositTests
{
    private static readonly DateTime Start = new(2026, 1, 1);
    private static readonly DateTime End = new(2026, 12, 31);

    // $500 of rent plus a $200 deposit, no tax.
    private static Invoice RentalInvoice() => new()
    {
        Id = "INV-1", InvoiceNumber = "INV-1", CustomerId = "CUST-1", OriginalCurrency = "USD",
        IssueDate = new DateTime(2026, 3, 1), Subtotal = 500m, SecurityDeposit = 200m, Total = 700m, TotalUSD = 700m,
        Status = InvoiceStatus.Sent
    };

    private static Payment Refund(decimal amount, decimal depositAmount) => new()
    {
        Id = $"PAY-{Guid.NewGuid():N}", InvoiceId = "INV-1", OriginalCurrency = "USD", IsRefund = true,
        Amount = -amount, AmountUSD = -amount, DepositAmount = depositAmount, Date = new DateTime(2026, 4, 1)
    };

    [Fact]
    public void RevenueFromASentInvoice_LeavesOutTheDeposit()
    {
        var data = new CompanyData();
        var invoice = RentalInvoice();
        invoice.OriginalCurrency = "EUR";
        invoice.TotalUSD = 770m;

        InvoiceModalsViewModel.CreateRevenueFromInvoice(invoice, data);

        var revenue = Assert.Single(data.Revenues);
        Assert.Equal((500m, 550m, 0m), (revenue.Total, revenue.TotalUSD, revenue.Fee));
    }

    [Fact]
    public void ARefundThatGaveBackTheDeposit_DoesNotComeOffRevenue()
    {
        var refunds = new[] { Refund(200m, depositAmount: 200m) };

        Assert.Equal(0m, RefundAggregator.GetRefundedInDateRangeUSD(refunds, Start, End));
    }

    [Fact]
    public void GivingBackTheDeposit_LeavesTheRentAsProfit()
    {
        var data = new CompanyData();
        data.Invoices.Add(RentalInvoice());
        data.Revenues.Add(new Revenue
        {
            Id = "REV-1", InvoiceId = "INV-1", Date = new DateTime(2026, 3, 1), OriginalCurrency = "USD",
            Total = 500m, TotalUSD = 500m, PaymentStatus = RevenuePaymentStatus.Paid
        });
        data.Payments.Add(Refund(200m, depositAmount: 200m));

        Assert.Equal(500m, ProfitCalculator.CalculateNetProfitUSD(data, Start, End));
    }

    // $500 rent + $65 tax + $200 deposit = $765. Refunding the rent and its tax takes $500 off profit.
    [Fact]
    public void RefundingTheRentOfADepositInvoice_TakesOffTheRentBeforeTax()
    {
        var invoice = RentalInvoice();
        invoice.TaxAmount = 65m;
        invoice.Total = 765m;
        var byId = new Dictionary<string, Invoice> { [invoice.Id] = invoice };

        var preTax = RefundAggregator.GetRefundedPreTaxInDateRangeUSD([Refund(565m, depositAmount: 0m)], byId, Start, End);

        Assert.Equal(500m, Math.Round(preTax, 2));
    }

    private static PortalPaymentRecord PortalRecord(int id, decimal amount, bool isRefund, decimal? depositAmount = null) => new()
    {
        Id = id,
        InvoiceId = "INV-1",
        CustomerName = "Bob",
        Amount = isRefund ? -amount : amount,
        Currency = "USD",
        PaymentMethod = "stripe",
        ProviderPaymentId = isRefund ? $"re_{id}" : "pi_1",
        CreatedAt = DateTime.UtcNow,
        IsRefund = isRefund,
        RefundedProviderPaymentId = isRefund ? "pi_1" : null,
        DepositAmount = depositAmount
    };

    private static (CompanyData Data, Invoice Invoice) PaidRentalInvoice()
    {
        var data = new CompanyData();
        data.Customers.Add(new Customer { Id = "CUST-1", Name = "Bob" });
        var invoice = RentalInvoice();
        data.Invoices.Add(invoice);
        PaymentPortalService.ProcessSyncedPayments([PortalRecord(1, 700m, isRefund: false)], data);
        return (data, invoice);
    }

    [Fact]
    public void ARefundMadeInArgoBooks_RecordsTheDepositPartItGaveBack()
    {
        var (data, _) = PaidRentalInvoice();

        PaymentPortalService.ProcessSyncedPayments([PortalRecord(2, 300m, isRefund: true, depositAmount: 200m)], data);

        Assert.Equal(200m, data.Payments.Single(p => p.IsRefund).DepositAmount);
    }

    // A refund made in the provider's dashboard says nothing about the deposit. It is taken as
    // giving the deposit back first, as long as any of it is still held.
    [Fact]
    public void ARefundMadeInTheProviderDashboard_ComesOutOfTheDepositFirst()
    {
        var (data, _) = PaidRentalInvoice();

        PaymentPortalService.ProcessSyncedPayments([PortalRecord(2, 250m, isRefund: true)], data);
        PaymentPortalService.ProcessSyncedPayments([PortalRecord(3, 100m, isRefund: true)], data);

        Assert.Equal([200m, 0m], data.Payments.Where(p => p.IsRefund).Select(p => p.DepositAmount));
    }

    // A deposit kept when the rental came back is revenue, so handing it back later comes off revenue.
    [Fact]
    public void RefundingAKeptDeposit_ComesOffRevenue()
    {
        var (data, invoice) = PaidRentalInvoice();
        data.Revenues.Add(new Revenue
        {
            Id = "REV-2", InvoiceId = invoice.Id, IsKeptDeposit = true, Total = 200m, TotalUSD = 200m, OriginalCurrency = "USD"
        });

        PaymentPortalService.ProcessSyncedPayments([PortalRecord(2, 200m, isRefund: true, depositAmount: 200m)], data);

        Assert.Equal(0m, data.Payments.Single(p => p.IsRefund).DepositAmount);
    }
}
