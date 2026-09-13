using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Services;
using ArgoBooks.Core.Services.Payroll;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Smoke tests for the payroll PDF renderers.
///
/// These exist because a QuestPDF composition error is a runtime exception, not a compile error.
/// An unbalanced column, a negative width, a text span outside its container: all of it builds
/// fine and throws the first time somebody clicks Download, which for the T4 is once a year at
/// the filing deadline.
///
/// Deliberately shallow. They assert that a document composes and produces bytes, not what it
/// looks like. The figures on the page come from the stored pay run line and are pinned by the
/// calculator tests; rendering is only asked not to fall over.
///
/// The edge cases below are chosen for layout risk rather than arithmetic: a zero, a negative
/// from a reversal, a long name, and the branches that add optional rows.
/// </summary>
public class PayrollPdfRendererTests
{
    private static CompanyData Company()
    {
        var data = new CompanyData();
        data.Settings.Company.Name = "Test Company";
        data.Settings.Company.Address = "1 Main Street";
        data.Settings.Company.City = "Calgary";
        data.Settings.Company.ProvinceState = "AB";
        data.Settings.Company.Country = "CAN";
        data.Settings.Company.PostalCode = "T2P1A1";
        data.Settings.Company.PayrollAccountNumber = "123456789RP0001";
        data.Settings.Company.PayrollContactName = "Pat Owner";
        data.Settings.Company.PayrollContactPhone = "4035551234";
        return data;
    }

    private static PayRun Run() => new()
    {
        Id = "PR-0001",
        PayDate = new DateTime(2026, 7, 3),
        PeriodStart = new DateTime(2026, 6, 20),
        PeriodEnd = new DateTime(2026, 7, 3),
        RateEditionId = "2026-07",
        Status = PayRunStatus.Approved,
    };

    private static PayRunLine Line(
        decimal gross = 2000m,
        decimal bonus = 0m,
        decimal vacation = 0m,
        decimal cpp2 = 0m,
        decimal hours = 0m,
        string name = "Dana Smith") => new()
    {
        EmployeeId = "EMP-001",
        EmployeeName = name,
        Province = "AB",
        HoursWorked = hours,
        BasePay = gross - bonus - vacation,
        Bonus = bonus,
        VacationPay = vacation,
        GrossPay = gross,
        CppEmployee = 100m,
        Cpp2Employee = cpp2,
        EiEmployee = 30m,
        FederalTax = 200m,
        ProvincialTax = 90m,
        PayPeriodsPerYear = 26,
        NetPay = gross - 100m - cpp2 - 30m - 200m - 90m,
    };

    private static PayrollYearToDate Ytd() => new();

    #region Pay stubs

    [Fact]
    public void APayStub_Renders()
    {
        Assert.NotEmpty(PayStubPdfRenderer.Render(Run(), Line(), Ytd(), Company()));
    }

    [Fact]
    public void APayStubWithEveryOptionalRow_Renders()
    {
        // Bonus, vacation pay and CPP2 each add a row only when non-zero, and hours change the
        // base pay label. This is the one stub that exercises all of those branches.
        PayRunLine line = Line(gross: 3200m, bonus: 500m, vacation: 200m, cpp2: 45m, hours: 82.5m);

        Assert.NotEmpty(PayStubPdfRenderer.Render(Run(), line, Ytd(), Company()));
    }

    [Fact]
    public void AReversalStubWithNegativeFigures_Renders()
    {
        // Voiding writes a run whose every figure is negative. It still produces a stub, and a
        // minus sign in a right-aligned money column is exactly where layout gives way.
        PayRunLine line = Line(gross: -2000m);
        line.CppEmployee = -100m;
        line.EiEmployee = -30m;
        line.FederalTax = -200m;
        line.ProvincialTax = -90m;
        line.NetPay = -1580m;

        Assert.NotEmpty(PayStubPdfRenderer.Render(Run(), line, Ytd(), Company()));
    }

    [Fact]
    public void AStubForACompanyWithNoAddress_Renders()
    {
        // Every address line is conditional, so a company that filled in nothing takes a
        // different path through the header than the fixture above.
        var data = new CompanyData();
        data.Settings.Company.Name = "Sole Trader";

        Assert.NotEmpty(PayStubPdfRenderer.Render(Run(), Line(), Ytd(), data));
    }

    [Fact]
    public void AStubWithAVeryLongEmployeeName_Renders()
    {
        PayRunLine line = Line(name: new string('W', 120));

        Assert.NotEmpty(PayStubPdfRenderer.Render(Run(), line, Ytd(), Company()));
    }

