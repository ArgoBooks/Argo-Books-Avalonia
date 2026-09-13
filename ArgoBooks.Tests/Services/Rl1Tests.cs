using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Services.Payroll;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for the RL-1 return.
///
/// The rules worth guarding are the ones where being wrong is invisible. An RL-1 that carries
/// the combined federal and Quebec tax in box E, or that quietly includes an Ontario employee,
/// looks entirely plausible on the page: nothing downstream would object, and the employee
/// would claim a Quebec credit for money Quebec never received. Those are the cases pinned
/// here, along with Revenu Quebec's paper filing threshold.
/// </summary>
public class Rl1Tests
{
    private const string Nq = "1234567890RS0001";

    private static CompanyData Data(params Employee[] employees)
    {
        var data = new CompanyData();
        data.Settings.Company.Name = "Test Company";
        data.Settings.Company.Address = "1 Rue Principale";
        data.Settings.Company.City = "Montreal";
        data.Settings.Company.ProvinceState = "QC";
        data.Settings.Company.Country = "CAN";
        data.Settings.Company.PostalCode = "H3B1A1";
        data.Settings.Company.QuebecIdentificationNumber = Nq;
        data.Employees.AddRange(employees);
        return data;
    }

    private static Employee Person(string id = "EMP-001", string name = "Dana Smith",
                                   string sin = "046454286", string province = "QC") => new()
    {
        Id = id,
        Name = name,
        Sin = sin,
        Province = province,
        PayType = PayType.Salary,
        PayRate = 52000m,
        PayFrequency = PayFrequency.Biweekly,
        Address = new Core.Models.Common.Address
        {
            Street = "10 Rue Sainte-Catherine",
            City = "Montreal",
            State = "QC",
            ZipCode = "H2X1K4",
        },
    };

    private static PayRun Run(string id, DateTime payDate, string employeeId, decimal gross,
                              decimal qpp = 100m, decimal qpp2 = 0m, decimal ei = 25m,
                              decimal fed = 180m, decimal prov = 140m, decimal qpip = 9m,
                              string province = "QC") => new()
    {
        Id = id,
        PayDate = payDate,
        Status = PayRunStatus.Approved,
        Lines =
        {
            new PayRunLine
            {
                EmployeeId = employeeId,
                EmployeeName = "Dana Smith",
                Province = province,
                GrossPay = gross,
                CppEmployee = qpp,
                CppEmployer = qpp,
                Cpp2Employee = qpp2,
                Cpp2Employer = qpp2,
                EiEmployee = ei,
                EiEmployer = Math.Round(ei * 1.4m, 2),
                FederalTax = fed,
                ProvincialTax = prov,
                QpipEmployee = qpip,
                QpipEmployer = Math.Round(qpip * 1.4m, 2),
                NetPay = gross - qpp - qpp2 - ei - fed - prov - qpip,
            },
        },
    };

    private static Rl1Return Built(CompanyData data, int year = 2026) => new Rl1Service().Build(data, year);

    #region Who is on the return

    [Fact]
    public void OnlyQuebecEmployees_GetASlip()
    {
        CompanyData data = Data(
            Person("EMP-001", "Dana Smith"),
            Person("EMP-002", "Alex Jones", "046454286", province: "ON"));

        data.PayRuns.Add(Run("PR-0001", new DateTime(2026, 7, 3), "EMP-001", 2000m));
        data.PayRuns.Add(Run("PR-0002", new DateTime(2026, 7, 3), "EMP-002", 2000m, province: "ON"));

        Rl1Return rl1 = Built(data);

        Assert.Single(rl1.Slips);
        Assert.Equal("EMP-001", rl1.Slips[0].EmployeeId);
    }

    [Fact]
    public void AnEmployerWithNoQuebecStaff_HasNothingToFile()
    {
        CompanyData data = Data(Person("EMP-001", "Dana Smith", province: "AB"));
        data.PayRuns.Add(Run("PR-0001", new DateTime(2026, 7, 3), "EMP-001", 2000m, province: "AB"));

        Assert.False(Rl1Service.HasQuebecEmployees(data, 2026));
        Assert.Empty(Built(data).Slips);
    }

    [Fact]
    public void AnEmployeeWhoMovedIntoQuebec_ReportsOnlyTheirQuebecPay()
    {
        // Their Ontario tax went to CRA. Putting it in box E has them claim a Quebec credit for
        // money Revenu Quebec never received.
        CompanyData data = Data(Person());
        data.PayRuns.Add(Run("PR-0001", new DateTime(2026, 3, 6), "EMP-001", 2000m,
            prov: 90m, qpip: 0m, province: "ON"));
        data.PayRuns.Add(Run("PR-0002", new DateTime(2026, 9, 4), "EMP-001", 3000m,
            prov: 140m, province: "QC"));

        Rl1Slip slip = Built(data).Slips.Single();

        Assert.Equal(3000m, slip.EmploymentIncome);
        Assert.Equal(140m, slip.QuebecIncomeTax);
    }

