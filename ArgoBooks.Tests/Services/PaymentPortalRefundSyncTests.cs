using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Portal;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for refund-record interpretation in PaymentPortalService.ProcessSyncedPayments.
/// Refund rows arrive from /payments-sync.php with isRefund=true and a negative amount;
/// the service must (a) insert a local Payment with IsRefund=true and negative Amount,
/// (b) link RefundedFromPaymentId to the source local Payment via PortalPaymentId,
/// (c) bump invoice.AmountRefunded, (d) recompute invoice status to Refunded /
/// PartiallyRefunded, and (e) emit a "Refund Issued" InvoiceHistoryEntry.
/// </summary>
public class PaymentPortalRefundSyncTests
{
    private static (CompanyData company, Invoice invoice, Customer customer) Seed(decimal invoiceTotal = 100m)
    {
        var customer = new Customer { Id = "CUST-001", Name = "Acme Corp" };
        var invoice = new Invoice
        {
            Id = "INV-001",
            InvoiceNumber = "INV-2026-001",
            CustomerId = customer.Id,
            Total = invoiceTotal,
            OriginalCurrency = "USD",
            Status = InvoiceStatus.Sent,
        };
        var company = new CompanyData();
        company.Customers.Add(customer);
        company.Invoices.Add(invoice);
        return (company, invoice, customer);
    }

    private static PortalPaymentRecord MakeOriginalPayment(string invoiceId, decimal amount, int serverId = 1, string providerPaymentId = "pi_abc")
        => new()
        {
            Id = serverId,
            InvoiceId = invoiceId,
            CustomerName = "Acme Corp",
            Amount = amount,
            ProcessingFee = 0m,
            Currency = "USD",
            PaymentMethod = "stripe",
            ProviderPaymentId = providerPaymentId,
            ProviderTransactionId = "ch_abc",
            ReferenceNumber = "REF-1",
            CreatedAt = DateTime.UtcNow,
            IsRefund = false,
        };

    private static PortalPaymentRecord MakeRefund(string invoiceId, decimal amount, int serverId = 99, string? refundedProviderPaymentId = "pi_abc", int? refundRequestId = null, string? reason = null)
        => new()
        {
            Id = serverId,
            InvoiceId = invoiceId,
            CustomerName = "Acme Corp",
            Amount = -Math.Abs(amount), // refunds are negative on the wire
            ProcessingFee = 0m,
            Currency = "USD",
            PaymentMethod = "stripe",
            ProviderPaymentId = "re_xyz",
            ProviderTransactionId = "ch_abc",
            ReferenceNumber = "RFD-1",
            CreatedAt = DateTime.UtcNow,
            IsRefund = true,
            RefundedProviderPaymentId = refundedProviderPaymentId,
            RefundRequestId = refundRequestId,
            RefundReason = reason,
        };

    [Fact]
    public void Sync_FullRefund_AfterFullPayment_TransitionsInvoiceToRefunded()
    {
        var (company, invoice, _) = Seed(invoiceTotal: 100m);

        // First sync: original $100 payment
        PaymentPortalService.ProcessSyncedPayments(
            new List<PortalPaymentRecord> { MakeOriginalPayment(invoice.Id, 100m, serverId: 1) },
            company);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(100m, invoice.AmountPaid);
        Assert.Equal(0m, invoice.AmountRefunded);

        // Second sync: full refund
        PaymentPortalService.ProcessSyncedPayments(
            new List<PortalPaymentRecord> { MakeRefund(invoice.Id, 100m, serverId: 2) },
            company);

        Assert.Equal(InvoiceStatus.Refunded, invoice.Status);
        Assert.Equal(100m, invoice.AmountPaid);     // gross unchanged
        Assert.Equal(100m, invoice.AmountRefunded);
        Assert.Equal(0m, invoice.NetPaid);

        var refundRow = company.Payments.Single(p => p.IsRefund);
        Assert.Equal(-100m, refundRow.Amount);
        Assert.Equal(PaymentSource.Online, refundRow.Source);
    }

    [Fact]
    public void Sync_PartialRefund_TransitionsInvoiceToPartiallyRefunded()
    {
        var (company, invoice, _) = Seed(invoiceTotal: 100m);

        PaymentPortalService.ProcessSyncedPayments(
            new List<PortalPaymentRecord> { MakeOriginalPayment(invoice.Id, 100m, serverId: 1) },
            company);

        PaymentPortalService.ProcessSyncedPayments(
            new List<PortalPaymentRecord> { MakeRefund(invoice.Id, 25m, serverId: 2) },
            company);

        Assert.Equal(InvoiceStatus.PartiallyRefunded, invoice.Status);
        Assert.Equal(25m, invoice.AmountRefunded);
        Assert.Equal(75m, invoice.NetPaid);
    }

