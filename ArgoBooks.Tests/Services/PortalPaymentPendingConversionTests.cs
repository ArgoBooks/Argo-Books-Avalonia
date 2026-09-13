using System.Net;
using System.Text;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Portal;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using ArgoBooks.Tests.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// An online payment on an invoice still waiting for its exchange rate took a USD amount of 0 and
/// was never queued. Once the invoice converted it read as paid with its whole USD balance still
/// owing, and the payment added nothing to cash (Calculations.md Rule 3a).
/// </summary>
public class PortalPaymentPendingConversionTests : ModalViewModelTestBase
{
    [Fact]
    public async Task PaymentOnAnInvoiceWaitingForItsRate_WaitsTooAndClearsTheBalanceOnceConverted()
    {
        UseNoExchangeRates();
        var issued = DateTime.Today.AddMonths(-1);
        var data = new CompanyData();
        var invoice = new Invoice
        {
            Id = "INV-1",
            InvoiceNumber = "INV-1",
            OriginalCurrency = "EUR",
            IssueDate = issued,
            Total = 100m,
            Balance = 100m,
            IsPendingConversion = true
        };
        data.Invoices.Add(invoice);
        data.PendingConversions.Add(new PendingConversion
        {
            TransactionId = invoice.Id,
            TransactionType = "Invoice",
            OriginalCurrency = "EUR",
            TransactionDate = issued,
            Total = 100m,
            Balance = 100m
        });

        PaymentPortalService.ProcessSyncedPayments(
        [
            new PortalPaymentRecord
            {
                Id = 1, InvoiceId = invoice.Id, Amount = 100m, Currency = "EUR",
                PaymentMethod = "stripe", CreatedAt = issued.AddDays(5)
            }
        ], data);

        var payment = Assert.Single(data.Payments);
        Assert.True(payment.IsPendingConversion);

        // The rates arrive, and the queue converts the invoice and the payment.
        var rates = new ExchangeRateService(new NoDiskPlatform(), new HttpClient(new EurHandler(0.9m)));
        var queue = new PendingConversionService(new NoDiskPlatform(), exchangeRateService: rates);
        await queue.ReconcileWithCompanyDataAsync(data);
        await queue.ProcessPendingConversionsAsync(data);

        Assert.False(payment.IsPendingConversion);
        Assert.Equal(invoice.TotalUSD, payment.AmountUSD);
        Assert.Equal(0m, invoice.BalanceUSD);
    }

    private sealed class EurHandler(decimal usdToEur) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = $$"""{ "success": true, "base": "USD", "rates": { "EUR": {{usdToEur}} } }""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class NoDiskPlatform : IPlatformService
    {
        public PlatformType Platform => PlatformType.Linux;
        public string GetAppDataPath() => Path.GetTempPath();
        public string GetTempPath() => Path.GetTempPath();
        public string GetDefaultDocumentsPath() => Path.GetTempPath();
        public string GetLogsPath() => Path.GetTempPath();
        public string GetCachePath() => Path.GetTempPath();
        public void EnsureDirectoryExists(string path) { }
        public bool SupportsFileSystem => false;
        public bool SupportsNativeDialogs => false;
        public bool SupportsBiometrics => false;
        public Task<bool> IsBiometricAvailableAsync() => Task.FromResult(false);
        public Task<string> GetBiometricAvailabilityDetailsAsync() => Task.FromResult("");
        public Task<bool> AuthenticateWithBiometricAsync(string reason) => Task.FromResult(false);
        public void StorePasswordForBiometric(string fileId, string password) { }
        public string? GetPasswordForBiometric(string fileId) => null;
        public void ClearPasswordForBiometric(string fileId) { }
        public bool SupportsAutoUpdate => false;
        public int MaxRecentCompanies => 10;
        public string NormalizePath(string path) => path;
        public string CombinePaths(params string[] paths) => Path.Combine(paths);
        public string GetMachineId() => "test-machine-id";
        public void RegisterFileTypeAssociations(string iconPath) { }
        public StringComparer PathComparer => StringComparer.Ordinal;
    }
}
