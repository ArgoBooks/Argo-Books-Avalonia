using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Services.Payroll;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the Record of Employment worksheet.
///
/// Nothing here bounces when it is wrong. Service Canada accepts the ROE and calculates the
/// claim from it, so a short hours figure or an earnings window of the wrong length quietly
/// shortens how long someone is paid. The two window lengths, their being different from each
/// other, and the reverse chronological order of block 15C are the cases most likely to be
/// broken by someone tidying this code later.
/// </summary>
public class RoeTests
{
    private static CompanyData Data(params Employee[] employees)
    {
        var data = new CompanyData();
        data.Settings.Company.Name = "Test Company";
        data.Settings.Company.PayrollAccountNumber = "123456789RP0001";
        data.Employees.AddRange(employees);
        return data;
    }

    private static Employee Person(
        PayType payType = PayType.Salary,
        PayFrequency frequency = PayFrequency.Biweekly,
        decimal? standardHours = 40m) => new()
    {
        Id = "EMP-001",
        Name = "Dana Smith",
        Sin = "046454286",
        Province = "AB",
        PayType = payType,
        PayRate = payType == PayType.Salary ? 52000m : 25m,
        PayFrequency = frequency,
        StandardHoursPerWeek = standardHours,
        StartDate = new DateTime(2024, 1, 8),
        EndDate = new DateTime(2026, 7, 10),
    };

    /// <summary>A run of consecutive biweekly periods, oldest first.</summary>
    private static void AddRuns(CompanyData data, int count, decimal gross = 2000m,
                                decimal hours = 80m, decimal vacation = 0m)
    {
        var end = new DateTime(2026, 7, 10);

        for (int i = 0; i < count; i++)
        {
            DateTime periodEnd = end.AddDays(-14 * i);

            data.PayRuns.Add(new PayRun
            {
                Id = $"PR-{i:0000}",
                PayDate = periodEnd.AddDays(5),
                PeriodStart = periodEnd.AddDays(-13),
                PeriodEnd = periodEnd,
                Status = PayRunStatus.Approved,
                Lines =
                {
                    new PayRunLine
                    {
                        EmployeeId = "EMP-001",
                        EmployeeName = "Dana Smith",
                        Province = "AB",
                        GrossPay = gross,
                        HoursWorked = hours,
                        VacationPay = vacation,
                        NetPay = gross,
                    },
                },
            });
        }
    }

    private static RoeWorksheet Built(CompanyData data) => new RoeService().Build(data, "EMP-001");

    #region The two windows

    [Theory]
    [InlineData(PayFrequency.Weekly, 53)]
    [InlineData(PayFrequency.Biweekly, 27)]
    [InlineData(PayFrequency.SemiMonthly, 25)]
    [InlineData(PayFrequency.Monthly, 13)]
    public void BlocksFifteenAAndC_CoverTheEquivalentOf53Weeks(PayFrequency frequency, int expected) =>
        Assert.Equal(expected, RoeService.HoursPeriodCount(frequency));

    [Theory]
    [InlineData(PayFrequency.Weekly, 27)]
    [InlineData(PayFrequency.Biweekly, 14)]
    [InlineData(PayFrequency.SemiMonthly, 13)]
    [InlineData(PayFrequency.Monthly, 7)]
    public void BlockFifteenB_CoversAShorterWindow(PayFrequency frequency, int expected) =>
        Assert.Equal(expected, RoeService.EarningsPeriodCount(frequency));

    [Fact]
    public void TheTwoWindows_AreNotTheSameLength()
    {
        // The single most likely way this gets "simplified" into a bug.
        foreach (PayFrequency frequency in Enum.GetValues<PayFrequency>())
        {
            Assert.True(RoeService.HoursPeriodCount(frequency) > RoeService.EarningsPeriodCount(frequency),
                $"{frequency} should use a longer window for hours than for earnings");
        }
    }

    [Fact]
    public void EarningsAreTotalledOverTheShorterWindow_EvenWhenMorePeriodsExist()
    {
        CompanyData data = Data(Person());
        AddRuns(data, 27, gross: 2000m);

        RoeWorksheet sheet = Built(data);

        // 14 biweekly periods for block 15B, not the 27 that were recorded.
        Assert.Equal(14 * 2000m, sheet.TotalInsurableEarnings);
        Assert.Equal(27, sheet.Periods.Count);
    }

    [Fact]
    public void MorePeriodsThanTheWindow_AreDroppedFromTheOldestEnd()
    {
        CompanyData data = Data(Person());
        AddRuns(data, 40);

        RoeWorksheet sheet = Built(data);

        Assert.Equal(27, sheet.Periods.Count);
        Assert.Equal(new DateTime(2026, 7, 10), sheet.Periods[0].PeriodEnd);
    }

