using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Services.Payroll;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the SampleCompanyService class.
/// Focuses on the static TimeShiftSampleData method which is testable without file I/O.
/// </summary>
public class SampleCompanyServiceTests
{
    #region TimeShiftSampleData Tests

    [Fact]
    public void TimeShiftSampleData_EmptyData_ReturnsFalse()
    {
        var data = new CompanyData();

        var result = SampleCompanyService.TimeShiftSampleData(data);

        Assert.False(result);
    }

    [Fact]
    public void TimeShiftSampleData_WithOldData_ShiftsDatesForward()
    {
        var data = new CompanyData();
        var oldDate = new DateTime(2024, 1, 15);
        data.Revenues.Add(new Revenue
        {
            Id = "REV-001",
            Date = oldDate,
            Total = 1000m,
            CreatedAt = oldDate,
            UpdatedAt = oldDate
        });

        var result = SampleCompanyService.TimeShiftSampleData(data);

        Assert.True(result);
        // The max date should now be close to yesterday
        var targetDate = DateTime.Today.AddDays(-1);
        Assert.Equal(targetDate.Date, data.Revenues[0].Date.Date);
    }

    [Fact]
    public void TimeShiftSampleData_AlreadyCurrent_ReturnsFalse()
    {
        var data = new CompanyData();
        var yesterday = DateTime.Today.AddDays(-1);
        data.Revenues.Add(new Revenue
        {
            Id = "REV-001",
            Date = yesterday,
            Total = 1000m,
            CreatedAt = yesterday,
            UpdatedAt = yesterday
        });

        var result = SampleCompanyService.TimeShiftSampleData(data);

        Assert.False(result);
    }

    [Fact]
    public void TimeShiftSampleData_ShiftsMultipleRevenues_MaintainsRelativeOffsets()
    {
        var data = new CompanyData();
        var baseDate = new DateTime(2024, 1, 15);

        data.Revenues.Add(new Revenue
        {
            Id = "REV-001",
            Date = baseDate,
            Total = 1000m,
            CreatedAt = baseDate,
            UpdatedAt = baseDate
        });
        data.Revenues.Add(new Revenue
        {
            Id = "REV-002",
            Date = baseDate.AddDays(-10),
            Total = 2000m,
            CreatedAt = baseDate.AddDays(-10),
            UpdatedAt = baseDate.AddDays(-10)
        });

        SampleCompanyService.TimeShiftSampleData(data);

        // Both dates should be shifted by the same offset
        var dayDifference = (data.Revenues[0].Date - data.Revenues[1].Date).Days;
        Assert.Equal(10, dayDifference);
    }

    [Fact]
    public void TimeShiftSampleData_PreservesMinValueDates()
    {
        var data = new CompanyData();
        var oldDate = new DateTime(2024, 1, 15);

        data.Revenues.Add(new Revenue
        {
            Id = "REV-001",
            Date = oldDate,
            Total = 1000m,
            CreatedAt = DateTime.MinValue,
            UpdatedAt = oldDate
        });

        SampleCompanyService.TimeShiftSampleData(data);

        // MinValue dates should not be shifted
        Assert.Equal(DateTime.MinValue, data.Revenues[0].CreatedAt);
    }

    #endregion

    #region Invoice Date Shifting Tests

    [Fact]
    public void TimeShiftSampleData_InvoiceDates_AreShifted()
    {
        var data = new CompanyData();
        var oldDate = new DateTime(2024, 1, 15);

        // Add a revenue to establish the max date
        data.Revenues.Add(new Revenue
        {
            Id = "REV-001",
            Date = oldDate,
            Total = 1000m,
            CreatedAt = oldDate,
            UpdatedAt = oldDate
        });

        data.Invoices.Add(new Invoice
        {
            Id = "INV-001",
            InvoiceNumber = "#INV-001",
            CustomerId = "CUST-001",
            IssueDate = oldDate.AddDays(-5),
            DueDate = oldDate.AddDays(25),
            Total = 1000m,
            CreatedAt = oldDate.AddDays(-5),
            UpdatedAt = oldDate.AddDays(-5)
        });

        SampleCompanyService.TimeShiftSampleData(data);

        var targetDate = DateTime.Today.AddDays(-1);
        // Invoice issue date should be shifted (5 days before the max revenue date)
        Assert.Equal(targetDate.Date.AddDays(-5), data.Invoices[0].IssueDate.Date);
        // Due date should be shifted (25 days after the old date)
        Assert.Equal(targetDate.Date.AddDays(25), data.Invoices[0].DueDate.Date);
    }

