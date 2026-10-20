using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Entities;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Models.Rentals;
using ArgoBooks.Core.Models.Reports;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the Sales by Customer, Expenses by Supplier, Payroll Summary and Rentals reports.
/// </summary>
public class SummaryReportsTests
{
    private static readonly ReportFilters Year2024 = new()
    {
        StartDate = new DateTime(2024, 1, 1),
        EndDate = new DateTime(2024, 12, 31)
    };

    private static AccountingTableData Report(CompanyData data, AccountingReportType type, ReportFilters? filters = null) =>
        new AccountingReportDataService(data, filters ?? Year2024).GetReportData(type);

    private static List<AccountingRow> DataRows(AccountingTableData data) =>
        data.Rows.Where(r => r.RowType == AccountingRowType.DataRow).ToList();

    private static AccountingRow Row(AccountingTableData data, string label) => data.Rows.Single(r => r.Label == label);

    // Formatted amounts show a negative in parentheses.
    private static decimal ParseAmount(string formatted)
    {
        var value = decimal.Parse(new string(formatted.Where(c => char.IsDigit(c) || c == '.').ToArray()),
            System.Globalization.CultureInfo.InvariantCulture);
        return formatted.StartsWith('(') ? -value : value;
    }

    private static Revenue Sale(string id, string? customerId, decimal total, decimal tax, DateTime date) => new()
    {
        Id = id, CustomerId = customerId, Date = date, OriginalCurrency = "USD",
        Total = total, TotalUSD = total, TaxAmount = tax, TaxAmountUSD = tax
    };

    private static Expense Purchase(string id, string? supplierId, decimal total, decimal tax, DateTime date) => new()
    {
        Id = id, SupplierId = supplierId, Date = date, OriginalCurrency = "USD",
        Total = total, TotalUSD = total, TaxAmount = tax, TaxAmountUSD = tax
    };

    #region Sales by Customer

    private static CompanyData TwoCustomersSales()
    {
        var data = new CompanyData();
        data.Customers.Add(new Customer { Id = "C1", Name = "Acme" });
        data.Customers.Add(new Customer { Id = "C2", Name = "Birch Co" });
        data.Revenues.Add(Sale("R1", "C1", 1050m, 50m, new DateTime(2024, 3, 1)));
        data.Revenues.Add(Sale("R2", "C1", 525m, 25m, new DateTime(2024, 6, 1)));
        data.Revenues.Add(Sale("R3", "C2", 300m, 0m, new DateTime(2024, 4, 1)));
        data.Revenues.Add(Sale("R4", "C2", 9999m, 0m, new DateTime(2023, 4, 1)));
        data.Revenues.Add(Sale("R5", null, 100m, 0m, new DateTime(2024, 5, 1)));
        data.Payments.Add(new Payment
        {
            Id = "REF-1", CustomerId = "C2", Date = new DateTime(2024, 5, 1), OriginalCurrency = "USD",
            Amount = -100m, AmountUSD = -100m, IsRefund = true
        });
        return data;
    }

    [Fact]
    public void SalesByCustomer_TotalsEachCustomersSalesBeforeTax_LessRefunds()
    {
        var result = Report(TwoCustomersSales(), AccountingReportType.SalesByCustomer);

        var rows = DataRows(result);
        Assert.Equal(["Acme", "Birch Co", "(Sales with no customer)"], rows.Select(r => r.Label));
        Assert.Equal(1500m, ParseAmount(rows[0].Values[0]));
        Assert.Equal("", rows[0].Values[1]);
        Assert.Equal(-100m, ParseAmount(rows[1].Values[1]));
        Assert.Equal(200m, ParseAmount(rows[1].Values[2]));
        Assert.Equal("83.3%", rows[0].Values[3]);
        Assert.Equal(1800m, ParseAmount(Row(result, "Total").Values[2]));
    }