    [Fact]
    public void AnEmployeeWhoLeftQuebec_StillGetsAnRl1ForTheirQuebecPay()
    {
        CompanyData data = Data(Person(province: "ON"));
        data.PayRuns.Add(Run("PR-0001", new DateTime(2026, 3, 6), "EMP-001", 2000m,
            prov: 140m, province: "QC"));
        data.PayRuns.Add(Run("PR-0002", new DateTime(2026, 9, 4), "EMP-001", 3000m,
            prov: 90m, qpip: 0m, province: "ON"));

        Assert.True(Rl1Service.HasQuebecEmployees(data, 2026));

        Rl1Slip slip = Built(data).Slips.Single();
        Assert.Equal(2000m, slip.EmploymentIncome);
        Assert.Equal(140m, slip.QuebecIncomeTax);
    }

    [Fact]
    public void DraftRuns_AreExcluded()
    {
        CompanyData data = Data(Person());

        PayRun draft = Run("PR-0002", new DateTime(2026, 7, 17), "EMP-001", 2000m);
        draft.Status = PayRunStatus.Draft;

        data.PayRuns.Add(Run("PR-0001", new DateTime(2026, 7, 3), "EMP-001", 2000m));
        data.PayRuns.Add(draft);

        Assert.Equal(2000m, Built(data).Slips[0].EmploymentIncome);
    }

    [Fact]
    public void AVoidedRunAndItsReversal_CancelToNothing()
    {
        CompanyData data = Data(Person());

        PayRun voided = Run("PR-0001", new DateTime(2026, 7, 3), "EMP-001", 2000m);
        voided.Status = PayRunStatus.Void;

        PayRun reversal = Run("PR-0001-R", new DateTime(2026, 7, 3), "EMP-001", -2000m,
            qpp: -100m, ei: -25m, fed: -180m, prov: -140m, qpip: -9m);

        data.PayRuns.Add(voided);
        data.PayRuns.Add(reversal);

        // No slip at all rather than a slip full of zeroes: there is nothing to report, and a
        // nil slip would have Revenu Quebec expecting a person who earned nothing.
        Assert.Empty(Built(data).Slips);
    }

    #endregion

    #region The boxes

    [Fact]
    public void BoxE_CarriesTheQuebecTaxAlone_NotTheCombinedWithholding()
    {
        CompanyData data = Data(Person());
        data.PayRuns.Add(Run("PR-0001", new DateTime(2026, 7, 3), "EMP-001", 2000m, fed: 180m, prov: 140m));

        // The federal 180 was withheld in the same run but goes to CRA on the T4. Putting 320
        // here would credit the employee with Quebec tax that Quebec never received.
        Assert.Equal(140m, Built(data).Slips[0].QuebecIncomeTax);
    }

    [Fact]
    public void PensionContributions_LandInBoxesBAAndBB()
    {
        CompanyData data = Data(Person());
        data.PayRuns.Add(Run("PR-0001", new DateTime(2026, 7, 3), "EMP-001", 2000m, qpp: 118m, qpp2: 12m));

        Rl1Slip slip = Built(data).Slips[0];

        Assert.Equal(118m, slip.QppContribution);
        Assert.Equal(12m, slip.AdditionalQppContribution);
    }

    [Fact]
    public void BoxesGAndI_MirrorGross_ForAnEmployeeWhoIsNotExempt()
    {
        CompanyData data = Data(Person());
        data.PayRuns.Add(Run("PR-0001", new DateTime(2026, 7, 3), "EMP-001", 2000m));

        Rl1Slip slip = Built(data).Slips[0];

        Assert.Equal(2000m, slip.QppPensionableSalary);
        Assert.Equal(2000m, slip.QpipEligibleSalary);
    }

    [Fact]
    public void AnExemptEmployee_ReportsNilRatherThanGross()
    {
        Employee person = Person();
        person.IsCppExempt = true;
        person.IsEiExempt = true;

        CompanyData data = Data(person);
        data.PayRuns.Add(Run("PR-0001", new DateTime(2026, 7, 3), "EMP-001", 2000m, qpp: 0m, ei: 0m, qpip: 0m));

        Rl1Slip slip = Built(data).Slips[0];

        Assert.Equal(0m, slip.QppPensionableSalary);
        Assert.Equal(0m, slip.QpipEligibleSalary);
    }