    [Fact]
    public void TimeShiftSampleData_InvoiceCreatedAt_IsShifted()
    {
        var data = new CompanyData();
        var oldDate = new DateTime(2024, 6, 1);

        data.Revenues.Add(new Revenue
        {
            Id = "REV-001",
            Date = oldDate,
            Total = 500m,
            CreatedAt = oldDate,
            UpdatedAt = oldDate
        });

        data.Invoices.Add(new Invoice
        {
            Id = "INV-002",
            InvoiceNumber = "#INV-002",
            CustomerId = "CUST-001",
            IssueDate = oldDate,
            DueDate = oldDate.AddDays(30),
            Total = 500m,
            CreatedAt = oldDate,
            UpdatedAt = oldDate
        });

        var shiftResult = SampleCompanyService.TimeShiftSampleData(data);

        Assert.True(shiftResult);
        Assert.NotEqual(oldDate, data.Invoices[0].CreatedAt);
        Assert.NotEqual(oldDate, data.Invoices[0].UpdatedAt);
    }

    #endregion

    #region Revenue Date Shifting Tests

    [Fact]
    public void TimeShiftSampleData_RevenueDates_AreShifted()
    {
        var data = new CompanyData();
        var oldDate = new DateTime(2024, 3, 20);

        data.Revenues.Add(new Revenue
        {
            Id = "REV-001",
            Date = oldDate,
            Total = 5000m,
            CreatedAt = oldDate,
            UpdatedAt = oldDate
        });

        SampleCompanyService.TimeShiftSampleData(data);

        var targetDate = DateTime.Today.AddDays(-1);
        Assert.Equal(targetDate.Date, data.Revenues[0].Date.Date);
        Assert.NotEqual(oldDate, data.Revenues[0].CreatedAt);
        Assert.NotEqual(oldDate, data.Revenues[0].UpdatedAt);
    }

    [Fact]
    public void TimeShiftSampleData_MultipleRevenues_AllDatesShifted()
    {
        var data = new CompanyData();
        var maxDate = new DateTime(2024, 6, 30);

        for (int i = 0; i < 5; i++)
        {
            data.Revenues.Add(new Revenue
            {
                Id = $"REV-{i:D3}",
                Date = maxDate.AddDays(-i * 7),
                Total = 1000m * (i + 1),
                CreatedAt = maxDate.AddDays(-i * 7),
                UpdatedAt = maxDate.AddDays(-i * 7)
            });
        }

        var result = SampleCompanyService.TimeShiftSampleData(data);

        Assert.True(result);
        var targetDate = DateTime.Today.AddDays(-1);

        // The most recent revenue should be at the target date
        Assert.Equal(targetDate.Date, data.Revenues[0].Date.Date);

        // All revenues should have been shifted
        foreach (var revenue in data.Revenues)
        {
            Assert.NotEqual(maxDate, revenue.Date);
        }
    }

    [Fact]
    public void TimeShiftSampleData_RevenueAndExpense_BothShifted()
    {
        var data = new CompanyData();
        var oldDate = new DateTime(2024, 4, 15);

        data.Revenues.Add(new Revenue
        {
            Id = "REV-001",
            Date = oldDate,
            Total = 5000m,
            CreatedAt = oldDate,
            UpdatedAt = oldDate
        });

        data.Expenses.Add(new Expense
        {
            Id = "EXP-001",
            Date = oldDate.AddDays(-3),
            Total = 2000m,
            CreatedAt = oldDate.AddDays(-3),
            UpdatedAt = oldDate.AddDays(-3)
        });

        SampleCompanyService.TimeShiftSampleData(data);

        var targetDate = DateTime.Today.AddDays(-1);
        Assert.Equal(targetDate.Date, data.Revenues[0].Date.Date);
        Assert.Equal(targetDate.Date.AddDays(-3), data.Expenses[0].Date.Date);
    }

    #endregion

    #region GetSampleCompanyPath Tests

    [Fact]
    public void GetSampleCompanyPath_ReturnsValidPath()
    {
        var path = SampleCompanyService.GetSampleCompanyPath();

        Assert.NotNull(path);
        Assert.EndsWith(".argo", path);
        Assert.Contains("SampleCompany", path);
    }

    [Fact]
    public void GetSampleCompanyPath_ContainsArgoBooksDirectory()
    {
        var path = SampleCompanyService.GetSampleCompanyPath();

        Assert.Contains("ArgoBooks", path);
    }

    #endregion

    #region Sample payroll