    [Fact]
    public void Sync_RefundLinks_RefundedFromPaymentId_ToOriginal()
    {
        var (company, invoice, _) = Seed(100m);
        PaymentPortalService.ProcessSyncedPayments(
            new List<PortalPaymentRecord> { MakeOriginalPayment(invoice.Id, 100m, serverId: 7, providerPaymentId: "pi_link_test") },
            company);
        var original = company.Payments.Single();
        Assert.Equal("pi_link_test", original.ProviderPaymentId); // populated by sync

        PaymentPortalService.ProcessSyncedPayments(
            new List<PortalPaymentRecord> { MakeRefund(invoice.Id, 100m, serverId: 8, refundedProviderPaymentId: "pi_link_test") },
            company);

        var refundRow = company.Payments.Single(p => p.IsRefund);
        Assert.True(refundRow.IsRefund);
        Assert.Equal("8", refundRow.PortalPaymentId);                    // server-row id, not provider id
        Assert.Equal(original.Id, refundRow.RefundedFromPaymentId);      // linked back via providerPaymentId match
    }

    [Fact]
    public void Sync_RefundIsIdempotent_OnDuplicateRecord()
    {
        var (company, invoice, _) = Seed(100m);
        PaymentPortalService.ProcessSyncedPayments(
            new List<PortalPaymentRecord> { MakeOriginalPayment(invoice.Id, 100m, serverId: 1) },
            company);

        var refund = MakeRefund(invoice.Id, 100m, serverId: 2);
        PaymentPortalService.ProcessSyncedPayments(new List<PortalPaymentRecord> { refund }, company);
        PaymentPortalService.ProcessSyncedPayments(new List<PortalPaymentRecord> { refund }, company);
        PaymentPortalService.ProcessSyncedPayments(new List<PortalPaymentRecord> { refund }, company);

        Assert.Single(company.Payments, p => p.IsRefund);
        Assert.Equal(InvoiceStatus.Refunded, invoice.Status);
        Assert.Equal(100m, invoice.AmountRefunded);
    }

    [Fact]
    public void Sync_RefundEmits_RefundIssuedHistoryEntry()
    {
        var (company, invoice, _) = Seed(100m);
        PaymentPortalService.ProcessSyncedPayments(
            new List<PortalPaymentRecord> { MakeOriginalPayment(invoice.Id, 100m, serverId: 1) },
            company);

        PaymentPortalService.ProcessSyncedPayments(
            new List<PortalPaymentRecord> { MakeRefund(invoice.Id, 60m, serverId: 2, reason: "Wrong color") },
            company);

        var refundEntry = invoice.History.SingleOrDefault(h => h.Action == "Refund Issued");
        Assert.NotNull(refundEntry);
        Assert.Contains("60", refundEntry.Details);
        Assert.Contains("Wrong color", refundEntry.Details);
    }

    [Fact]
    public void Sync_OverRefund_ClampsToFullyRefundedStatus()
    {
        var (company, invoice, _) = Seed(100m);
        PaymentPortalService.ProcessSyncedPayments(
            new List<PortalPaymentRecord> { MakeOriginalPayment(invoice.Id, 100m, serverId: 1) },
            company);

        // Two refunds totaling more than the original payment (defensive case;
        // the server pre-flights this but the desktop must not crash).
        PaymentPortalService.ProcessSyncedPayments(new List<PortalPaymentRecord> {
            MakeRefund(invoice.Id, 60m, serverId: 2),
            MakeRefund(invoice.Id, 60m, serverId: 3),
        }, company);

        Assert.Equal(InvoiceStatus.Refunded, invoice.Status);
        Assert.Equal(120m, invoice.AmountRefunded);
        Assert.Equal(-20m, invoice.NetPaid);
    }