    [Fact]
    public void SalesByCustomer_AddsUpToTheIncomeStatementsRevenue()
    {
        var data = TwoCustomersSales();

        var byCustomer = Report(data, AccountingReportType.SalesByCustomer);
        var incomeStatement = Report(data, AccountingReportType.IncomeStatement);

        Assert.Equal(ParseAmount(Row(incomeStatement, "Total Revenue").Values[0]), ParseAmount(Row(byCustomer, "Total").Values[2]));
    }

    [Fact]
    public void SalesByCustomer_WithNoSales_SaysSo()
    {
        var result = Report(new CompanyData(), AccountingReportType.SalesByCustomer);

        Assert.Equal("No sales in this period", Assert.Single(DataRows(result)).Label);
    }

    #endregion

    #region Expenses by Supplier

    [Fact]
    public void ExpensesBySupplier_TotalsEachSupplier_BeforeAndAfterTax()
    {
        var data = new CompanyData();
        data.Suppliers.Add(new Supplier { Id = "S1", Name = "Paper Mill" });
        data.Suppliers.Add(new Supplier { Id = "S2", Name = "Hardware Store" });
        data.Expenses.Add(Purchase("E1", "S1", 210m, 10m, new DateTime(2024, 2, 1)));
        data.Expenses.Add(Purchase("E2", "S1", 105m, 5m, new DateTime(2024, 8, 1)));
        data.Expenses.Add(Purchase("E3", "S2", 50m, 0m, new DateTime(2024, 3, 1)));
        data.Expenses.Add(Purchase("E4", "S2", 800m, 0m, new DateTime(2025, 3, 1)));
        data.Expenses.Add(Purchase("E5", null, 20m, 0m, new DateTime(2024, 3, 1)));

        var result = Report(data, AccountingReportType.ExpensesBySupplier);

        var rows = DataRows(result);
        Assert.Equal(["Paper Mill", "Hardware Store", "(Expenses with no supplier)"], rows.Select(r => r.Label));
        Assert.Equal("2", rows[0].Values[0]);
        Assert.Equal(300m, ParseAmount(rows[0].Values[1]));
        Assert.Equal(15m, ParseAmount(rows[0].Values[2]));
        Assert.Equal(315m, ParseAmount(rows[0].Values[3]));

        var total = Row(result, "Total");
        Assert.Equal("4", total.Values[0]);
        Assert.Equal(385m, ParseAmount(total.Values[3]));
    }

    [Fact]
    public void ExpensesBySupplier_PutsExpensesWithNoSupplierLast_EvenWhenTheyAreTheLargest()
    {
        var data = new CompanyData();
        data.Suppliers.Add(new Supplier { Id = "S1", Name = "Paper Mill" });
        data.Expenses.Add(Purchase("E1", "S1", 50m, 0m, new DateTime(2024, 2, 1)));
        data.Expenses.Add(Purchase("E2", null, 900m, 0m, new DateTime(2024, 2, 1)));
        data.Expenses.Add(Purchase("E3", "S-GONE", 400m, 0m, new DateTime(2024, 2, 1)));

        var result = Report(data, AccountingReportType.ExpensesBySupplier);

        Assert.Equal(["Paper Mill", "(Expenses with no supplier)", "(Supplier no longer on file)"], DataRows(result).Select(r => r.Label));
    }

    [Fact]
    public void ExpensesBySupplier_GivesPayrollWagesARowOfTheirOwn()
    {
        var data = new CompanyData();
        data.Suppliers.Add(new Supplier { Id = "S1", Name = "Paper Mill" });
        data.Expenses.Add(Purchase("E1", "S1", 50m, 0m, new DateTime(2024, 2, 1)));
        data.Expenses.Add(Purchase("E2", null, 2000m, 0m, new DateTime(2024, 2, 15)));
        data.Expenses.Add(Purchase("E3", null, 30m, 0m, new DateTime(2024, 2, 20)));
        data.PayRuns.Add(Run("PR1", new DateTime(2024, 2, 15), PayRunStatus.Approved, Line("EMP1", "Avery Lee", 2500m)));
        data.PayRuns[0].Lines[0].ExpenseId = "E2";

        var result = Report(data, AccountingReportType.ExpensesBySupplier);

        Assert.Equal(["Paper Mill", "(Payroll wages)", "(Expenses with no supplier)"], DataRows(result).Select(r => r.Label));
    }

