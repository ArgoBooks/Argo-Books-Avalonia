using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.BankMatching;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Bills: expenses paid after they arrive. The formal reports count the cost on the bill's date and
/// owe it as Accounts Payable until paid; cash, the dashboard and the charts count it once paid.
/// </summary>
public class BillsTests
{
    private static readonly DateTime BillDate = new(2024, 1, 15);
    private static readonly DateTime DueDate = new(2024, 2, 14);
    private static readonly DateTime PaidOn = new(2024, 3, 3);

    // A US company, so no purchase tax is claimed back and the figures below are round.
    private static CompanyData Company(DateTime? paidDate = null)
    {
        var data = new CompanyData();
        data.Settings.Company.Country = "United States";
        data.Settings.Localization.Currency = "USD";
        data.Suppliers.Add(new Supplier { Id = "S1", Name = "Landlord" });
        data.Revenues.Add(new Revenue
        {
            Id = "R1", Date = new DateTime(2024, 1, 10), OriginalCurrency = "USD",
            Subtotal = 1000m, Total = 1000m, TotalUSD = 1000m
        });
        data.Expenses.Add(Bill(paidDate));
        return data;
    }

    private static Expense Bill(DateTime? paidDate) => new()
    {
        Id = "B1", SupplierId = "S1", Date = BillDate, OriginalCurrency = "USD",
        Amount = 300m, Total = 300m, TotalUSD = 300m, DueDate = DueDate, PaidDate = paidDate
    };

    private static ReportFilters Range(DateTime start, DateTime end) => new() { StartDate = start, EndDate = end };

    private static ReportFilters UpTo(DateTime end) => new() { EndDate = end };

    private static AccountingTableData Report(CompanyData data, AccountingReportType type, ReportFilters filters) =>
        new AccountingReportDataService(data, filters).GetReportData(type);

    // Formatted amounts show a negative in parentheses.
    private static decimal ParseAmount(string formatted)
    {
        var value = decimal.Parse(new string(formatted.Where(c => char.IsDigit(c) || c == '.').ToArray()),
            System.Globalization.CultureInfo.InvariantCulture);
        return formatted.StartsWith('(') ? -value : value;
    }

    private static decimal Amount(AccountingTableData table, string label, int column = 0) =>
        ParseAmount(table.Rows.Single(r => r.Label == label).Values[column]);

    #region The bill itself

    [Fact]
    public void UnpaidBill_IsOwedUntilPaid_AndOverdueAfterItsDueDate()
    {
        var bill = Bill(null);

        Assert.False(bill.IsPaid);
        Assert.Null(bill.CashDate);
        Assert.True(bill.IsOwedOn(new DateTime(2024, 1, 31)));
        Assert.False(bill.IsOwedOn(new DateTime(2024, 1, 14)));
        Assert.False(bill.IsOverdueOn(DueDate));
        Assert.True(bill.IsOverdueOn(DueDate.AddDays(1)));
    }

    [Fact]
    public void PaidBill_LeftTheBankOnItsPaidDate_AndIsOwedOnlyBefore()
    {
        var bill = Bill(PaidOn);

        Assert.True(bill.IsPaid);
        Assert.Equal(PaidOn, bill.CashDate);
        Assert.True(bill.IsOwedOn(PaidOn.AddDays(-1)));
        Assert.False(bill.IsOwedOn(PaidOn));
        Assert.False(bill.IsOverdueOn(PaidOn.AddDays(30)));
    }

    [Fact]
    public void ExpenseWithNoDueDate_IsPaidOnItsOwnDate()
    {
        var expense = new Expense { Date = BillDate, Total = 50m, TotalUSD = 50m };

        Assert.True(expense.IsPaid);
        Assert.False(expense.IsBill);
        Assert.Equal(BillDate, expense.CashDate);
        Assert.False(expense.IsOwedOn(BillDate));
    }

    #endregion

    #region Dashboard totals

    [Fact]
    public void DashboardExpenses_LeaveOutAnUnpaidBill_AndCountItOnItsDateOncePaid()
    {
        var january = (new DateTime(2024, 1, 1), new DateTime(2024, 1, 31));

        Assert.Equal(0m, ExpenseAggregator.SumExpensesUSD(Company().Expenses, january.Item1, january.Item2));
        Assert.Equal(300m, ExpenseAggregator.SumExpensesUSD(Company(PaidOn).Expenses, january.Item1, january.Item2));
    }

    [Fact]
    public void DashboardProfit_IsNotReducedByAnUnpaidBill()
    {
        var start = new DateTime(2024, 1, 1);
        var end = new DateTime(2024, 1, 31);

        Assert.Equal(1000m, ProfitCalculator.CalculateNetProfitUSD(Company(), start, end));
        Assert.Equal(700m, ProfitCalculator.CalculateNetProfitUSD(Company(PaidOn), start, end));
    }

    [Fact]
    public void CashPaid_PlacesABillOnTheDayItWasPaid()
    {
        var expenses = Company(PaidOn).Expenses;
        decimal Same(decimal usd, DateTime _) => usd;

        Assert.Equal(0m, ExpenseAggregator.SumCashPaidDisplay(expenses, new DateTime(2024, 1, 1), new DateTime(2024, 1, 31), Same));
        Assert.Equal(300m, ExpenseAggregator.SumCashPaidDisplay(expenses, new DateTime(2024, 3, 1), new DateTime(2024, 3, 31), Same));
    }

    #endregion

    #region Formal reports

    [Fact]
    public void IncomeStatement_CountsTheBillOnItsDate_PaidOrNot()
    {
        var incomeStatement = Report(Company(), AccountingReportType.IncomeStatement, Range(new DateTime(2024, 1, 1), new DateTime(2024, 1, 31)));

        Assert.Equal(300m, Amount(incomeStatement, "Total Operating Expenses"));
        Assert.Equal(700m, Amount(incomeStatement, "NET INCOME"));
    }

