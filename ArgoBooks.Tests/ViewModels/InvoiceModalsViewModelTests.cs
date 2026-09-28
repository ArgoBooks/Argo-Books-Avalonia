using System.Net;
using System.Reflection;
using System.Text.Json;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Invoices;
using ArgoBooks.Core.Models.Portal;
using ArgoBooks.Core.Services;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// Drives the real InvoiceModalsViewModel against an in-memory company. Guards the invoice-paper
/// editing flows where a per-invoice tweak must NOT leak into shared, persisted state: editing the
/// logo on the paper must not mutate the shared template, and a recurring schedule must inherit the
/// invoice's actual payment terms rather than a hardcoded default.
/// </summary>
public class InvoiceModalsViewModelTests : ModalViewModelTestBase
{
    [Fact]
    public void SetLogoFromPaper_AppliesTheLogoToEveryTemplate()
    {
        // The invoice logo is a single company-wide choice, so setting it on the paper (while one
        // template is selected) must land on every template, not just the selected one.
        Company.InvoiceTemplates.Add(new InvoiceTemplate { Id = "tmpl-a", Name = "AAA a", ShowLogo = false });
        Company.InvoiceTemplates.Add(new InvoiceTemplate { Id = "tmpl-b", Name = "AAB b", ShowLogo = false });
        var vm = new InvoiceModalsViewModel();
        vm.OpenCreateModal();
        vm.SelectedTemplate = vm.TemplateOptions.First(t => t.Id == "tmpl-a");

        vm.SetLogoFromPaper("LOGO-DATA");

        Assert.All(Company.InvoiceTemplates, t =>
        {
            Assert.Equal("LOGO-DATA", t.LogoBase64);
            Assert.True(t.ShowLogo);
        });
    }

    [Fact]
    public void DeleteLogoFromPaper_RemovesTheLogoFromEveryTemplate()
    {
        Company.InvoiceTemplates.Add(new InvoiceTemplate { Id = "tmpl-a", Name = "AAA a", LogoBase64 = "LOGO", ShowLogo = true });
        Company.InvoiceTemplates.Add(new InvoiceTemplate { Id = "tmpl-b", Name = "AAB b", LogoBase64 = "LOGO", ShowLogo = true });
        var vm = new InvoiceModalsViewModel();
        vm.OpenCreateModal();
        vm.SelectedTemplate = vm.TemplateOptions.First(t => t.Id == "tmpl-a");

        vm.DeleteLogoFromPaper();

        Assert.All(Company.InvoiceTemplates, t =>
        {
            Assert.Null(t.LogoBase64);
            Assert.False(t.ShowLogo);
        });
    }