    #endregion

    #region Payroll Summary

    private static PayRunLine Line(string employeeId, string name, decimal gross) => new()
    {
        EmployeeId = employeeId, EmployeeName = name, Province = "SK", GrossPay = gross,
        CppEmployee = gross * 0.05m, CppEmployer = gross * 0.05m,
        EiEmployee = gross * 0.02m, EiEmployer = gross * 0.028m,
        FederalTax = gross * 0.1m, ProvincialTax = gross * 0.05m,
        NetPay = gross * 0.78m
    };

    private static PayRun Run(string id, DateTime payDate, PayRunStatus status, params PayRunLine[] lines) => new()
    {
        Id = id, PayDate = payDate, PeriodStart = payDate.AddDays(-14), PeriodEnd = payDate, Status = status, Lines = [.. lines]
    };

    [Fact]
    public void PayrollSummary_TotalsEachEmployee_AndTheEmployersShare()
    {
        var data = new CompanyData();
        data.Employees.Add(new Employee { Id = "E1", Name = "Avery Lee" });
        data.Employees.Add(new Employee { Id = "E2", Name = "Blake Moss" });
        data.PayRuns.Add(Run("PR1", new DateTime(2024, 3, 15), PayRunStatus.Approved, Line("E1", "Avery Lee", 2000m), Line("E2", "Blake Moss", 1000m)));
        data.PayRuns.Add(Run("PR2", new DateTime(2024, 3, 29), PayRunStatus.Approved, Line("E1", "Avery Lee", 2000m)));
        data.PayRuns.Add(Run("PR3", new DateTime(2024, 4, 12), PayRunStatus.Draft, Line("E2", "Blake Moss", 5000m)));

        var result = Report(data, AccountingReportType.PayrollSummary);

        var avery = Row(result, "Avery Lee");
        Assert.Equal(4000m, ParseAmount(avery.Values[0]));
        Assert.Equal(200m, ParseAmount(avery.Values[1]));
        Assert.Equal(80m, ParseAmount(avery.Values[2]));
        Assert.Equal(600m, ParseAmount(avery.Values[3]));
        Assert.Equal(3120m, ParseAmount(avery.Values[4]));
        Assert.Equal(1000m, ParseAmount(Row(result, "Blake Moss").Values[0]));

        Assert.Equal(5000m, ParseAmount(Row(result, "Total").Values[0]));
        var employer = Row(result, "Employer contributions");
        Assert.Equal(250m, ParseAmount(employer.Values[1]));
        Assert.Equal(140m, ParseAmount(employer.Values[2]));
        Assert.Equal(5390m, ParseAmount(Row(result, "Total payroll cost").Values[0]));
        Assert.Equal("From 2 pay runs.", result.Footnote);
    }

    [Fact]
    public void PayrollSummary_AVoidedRunAndItsReversalCancelOut()
    {
        var data = new CompanyData();
        data.Employees.Add(new Employee { Id = "E1", Name = "Avery Lee" });
        var reversal = Line("E1", "Avery Lee", 2000m);
        reversal.GrossPay = -2000m;
        reversal.CppEmployee = -100m;
        reversal.CppEmployer = -100m;
        reversal.EiEmployee = -40m;
        reversal.EiEmployer = -56m;
        reversal.FederalTax = -200m;
        reversal.ProvincialTax = -100m;
        reversal.NetPay = -1560m;
        data.PayRuns.Add(Run("PR1", new DateTime(2024, 3, 15), PayRunStatus.Void, Line("E1", "Avery Lee", 2000m)));
        var reversing = Run("PR2", new DateTime(2024, 3, 20), PayRunStatus.Approved, reversal);
        reversing.VoidsPayRunId = "PR1";
        data.PayRuns.Add(reversing);

        var result = Report(data, AccountingReportType.PayrollSummary);

        Assert.Equal(0m, ParseAmount(Row(result, "Avery Lee").Values[0]));
        Assert.Equal(0m, ParseAmount(Row(result, "Total payroll cost").Values[0]));
    }

