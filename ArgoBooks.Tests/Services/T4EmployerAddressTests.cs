using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Services.Payroll;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The employer's own address is validated, not just the employees'.
///
/// BuildSummary writes the company's province, postal code and country into the same coded
/// fields it writes an employee's, and CRA rejects a two-letter code that is not a province.
/// </summary>
public class T4EmployerAddressTests
{
    private const string Bn = "123456789RP0001";

    private static CompanyData Data()
    {
        var data = new CompanyData();
        data.Settings.Company.Name = "Test Company";
        data.Settings.Company.Address = "1 Main Street";
        data.Settings.Company.City = "Calgary";
        data.Settings.Company.ProvinceState = "AB";
        data.Settings.Company.Country = "CAN";
        data.Settings.Company.PostalCode = "T2P1A1";
        data.Settings.Company.PayrollAccountNumber = Bn;
        data.Settings.Company.PayrollContactName = "Pat Owner";
        data.Settings.Company.PayrollContactPhone = "4035551234";
        data.Settings.Company.PayrollContactEmail = "pat@example.com";

        data.Employees.Add(new Employee
        {
            Id = "EMP-001",
            Name = "Dana Smith",
            Sin = "046454286",
            Province = "AB",
            PayType = PayType.Salary,
            PayRate = 52000m,
            PayFrequency = PayFrequency.Biweekly,
            DentalBenefit = DentalBenefitCode.PayeeOnly,
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
                    Province = "AB",
                    GrossPay = 2000m,
                    CppEmployee = 100m,
                    CppEmployer = 100m,
                    EiEmployee = 30m,
                    EiEmployer = 42m,
                    FederalTax = 200m,
                    ProvincialTax = 90m,
                    NetPay = 1580m,
                },
            },
        });

        return data;
    }

    private static List<string> Problems(CompanyData data) =>
        T4Service.Validate(data, new T4Service().Build(data, 2026));

    [Fact]
    public void AWellFormedCompanyAddress_RaisesNothing()
    {
        Assert.Empty(Problems(Data()));
    }

    [Theory]
    [InlineData("Alberta")]
    [InlineData("AL")]
    [InlineData("QU")]
    public void ACompanyProvinceThatIsNotACode_IsRefused(string province)
    {
        CompanyData data = Data();
        data.Settings.Company.ProvinceState = province;

        Assert.Contains(Problems(data), p => p.Contains("company province", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A foreign employer's state is not a Canadian province and must not be judged as one.
    /// </summary>
    [Fact]
    public void AForeignCompanysState_IsLeftAlone()
    {
        CompanyData data = Data();
        data.Settings.Company.Country = "USA";
        data.Settings.Company.ProvinceState = "CA";
        data.Settings.Company.PostalCode = "94105";

        Assert.DoesNotContain(Problems(data), p => p.Contains("company province", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("T2P")]
    [InlineData("123456")]
    [InlineData("NOTAPOSTCODE")]
    public void ACompanyPostalCodeCraWillNotTake_IsRefused(string postalCode)
    {
        CompanyData data = Data();
        data.Settings.Company.PostalCode = postalCode;

        Assert.Contains(Problems(data), p => p.Contains("postal code", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ACompanyAddressWithACharacterCraRejects_IsRefused()
    {
        CompanyData data = Data();
        data.Settings.Company.Address = "1 Main Street, Suite #4";

        Assert.Contains(Problems(data), p => p.Contains("company address", StringComparison.OrdinalIgnoreCase));
    }
}