    #endregion

    #region Block 15C ordering

    [Fact]
    public void BlockFifteenC_IsMostRecentFirst()
    {
        CompanyData data = Data(Person());
        AddRuns(data, 5);

        List<RoePayPeriod> periods = Built(data).Periods;

        Assert.Equal(new DateTime(2026, 7, 10), periods[0].PeriodEnd);
        Assert.Equal(new DateTime(2026, 6, 26), periods[1].PeriodEnd);
        Assert.True(periods.SequenceEqual(periods.OrderByDescending(p => p.PeriodEnd)),
            "block 15C must be in reverse chronological order");
    }

    #endregion

    #region Hours

    [Fact]
    public void AnHourlyEmployee_UsesTheHoursActuallyEntered()
    {
        CompanyData data = Data(Person(PayType.Hourly, standardHours: null));
        AddRuns(data, 3, hours: 72m);

        Assert.Equal(216m, Built(data).TotalInsurableHours);
    }

    [Fact]
    public void ASalariedEmployee_UsesTheirContractWeek()
    {
        CompanyData data = Data(Person(PayType.Salary, standardHours: 37.5m));
        AddRuns(data, 3, hours: 0m);

        // 37.5 a week, biweekly, so 75 a period.
        Assert.Equal(225m, Built(data).TotalInsurableHours);
    }

