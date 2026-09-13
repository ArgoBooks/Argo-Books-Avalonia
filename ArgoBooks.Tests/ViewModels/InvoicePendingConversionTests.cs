using System.Reflection;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// An invoice in another currency with no rate for its issue date (future dated, or saved
/// offline) kept a USD total of 0, or a stale one, and was never queued, so it dropped out of
/// Outstanding Invoices for good. It waits for its own date's rate instead (Rule 3a).
/// </summary>
public class InvoicePendingConversionTests : ModalViewModelTestBase
{
    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    private static IDisposable NoExchangeRates()
    {
        var instance = typeof(ExchangeRateService).GetProperty(nameof(ExchangeRateService.Instance), BindingFlags.Public | BindingFlags.Static)!;
        var prior = instance.GetValue(null);
        instance.SetValue(null, null);
        return new Restore(() => instance.SetValue(null, prior));
    }

    [Fact]
    public async Task SavingADraft_WithNoRateForItsDate_WaitsForConversion()
    {
        using var noRates = NoExchangeRates();
        Company.Customers.Add(new Customer { Id = "CUS-1", Name = "Acme" });
        Company.Invoices.Add(new Invoice
        {
            Id = "INV-1",
            InvoiceNumber = "INV-1",
            CustomerId = "CUS-1",
            Status = InvoiceStatus.Draft,
            OriginalCurrency = "EUR",
            IssueDate = new DateTime(2026, 3, 1),
            DueDate = new DateTime(2026, 4, 1),
            Total = 100m,
            TotalUSD = 90m,
            LineItems = { new LineItem { Description = "Widget", Quantity = 1, UnitPrice = 100m } }
        });

        var vm = new InvoiceModalsViewModel();
        vm.ContinueDraftInvoice(new InvoiceDisplayItem { Id = "INV-1" });
        await vm.SaveAsDraftCommand.ExecuteAsync(null);

        var invoice = Assert.Single(Company.Invoices);
        Assert.Equal("EUR", invoice.OriginalCurrency);
        Assert.True(invoice.IsPendingConversion);
        Assert.Equal(0m, invoice.EffectiveTotalUSD);
        var queued = Assert.Single(Company.PendingConversions);
        Assert.Equal((invoice.Id, "Invoice", "EUR", 100m), (queued.TransactionId, queued.TransactionType, queued.OriginalCurrency, queued.Total));
    }
}