    [Fact]
    public void Sync_BackfillsProviderPaymentId_OnAlreadySyncedRow()
    {
        // Existing customers had Payment rows synced before this release, with
        // ProviderPaymentId still null because the field didn't exist. The next
        // sync touches these rows and backfills the new field (without
        // creating duplicates) so the Refund button can appear retroactively.
        var (company, invoice, _) = Seed(invoiceTotal: 100m);

        // Simulate a pre-update Payment: PortalPaymentId is set (it was synced)
        // but ProviderPaymentId is null (the field was new).
        var stalePayment = new Payment
        {
            Id = "PAY-old",
            InvoiceId = invoice.Id,
            CustomerId = "CUST-001",
            Amount = 100m,
            PaymentMethod = PaymentMethod.Stripe,
            Source = PaymentSource.Online,
            PortalPaymentId = "42",
            ProviderPaymentId = null, // pre-update state
            OriginalCurrency = "USD",
        };
        company.Payments.Add(stalePayment);

        // Sync the same row again with a populated ProviderPaymentId
        PaymentPortalService.ProcessSyncedPayments(
            new List<PortalPaymentRecord> { MakeOriginalPayment(invoice.Id, 100m, serverId: 42, providerPaymentId: "pi_backfilled") },
            company);

        // Backfill happened, no duplicate created
        Assert.Single(company.Payments);
        Assert.Equal("pi_backfilled", company.Payments[0].ProviderPaymentId);
    }

    [Fact]
    public void Sync_RegularPayment_AfterRefund_DoesNotResetRefundedStatus()
    {
        // Edge case: a customer pays, gets refunded, then pays again. The new
        // payment should reflect, but the refund history must remain intact.
        var (company, invoice, _) = Seed(100m);
        PaymentPortalService.ProcessSyncedPayments(
            new List<PortalPaymentRecord> { MakeOriginalPayment(invoice.Id, 100m, serverId: 1) },
            company);
        PaymentPortalService.ProcessSyncedPayments(
            new List<PortalPaymentRecord> { MakeRefund(invoice.Id, 100m, serverId: 2) },
            company);
        Assert.Equal(InvoiceStatus.Refunded, invoice.Status);

        PaymentPortalService.ProcessSyncedPayments(
            new List<PortalPaymentRecord> { MakeOriginalPayment(invoice.Id, 100m, serverId: 3, providerPaymentId: "pi_second") },
            company);

        // The new positive payment makes the invoice "Paid" again on top of the
        // refund. AmountPaid should now be 200; AmountRefunded stays 100.
        Assert.Equal(200m, invoice.AmountPaid);
        Assert.Equal(100m, invoice.AmountRefunded);
        Assert.Equal(InvoiceStatus.PartiallyRefunded, invoice.Status);
    }

    private static Revenue AddInvoiceRevenue(CompanyData company, Invoice invoice)
    {
        var revenue = new Revenue
        {
            Id = "REV-001",
            InvoiceId = invoice.Id,
            OriginalCurrency = "USD",
            Total = invoice.Total,
            TotalUSD = invoice.Total,
            PaymentStatus = RevenuePaymentStatus.Unpaid
        };
        company.Revenues.Add(revenue);
        return revenue;
    }

    /// <summary>
    /// A refund is subtracted from revenue on the day it is issued (Calculations.md §8), so the
    /// revenue it refunds has to stay counted. Marking that revenue uncollected as well took the
    /// money off twice: a full refund showed as -$100, a $25 one as -$25 instead of $75.
    /// </summary>
    [Theory]
    [InlineData(100)]
    [InlineData(25)]
    public void Sync_RefundAfterFullPayment_KeepsTheRevenueCounted(int refunded)
    {
        var (company, invoice, _) = Seed(100m);
        var revenue = AddInvoiceRevenue(company, invoice);
        PaymentPortalService.ProcessSyncedPayments(
            new List<PortalPaymentRecord> { MakeOriginalPayment(invoice.Id, 100m, serverId: 1) },
            company);
        Assert.Equal(RevenuePaymentStatus.Paid, revenue.PaymentStatus);

        PaymentPortalService.ProcessSyncedPayments(
            new List<PortalPaymentRecord> { MakeRefund(invoice.Id, refunded, serverId: 2) },
            company);

        Assert.Equal(RevenuePaymentStatus.Paid, revenue.PaymentStatus);
    }

    [Fact]
    public void Sync_RefundOfAPartPayment_LeavesTheRevenueUncollected()
    {
        var (company, invoice, _) = Seed(100m);
        var revenue = AddInvoiceRevenue(company, invoice);
        PaymentPortalService.ProcessSyncedPayments(
            new List<PortalPaymentRecord> { MakeOriginalPayment(invoice.Id, 40m, serverId: 1) },
            company);

        PaymentPortalService.ProcessSyncedPayments(
            new List<PortalPaymentRecord> { MakeRefund(invoice.Id, 10m, serverId: 2) },
            company);

        Assert.Equal(RevenuePaymentStatus.Unpaid, revenue.PaymentStatus);
    }
}