    /// <summary>
    /// A reference date the shipped rate editions cover for every run the generator builds, which
    /// reach back a fortnight from it. Payroll is skipped outright when nothing covers a pay date,
    /// which is correct behaviour and would quietly make every assertion below vacuous.
    ///
    /// Fixed rather than relative to today, so these test the generator and not the calendar, and
    /// far enough back that the time-shift has something to move: it does nothing when the data
    /// is already current.
    /// </summary>
    private static readonly DateTime Covered = new(2026, 6, 15);

    /// <summary>
    /// The state the generator actually runs against: the three employees the workbook's
    /// Employees sheet carries, with what that sheet holds and nothing else. One salaried, two
    /// hourly, all biweekly, no province, no SIN and no address, which is what the generator has
    /// to fill in.
    /// </summary>
    private static CompanyData WithSamplePayroll()
    {
        var data = new CompanyData();

        // The real sample company fills these in before payroll is added. Year end validation
        // reads them, so a bare CompanyData would fail on the company rather than the payroll.
        data.Settings.Company.Name = "TechFlow Solutions";
        data.Settings.Localization.Currency = "CAD";
        data.Employees.AddRange(ImportedEmployees());

        SampleCompanyService.AddSamplePayroll(data, Covered);
        return data;
    }

    private static Employee[] ImportedEmployees() =>
    [
        new() { Id = "EMP-001", Name = "Marcus Johnson", PayType = PayType.Salary, PayRate = 30000m, PayFrequency = PayFrequency.Biweekly },
        new() { Id = "EMP-002", Name = "Michelle Sanders", PayType = PayType.Hourly, PayRate = 24m, PayFrequency = PayFrequency.Biweekly },
        new() { Id = "EMP-003", Name = "Ryan Cooper", PayType = PayType.Hourly, PayRate = 22m, PayFrequency = PayFrequency.Biweekly },
    ];

    [Fact]
    public void SamplePayroll_AddsApprovedRunsAndInventsNobody()
    {
        CompanyData data = WithSamplePayroll();

        // Exactly the imported three: the generator tops them up, it does not add its own.
        Assert.Equal(
            ImportedEmployees().Select(e => e.Name),
            data.Employees.Select(e => e.Name));
        Assert.NotEmpty(data.PayRuns);
        Assert.All(data.PayRuns, r => Assert.Equal(PayRunStatus.Approved, r.Status));
    }

    /// <summary>
    /// Nobody to pay means nothing to add. Running anyway would leave pay runs with no lines on
    /// them, which reads as a broken payroll rather than an empty one.
    /// </summary>
    [Fact]
    public void SamplePayroll_DoesNothingWithoutEmployees()
    {
        var data = new CompanyData();
        data.Settings.Company.Name = "TechFlow Solutions";

        SampleCompanyService.AddSamplePayroll(data, Covered);

        Assert.Empty(data.PayRuns);
        Assert.Empty(data.Employees);
        Assert.True(string.IsNullOrEmpty(data.Settings.Company.PayrollAccountNumber));
    }

    /// <summary>
    /// What the sheet supplies survives, and only what payroll needs is filled in on top.
    /// </summary>
    [Fact]
    public void SamplePayroll_CompletesEmployeesThatCameFromTheSpreadsheet()
    {
        var data = new CompanyData();
        data.Settings.Company.Name = "TechFlow Solutions";
        data.Settings.Localization.Currency = "CAD";
        data.Employees.Add(new Employee
        {
            Id = "EMP-001",
            Name = "Marcus Johnson",
            PayType = PayType.Salary,
            PayRate = 95000m,
            PayFrequency = PayFrequency.Biweekly,
        });

        SampleCompanyService.AddSamplePayroll(data, Covered);

        Employee employee = data.Employees.Single();

        // The spreadsheet's own values survive.
        Assert.Equal("Marcus Johnson", employee.Name);
        Assert.Equal(95000m, employee.PayRate);

        // What payroll needs and the sheet does not carry is filled in.
        Assert.NotEmpty(employee.Province);
        Assert.Equal(9, employee.Sin.Length);
        Assert.NotEmpty(employee.Address.City);

        Assert.NotEmpty(data.PayRuns);
        Assert.All(data.PayRuns.SelectMany(r => r.Lines), l => Assert.True(l.GrossPay > 0));
    }

    /// <summary>
    /// The point of building these through PayrollService rather than writing figures out by
    /// hand: every deduction is real CRA arithmetic, so nobody has to redo the sample when the
    /// rates change.
    /// </summary>
    [Fact]
    public void SamplePayroll_HasRealDeductionsOnEveryLine()
    {
        CompanyData data = WithSamplePayroll();

        Assert.All(data.PayRuns.SelectMany(r => r.Lines), line =>
        {
            Assert.True(line.GrossPay > 0, "every line should have been paid something");
            Assert.True(line.NetPay > 0 && line.NetPay < line.GrossPay, "net should be gross less deductions");
            Assert.True(line.FederalTax > 0);
        });
    }

