using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Assets;
using ArgoBooks.Core.Models.Common;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Tracking;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Payroll on the accrual basis, refunds recorded with returns, and the Balance Sheet's equity
/// (docs/Calculations.md §9 and §10). Every amount is in USD, so nothing needs converting.
/// </summary>
public class PayrollAndReturnBooksTests
{
    private static readonly DateTime PayDate = new(2024, 6, 14);

    private static readonly ReportFilters Year2024 = new()
    {
        StartDate = new DateTime(2024, 1, 1),
        EndDate = new DateTime(2024, 12, 31)
    };

    private static AccountingTableData Report(CompanyData data, AccountingReportType type) =>
        new AccountingReportDataService(data, Year2024).GetReportData(type);

    private static decimal Amount(AccountingTableData data, string label)
    {
        var formatted = data.Rows.Single(r => r.Label == label).Values[0];
        var value = decimal.Parse(new string(formatted.Where(c => char.IsDigit(c) || c == '.').ToArray()),
            System.Globalization.CultureInfo.InvariantCulture);
        return formatted.StartsWith('(') ? -value : value;
    }

    private static Expense Expense(string id, DateTime date, decimal total, decimal tax = 0m) => new()
    {
        Id = id, Date = date, Description = id, OriginalCurrency = "USD", Total = total, Amount = total - tax, TaxAmount = tax,
        LineItems = [new LineItem { Description = id, Quantity = 1, UnitPrice = total - tax }]
    };

    #region Payroll

    /// <summary>
    /// One approved run: $1,000 gross, $186 withheld, $814 net pay recorded as an expense, and $72.40
    /// of employer CPP and EI, so $258.40 is owed to the agencies.
    /// </summary>
    private static CompanyData PaidRun(string province = "ON")
    {
        var data = new CompanyData();
        var wages = Expense("EXP-W", PayDate, 814m);
        data.Expenses.Add(wages);
        data.PayRuns.Add(new PayRun
        {
            Id = "PR-0001", PayDate = PayDate, Status = PayRunStatus.Approved,
            Lines =
            [
                new PayRunLine
                {
                    EmployeeName = "Pat", Province = province, GrossPay = 1000m, CppEmployee = 50m, CppEmployer = 50m,
                    EiEmployee = 16m, EiEmployer = 22.4m, FederalTax = 80m, ProvincialTax = 40m, NetPay = 814m,
                    ExpenseId = wages.Id
                }
            ]
        });
        return data;
    }

    [Fact]
    public void IncomeStatement_CountsGrossPayAndTheEmployersShare_NotTheNetPayExpense()
    {
        var result = Report(PaidRun(), AccountingReportType.IncomeStatement);

        Assert.Equal(1000m, Amount(result, "Wages and Salaries"));
        Assert.Equal(72.4m, Amount(result, "Employer Payroll Contributions"));
        Assert.Equal(1072.4m, Amount(result, "Total Operating Expenses"));
        Assert.DoesNotContain(result.Rows, r => r.Label == "Uncategorized");
    }

    [Fact]
    public void BalanceSheet_OwesTheRemittanceUntilItsPaymentIsRecorded()
    {
        var data = PaidRun();

        var before = Report(data, AccountingReportType.BalanceSheet);
        Assert.Equal(258.4m, Amount(before, "Payroll Remittances Owing"));
        Assert.Equal(-814m, Amount(before, "Cash (Estimated)"));
        Assert.Equal(Amount(before, "TOTAL ASSETS"), Amount(before, "TOTAL LIABILITIES & EQUITY"));

        PayrollBooks.RecordRemittance(data, new DateTime(2024, 7, 15));

        var after = Report(data, AccountingReportType.BalanceSheet);
        Assert.DoesNotContain(after.Rows, r => r.Label == "Payroll Remittances Owing");
        Assert.Equal(-1072.4m, Amount(after, "Cash (Estimated)"));
        Assert.Equal(Amount(after, "TOTAL ASSETS"), Amount(after, "TOTAL LIABILITIES & EQUITY"));

        // The remittance expense isn't a second payroll cost on the Income Statement.
        Assert.Equal(1072.4m, Amount(Report(data, AccountingReportType.IncomeStatement), "Total Operating Expenses"));
    }

    [Fact]
    public void BalanceSheet_StillOwesARemittancePaidAfterItsDate()
    {
        var data = PaidRun();
        PayrollBooks.RecordRemittance(data, new DateTime(2025, 1, 10));

        Assert.Equal(258.4m, Amount(Report(data, AccountingReportType.BalanceSheet), "Payroll Remittances Owing"));
    }

