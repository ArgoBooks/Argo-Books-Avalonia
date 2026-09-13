using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Invoices paid by hand before the fix left their revenue unpaid, and portal refunds marked the
/// revenue of a paid invoice unpaid. The open-time heal counts that revenue again, and only ever
/// upgrades: it never takes a revenue out of the totals.
/// </summary>
public class InvoiceRevenueHealTests
{
    private static (CompanyData Data, Invoice Invoice, Revenue Revenue) PaidInvoice(RevenuePaymentStatus revenueStatus)
    {
        var data = new CompanyData();
        var invoice = new Invoice
        {
            Id = "INV-1", OriginalCurrency = "USD", Total = 100m, TotalUSD = 100m, Balance = 100m,
            Status = InvoiceStatus.Sent
        };
        data.Invoices.Add(invoice);
        data.Payments.Add(new Payment
        {
            Id = "PAY-1", InvoiceId = "INV-1", OriginalCurrency = "USD", Amount = 100m, AmountUSD = 100m
        });
        var revenue = new Revenue
        {
            Id = "REV-1", InvoiceId = "INV-1", OriginalCurrency = "USD", Total = 100m, TotalUSD = 100m,
            PaymentStatus = revenueStatus
        };
        data.Revenues.Add(revenue);
        return (data, invoice, revenue);
    }

    [Fact]
    public void Heal_CountsTheRevenueOfAnInvoicePaidInFull()
    {
        var (data, _, revenue) = PaidInvoice(RevenuePaymentStatus.Unpaid);

        CompanyManager.HealInvoiceTotalsIfNeeded(data);

        Assert.Equal(RevenuePaymentStatus.Paid, revenue.PaymentStatus);
        Assert.True(data.ChangesMade);
    }

    [Fact]
    public void Heal_CountsTheRevenueOfAPaidInvoiceRefundedSince()
    {
        var (data, _, revenue) = PaidInvoice(RevenuePaymentStatus.Unpaid);
        data.Payments.Add(new Payment
        {
            Id = "PAY-2", InvoiceId = "INV-1", OriginalCurrency = "USD", Amount = -100m, AmountUSD = -100m, IsRefund = true
        });

        CompanyManager.HealInvoiceTotalsIfNeeded(data);

        Assert.Equal(RevenuePaymentStatus.Paid, revenue.PaymentStatus);
    }

    [Fact]
    public void Heal_LeavesTheRevenueOfAPartPaidInvoiceAsItIs()
    {
        var (data, _, revenue) = PaidInvoice(RevenuePaymentStatus.Paid);
        data.Payments.Single().Amount = 40m;
        data.Payments.Single().AmountUSD = 40m;

        CompanyManager.HealInvoiceTotalsIfNeeded(data);

        // Marked paid by hand, and the heal never takes revenue out of the totals.
        Assert.Equal(RevenuePaymentStatus.Paid, revenue.PaymentStatus);
    }

    // EUR 500 of rent and a EUR 200 deposit, at 1.10 USD.
    private static (CompanyData Data, Revenue Revenue) DepositInvoice()
    {
        var data = new CompanyData();
        data.Invoices.Add(new Invoice
        {
            Id = "INV-1", OriginalCurrency = "EUR", Subtotal = 500m, SecurityDeposit = 200m, Total = 700m, TotalUSD = 770m,
            Status = InvoiceStatus.Sent
        });
        var revenue = new Revenue
        {
            Id = "REV-1", InvoiceId = "INV-1", OriginalCurrency = "EUR", Total = 700m, TotalUSD = 770m,
            Fee = 200m, FeeUSD = 220m, PaymentStatus = RevenuePaymentStatus.Paid
        };
        data.Revenues.Add(revenue);
        return (data, revenue);
    }

    // A deposit is held, not earned (Calculations.md §4), and revenue created from an invoice counted it.
    [Fact]
    public void Heal_TakesTheDepositOutOfTheInvoicesRevenue()
    {
        var (data, revenue) = DepositInvoice();

        CompanyManager.HealInvoiceTotalsIfNeeded(data);

        Assert.Equal((500m, 550m, 0m, 0m), (revenue.Total, revenue.TotalUSD, revenue.Fee, revenue.FeeUSD));
        Assert.True(data.ChangesMade);
    }

    [Fact]
    public void Heal_TakesPastRefundsOutOfTheDepositFirst()
    {
        var (data, _) = DepositInvoice();
        data.Payments.Add(new Payment
        {
            Id = "PAY-1", InvoiceId = "INV-1", OriginalCurrency = "EUR", Amount = -250m, AmountUSD = -275m,
            IsRefund = true, Date = new DateTime(2026, 4, 1)
        });
        data.Payments.Add(new Payment
        {
            Id = "PAY-2", InvoiceId = "INV-1", OriginalCurrency = "EUR", Amount = -100m, AmountUSD = -110m,
            IsRefund = true, Date = new DateTime(2026, 5, 1)
        });

        CompanyManager.HealInvoiceTotalsIfNeeded(data);

        Assert.Equal([200m, 0m], data.Payments.Select(p => p.DepositAmount));
    }

    // Revenue the user entered before making the invoice from it never had the deposit in it.
    [Fact]
    public void Heal_LeavesRevenueThatNeverHadTheDepositAlone()
    {
        var (data, revenue) = DepositInvoice();
        revenue.Total = 500m;
        revenue.TotalUSD = 550m;
        revenue.Fee = 0m;
        revenue.FeeUSD = 0m;

        CompanyManager.HealInvoiceTotalsIfNeeded(data);

        Assert.Equal((500m, 550m), (revenue.Total, revenue.TotalUSD));
    }
}