    [Fact]
    public void BalanceSheet_OwesAnUnpaidBillAsAccountsPayable_AndKeepsTheCash()
    {
        var balanceSheet = Report(Company(), AccountingReportType.BalanceSheet, UpTo(new DateTime(2024, 1, 31)));

        Assert.Equal(1000m, Amount(balanceSheet, "Cash (Estimated)"));
        Assert.Equal(300m, Amount(balanceSheet, "Accounts Payable"));
        Assert.Equal(700m, Amount(balanceSheet, "Retained Earnings"));
        Assert.Equal(0m, Amount(balanceSheet, "Owner's Equity"));
        Assert.Equal(Amount(balanceSheet, "TOTAL ASSETS"), Amount(balanceSheet, "TOTAL LIABILITIES & EQUITY"));
    }

    [Fact]
    public void BalanceSheet_AfterTheBillIsPaid_HasLessCashAndNothingOwed()
    {
        var balanceSheet = Report(Company(PaidOn), AccountingReportType.BalanceSheet, UpTo(new DateTime(2024, 3, 31)));

        Assert.Equal(700m, Amount(balanceSheet, "Cash (Estimated)"));
        Assert.Equal(0m, Amount(balanceSheet, "Accounts Payable"));
        Assert.Equal(0m, Amount(balanceSheet, "Owner's Equity"));
    }

    [Fact]
    public void BalanceSheet_BeforeThePaidDate_StillOwesTheBill()
    {
        var balanceSheet = Report(Company(PaidOn), AccountingReportType.BalanceSheet, UpTo(new DateTime(2024, 2, 29)));

        Assert.Equal(1000m, Amount(balanceSheet, "Cash (Estimated)"));
        Assert.Equal(300m, Amount(balanceSheet, "Accounts Payable"));
    }

    [Fact]
    public void CashFlow_CountsTheBillInTheMonthItWasPaid()
    {
        var january = Report(Company(PaidOn), AccountingReportType.CashFlowStatement, Range(new DateTime(2024, 1, 1), new DateTime(2024, 1, 31)));
        var march = Report(Company(PaidOn), AccountingReportType.CashFlowStatement, Range(new DateTime(2024, 3, 1), new DateTime(2024, 3, 31)));

        Assert.Equal(0m, Amount(january, "Cash Paid for Expenses"));
        Assert.Equal(-300m, Amount(march, "Cash Paid for Expenses"));
    }

    [Fact]
    public void APAging_PutsTheBillInItsOverdueBucket_AndAddsUpToAccountsPayable()
    {
        var asOf = UpTo(new DateTime(2024, 3, 31));
        var aging = Report(Company(), AccountingReportType.AccountsPayableAging, asOf);
        var balanceSheet = Report(Company(), AccountingReportType.BalanceSheet, asOf);

        // Due Feb 14, so 46 days past due on Mar 31.
        var row = aging.Rows.Single(r => r.Label == "Landlord");
        Assert.Equal(300m, ParseAmount(row.Values[2]));
        Assert.Equal(Amount(balanceSheet, "Accounts Payable"), Amount(aging, "TOTAL", 5));
    }

    [Fact]
    public void APAging_WithNothingOwed_SaysSo()
    {
        var aging = Report(Company(PaidOn), AccountingReportType.AccountsPayableAging, UpTo(new DateTime(2024, 3, 31)));

        Assert.Contains(aging.Rows, r => r.Label == "No unpaid bills");
        Assert.Equal(0m, Amount(aging, "TOTAL", 5));
    }

    #endregion

    #region Bank matching and returns

    private static BankStatementLine BankLine(DateTime date) => new()
    {
        Id = Guid.NewGuid().ToString("N"), Date = date, Amount = -300m, Description = "Landlord",
        MatchStatus = BankLineMatchStatus.Unmatched
    };

    private static BankMatchCandidate CandidateFor(BankStatementLine line) => new()
    {
        LineId = line.Id, RecordType = BookRecordType.Expense, RecordId = "B1", Confidence = 0.9
    };

    [Fact]
    public void MatchingABankPayment_PaysTheBillThatDay_AndUnmatchingOwesItAgain()
    {
        var data = Company();
        var bill = data.Expenses[0];
        var line = BankLine(PaidOn);
        var matcher = new BankMatchingService();

        matcher.ConfirmMatch(line, CandidateFor(line), data);
        Assert.Equal(PaidOn, bill.PaidDate);

        matcher.UnlinkMatch(line, data);
        Assert.Null(bill.PaidDate);
        Assert.False(bill.IsPaid);
    }

    [Fact]
    public void UnmatchingABillPaidByHand_LeavesItPaid()
    {
        var data = Company(PaidOn);
        var bill = data.Expenses[0];
        var line = BankLine(PaidOn);
        var matcher = new BankMatchingService();

        matcher.ConfirmMatch(line, CandidateFor(line), data);
        matcher.UnlinkMatch(line, data);

        Assert.Equal(PaidOn, bill.PaidDate);
    }

    [Fact]
    public void ReturningGoodsFromAnUnpaidBill_RecordsNoSupplierCredit()
    {
        var data = Company();
        var returnRecord = new Return
        {
            Id = "RET-1", ReturnType = "Expense", OriginalTransactionId = "B1",
            ReturnDate = new DateTime(2024, 1, 20), RefundAmount = 300m
        };

        Assert.Null(ReturnRefunds.Record(data, returnRecord));
        Assert.Single(data.Expenses);
    }

    #endregion
}