    [Fact]
    public void ASalariedEmployeeWithNoContractHours_ReportsNothingRatherThanZero()
    {
        // Zero hours would be accepted by ROE Web and would cost the employee their claim.
        CompanyData data = Data(Person(PayType.Salary, standardHours: null));
        AddRuns(data, 3, hours: 0m);

        RoeWorksheet sheet = Built(data);

        Assert.Null(sheet.TotalInsurableHours);
        Assert.NotNull(sheet.HoursUnavailableReason);
        Assert.Contains(RoeService.Validate(sheet), p => p.Contains("standard hours", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ASalariedNilPeriod_EarnsNoHours()
    {
        CompanyData data = Data(Person(PayType.Salary, standardHours: 40m));
        AddRuns(data, 2, gross: 2000m, hours: 0m);
        AddRuns(data, 0);

        // A period with no earnings had no work in it, so crediting 80 contract hours to it
        // would invent hours nobody worked.
        data.PayRuns.Add(new PayRun
        {
            Id = "PR-NIL",
            PayDate = new DateTime(2026, 5, 20),
            PeriodStart = new DateTime(2026, 5, 1),
            PeriodEnd = new DateTime(2026, 5, 15),
            Status = PayRunStatus.Approved,
            Lines = { new PayRunLine { EmployeeId = "EMP-001", GrossPay = 0m, NetPay = 0m } },
        });

        RoeWorksheet sheet = Built(data);
        RoePayPeriod nil = sheet.Periods.Single(p => p.PeriodEnd == new DateTime(2026, 5, 15));

        Assert.Equal(0m, nil.InsurableHours);
        Assert.Equal(0m, nil.InsurableEarnings);
    }

    #endregion

    #region Dates

    [Fact]
    public void BlockTwelve_IsNeverEarlierThanBlockEleven()
    {
        Employee person = Person();

        // A final unpaid stretch: they stopped being paid after the last pay period ended.
        person.EndDate = new DateTime(2026, 7, 20);

        CompanyData data = Data(person);
        AddRuns(data, 3);

        RoeWorksheet sheet = Built(data);

        Assert.True(sheet.FinalPeriodEnd >= sheet.LastDayPaid,
            "block 12 can never be earlier than block 11");
    }

    [Fact]
    public void TheDeadline_IsFiveDaysAfterTheEndOfTheFinalPayPeriod()
    {
        // Not five days after the last day worked, which is the usual misreading.
        CompanyData data = Data(Person());
        AddRuns(data, 3);

        RoeWorksheet sheet = Built(data);

        Assert.Equal(sheet.FinalPeriodEnd!.Value.AddDays(5), sheet.Deadline);
    }

    [Fact]
    public void AMissingStartDate_FallsBackToTheEarliestPayPeriod()
    {
        Employee person = Person();
        person.StartDate = null;

        CompanyData data = Data(person);
        AddRuns(data, 3);

        Assert.Equal(new DateTime(2026, 5, 30), Built(data).FirstDayWorked);
    }

    #endregion

    #region Exclusions

    [Fact]
    public void DraftRuns_AreExcluded()
    {
        CompanyData data = Data(Person());
        AddRuns(data, 3);
        data.PayRuns[0].Status = PayRunStatus.Draft;

        Assert.Equal(2, Built(data).Periods.Count);
    }

    [Fact]
    public void AVoidedRunAndItsReversal_ShareOnePeriodAndNetToZero()
    {
        CompanyData data = Data(Person());
        AddRuns(data, 1, gross: 2000m);

        data.PayRuns[0].Status = PayRunStatus.Void;

        PayRun reversal = new()
        {
            Id = "PR-0000-R",
            PayDate = data.PayRuns[0].PayDate,
            PeriodStart = data.PayRuns[0].PeriodStart,
            PeriodEnd = data.PayRuns[0].PeriodEnd,
            Status = PayRunStatus.Approved,
            Lines = { new PayRunLine { EmployeeId = "EMP-001", GrossPay = -2000m, HoursWorked = -80m } },
        };
        data.PayRuns.Add(reversal);

        RoeWorksheet sheet = Built(data);

        // One period, not two: they share a period end, so a void must not consume two of the
        // 27 slots and push a real period off the end.
        Assert.Single(sheet.Periods);
        Assert.Equal(0m, sheet.Periods[0].InsurableEarnings);
    }

    [Fact]
    public void AnEiExemptEmployee_HasNoInsurableEarnings()
    {
        Employee person = Person();
        person.IsEiExempt = true;

        CompanyData data = Data(person);
        AddRuns(data, 3, gross: 2000m);

        Assert.Equal(0m, Built(data).TotalInsurableEarnings);
    }

    /// <summary>
    /// Block 17A is vacation pay paid BECAUSE OF the separation, not the year's vacation pay.
    ///
    /// This test previously asserted the sum across every period, which is the one thing Service
    /// Canada's ROE guide says must not be reported: its chart lists vacation pay "included with
    /// each pay" as do-not-report, and 17A as the amount payable on layoff or termination. An
    /// inflated 17A moves the date EI benefits start, so it costs the employee.
    /// </summary>
    [Fact]
    public void VacationPay_IsTheFinalPeriodOnly_NotTheWholeHistory()
    {
        CompanyData data = Data(Person());
        AddRuns(data, 3, vacation: 80m);

        Assert.Equal(80m, Built(data).VacationPay);
    }

    /// <summary>The separation payout is the one that lands in the final period.</summary>
    [Fact]
    public void VacationPay_TakesTheSeparationPayout()
    {
        CompanyData data = Data(Person());
        AddRuns(data, 3, vacation: 80m);

        // The most recent period end, which AddRuns lays out first.
        PayRun finalRun = data.PayRuns.OrderByDescending(r => r.PeriodEnd).First();
        finalRun.Lines[0].VacationPay = 2400m;

        Assert.Equal(2400m, Built(data).VacationPay);
    }

    #endregion

    #region The final pay period

    private static PayRun OneRun(string id, DateTime periodEnd, decimal gross, decimal hours) => new()
    {
        Id = id,
        PayDate = periodEnd.AddDays(5),
        PeriodStart = periodEnd.AddDays(-13),
        PeriodEnd = periodEnd,
        Status = PayRunStatus.Approved,
        Lines =
        {
            new PayRunLine
            {
                EmployeeId = "EMP-001",
                EmployeeName = "Dana Smith",
                Province = "AB",
                GrossPay = gross,
                HoursWorked = hours,
                NetPay = gross,
            },
        },
    };

    /// <summary>
    /// Someone who left without an end date is still ticked on the next pay run, and approving
    /// it at nil hours records a period that paid nothing. Taken as the final period, it moves
    /// blocks 11 and 12 past the last pay and shifts all of block 15C by one.
    /// </summary>
    [Fact]
    public void ALaterRunThatPaidNothing_IsNotTheFinalPayPeriod()
    {
        Employee person = Person(PayType.Hourly);
        person.EndDate = null;
        CompanyData data = Data(person);
        AddRuns(data, 5);
        data.PayRuns.Add(OneRun("PR-NIL", new DateTime(2026, 7, 24), gross: 0m, hours: 0m));

        RoeWorksheet sheet = Built(data);

        Assert.Equal(new DateTime(2026, 7, 10), sheet.FinalPeriodEnd);
        Assert.Equal(new DateTime(2026, 7, 10), sheet.LastDayPaid);
        Assert.Equal(new DateTime(2026, 7, 10), sheet.Periods[0].PeriodEnd);
        Assert.Equal(5, sheet.Periods.Count);
    }

    [Fact]
    public void AVoidedFinalRun_IsNotTheFinalPayPeriod()
    {
        Employee person = Person(PayType.Hourly);
        person.EndDate = null;
        CompanyData data = Data(person);
        AddRuns(data, 5);

        PayRun voided = OneRun("PR-LAST", new DateTime(2026, 7, 24), gross: 2000m, hours: 80m);
        voided.Status = PayRunStatus.Void;
        data.PayRuns.Add(voided);
        data.PayRuns.Add(OneRun("PR-LAST-R", new DateTime(2026, 7, 24), gross: -2000m, hours: -80m));

        RoeWorksheet sheet = Built(data);

        Assert.Equal(new DateTime(2026, 7, 10), sheet.FinalPeriodEnd);
        Assert.Equal(5, sheet.Periods.Count);
    }

    /// <summary>Only the nil periods after the last pay go. One in the middle is part of the history.</summary>
    [Fact]
    public void ANilPeriodBeforeTheLastPay_StaysInBlock15C()
    {
        CompanyData data = Data(Person(PayType.Hourly));
        AddRuns(data, 5);
        PayRunLine unpaid = data.PayRuns.Single(r => r.PeriodEnd == new DateTime(2026, 6, 26)).Lines[0];
        unpaid.GrossPay = 0m;
        unpaid.HoursWorked = 0m;

        RoeWorksheet sheet = Built(data);

        Assert.Equal(5, sheet.Periods.Count);
        Assert.Equal(0m, sheet.Periods[1].InsurableEarnings);
    }

    #endregion

    #region The contact

    [Fact]
    public void TheContact_KeepsEveryGivenName()
    {
        CompanyData data = Data(Person());
        data.Settings.Company.PayrollContactName = "Mary Ann Smith";
        AddRuns(data, 3);

        RoeWorksheet sheet = Built(data);

        Assert.Equal("Mary Ann", sheet.ContactFirstName);
        Assert.Equal("Smith", sheet.ContactLastName);
    }

    /// <summary>
    /// The T4 screen keeps the phone as typed. The ROE's phone box holds ten digits, and fed the
    /// typed text it counted brackets and dashes toward the ten and cut the number short.
    /// </summary>
    [Theory]
    [InlineData("(403) 555-1234")]
    [InlineData("1-403-555-1234")]
    [InlineData("403.555.1234")]
    public void TheContactPhone_ArrivesAsTheTenDigitNumber(string stored)
    {
        CompanyData data = Data(Person());
        data.Settings.Company.PayrollContactPhone = stored;
        AddRuns(data, 3);

        Assert.Equal("4035551234", Built(data).ContactPhone);
    }

    [Fact]
    public void ExportingTheContactAsSeeded_LeavesTheStoredOneAlone()
    {
        var company = new CompanyInfo { PayrollContactName = "Mary Ann Smith", PayrollContactPhone = "(403) 555-1234" };

        Assert.False(RoeService.ApplyContact(company, "Mary Ann", "Smith", "4035551234"));
        Assert.Equal("Mary Ann Smith", company.PayrollContactName);
        Assert.Equal("(403) 555-1234", company.PayrollContactPhone);
    }

    [Fact]
    public void AContactChangedOnTheRoe_IsKeptForTheNextOne()
    {
        var company = new CompanyInfo { PayrollContactName = "Mary Ann Smith", PayrollContactPhone = "(403) 555-1234" };

        Assert.True(RoeService.ApplyContact(company, "Pat", "Owner", "3065550000"));
        Assert.Equal("Pat Owner", company.PayrollContactName);
        Assert.Equal("3065550000", company.PayrollContactPhone);
    }

    #endregion

    #region Validation and rendering

    [Fact]
    public void AMissingSin_IsReported()
    {
        Employee person = Person();
        person.Sin = string.Empty;

        CompanyData data = Data(person);
        AddRuns(data, 20);

        Assert.Contains(RoeService.Validate(Built(data)),
            p => p.Contains("social insurance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TooFewPayPeriods_AreCalledOut()
    {
        CompanyData data = Data(Person());
        AddRuns(data, 3);

        Assert.Contains(RoeService.Validate(Built(data)),
            p => p.Contains("by hand", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AFullHistory_HasNothingToReport()
    {
        CompanyData data = Data(Person());
        AddRuns(data, 20);

        Assert.Empty(RoeService.Validate(Built(data)));
    }

    [Fact]
    public void TheWorksheet_Renders()
    {
        CompanyData data = Data(Person());
        AddRuns(data, 20);

        Assert.NotEmpty(RoePdfRenderer.Render(Built(data)));
    }

    [Fact]
    public void AnUnknownEmployee_Throws()
    {
        CompanyData data = Data(Person());
        Assert.Throws<ArgumentException>(() => new RoeService().Build(data, "NOPE"));
    }

    #endregion
}
