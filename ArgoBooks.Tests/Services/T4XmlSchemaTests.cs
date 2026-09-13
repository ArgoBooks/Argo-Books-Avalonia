using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Services.Payroll;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Validates the generated T4 submission against CRA's own published XSD package.
///
/// T4Tests covers the rules we know about. This covers the ones we do not: the schema is the
/// authority on element names, ordering and cardinality, and a mismatch in any of them rejects
/// the whole submission at the filing deadline rather than failing anywhere useful.
///
/// The schemas are committed under Schemas/Cra so this runs offline. See the README there for
/// the January refresh.
/// </summary>
public class T4XmlSchemaTests
{
    /// <summary>
    /// The edition of the CRA package in Schemas/Cra, from the Version# header in the files.
    ///
    /// Checked against the version named in T4XmlWriter, so refreshing the schemas without
    /// re-reading the specification, or the reverse, fails here instead of in February.
    /// </summary>
    private const string SchemaVersion = "1.26";

    private const string Bn = "123456789RP0001";

    private static string SchemaDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Schemas", "Cra");

    private static XmlSchemaSet Schemas()
    {
        var set = new XmlSchemaSet();

        // T619_T4.xsd includes the other three and imports standarddatatypes.xsd, all by
        // relative path, so resolving against the directory is what wires the package together.
        set.XmlResolver = new XmlUrlResolver();
        set.Add(null, Path.Combine(SchemaDirectory, "T619_T4.xsd"));
        set.Compile();
        return set;
    }

    private static CompanyData Company()
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
        return data;
    }

    private static Employee Person(string id, string name, string sin, string province) => new()
    {
        Id = id,
        Name = name,
        Sin = sin,
        Province = province,
        PayType = PayType.Salary,
        PayRate = 52000m,
        PayFrequency = PayFrequency.Biweekly,
        DentalBenefit = DentalBenefitCode.PayeeOnly,
    };

    private static PayRun Run(string id, DateTime payDate, string employeeId, string name, string province) => new()
    {
        Id = id,
        PayDate = payDate,
        Status = PayRunStatus.Approved,
        Lines =
        {
            new PayRunLine
            {
                EmployeeId = employeeId,
                EmployeeName = name,
                Province = province,
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
    };

    private static List<string> Validate(XDocument document)
    {
        var errors = new List<string>();
        document.Validate(Schemas(), (_, e) => errors.Add($"{e.Severity}: {e.Message}"));
        return errors;
    }

    [Fact]
    public void ASubmission_ValidatesAgainstTheCraSchema()
    {
        CompanyData data = Company();
        data.Employees.Add(Person("EMP-001", "Dana Smith", "046454286", "AB"));
        data.PayRuns.Add(Run("PR-0001", new DateTime(2026, 7, 3), "EMP-001", "Dana Smith", "AB"));

        XDocument xml = T4XmlWriter.Build(new T4Service().Build(data, 2026));

        Assert.Empty(Validate(xml));
    }

    [Fact]
    public void AQuebecSlip_ValidatesAgainstTheCraSchema()
    {
        // Quebec takes a different branch through the amounts block: QPP replaces CPP and the
        // QPIP elements appear. Both change which elements are present and in what order.
        CompanyData data = Company();
        data.Employees.Add(Person("EMP-002", "Jean Tremblay", "046454286", "QC"));
        data.PayRuns.Add(Run("PR-0002", new DateTime(2026, 7, 3), "EMP-002", "Jean Tremblay", "QC"));

        XDocument xml = T4XmlWriter.Build(new T4Service().Build(data, 2026));

        Assert.Empty(Validate(xml));
    }

    [Fact]
    public void SeveralSlips_ValidateAgainstTheCraSchema()
    {
        CompanyData data = Company();
        data.Employees.Add(Person("EMP-001", "Dana Smith", "046454286", "AB"));
        data.Employees.Add(Person("EMP-002", "Jean Tremblay", "046454286", "QC"));
        data.PayRuns.Add(Run("PR-0001", new DateTime(2026, 7, 3), "EMP-001", "Dana Smith", "AB"));
        data.PayRuns.Add(Run("PR-0002", new DateTime(2026, 7, 3), "EMP-002", "Jean Tremblay", "QC"));

        XDocument xml = T4XmlWriter.Build(new T4Service().Build(data, 2026));

        Assert.Empty(Validate(xml));
    }

    [Fact]
    public void EveryElement_CarriesAValue()
    {
        // CRA added this in October 2025: an optional element present but empty rejects the
        // whole submission. The schema does not catch it, because an empty element is still
        // structurally valid, so it needs asserting separately.
        CompanyData data = Company();
        data.Employees.Add(Person("EMP-001", "Dana Smith", "046454286", "AB"));
        data.PayRuns.Add(Run("PR-0001", new DateTime(2026, 7, 3), "EMP-001", "Dana Smith", "AB"));

        XDocument xml = T4XmlWriter.Build(new T4Service().Build(data, 2026));

        string[] empty = xml.Descendants()
            .Where(e => !e.HasElements && string.IsNullOrEmpty(e.Value))
            .Select(e => e.Name.LocalName)
            .ToArray();

        Assert.Empty(empty);
    }

    [Fact]
    public void TheVendoredSchemas_AreTheEditionTheWriterWasBuiltFor()
    {
        // Guards the pairing rather than either half: the schema files and the specification
        // the writer follows are revised together every January, and updating one without the
        // other is silent until a submission is rejected.
        string header = File.ReadAllText(Path.Combine(SchemaDirectory, "T619_T4.xsd"));

        Assert.Contains($"Version#:\t{SchemaVersion}", header);
    }
}