    [Fact]
    public void RecordRemittance_PaysEachAgencyItsShare_AndUndoTakesItBack()
    {
        var data = PaidRun("QC");
        var run = data.PayRuns[0];

        var recorded = PayrollBooks.RecordRemittance(data, new DateTime(2024, 7, 15));

        // Quebec income tax and QPP go to Revenu Quebec; federal tax and EI go to CRA.
        Assert.Equal(2, recorded.Count);
        Assert.Equal(118.4m, recorded.Single(r => r.Agency == PayrollBooks.Agency.Cra).Expense.Total);
        Assert.Equal(140m, recorded.Single(r => r.Agency == PayrollBooks.Agency.Quebec).Expense.Total);
        Assert.NotNull(run.CraRemittanceExpenseId);
        Assert.Empty(PayrollBooks.Unremitted(data, PayrollBooks.Agency.Cra, new DateTime(2024, 7, 15)));

        PayrollBooks.UndoRemittance(data, recorded);
        Assert.Null(run.CraRemittanceExpenseId);
        Assert.Single(data.Expenses);

        PayrollBooks.RedoRemittance(data, recorded);
        Assert.Equal(3, data.Expenses.Count);
        Assert.Equal(recorded.Single(r => r.Agency == PayrollBooks.Agency.Quebec).Expense.Id, run.QuebecRemittanceExpenseId);
    }

    [Fact]
    public void NextRemittance_LeavesOutOneAlreadyRecorded()
    {
        var data = PaidRun();
        // After May's remittance is due on 15 June, so June's pay is the next one due.
        var today = new DateTime(2024, 6, 20);
        Assert.Equal(258.4m, PayrollService.NextRemittanceByAgency(data.PayRuns, today).Cra);

        PayrollBooks.RecordRemittance(data, today);

        Assert.Equal(0m, PayrollService.NextRemittanceByAgency(data.PayRuns, today).Cra);
    }

    [Fact]
    public void Charts_PutPayrollInItsOwnSlice_AndLeaveItOutOfSupplierCountries()
    {
        var data = PaidRun();
        var service = new ReportChartDataService(data, Year2024);

        Assert.Equal(814d, Assert.Single(service.GetExpenseDistribution(), p => p.Label == "Payroll").Value);
        Assert.Empty(service.GetExpensesByCountryOfDestination());
    }

    #endregion

    #region Equity

    [Fact]
    public void Equity_RetainedEarningsAreTheProfitToDate_AndOwnersEquityTheRest()
    {
        var data = new CompanyData();
        data.Revenues.Add(new Revenue { Id = "SAL-1", Date = new DateTime(2023, 5, 1), OriginalCurrency = "USD", Total = 100m });
        data.Expenses.Add(Expense("EXP-1", new DateTime(2024, 3, 1), 30m));
        data.Assets.Add(new FixedAsset { Id = "AST-1", Name = "Lot", Type = AssetType.Land, PurchaseDate = new DateTime(2020, 1, 1), Cost = 5000m, Currency = "USD" });

        var result = Report(data, AccountingReportType.BalanceSheet);

        Assert.Equal(70m, Amount(result, "Retained Earnings"));
        Assert.Equal(5000m, Amount(result, "Owner's Equity"));
        Assert.Equal(Amount(result, "TOTAL ASSETS"), Amount(result, "TOTAL LIABILITIES & EQUITY"));
    }

    #endregion

    #region Returns

    private static Revenue Sale(RevenuePaymentStatus status = RevenuePaymentStatus.Paid, string? invoiceId = null) => new()
    {
        Id = "SAL-1", Date = new DateTime(2024, 3, 1), OriginalCurrency = "USD", Total = 113m, Amount = 100m, TaxAmount = 13m,
        PaymentStatus = status, InvoiceId = invoiceId, CustomerId = "CUS-1",
        LineItems = [new LineItem { Description = "Chair", Quantity = 1, UnitPrice = 100m }]
    };

    private static Return ReturnOf(string id, string type) => new()
    {
        Id = "RET-1", OriginalTransactionId = id, ReturnType = type, ReturnDate = new DateTime(2024, 3, 10),
        RefundAmount = 113m, Status = ReturnStatus.Completed
    };

    [Fact]
    public void ReturnedSale_RefundsRevenueTaxAndCash()
    {
        var data = new CompanyData();
        data.Revenues.Add(Sale());
        var returnRecord = ReturnOf("SAL-1", "Customer");
        data.Returns.Add(returnRecord);

        var refund = ReturnRefunds.Record(data, returnRecord);

        var payment = Assert.Single(data.Payments);
        Assert.Same(payment, refund!.Payment);
        Assert.Equal(-113m, payment.Amount);
        Assert.Equal(13m, payment.RefundTaxAmount);
        Assert.Equal(payment.Id, returnRecord.RefundRecordId);

        Assert.Equal(0m, Amount(Report(data, AccountingReportType.IncomeStatement), "Total Revenue"));
        Assert.Equal(0m, Amount(Report(data, AccountingReportType.TaxSummary), "NET TAX LIABILITY"));
        Assert.Equal(0m, Amount(Report(data, AccountingReportType.BalanceSheet), "Cash (Estimated)"));
    }

