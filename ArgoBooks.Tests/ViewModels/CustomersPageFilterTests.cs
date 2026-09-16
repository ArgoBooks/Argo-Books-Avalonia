using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// The customers filter modal's payment status, country and outstanding amount.
///
/// The page copied only the customer status and last rental dates from the modal, so these three
/// could be set, applied and shown as active while changing nothing in the list.
/// </summary>
public class CustomersPageFilterTests : ModalViewModelTestBase
{
    private void Seed()
    {
        Company.Customers.Add(new Customer { Id = "CUS-1", Name = "Paid Up", Address = new Address { Country = "US" } });
        Company.Customers.Add(new Customer { Id = "CUS-2", Name = "Recently Late", Address = new Address { Country = "Canada" } });
        Company.Customers.Add(new Customer { Id = "CUS-3", Name = "Long Late", Address = new Address { Country = "United States" } });

        // A paid invoice and a never-sent draft are not owed, however late their due dates.
        Company.Invoices.Add(NewInvoice("CUS-1", InvoiceStatus.Paid, balance: 0m, dueDaysAgo: 200));
        Company.Invoices.Add(NewInvoice("CUS-1", InvoiceStatus.Draft, balance: 900m, dueDaysAgo: 200));
        Company.Invoices.Add(NewInvoice("CUS-1", InvoiceStatus.Sent, balance: 50m, dueDaysAgo: -10));
        Company.Invoices.Add(NewInvoice("CUS-2", InvoiceStatus.Sent, balance: 300m, dueDaysAgo: 20));
        Company.Invoices.Add(NewInvoice("CUS-3", InvoiceStatus.Partial, balance: 1200m, dueDaysAgo: 120));
    }

    private static Invoice NewInvoice(string customerId, InvoiceStatus status, decimal balance, int dueDaysAgo) => new()
    {
        CustomerId = customerId,
        Status = status,
        Total = balance,
        Balance = balance,
        IssueDate = DateTime.Today.AddDays(-dueDaysAgo - 30),
        DueDate = DateTime.Today.AddDays(-dueDaysAgo),
    };

    private (CustomersPageViewModel Page, CustomerModalsViewModel Modals) Wired()
    {
        Seed();
        var page = new CustomersPageViewModel();
        var modals = new CustomerModalsViewModel();

        // The page subscribes through the app shell, which tests don't build.
        modals.FiltersApplied += page.OnFiltersApplied;
        modals.FiltersCleared += page.OnFiltersCleared;
        return (page, modals);
    }

    private static List<string> Names(CustomersPageViewModel page) => [.. page.Customers.Select(c => c.Name).Order()];

    [Theory]
    [InlineData("Current", "Paid Up")]
    [InlineData("Overdue", "Recently Late")]
    [InlineData("Delinquent", "Long Late")]
    public void PaymentStatus_GroupsCustomersByHowLateTheirOldestUnpaidInvoiceIs(string status, string expected)
    {
        var (page, modals) = Wired();
        modals.OpenFilterModal();

        modals.FilterPaymentStatus = status;
        modals.ApplyFiltersCommand.Execute(null);

        Assert.Equal([expected], Names(page));
    }

    [Fact]
    public void Country_MatchesHoweverTheAddressSpellsIt()
    {
        var (page, modals) = Wired();
        modals.OpenFilterModal();

        Assert.Equal(["All", "Canada", "United States"], modals.CountryOptions);

        modals.FilterCountry = "United States";
        modals.ApplyFiltersCommand.Execute(null);

        Assert.Equal(["Long Late", "Paid Up"], Names(page));
    }

    [Fact]
    public void OutstandingRange_CountsOnlyWhatIsStillOwed()
    {
        // Paid Up owes 50: its draft's 900 was never billed.
        var (page, modals) = Wired();
        modals.OpenFilterModal();

        modals.FilterOutstandingMin = "100";
        modals.FilterOutstandingMax = "1000";
        modals.ApplyFiltersCommand.Execute(null);

        Assert.Equal(["Recently Late"], Names(page));
    }

    [Fact]
    public void ClearingFilters_ShowsEveryCustomerAgain()
    {
        var (page, modals) = Wired();
        modals.OpenFilterModal();
        modals.FilterPaymentStatus = "Delinquent";
        modals.ApplyFiltersCommand.Execute(null);
        Assert.Equal(["Long Late"], Names(page));

        modals.OpenFilterModal();
        modals.ClearFiltersCommand.Execute(null);

        Assert.Equal(["Long Late", "Paid Up", "Recently Late"], Names(page));
    }
}