    #endregion

    #region Rentals

    [Fact]
    public void Rentals_ListsWhatIsOutToday_AndWhatCameBackInThePeriod()
    {
        var today = DateTime.Today;
        var data = new CompanyData();
        data.Customers.Add(new Customer { Id = "C1", Name = "Acme" });
        data.Rentals.Add(new RentalRecord
        {
            Id = "RNT-1", CustomerId = "C1", Status = RentalStatus.Active, Quantity = 1,
            StartDate = today.AddDays(-10), DueDate = today.AddDays(-2), SecurityDeposit = 200m
        });
        data.Rentals.Add(new RentalRecord
        {
            Id = "RNT-2", CustomerId = "C1", Status = RentalStatus.Returned, Quantity = 1,
            StartDate = today.AddDays(-20), DueDate = today.AddDays(-15), ReturnDate = today.AddDays(-14),
            SecurityDeposit = 100m, DepositRefunded = 60m, TotalCost = 300m
        });
        data.Rentals.Add(new RentalRecord
        {
            Id = "RNT-3", CustomerId = "C1", Status = RentalStatus.Reserved, Quantity = 1,
            StartDate = today.AddDays(5), DueDate = today.AddDays(9), SecurityDeposit = 500m
        });
        var filters = new ReportFilters { StartDate = today.AddDays(-30), EndDate = today };

        var result = Report(data, AccountingReportType.Rentals, filters);

        var rows = DataRows(result);
        Assert.Equal(2, rows.Count);
        Assert.EndsWith("overdue", rows[0].Values[1]);
        Assert.Equal(200m, ParseAmount(rows[0].Values[3]));
        Assert.Equal(340m, ParseAmount(rows[1].Values[2]));
        Assert.Equal(0m, ParseAmount(rows[1].Values[3]));
        Assert.Equal(340m, ParseAmount(Row(result, "Earned from rentals returned in this period").Values[2]));
        Assert.Equal(200m, ParseAmount(Row(result, "Deposits held today").Values[3]));
    }

    [Fact]
    public void Rentals_WithNoRentals_SaysSoInBothSections()
    {
        var result = Report(new CompanyData(), AccountingReportType.Rentals);

        Assert.Contains(result.Rows, r => r.Label == "Nothing is rented out");
        Assert.Contains(result.Rows, r => r.Label == "Nothing came back in this period");
    }

    #endregion

    [Theory]
    [InlineData(ReportTemplateFactory.TemplateNames.SalesByCustomer, AccountingReportType.SalesByCustomer)]
    [InlineData(ReportTemplateFactory.TemplateNames.ExpensesBySupplier, AccountingReportType.ExpensesBySupplier)]
    [InlineData(ReportTemplateFactory.TemplateNames.PayrollSummary, AccountingReportType.PayrollSummary)]
    [InlineData(ReportTemplateFactory.TemplateNames.Rentals, AccountingReportType.Rentals)]
    public void Templates_AreBuiltInAndUseTheirReport(string templateName, AccountingReportType type)
    {
        Assert.True(ReportTemplateFactory.IsBuiltInTemplate(templateName));

        var config = ReportTemplateFactory.CreateFromTemplate(templateName);

        Assert.Equal(type, Assert.Single(config.Elements.OfType<AccountingTableReportElement>()).ReportType);
    }
}