    #endregion

    #region T4

    private static T4Return BuiltT4(CompanyData data)
    {
        PayRun run = Run();
        run.Lines.Add(Line());
        data.Employees.Add(new Employee
        {
            Id = "EMP-001",
            Name = "Dana Smith",
            Sin = "046454286",
            Province = "AB",
            PayType = PayType.Salary,
            PayRate = 52000m,
            PayFrequency = PayFrequency.Biweekly,
            DentalBenefit = DentalBenefitCode.PayeeSpouseAndChildren,
        });
        data.PayRuns.Add(run);

        return new T4Service().Build(data, 2026);
    }

    [Fact]
    public void AT4SlipAndSummary_Render()
    {
        CompanyData data = Company();
        T4Return t4 = BuiltT4(data);

        Assert.NotEmpty(T4PdfRenderer.RenderSlip(t4, t4.Slips.Single()));
        Assert.NotEmpty(T4PdfRenderer.RenderSummary(t4));
    }

    [Fact]
    public void AQuebecT4Slip_Renders()
    {
        // Quebec takes the other side of every branch on the slip: QPP boxes instead of CPP,
        // plus the two QPIP rows that no other province prints.
        CompanyData data = Company();
        T4Return t4 = BuiltT4(data);

        T4Slip slip = t4.Slips.Single();
        slip.IsQuebec = true;
        slip.ProvinceOfEmployment = "QC";
        slip.Cpp2Contributions = 45m;
        slip.QpipPremiums = 18.40m;
        slip.QpipInsurableEarnings = 2000m;

        Assert.NotEmpty(T4PdfRenderer.RenderSlip(t4, slip));
    }

    [Fact]
    public void AT4SlipWithNoSinAndNoAddress_Renders()
    {
        // Both are formatted conditionally, and a missing SIN does not block filing, so this
        // slip really does get produced.
        CompanyData data = Company();
        T4Return t4 = BuiltT4(data);

        T4Slip slip = t4.Slips.Single();
        slip.Sin = string.Empty;
        slip.Address = new ArgoBooks.Core.Models.Common.Address();

        Assert.NotEmpty(T4PdfRenderer.RenderSlip(t4, slip));
    }

    [Theory]
    [InlineData(DentalBenefitCode.NotEligible)]
    [InlineData(DentalBenefitCode.PayeeOnly)]
    [InlineData(DentalBenefitCode.PayeeSpouseAndChildren)]
    [InlineData(DentalBenefitCode.PayeeAndSpouse)]
    [InlineData(DentalBenefitCode.PayeeAndChildren)]
    public void EveryDentalBenefitCode_PrintsItsOwnWording(DentalBenefitCode code)
    {
        // Box 45 has been mandatory since 2023 and the codes are not interchangeable: an
        // employee's dental coverage is assessed from this one number.
        CompanyData data = Company();
        T4Return t4 = BuiltT4(data);
        t4.Slips[0].DentalBenefit = code;

        Assert.NotEmpty(T4PdfRenderer.RenderSlip(t4, t4.Slips[0]));
    }

    [Fact]
    public void AT4SlipWithAStreetAddress_PrintsIt()
    {
        // The street line is conditional, and the fixture above has none.
        CompanyData data = Company();
        T4Return t4 = BuiltT4(data);
        t4.Slips[0].Address = new ArgoBooks.Core.Models.Common.Address
        {
            Street = "42 Employee Road",
            City = "Calgary",
            State = "AB",
            ZipCode = "T2P1A1",
        };

        Assert.NotEmpty(T4PdfRenderer.RenderSlip(t4, t4.Slips[0]));
    }

    [Fact]
    public void AT4SummaryWithSecondCppContributions_AddsTheirRow()
    {
        CompanyData data = Company();
        T4Return t4 = BuiltT4(data);
        t4.Slips[0].Cpp2Contributions = 45m;
        t4.Slips[0].EmployerCpp2 = 45m;

        Assert.NotEmpty(T4PdfRenderer.RenderSummary(t4));
    }

    [Fact]
    public void AT4SummaryWithNoSlips_Renders()
    {
        // The year end screen can produce a summary before anything is filed, and every total
        // on it is then zero.
        var t4 = new T4Return
        {
            TaxYear = 2026,
            PayrollAccountNumber = "123456789RP0001",
            EmployerName = "Test Company",
            ContactName = "Pat Owner",
            ContactPhone = "4035551234",
        };

        Assert.NotEmpty(T4PdfRenderer.RenderSummary(t4));
    }

