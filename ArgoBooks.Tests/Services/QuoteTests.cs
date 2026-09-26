using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Portal;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.InvoiceTemplates;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The things about quotes that would cost real money if they broke: the records surviving a save,
/// old files still opening, a customer's answer landing on the right quote, a conversion producing
/// the same figures the customer agreed to, and the shared templates still printing invoices the
/// way they always did.
/// </summary>
public class QuoteTests
{
    private static FileService CreateService() =>
        new(new CompressionService(), new FooterService(), new EncryptionService());

    private static Quote SampleQuote(string id = "QUO-2026-00001") => new()
    {
        Id = id,
        QuoteNumber = $"#{id}",
        CustomerId = "CUS-001",
        IssueDate = new DateTime(2026, 9, 1),
        ValidUntil = new DateTime(2026, 10, 1),
        LineItems =
        [
            new() { Description = "Design work", Quantity = 10m, UnitPrice = 95m, ProductId = "PRD-007" },
            new() { Description = "Hosting", Quantity = 1m, UnitPrice = 240m, Discount = 40m }
        ],
        Subtotal = 1150m,
        TaxRate = 13m,
        TaxAmount = 149.50m,
        Total = 1299.50m,
        Notes = "Prices hold for 30 days.",
        TemplateId = "TPL-001",
        OriginalCurrency = "CAD",
        Status = QuoteStatus.Sent,
        SentAt = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc)
    };

    #region Persistence

    [Fact]
    public async Task Quotes_SurviveSaveAndReopen()
    {
        var service = CreateService();
        var filePath = Path.Combine(Path.GetTempPath(), $"argo-quotes-{Guid.NewGuid():N}.argo");
        string? temp = null;

        try
        {
            await service.CreateCompanyAsync(filePath, "Quote Co");
            temp = await service.OpenCompanyAsync(filePath);
            var data = await service.LoadCompanyDataAsync(temp);

            data.Quotes.Add(SampleQuote());
            data.IdCounters.Quote = 1;

            // OpenCompanyAsync extracts the data files straight into the temp root.
            await service.SaveCompanyDataAsync(temp, data);
            await service.SaveCompanyAsync(filePath, temp, null);

            CleanupTemp(temp);
            temp = await service.OpenCompanyAsync(filePath);
            var reopened = await service.LoadCompanyDataAsync(temp);

            var quote = Assert.Single(reopened.Quotes);
            Assert.Equal("QUO-2026-00001", quote.Id);
            Assert.Equal(QuoteStatus.Sent, quote.Status);
            Assert.Equal(1299.50m, quote.Total);
            Assert.Equal("CAD", quote.OriginalCurrency);
            Assert.Equal(2, quote.LineItems.Count);
            Assert.Equal(new DateTime(2026, 10, 1), quote.ValidUntil);
            Assert.Equal(1, reopened.IdCounters.Quote);
        }
        finally
        {
            CleanupTemp(temp);
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public async Task CompanyFileWithoutQuotesJson_StillOpens()
    {
        var service = CreateService();
        var filePath = Path.Combine(Path.GetTempPath(), $"argo-noquotes-{Guid.NewGuid():N}.argo");
        string? temp = null;

        try
        {
            await service.CreateCompanyAsync(filePath, "Legacy Co");
            temp = await service.OpenCompanyAsync(filePath);

            // Every file written before quotes shipped looks like this.
            var quotesFile = Path.Combine(temp, "quotes.json");
            if (File.Exists(quotesFile)) File.Delete(quotesFile);
            await service.SaveCompanyAsync(filePath, temp, null);

            CleanupTemp(temp);
            temp = await service.OpenCompanyAsync(filePath);
            var data = await service.LoadCompanyDataAsync(temp);

            Assert.Empty(data.Quotes);
            Assert.Equal("Legacy Co", data.Settings.Company.Name);
        }
        finally
        {
            CleanupTemp(temp);
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    private static void CleanupTemp(string? temp)
    {
        if (temp != null && Directory.Exists(temp))
            Directory.Delete(temp, recursive: true);
    }

    #endregion

    #region Portal responses

    [Fact]
    public void ApplyQuoteResponses_AcceptedAnswer_UpdatesTheQuote()
    {
        var data = new CompanyData();
        var quote = SampleQuote();
        data.Quotes.Add(quote);

        var respondedAt = new DateTime(2026, 9, 20, 14, 3, 0, DateTimeKind.Utc);
        var (settledIds, localIds, changed) = PaymentPortalService.ApplyQuoteResponses(
        [
            new PortalQuoteResponseRecord
            {
                QuoteId = quote.Id,
                Status = "accepted",
                RespondedAt = respondedAt,
                ResponseNote = "Looks good, go ahead."
            }
        ], data);

        Assert.Equal(1, changed);
        Assert.Empty(settledIds);
        Assert.Equal([quote.Id], localIds);
        Assert.Equal(QuoteStatus.Accepted, quote.Status);
        Assert.Equal(respondedAt, quote.RespondedAt);
        Assert.Equal("Looks good, go ahead.", quote.ResponseNote);
        Assert.Contains(quote.History, h => h.Action == "Accepted");
    }

    [Fact]
    public void ApplyQuoteResponses_ConvertedQuote_IsNotDowngraded()
    {
        var data = new CompanyData();
        var quote = SampleQuote();
        quote.Status = QuoteStatus.Converted;
        quote.ConvertedInvoiceId = "INV-2026-00001";
        data.Quotes.Add(quote);

        var (settledIds, localIds, changed) = PaymentPortalService.ApplyQuoteResponses(
        [
            new PortalQuoteResponseRecord { QuoteId = quote.Id, Status = "declined", RespondedAt = DateTime.UtcNow }
        ], data);

        Assert.Equal(0, changed);
        Assert.Equal(QuoteStatus.Converted, quote.Status);
        Assert.Null(quote.RespondedAt);
        // The quote is here, so its id waits on a save before the server is told to drop it.
        Assert.Empty(settledIds);
        Assert.Equal([quote.Id], localIds);
    }

    [Fact]
    public void ApplyQuoteAnswer_FromAPublishResponse_WritesTheAnswerOntoTheQuote()
    {
        // The customer answered between the last sync and a resend. The server keeps their answer
        // and emails nobody, so the app has to take the answer back off the publish response.
        var quote = SampleQuote();
        var respondedAt = new DateTime(2026, 9, 21, 9, 30, 0, DateTimeKind.Utc);

        var applied = PaymentPortalService.ApplyQuoteAnswer(quote, "declined", respondedAt, "Too expensive.");

        Assert.True(applied);
        Assert.Equal(QuoteStatus.Declined, quote.Status);
        Assert.Equal(respondedAt, quote.RespondedAt);
        Assert.Equal("Too expensive.", quote.ResponseNote);
        Assert.Contains(quote.History, h => h.Action == "Declined");
    }

    [Fact]
    public void ApplyQuoteAnswer_OnAPlainSentResponse_ChangesNothing()
    {
        var quote = SampleQuote();
        quote.Status = QuoteStatus.Draft;

        Assert.False(PaymentPortalService.ApplyQuoteAnswer(quote, "sent", DateTime.UtcNow, null));
        Assert.Equal(QuoteStatus.Draft, quote.Status);
        Assert.Null(quote.RespondedAt);
    }

    [Fact]
    public void QuotePublishRequest_SendsRevisionOnlyWhenAsked()
    {
        var data = new CompanyData();
        var customer = new Core.Models.Entities.Customer { Id = "CUS-001", Name = "Bright Ltd", Email = "hi@bright.test" };
        var quote = SampleQuote();

        var plain = PaymentPortalService.BuildQuotePublishRequest(quote, data, customer, sendEmail: true, message: null);
        Assert.False(plain.Revision);

        var revised = PaymentPortalService.BuildQuotePublishRequest(
            quote, data, customer, sendEmail: true, message: null, status: "sent", revision: true);
        Assert.True(revised.Revision);

        // The wire name is what the server reads; a rename here would silently reopen nothing.
        var json = System.Text.Json.JsonSerializer.Serialize(revised);
        Assert.Contains("\"revision\":true", json);
    }

    [Fact]
    public void ApplyQuoteResponses_QuoteMissingLocally_IsStillConfirmed()
    {
        var data = new CompanyData();

        var (settledIds, localIds, changed) = PaymentPortalService.ApplyQuoteResponses(
        [
            new PortalQuoteResponseRecord { QuoteId = "QUO-2026-09999", Status = "accepted", RespondedAt = DateTime.UtcNow }
        ], data);

        // Nothing local to lose, so it is confirmed at once rather than coming back for ever.
        Assert.Equal(0, changed);
        Assert.Equal(["QUO-2026-09999"], settledIds);
        Assert.Empty(localIds);
    }

    #endregion

    #region Conversion

    [Fact]
    public void Convert_ProducesADraftInvoiceWithTheSameFigures()
    {
        var data = new CompanyData();
        var quote = SampleQuote();
        quote.Status = QuoteStatus.Accepted;
        data.Quotes.Add(quote);

        var invoice = QuoteConversionService.Convert(quote, data);

        Assert.NotNull(invoice);
        Assert.Equal(InvoiceStatus.Draft, invoice!.Status);
        Assert.Equal(quote.CustomerId, invoice.CustomerId);
        Assert.Equal(quote.Subtotal, invoice.Subtotal);
        Assert.Equal(quote.TaxRate, invoice.TaxRate);
        Assert.Equal(quote.TaxAmount, invoice.TaxAmount);
        Assert.Equal(quote.Total, invoice.Total);
        Assert.Equal(quote.Total, invoice.Balance);
        Assert.Equal(quote.Notes, invoice.Notes);
        Assert.Equal(quote.TemplateId, invoice.TemplateId);
        Assert.Equal(quote.OriginalCurrency, invoice.OriginalCurrency);
        Assert.Contains(invoice.History, h => h.Details == $"Created from quote {quote.Id}");

        Assert.Equal(quote.LineItems.Count, invoice.LineItems.Count);
        for (var i = 0; i < quote.LineItems.Count; i++)
        {
            Assert.Equal(quote.LineItems[i].Description, invoice.LineItems[i].Description);
            Assert.Equal(quote.LineItems[i].Quantity, invoice.LineItems[i].Quantity);
            Assert.Equal(quote.LineItems[i].UnitPrice, invoice.LineItems[i].UnitPrice);
            Assert.Equal(quote.LineItems[i].Discount, invoice.LineItems[i].Discount);
            // The line remembers the product it was priced from, all the way onto the invoice.
            Assert.Equal(quote.LineItems[i].ProductId, invoice.LineItems[i].ProductId);
            Assert.Equal(quote.LineItems[i].Subtotal, invoice.LineItems[i].Subtotal);
            // Deep copy: editing the invoice must not rewrite what the customer was shown.
            Assert.NotSame(quote.LineItems[i], invoice.LineItems[i]);
        }

        Assert.Equal(QuoteStatus.Converted, quote.Status);
        Assert.Equal(invoice.Id, quote.ConvertedInvoiceId);
        Assert.Single(data.Invoices);
    }

    // The draft was added with no USD amount and not waiting for one, so it counted as 0 USD for
    // good and never converted (Rule 3a).
    [Fact]
    public void Convert_StoresTheInvoicesUsdAmounts_OrQueuesThemWhileTheRateIsMissing()
    {
        var data = new CompanyData();
        var usdQuote = SampleQuote();
        usdQuote.OriginalCurrency = "USD";
        var foreignQuote = SampleQuote();
        foreignQuote.Id = "QUO-2026-00002";
        foreignQuote.OriginalCurrency = "XAF";
        data.Quotes.Add(usdQuote);
        data.Quotes.Add(foreignQuote);

        var usd = QuoteConversionService.Convert(usdQuote, data)!;
        var foreign = QuoteConversionService.Convert(foreignQuote, data)!;

        Assert.False(usd.IsPendingConversion);
        Assert.Equal(usd.Total, usd.TotalUSD);
        Assert.True(foreign.IsPendingConversion);
        var queued = Assert.Single(data.PendingConversions);
        Assert.Equal(UsdConversion.KeyOf(foreign), queued.Key);
        Assert.Equal(foreign.Total, queued.Total);
    }

    [Fact]
    public void Convert_CreatesNoRevenue()
    {
        var data = new CompanyData();
        var quote = SampleQuote();
        data.Quotes.Add(quote);

        QuoteConversionService.Convert(quote, data);

        Assert.Empty(data.Revenues);
        Assert.Empty(data.Payments);
        Assert.Empty(data.RecurringInvoices);
    }

    [Fact]
    public void Convert_AnAlreadyConvertedQuote_DoesNothing()
    {
        var data = new CompanyData();
        var quote = SampleQuote();
        data.Quotes.Add(quote);

        QuoteConversionService.Convert(quote, data);
        var second = QuoteConversionService.Convert(quote, data);

        Assert.Null(second);
        Assert.Single(data.Invoices);
    }

    #endregion

    #region Rendering

    private static CompanyData CompanyWithCustomer()
    {
        var data = new CompanyData();
        data.Settings.Company.Name = "Acme Studio";
        data.Customers.Add(new Core.Models.Entities.Customer { Id = "CUS-001", Name = "Bright Ltd", Email = "hi@bright.test" });
        return data;
    }

    [Fact]
    public void RenderQuote_UsesQuoteWordingAndHidesTheAmountToPay()
    {
        var data = CompanyWithCustomer();
        var template = InvoiceTemplateFactory.CreateProfessionalTemplate();
        var quote = SampleQuote();

        var html = new InvoiceHtmlRenderer().RenderQuote(quote, template, data);

        Assert.Contains("Quote #", html);
        Assert.Contains("Valid Until", html);
        Assert.DoesNotContain("Invoice #", html);
        Assert.DoesNotContain("Due Date", html);
        Assert.DoesNotContain("Amount to Pay", html);
        Assert.DoesNotContain("Payment Instructions", html);
        Assert.Contains(quote.QuoteNumber, html);
    }

    [Fact]
    public void RenderQuote_TakesTheTemplatesOwnCasingForTheHeading()
    {
        var data = CompanyWithCustomer();
        var quote = SampleQuote();
        var renderer = new InvoiceHtmlRenderer();

        var shouting = InvoiceTemplateFactory.CreateProfessionalTemplate();
        shouting.HeaderText = "INVOICE";
        Assert.Contains("QUOTE", renderer.RenderQuote(quote, shouting, data));

        var titleCase = InvoiceTemplateFactory.CreateProfessionalTemplate();
        titleCase.HeaderText = "Invoice";
        var html = renderer.RenderQuote(quote, titleCase, data);
        Assert.Contains("Quote", html);
        Assert.DoesNotContain("QUOTE", html);
    }

    [Fact]
    public void RenderInvoice_StillPrintsTheInvoiceWording()
    {
        var data = CompanyWithCustomer();
        var template = InvoiceTemplateFactory.CreateProfessionalTemplate();
        var invoice = SampleQuote().ToRenderableInvoice();
        invoice.Balance = invoice.Total;

        var html = new InvoiceHtmlRenderer().RenderInvoice(invoice, template, data);

        Assert.Contains("Invoice #", html);
        Assert.Contains("Due Date", html);
        Assert.Contains("Amount to Pay", html);
        Assert.Contains("<title>Invoice ", html);
        Assert.DoesNotContain("Quote #", html);
        Assert.DoesNotContain("Valid Until", html);
    }

    [Fact]
    public void RenderQuote_LeavesNoUnfilledPlaceholdersInAnyTemplate()
    {
        var data = CompanyWithCustomer();
        var quote = SampleQuote();
        var renderer = new InvoiceHtmlRenderer();

        foreach (var template in InvoiceTemplateFactory.CreateDefaultTemplates())
        {
            var html = renderer.RenderQuote(quote, template, data);
            Assert.DoesNotContain("{{", html);
            Assert.DoesNotContain("Amount to Pay", html);
            Assert.DoesNotContain("AMOUNT TO PAY", html);
        }
    }

    [Fact]
    public void RenderQuote_EditablePaper_StillHidesThePaymentBlocks()
    {
        // The editor renders the same paper with the edit affordances switched on, which turns on
        // template branches the read-only render skips. The quote must still ask for no money.
        var data = CompanyWithCustomer();
        var quote = SampleQuote();
        var renderer = new InvoiceHtmlRenderer();

        foreach (var template in InvoiceTemplateFactory.CreateDefaultTemplates())
        {
            var html = renderer.RenderInvoice(
                quote.ToRenderableInvoice(), template, data, "$",
                editable: true, DocumentLabels.ForQuote(template.HeaderText));

            Assert.DoesNotContain("{{", html);
            Assert.DoesNotContain("Amount to Pay", html);
            Assert.DoesNotContain("AMOUNT TO PAY", html);
            Assert.DoesNotContain("Payment Instructions", html);
        }
    }

    [Fact]
    public void RenderInvoice_DefaultLabelsAreTheInvoiceLabels()
    {
        var data = CompanyWithCustomer();
        var invoice = SampleQuote().ToRenderableInvoice();

        var renderer = new InvoiceHtmlRenderer();
        foreach (var template in InvoiceTemplateFactory.CreateDefaultTemplates())
        {
            var implicitLabels = renderer.RenderInvoice(invoice, template, data);
            var explicitLabels = renderer.RenderInvoice(invoice, template, data, "$", false, DocumentLabels.Invoice);

            Assert.Equal(implicitLabels, explicitLabels);
        }
    }

    [Fact]
    public void RenderPlainText_ForAQuote_UsesQuoteWordingAndNoPaymentInstructions()
    {
        var data = CompanyWithCustomer();
        var template = InvoiceTemplateFactory.CreateProfessionalTemplate();
        template.ShowPaymentInstructions = true;
        template.PaymentInstructions = "Wire to account 12345.";

        var quote = SampleQuote();
        var text = new InvoiceHtmlRenderer().RenderPlainText(
            quote.ToRenderableInvoice(), template, data, "$", DocumentLabels.ForQuote(template.HeaderText));

        Assert.Contains("Quote #:", text);
        Assert.Contains("Valid Until:", text);
        Assert.DoesNotContain("PAYMENT INSTRUCTIONS:", text);

        var invoiceText = new InvoiceHtmlRenderer().RenderPlainText(quote.ToRenderableInvoice(), template, data);
        Assert.Contains("Invoice #:", invoiceText);
        Assert.Contains("Due Date:", invoiceText);
        Assert.Contains("PAYMENT INSTRUCTIONS:", invoiceText);
    }

    #endregion

    #region Expiry

    [Fact]
    public void IsExpired_OnlyAppliesToASentQuotePastItsDate()
    {
        var sentAndPast = SampleQuote();
        sentAndPast.ValidUntil = DateTime.Today.AddDays(-1);
        Assert.True(sentAndPast.IsExpired);

        var sentAndCurrent = SampleQuote();
        sentAndCurrent.ValidUntil = DateTime.Today;
        Assert.False(sentAndCurrent.IsExpired);

        var acceptedAndPast = SampleQuote();
        acceptedAndPast.Status = QuoteStatus.Accepted;
        acceptedAndPast.ValidUntil = DateTime.Today.AddDays(-1);
        Assert.False(acceptedAndPast.IsExpired);
    }

    #endregion
}