    [Fact]
    public void BoxG_IsCappedAtTheAdditionalMaximum_WhenThereIsAQpp2Contribution()
    {
        // RL-1.G-V section 5.9 gives box G two maximums: the maximum pensionable earnings "if an
        // amount is entered in box B.A only", and the ADDITIONAL maximum "if amounts are entered
        // in boxes B.A and B.B". Anyone paid above the first ceiling has QPP2 withheld and so
        // has an amount in box B.B, which selects the second.
        //
        // Capping at the first ceiling reported less pensionable salary than the QPP2 in box B.B
        // was charged on, which is the pair Revenu Quebec checks against each other.
        PayrollRateTable rates = new Core.Services.PayrollRateService().GetForDate(new DateTime(2026, 12, 31))!;
        decimal betweenTheCeilings = (rates.Quebec!.Qpp.YmpeCeiling + rates.Quebec.Qpp2.YampeCeiling) / 2m;

        CompanyData data = Data(Person());
        data.PayRuns.Add(Run("PR-0001", new DateTime(2026, 7, 3), "EMP-001", betweenTheCeilings, qpp2: 200m));

        Rl1Slip slip = Built(data).Slips[0];

        Assert.True(betweenTheCeilings > rates.Quebec.Qpp.YmpeCeiling, "the fixture must straddle the first ceiling");
        Assert.Equal(betweenTheCeilings, slip.QppPensionableSalary);
    }

    [Fact]
    public void BoxI_IsNotNil_WhenAQpipPremiumWasWithheld()
    {
        // Box I was gated on the EI exemption, and the two exemptions are not the same one. EI
        // exemption is the owner holding more than 40% of the voting shares; QPIP has its own
        // rules and its own base, and the calculator withholds it from a Quebec employee either
        // way. That produced a slip with a premium in box H and a nil box I, which is the one
        // pair that cannot be right: RL-1.G-V takes "0" in box I to mean there was no eligible
        // salary at all.
        Employee person = Person();
        person.IsEiExempt = true;

        CompanyData data = Data(person);
        data.PayRuns.Add(Run("PR-0001", new DateTime(2026, 7, 3), "EMP-001", 2000m, ei: 0m, qpip: 8.60m));

        Rl1Slip slip = Built(data).Slips[0];

        Assert.Equal(8.60m, slip.QpipPremium);
        Assert.Equal(2000m, slip.QpipEligibleSalary);
    }

    [Fact]
    public void BoxesGAndI_AreCappedAtTheYearsCeilings_ForAHighEarner()
    {
        CompanyData data = Data(Person());

        // Well above both ceilings. A slip reporting the whole $200,000 would look entirely
        // reasonable and would have Revenu Quebec expect contributions on money that was never
        // pensionable or eligible.
        data.PayRuns.Add(Run("PR-0001", new DateTime(2026, 7, 3), "EMP-001", 200_000m));

        Rl1Slip slip = Built(data).Slips[0];

        Assert.Equal(200_000m, slip.EmploymentIncome);
        Assert.True(slip.QppPensionableSalary < 200_000m, "box G was not capped");
        Assert.Equal(103_000m, slip.QpipEligibleSalary);
    }

    [Fact]
    public void BoxesDAndF_AreNil_BecauseTheAppDoesNotCollectThem()
    {
        CompanyData data = Data(Person());
        data.PayRuns.Add(Run("PR-0001", new DateTime(2026, 7, 3), "EMP-001", 2000m));

        Rl1Slip slip = Built(data).Slips[0];

        Assert.Equal(0m, slip.RppContribution);
        Assert.Equal(0m, slip.UnionDues);
    }

    [Fact]
    public void QpipEmployer_IsCarriedForTheSummary_EvenThoughItIsNotOnTheSlip()
    {
        CompanyData data = Data(Person());
        data.PayRuns.Add(Run("PR-0001", new DateTime(2026, 7, 3), "EMP-001", 2000m, qpip: 9m));

        Rl1Return rl1 = Built(data);

        Assert.Equal(9m, rl1.TotalQpip);
        Assert.Equal(12.60m, rl1.TotalEmployerQpip);
    }

    [Fact]
    public void TheSummaryTotal_ExcludesEmploymentInsurance()
    {
        CompanyData data = Data(Person());
        data.PayRuns.Add(Run("PR-0001", new DateTime(2026, 7, 3), "EMP-001", 2000m,
            qpp: 100m, ei: 25m, prov: 140m, qpip: 9m));

        Rl1Return rl1 = Built(data);

        // EI is federal and is remitted to CRA. 100 + 100 employer + 9 + 12.60 employer + 140.
        Assert.Equal(361.60m, rl1.TotalRemittable);
    }

    #endregion

    #region Names