    [Fact]
    public async Task CreateAndSendInvoice_LineItemWithoutAProduct_ShowsAProductError()
    {
        // Customer deliberately has no email so, before the fix, the send path stops at the email check
        // (never reaching the confirm dialog) with a non-product error; after the fix the product check
        // fires first. Either way this stays headless-safe.
        Company.Customers.Add(new Customer { Id = "CUST-1", Name = "Acme" });
        var vm = new InvoiceModalsViewModel();
        vm.OpenCreateModal();
        vm.SelectedCustomer = vm.CustomerOptions.First(c => c.Id == "CUST-1");
        // A line with amounts (so the total is positive) but no product selected.
        vm.LineItems[0].Quantity = 1;
        vm.LineItems[0].UnitPrice = 100;

        await vm.CreateAndSendInvoiceCommand.ExecuteAsync(null);

        Assert.True(vm.HasSendError);
        Assert.Contains("product", vm.SendErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAsDraft_RecurringInvoice_DerivesPaymentTermsFromTheInvoiceDueDate()
    {
        Company.Customers.Add(new Customer { Id = "CUST-1", Name = "Acme" });
        var vm = new InvoiceModalsViewModel();
        vm.OpenCreateModal();
        vm.SelectedCustomer = vm.CustomerOptions.First(c => c.Id == "CUST-1");
        // Issue Jan 1, due Jan 16 -> a 15-day term.
        vm.ModalIssueDate = new DateTimeOffset(new DateTime(2026, 1, 1), TimeSpan.Zero);
        vm.ModalDueDate = new DateTimeOffset(new DateTime(2026, 1, 16), TimeSpan.Zero);
        vm.IsRecurring = true;
        vm.RecurringFrequency = Frequency.Monthly;
        vm.RecurringStartDate = new DateTimeOffset(new DateTime(2026, 1, 1), TimeSpan.Zero);

        await vm.SaveAsDraftCommand.ExecuteAsync(null);

        var schedule = Assert.Single(Company.RecurringInvoices);
        Assert.Equal("Net 15", schedule.PaymentTerms);
    }

    /// <summary>
    /// The invoice being saved stands in for every occurrence up to its own issue date, and the
    /// schedule picks up with the first one after it. A start after the issue date is itself the
    /// next invoice; one before it must not produce a draft beside the invoice just saved.
    /// </summary>
    [Theory]
    [InlineData(10, 1, 10, 1)]
    [InlineData(8, 1, 10, 1)]
    [InlineData(9, 10, 10, 10)]
    public async Task SaveAsDraft_RecurringInvoice_NextInvoiceIsTheFirstOccurrenceAfterTheSavedOne(
        int startMonth, int startDay, int nextMonth, int nextDay)
    {
        Company.Customers.Add(new Customer { Id = "CUST-1", Name = "Acme" });
        var vm = new InvoiceModalsViewModel();
        vm.OpenCreateModal();
        vm.SelectedCustomer = vm.CustomerOptions.First(c => c.Id == "CUST-1");
        vm.ModalIssueDate = new DateTimeOffset(new DateTime(2026, 9, 10), TimeSpan.Zero);
        vm.ModalDueDate = new DateTimeOffset(new DateTime(2026, 10, 10), TimeSpan.Zero);
        vm.IsRecurring = true;
        vm.RecurringFrequency = Frequency.Monthly;
        vm.RecurringStartDate = new DateTimeOffset(new DateTime(2026, startMonth, startDay), TimeSpan.Zero);

        await vm.SaveAsDraftCommand.ExecuteAsync(null);

        var schedule = Assert.Single(Company.RecurringInvoices);
        Assert.Equal(new DateTime(2026, nextMonth, nextDay), schedule.NextInvoiceDate);
    }

    /// <summary>
    /// Answers the portal's publish calls with the replies a test queues, and keeps the invoice id
    /// each call carried.
    /// </summary>
    private sealed class PortalStub : HttpMessageHandler
    {
        public Queue<Func<HttpResponseMessage>> Replies { get; } = new();
        public List<string> PublishedInvoiceIds { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);
            PublishedInvoiceIds.Add(json.RootElement.GetProperty("invoiceId").GetString()!);
            return Replies.Dequeue()();
        }
    }

    private static HttpResponseMessage Published() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("""{"success":true,"message":"Invoice published"}""")
    };

    private static HttpResponseMessage Rejected() => new(HttpStatusCode.BadRequest)
    {
        Content = new StringContent("""{"success":false,"message":"Missing required fields: customerName","errorCode":"MISSING_FIELDS"}""")
    };

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    /// <summary>Sends through a portal that answers from <paramref name="stub"/>.</summary>
    private static IDisposable UsePortal(PortalStub stub)
    {
        var service = new PaymentPortalService();
        typeof(PaymentPortalService).GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(service, new HttpClient(stub));

        var portal = typeof(App).GetProperty(nameof(App.PaymentPortalService), BindingFlags.Public | BindingFlags.Static)!;
        var priorPortal = portal.GetValue(null);
        portal.SetValue(null, service);

        var hadKey = PortalSettings.IsConfigured;
        var priorKey = PortalSettings.ApiKey;
        PortalSettings.SetActiveApiKey("test-key");

        return new Restore(() =>
        {
            portal.SetValue(null, priorPortal);
            if (hadKey)
                PortalSettings.SetActiveApiKey(priorKey);
            else
                PortalSettings.DeactivateApiKey();
            service.Dispose();
        });
    }