    /// <summary>An hourly employee earns nothing until hours are put on the run.</summary>
    [Fact]
    public void SamplePayroll_PaysTheHourlyEmployees()
    {
        CompanyData data = WithSamplePayroll();

        Employee[] hourly = [.. data.Employees.Where(e => e.PayType == PayType.Hourly)];
        Assert.NotEmpty(hourly);

        foreach (Employee employee in hourly)
        {
            Assert.All(
                data.PayRuns.SelectMany(r => r.Lines).Where(l => l.EmployeeId == employee.Id),
                line => Assert.Equal(40m * employee.PayRate, line.BasePay));
        }
    }

    /// <summary>
    /// Runs are built oldest first so each one's deductions see the year-to-date the ones before
    /// it produced. Built the other way round everybody restarts at zero and the annual ceilings
    /// never bite.
    /// </summary>
    [Fact]
    public void SamplePayroll_AccumulatesYearToDate()
    {
        CompanyData data = WithSamplePayroll();

        Employee salaried = data.Employees.First(e => e.PayType == PayType.Salary);
        PayrollYearToDate ytd = new PayrollService().YearToDateFor(data, salaried.Id);

        decimal paid = data.PayRuns
            .SelectMany(r => r.Lines)
            .Where(l => l.EmployeeId == salaried.Id)
            .Sum(l => l.GrossPay);

        Assert.Equal(paid, ytd.PensionableEarnings);
        Assert.True(ytd.CppEmployee > 0);
    }

    /// <summary>Wages have to reach the books, or the sample contradicts what the app claims.</summary>
    [Fact]
    public void SamplePayroll_RecordsTheWagesAsExpenses()
    {
        CompanyData data = WithSamplePayroll();

        Assert.All(data.PayRuns.SelectMany(r => r.Lines), line => Assert.NotNull(line.ExpenseId));
        Assert.Equal(data.PayRuns.Sum(r => r.Lines.Count), data.Expenses.Count);
    }

    /// <summary>
    /// The sample must not trip the year end validation it exists to demonstrate. Every rule
    /// added for the T4 filing applies to these employees too.
    /// </summary>
    [Fact]
    public void SamplePayroll_PassesYearEndValidation()
    {
        CompanyData data = WithSamplePayroll();

        T4Return t4 = new T4Service().Build(data, Covered.Year);

        Assert.Empty(T4Service.Validate(data, t4));
        Assert.Empty(T4Service.Warnings(t4));
    }

    /// <summary>
    /// The wage expenses shift with everything else, so the runs have to move by the same offset
    /// or a pay run and the expense it created land on different days.
    /// </summary>
    [Fact]
    public void TimeShift_MovesPayrollWithTheRestOfTheData()
    {
        CompanyData data = WithSamplePayroll();

        DateTime firstPayDate = data.PayRuns.Min(r => r.PayDate);
        DateTime firstWage = data.Expenses.Min(e => e.Date);

        SampleCompanyService.TimeShiftSampleData(data);

        Assert.Equal(
            (data.Expenses.Min(e => e.Date) - firstWage).Days,
            (data.PayRuns.Min(r => r.PayDate) - firstPayDate).Days);

        Assert.NotEqual(firstPayDate, data.PayRuns.Min(r => r.PayDate));
    }

    /// <summary>
    /// The guard is on pay runs, not employees: the workbook always supplies employees, so they
    /// say nothing about whether payroll has been set up, and they are topped up rather than
    /// treated as a reason to stop. What has to hold instead is that a company which already has
    /// payroll is left completely alone.
    /// </summary>
    [Fact]
    public void SamplePayroll_DoesNothingWhenPayrollAlreadyExists()
    {
        var data = new CompanyData();
        data.Employees.Add(new Employee { Id = "EMP-900", Name = "Real Person", Province = "AB" });
        data.PayRuns.Add(new PayRun { Id = "PR-0001", PayDate = Covered, Status = PayRunStatus.Approved });

        SampleCompanyService.AddSamplePayroll(data, Covered);

        Employee employee = data.Employees.Single();

        Assert.Equal("Real Person", employee.Name);
        Assert.Equal("AB", employee.Province);
        Assert.Empty(employee.Sin);
        Assert.Single(data.PayRuns);
        Assert.Empty(data.Expenses);
    }

    #endregion
}