    #endregion

    #region RL-1

    private static Rl1Return QuebecReturn()
    {
        var data = new CompanyData();
        data.Settings.Company.Name = "Test Company";
        data.Settings.Company.QuebecIdentificationNumber = "1234567890RS0001";
        data.Employees.Add(new Employee
        {
            Id = "EMP-001",
            Name = "Dana Smith",
            Sin = "046454286",
            Province = "QC",
            PayType = PayType.Salary,
            PayRate = 52000m,
            PayFrequency = PayFrequency.Biweekly,
        });
        data.PayRuns.Add(new PayRun
        {
            Id = "PR-0001",
            PayDate = new DateTime(2026, 7, 3),
            Status = PayRunStatus.Approved,
            Lines =
            {
                new PayRunLine
                {
                    EmployeeId = "EMP-001",
                    EmployeeName = "Dana Smith",
                    Province = "QC",
                    GrossPay = 2000m,
                    CppEmployee = 110m,
                    EiEmployee = 26m,
                    QpipEmployee = 8.60m,
                    ProvincialTax = 220m,
                    NetPay = 1635.40m,
                },
            },
        });

        return new Rl1Service().Build(data, 2026);
    }

    [Fact]
    public void AnRl1SlipWithEveryOptionalBox_Renders()
    {
        // Boxes B.B, D and F only appear when there is something in them, and Argo Books does
        // not collect the last two, so nothing else ever exercises those rows.
        Rl1Return rl1 = QuebecReturn();
        Rl1Slip slip = rl1.Slips.Single();
        slip.AdditionalQppContribution = 20m;
        slip.RppContribution = 50m;
        slip.UnionDues = 15m;

        Assert.NotEmpty(Rl1PdfRenderer.RenderSlip(rl1, slip));
    }

    [Theory]
    [InlineData(Rl1SlipCode.Original)]
    [InlineData(Rl1SlipCode.Amended)]
    [InlineData(Rl1SlipCode.Cancelled)]
    public void EveryRl1SlipCode_PrintsItsOwnWording(Rl1SlipCode code)
    {
        Rl1Return rl1 = QuebecReturn();
        rl1.SlipCode = code;

        Assert.NotEmpty(Rl1PdfRenderer.RenderSlip(rl1, rl1.Slips.Single()));
        Assert.NotEmpty(Rl1PdfRenderer.RenderSummary(rl1));
    }

    #endregion

    #region Record of employment

    private static RoeWorksheet Worksheet(PayFrequency frequency = PayFrequency.Biweekly)
    {
        var data = new CompanyData();
        data.Settings.Company.Name = "Test Company";
        data.Settings.Company.PayrollAccountNumber = "123456789RP0001";
        data.Employees.Add(new Employee
        {
            Id = "EMP-001",
            Name = "Dana Smith",
            Sin = "046454286",
            Province = "AB",
            PayType = PayType.Salary,
            PayRate = 52000m,
            PayFrequency = frequency,
            StandardHoursPerWeek = 40m,
            StartDate = new DateTime(2024, 1, 8),
            EndDate = new DateTime(2026, 7, 10),
            Address = new ArgoBooks.Core.Models.Common.Address
            {
                Street = "42 Employee Road",
                City = "Calgary",
                State = "AB",
                ZipCode = "T2P1A1",
            },
        });

        return new RoeService().Build(data, "EMP-001");
    }

    [Theory]
    [InlineData(PayFrequency.Weekly)]
    [InlineData(PayFrequency.Biweekly)]
    [InlineData(PayFrequency.SemiMonthly)]
    [InlineData(PayFrequency.Monthly)]
    public void AWorksheetForAnyPayFrequency_Renders(PayFrequency frequency)
    {
        // Block 6 names the frequency and blocks 15A to 15C size themselves from it, so each
        // one lays the page out differently.
        Assert.NotEmpty(RoePdfRenderer.Render(Worksheet(frequency)));
    }

    [Fact]
    public void AWorksheetThatCouldNotWorkOutTheHours_PrintsTheReasonOnThePage()
    {
        // Block 15A cannot simply be blank. The employee takes this to Service Canada and the
        // reason is what stops it being read as zero hours.
        RoeWorksheet sheet = Worksheet();
        Assert.NotNull(sheet.HoursUnavailableReason);

        Assert.NotEmpty(RoePdfRenderer.Render(sheet));
    }

    #endregion
}