    private InvoiceModalsViewModel NewInvoiceToSend()
    {
        Company.Customers.Add(new Customer { Id = "CUST-1", Name = "Acme", Email = "billing@acme.test" });
        Company.Products.Add(new Product { Id = "PRD-1", Name = "Widget", Type = CategoryType.Revenue, UnitPrice = 100m });
        var vm = new InvoiceModalsViewModel();
        vm.OpenCreateModal();
        vm.SelectedCustomer = vm.CustomerOptions.First(c => c.Id == "CUST-1");
        vm.LineItems[0].SelectedProduct = vm.ProductOptions.First(p => p.Id == "PRD-1");
        vm.LineItems[0].Quantity = 1;
        vm.LineItems[0].UnitPrice = 100m;
        return vm;
    }

    /// <summary>
    /// A send the portal turns down leaves nothing behind, so its number goes back for the next
    /// invoice. Keeping it left a permanent gap in the sequence.
    /// </summary>
    [Fact]
    public async Task CreateAndSendInvoice_PortalTurnsItDown_GivesTheNumberBack()
    {
        var portal = new PortalStub();
        using var _ = UsePortal(portal);
        var vm = NewInvoiceToSend();
        portal.Replies.Enqueue(Rejected);

        await vm.CreateAndSendInvoiceCommand.ExecuteAsync(null);

        Assert.True(vm.HasSendError);
        Assert.Empty(Company.Invoices);
        Assert.Equal(0, Company.IdCounters.Invoice);
    }

    /// <summary>
    /// A send that times out may still have reached the portal, which then published and emailed the
    /// invoice under its id. Sending again under a new number gave the customer two invoices to pay,
    /// and a payment on the first had no invoice to land on. The retry keeps the number, so the portal
    /// updates the invoice it already holds.
    /// </summary>
    [Fact]
    public async Task CreateAndSendInvoice_TimedOut_RetryKeepsTheSameNumber()
    {
        var portal = new PortalStub();
        using var _ = UsePortal(portal);
        var vm = NewInvoiceToSend();
        portal.Replies.Enqueue(() => throw new TaskCanceledException());
        portal.Replies.Enqueue(Published);

        await vm.CreateAndSendInvoiceCommand.ExecuteAsync(null);
        Assert.True(vm.HasSendError);
        await vm.CreateAndSendInvoiceCommand.ExecuteAsync(null);

        var invoice = Assert.Single(Company.Invoices);
        Assert.Equal(2, portal.PublishedInvoiceIds.Count);
        Assert.Equal(portal.PublishedInvoiceIds[0], portal.PublishedInvoiceIds[1]);
        Assert.Equal(portal.PublishedInvoiceIds[0], invoice.Id);
        Assert.Equal(1, Company.IdCounters.Invoice);
    }

    /// <summary>
    /// Sending a continued draft wrote the form into the draft before publishing, so a failed send
    /// left the draft holding edits the user then chose to discard.
    /// </summary>
    [Fact]
    public async Task CreateAndSendInvoice_ContinuedDraftFailsToSend_LeavesTheDraftAsItWas()
    {
        var portal = new PortalStub();
        using var _ = UsePortal(portal);
        Company.Customers.Add(new Customer { Id = "CUST-2", Name = "Beta", Email = "billing@beta.test" });
        var vm = NewInvoiceToSend();
        await vm.SaveAsDraftCommand.ExecuteAsync(null);
        var draft = Assert.Single(Company.Invoices);
        var total = draft.Total;

        vm.ContinueDraftInvoice(new InvoiceDisplayItem { Id = draft.Id });
        vm.SelectedCustomer = vm.CustomerOptions.First(c => c.Id == "CUST-2");
        vm.LineItems[0].UnitPrice = 250m;
        portal.Replies.Enqueue(Rejected);
        await vm.CreateAndSendInvoiceCommand.ExecuteAsync(null);

        Assert.True(vm.HasSendError);
        Assert.Equal("CUST-1", draft.CustomerId);
        Assert.Equal(total, draft.Total);
        Assert.Equal(100m, Assert.Single(draft.LineItems).UnitPrice);
        Assert.Equal(InvoiceStatus.Draft, draft.Status);
    }
}
