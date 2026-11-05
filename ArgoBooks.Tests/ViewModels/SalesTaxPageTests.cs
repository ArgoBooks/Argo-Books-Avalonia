using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.ViewModels;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>The Sales Tax page and its filing form: filing a period, taking it back, and books changed after filing.</summary>
public class SalesTaxPageTests : ModalViewModelTestBase
{
    // A sale three months back with 13 tax, so its quarter has ended and is waiting to be filed.
    private Revenue Sale()
    {
        Company.Settings.Localization.Currency = "USD";
        var sale = new Revenue
        {
            Id = "SAL-1", Date = DateTime.Today.AddMonths(-4), OriginalCurrency = "USD",
            Amount = 100m, TaxAmount = 13m, Total = 113m, TotalUSD = 113m, PaymentStatus = RevenuePaymentStatus.Paid,
            LineItems = [new LineItem { Description = "Chair", Quantity = 1, UnitPrice = 100m }]
        };
        Company.Revenues.Add(sale);
        return sale;
    }

    private static SalesTaxPeriodRow Waiting(SalesTaxPageViewModel page) =>
        page.Periods.Single(p => p.CanFile && p.Amounts.Net != 0);

    [Fact]
    public void FilingAPeriod_SavesTheReturn_AndUndoTakesItBack()
    {
        Sale();
        var page = new SalesTaxPageViewModel();
        var modals = new SalesTaxModalsViewModel();

        modals.OpenFileModal(Waiting(page));
        modals.ConfirmationNumber = "  ABC-123 ";
        modals.ConfirmFileCommand.Execute(null);

        var filing = Assert.Single(Company.SalesTaxFilings);
        Assert.Equal(13m, filing.Amount);
        Assert.Equal("ABC-123", filing.ConfirmationNumber);
        Assert.Equal(SalesTaxFrequency.Quarterly, Company.Settings.Company.SalesTaxFrequency);
        Assert.False(modals.IsFileModalOpen);

        Undo();
        Assert.Empty(Company.SalesTaxFilings);
        Assert.Null(Company.Settings.Company.SalesTaxFrequency);

        Redo();
        Assert.Single(Company.SalesTaxFilings);
        page.Cleanup();
    }

    [Fact]
    public void BooksChangedAfterFiling_ShowOnTheFiledPeriod()
    {
        var sale = Sale();
        var page = new SalesTaxPageViewModel();
        var modals = new SalesTaxModalsViewModel();
        modals.OpenFileModal(Waiting(page));
        modals.ConfirmFileCommand.Execute(null);
        page.Cleanup();

        sale.TaxAmount = 15m;
        sale.Total = 115m;
        sale.TotalUSD = 115m;

        var reopened = new SalesTaxPageViewModel();
        var filed = reopened.Periods.Single(p => p.IsFiled);
        Assert.Equal(13m, filed.Amounts.Net);
        Assert.Equal(2m, filed.ChangedBy);
        Assert.Contains("2.00", reopened.OwedNow);
        reopened.Cleanup();
    }

    [Fact]
    public void TakingBackAFiling_OwesThePeriodAgain()
    {
        Sale();
        var page = new SalesTaxPageViewModel();
        var modals = new SalesTaxModalsViewModel();
        modals.OpenFileModal(Waiting(page));
        modals.ConfirmFileCommand.Execute(null);

        modals.RemoveFiling(Company.SalesTaxFilings.Single());

        Assert.Empty(Company.SalesTaxFilings);
        Undo();
        Assert.Single(Company.SalesTaxFilings);
        page.Cleanup();
    }

    [Fact]
    public void ChoosingTheShownFrequency_SavesIt()
    {
        Sale();
        var page = new SalesTaxPageViewModel();
        Assert.Null(page.SelectedFrequency);
        Assert.False(page.IsFrequencyChosen);

        page.SelectedFrequency = "Quarterly";

        Assert.Equal(SalesTaxFrequency.Quarterly, Company.Settings.Company.SalesTaxFrequency);
        page.Cleanup();
    }

    [Fact]
    public void APeriodWithNoTax_HasNothingToPay_AndIsNotChased()
    {
        Company.Settings.Localization.Currency = "USD";
        Company.Revenues.Add(new Revenue
        {
            Id = "SAL-1", Date = DateTime.Today.AddMonths(-4), OriginalCurrency = "USD", Amount = 100m, Total = 100m, TotalUSD = 100m,
            PaymentStatus = RevenuePaymentStatus.Paid
        });

        var page = new SalesTaxPageViewModel();

        Assert.DoesNotContain(page.Periods, p => p.Status is "Due" or "Overdue");
        Assert.Contains(page.Periods, p => p.Status == "Nothing to pay" && p.CanFile);
        page.Cleanup();
    }

    [Fact]
    public void UndoingAFiling_KeepsAFrequencyChosenSince()
    {
        Sale();
        var page = new SalesTaxPageViewModel();
        var modals = new SalesTaxModalsViewModel();
        modals.OpenFileModal(Waiting(page));
        modals.ConfirmFileCommand.Execute(null);

        page.SelectedFrequency = "Monthly";
        Undo();

        Assert.Equal(SalesTaxFrequency.Monthly, Company.Settings.Company.SalesTaxFrequency);
        page.Cleanup();
    }

    [Fact]
    public void APaymentDateBeforeThePeriodEnds_IsRefused()
    {
        Sale();
        var page = new SalesTaxPageViewModel();
        var modals = new SalesTaxModalsViewModel();
        var row = Waiting(page);
        modals.OpenFileModal(row);

        modals.PaidOn = row.Period.End;
        modals.ConfirmFileCommand.Execute(null);

        Assert.NotEmpty(modals.PaidOnError);
        Assert.Empty(Company.SalesTaxFilings);
        Assert.True(modals.IsFileModalOpen);
        page.Cleanup();
    }

    [Fact]
    public void TakingBackAMatchedFiling_UnmatchesItsBankLine_AndUndoMatchesItAgain()
    {
        Sale();
        var page = new SalesTaxPageViewModel();
        var modals = new SalesTaxModalsViewModel();
        modals.OpenFileModal(Waiting(page));
        modals.ConfirmFileCommand.Execute(null);
        var filing = Company.SalesTaxFilings.Single();
        var line = new ArgoBooks.Core.Models.BankMatching.BankStatementLine
        {
            Id = "L1", Amount = -13m, MatchStatus = BankLineMatchStatus.Matched,
            MatchedRecordType = BookRecordType.SalesTaxFiling, MatchedRecordId = filing.Id
        };
        Company.BankImportSessions.Add(new ArgoBooks.Core.Models.BankMatching.BankImportSession { Lines = [line] });

        modals.RemoveFiling(filing);
        Assert.Null(line.MatchedRecordId);

        Undo();
        Assert.Equal(filing.Id, line.MatchedRecordId);
        Assert.Equal(BankLineMatchStatus.Matched, line.MatchStatus);
        page.Cleanup();
    }
}