    [Fact]
    public void ReturnedPartOfASale_GivesBackPartOfItsTax()
    {
        var data = new CompanyData();
        data.Revenues.Add(Sale());
        var returnRecord = ReturnOf("SAL-1", "Customer");
        returnRecord.RefundAmount = 56.5m;

        var payment = ReturnRefunds.Record(data, returnRecord)!.Payment!;

        Assert.Equal(-56.5m, payment.Amount);
        Assert.Equal(6.5m, payment.RefundTaxAmount);
    }

    [Fact]
    public void ReturnedSale_NothingPaid_RecordsNoRefund()
    {
        var data = new CompanyData();
        data.Revenues.Add(Sale(RevenuePaymentStatus.Unpaid));
        var returnRecord = ReturnOf("SAL-1", "Customer");

        Assert.Null(ReturnRefunds.Record(data, returnRecord));
        Assert.Empty(data.Payments);
    }

    [Fact]
    public void ReturnedSale_PaidOnline_LeavesTheRefundToThePaymentProvider()
    {
        var data = new CompanyData();
        data.Invoices.Add(new Invoice { Id = "INV-1", OriginalCurrency = "USD", Total = 113m, TaxAmount = 13m, AmountPaid = 113m, Status = InvoiceStatus.Paid });
        data.Payments.Add(new Payment { Id = "PAY-1", InvoiceId = "INV-1", Amount = 113m, OriginalCurrency = "USD", Source = PaymentSource.Online });
        data.Revenues.Add(Sale(invoiceId: "INV-1"));

        Assert.Null(ReturnRefunds.Record(data, ReturnOf("SAL-1", "Customer")));
        Assert.Single(data.Payments);
    }

    [Fact]
    public void ReturnedSale_OnAnInvoice_ShowsTheInvoiceRefunded_AndUndoPutsItBack()
    {
        var data = new CompanyData();
        var invoice = new Invoice { Id = "INV-1", OriginalCurrency = "USD", Total = 113m, TaxAmount = 13m, Status = InvoiceStatus.Sent };
        data.Invoices.Add(invoice);
        data.Payments.Add(new Payment { Id = "PAY-1", InvoiceId = "INV-1", Amount = 113m, OriginalCurrency = "USD" });
        InvoiceTotalsService.Recalculate(invoice, data.Payments);
        data.Revenues.Add(Sale(invoiceId: "INV-1"));

        var refund = ReturnRefunds.Record(data, ReturnOf("SAL-1", "Customer"));
        Assert.Equal(InvoiceStatus.Refunded, invoice.Status);

        ReturnRefunds.Remove(data, refund!);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Single(data.Payments);
    }

    [Fact]
    public void ReturnedPurchase_RecordsASupplierCredit_OffExpensesAndTheTaxClaimedBack()
    {
        var data = new CompanyData();
        data.Expenses.Add(Expense("EXP-1", new DateTime(2024, 3, 1), 113m, 13m));
        var returnRecord = ReturnOf("EXP-1", "Expense");

        var credit = ReturnRefunds.Record(data, returnRecord)!.Expense!;

        Assert.Equal(-113m, credit.Total);
        Assert.Equal(-13m, credit.TaxAmount);
        Assert.Equal(0m, Amount(Report(data, AccountingReportType.IncomeStatement), "Total Operating Expenses"));
        Assert.Equal(0m, Amount(Report(data, AccountingReportType.TaxSummary), "NET TAX LIABILITY"));
    }

    [Fact]
    public void ReturnedStock_TakesBackThePstItsPurchaseCounted()
    {
        // British Columbia: the 7% PST on stock bought is an expense, and returning the stock takes it back off.
        var data = new CompanyData();
        data.Settings.Company.Country = "Canada";
        data.Settings.Company.ProvinceState = "BC";
        var purchase = Expense("EXP-1", new DateTime(2024, 3, 1), 112m, 12m);
        purchase.LineItems[0].IsStockPurchase = true;
        data.Expenses.Add(purchase);
        var returnRecord = ReturnOf("EXP-1", "Expense");
        returnRecord.RefundAmount = 112m;

        ReturnRefunds.Record(data, returnRecord);

        Assert.Equal(0m, Amount(Report(data, AccountingReportType.IncomeStatement), "Total Operating Expenses"));
    }

    [Fact]
    public void SupplierCredit_GivesBackOnlyTheTaxItsPurchaseClaimed()
    {
        // In British Columbia only the 5% GST of the 12% tax is claimed back.
        var data = new CompanyData();
        var purchase = Expense("EXP-1", new DateTime(2024, 3, 1), 112m, 12m);
        data.Expenses.Add(purchase);
        var returnRecord = ReturnOf("EXP-1", "Expense");
        returnRecord.RefundAmount = 112m;

        var credit = ReturnRefunds.Record(data, returnRecord)!.Expense!;
        var rule = PurchaseTax.RuleFor("Canada", "BC");

        Assert.Equal(5m, PurchaseTax.Claimable(purchase, rule));
        Assert.Equal(-5m, PurchaseTax.Claimable(credit, rule));
    }

    #endregion
}