    [Fact]
    public void AMiddleName_StaysInTheGivenName_UnlikeTheT4WhichTakesAnInitial()
    {
        CompanyData data = Data(Person("EMP-001", "Marie Claire Tremblay"));
        data.PayRuns.Add(Run("PR-0001", new DateTime(2026, 7, 3), "EMP-001", 2000m));

        Rl1Slip slip = Built(data).Slips[0];

        Assert.Equal("Tremblay", slip.Surname);
        Assert.Equal("Marie Claire", slip.GivenName);
    }

    #endregion

    #region Validation

    [Fact]
    public void AMissingIdentificationNumber_BlocksFiling()
    {
        CompanyData data = Data(Person());
        data.Settings.Company.QuebecIdentificationNumber = null;
        data.PayRuns.Add(Run("PR-0001", new DateTime(2026, 7, 3), "EMP-001", 2000m));

        Assert.Contains(Rl1Service.Validate(data, Built(data)),
            p => p.Contains("identification number", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("1234567890RS0001", true)]
    [InlineData("1234567890RR0001", true)]
    [InlineData("123456789RP0001", false)]   // the CRA number, which is a different thing
    [InlineData("1234567890RS001", false)]
    [InlineData("", false)]
    public void TheIdentificationNumber_IsTenDigitsThenTwoLettersThenFour(string value, bool expected) =>
        Assert.Equal(expected, Rl1Service.IsQuebecIdentificationNumber(value));

    /// <summary>
    /// The slip count is not what decides whether these PDFs can be filed, and it used to be
    /// treated as though it were: six or more raised a blocking problem, which left four
    /// implying that mailing them would work.
    ///
    /// It never would. Revenu Quebec accepts a paper slip printed by software only when it
    /// carries an authorization number, which it issues to a developer per taxation year after
    /// certifying the software, plus a two-dimensional barcode on copy 1. Argo Books has neither,
    /// and "the RL slip does not have an authorization number" is the first entry on Revenu
    /// Quebec's own list of the most common reasons a slip is rejected.
    ///
    /// So the count raises nothing either way, and the notice is permanent.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(6)]
    public void TheSlipCount_DoesNotDecideAnything(int count)
    {
        var people = Enumerable.Range(1, count)
            .Select(i => Person($"EMP-{i:000}", $"Dana Smith{i}"))
            .ToArray();

        CompanyData data = Data(people);

        foreach (Employee person in people)
        {
            data.PayRuns.Add(Run($"PR-{person.Id}", new DateTime(2026, 7, 3), person.Id, 2000m));
        }

        Assert.Empty(Rl1Service.Validate(data, Built(data)));
    }

    /// <summary>The notice has to name the reason, or it reads as a disclaimer to skip past.</summary>
    [Fact]
    public void TheFilingNotice_SaysTheseAreNotFilableSlips()
    {
        Assert.Contains("authorization number", Rl1Service.FilingNotice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("worksheet", Rl1Service.FilingNotice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AMissingSin_BlocksFiling()
    {
        CompanyData data = Data(Person(sin: string.Empty));
        data.PayRuns.Add(Run("PR-0001", new DateTime(2026, 7, 3), "EMP-001", 2000m));

        Assert.Contains(Rl1Service.Validate(data, Built(data)),
            p => p.Contains("social insurance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AMissingAddress_BlocksFiling()
    {
        Employee person = Person();
        person.Address = new Core.Models.Common.Address();

        CompanyData data = Data(person);
        data.PayRuns.Add(Run("PR-0001", new DateTime(2026, 7, 3), "EMP-001", 2000m));

        Assert.Contains(Rl1Service.Validate(data, Built(data)),
            p => p.Contains("address", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ADraftRun_BlocksFiling()
    {
        CompanyData data = Data(Person());
        data.PayRuns.Add(Run("PR-0001", new DateTime(2026, 7, 3), "EMP-001", 2000m));

        PayRun draft = Run("PR-0002", new DateTime(2026, 7, 17), "EMP-001", 2000m);
        draft.Status = PayRunStatus.Draft;
        data.PayRuns.Add(draft);

        Assert.Contains(Rl1Service.Validate(data, Built(data)),
            p => p.Contains("draft", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region PDFs

    [Fact]
    public void TheSlipAndSummary_Render()
    {
        CompanyData data = Data(Person());
        data.PayRuns.Add(Run("PR-0001", new DateTime(2026, 7, 3), "EMP-001", 2000m));

        Rl1Return rl1 = Built(data);

        Assert.NotEmpty(Rl1PdfRenderer.RenderSlip(rl1, rl1.Slips[0]));
        Assert.NotEmpty(Rl1PdfRenderer.RenderSummary(rl1));
    }

    #endregion
}
