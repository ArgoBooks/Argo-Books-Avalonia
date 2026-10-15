using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for <see cref="CompanyManager.MigrateRevenueLinkedPayments"/>, the one-time step that
/// removes payments recorded against a revenue, which files saved by v2.0.11 or earlier can hold.
/// </summary>
public class RevenuePaymentsMigrationTests
{
    /// <summary>
    /// A payment against a revenue is the only record that the money arrived. When it is removed,
    /// the revenue must count as collected, or the cash disappears from the books. Every such
    /// payment goes, including a second one on the same revenue, and the company is flagged for a
    /// full save so the removal reaches the file.
    /// </summary>
    [Fact]
    public void MigrateRevenueLinkedPayments_RevenueOnlyPayments_RemovesThemAndMarksRevenuePaid()
    {
        var data = new CompanyData
        {
            Revenues = { new Revenue { Id = "REV-1", Total = 100m, PaymentStatus = RevenuePaymentStatus.Pending } },
            Payments =
            {
                new Payment { Id = "PAY-1", RevenueId = "REV-1", Amount = 60m, OriginalCurrency = "USD" },
                new Payment { Id = "PAY-2", RevenueId = "REV-1", Amount = 40m, OriginalCurrency = "USD" }
            }
        };

        CompanyManager.MigrateRevenueLinkedPayments(data);

        Assert.Empty(data.Payments);
        Assert.Equal(RevenuePaymentStatus.Paid, data.Revenues[0].PaymentStatus);
        Assert.True(data.ChangesMade);
        Assert.Equal(CompanyManager.RevenuePaymentsMigrationVersion, data.Settings.RevenuePaymentsMigratedVersion);
    }

    /// <summary>
    /// Payments against an invoice are what the invoice's amount paid and balance are calculated
    /// from, so the step must leave them exactly as they were, including one that also names a
    /// revenue. With nothing removed, opening the company must not leave it with unsaved changes.
    /// </summary>
    [Fact]
    public void MigrateRevenueLinkedPayments_InvoicePayments_AreKeptUntouched()
    {
        var invoiceOnly = new Payment { Id = "PAY-1", InvoiceId = "INV-1", Amount = 40m, OriginalCurrency = "USD" };
        var invoiceAndRevenue = new Payment
        {
            Id = "PAY-2", InvoiceId = "INV-2", RevenueId = "REV-2", Amount = 25m, OriginalCurrency = "USD"
        };
        var data = new CompanyData
        {
            Revenues = { new Revenue { Id = "REV-2", Total = 50m, PaymentStatus = RevenuePaymentStatus.Pending } },
            Payments = { invoiceOnly, invoiceAndRevenue }
        };

        CompanyManager.MigrateRevenueLinkedPayments(data);

        Assert.Equal(2, data.Payments.Count);
        Assert.Same(invoiceOnly, data.Payments[0]);
        Assert.Same(invoiceAndRevenue, data.Payments[1]);
        Assert.Equal(40m, invoiceOnly.Amount);
        Assert.Equal("INV-1", invoiceOnly.InvoiceId);
        Assert.Equal(25m, invoiceAndRevenue.Amount);
        Assert.Equal("INV-2", invoiceAndRevenue.InvoiceId);
        // A half-paid invoice's revenue is still waiting for the rest of its money.
        Assert.Equal(RevenuePaymentStatus.Pending, data.Revenues[0].PaymentStatus);
        Assert.False(data.ChangesMade);
        Assert.Equal(CompanyManager.RevenuePaymentsMigrationVersion, data.Settings.RevenuePaymentsMigratedVersion);
    }

    /// <summary>
    /// Once the marker is stamped, the step must not run again on that file, so a company that has
    /// already been moved to the newer format keeps its payments and revenue statuses as they are.
    /// </summary>
    [Fact]
    public void MigrateRevenueLinkedPayments_AlreadyMigrated_LeavesEverythingAlone()
    {
        var data = new CompanyData
        {
            Settings = new CompanySettings
            {
                RevenuePaymentsMigratedVersion = CompanyManager.RevenuePaymentsMigrationVersion
            },
            Revenues = { new Revenue { Id = "REV-1", Total = 100m, PaymentStatus = RevenuePaymentStatus.Pending } },
            Payments = { new Payment { Id = "PAY-1", RevenueId = "REV-1", Amount = 60m, OriginalCurrency = "USD" } }
        };

        CompanyManager.MigrateRevenueLinkedPayments(data);

        Assert.Equal("PAY-1", Assert.Single(data.Payments).Id);
        Assert.Equal(RevenuePaymentStatus.Pending, data.Revenues[0].PaymentStatus);
        Assert.False(data.ChangesMade);
    }
}
