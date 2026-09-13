using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Employees and pay runs survive a save and a reopen.
///
/// A round trip is the only shape of test that catches an absent
/// write, because every in-memory assertion passes without it.
/// </summary>
public class PayrollPersistenceTests : IDisposable
{
    private readonly string _file = Path.Combine(
        Path.GetTempPath(), $"argo-payroll-{Guid.NewGuid():N}.argo");

    private readonly List<string> _temps = [];

    private static FileService Service() =>
        new(new CompressionService(), new FooterService(), new EncryptionService());

    private static Employee Person(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Province = "AB",
        PayType = PayType.Salary,
        PayRate = 62_400m,
        PayFrequency = PayFrequency.Biweekly,
        Sin = "046454286",
        StandardHoursPerWeek = 37.5m,
    };

    private async Task<string> Open(FileService service)
    {
        string temp = await service.OpenCompanyAsync(_file);
        _temps.Add(temp);
        return temp;
    }

    [Fact]
    public async Task EmployeesAndPayRuns_SurviveASaveAndReopen()
    {
        FileService service = Service();
        await service.CreateCompanyAsync(_file, "Payroll Co");

        string temp = await Open(service);
        CompanyData data = await service.LoadCompanyDataAsync(temp);

        data.Employees.Add(Person("EMP-001", "Dana Smith"));
        data.Employees.Add(Person("EMP-002", "Chris Okafor"));

        var payroll = new PayrollService();
        PayRun run = payroll.CreateDraft(
            data, new DateTime(2026, 8, 14), new DateTime(2026, 8, 1), new DateTime(2026, 8, 14))!;

        payroll.Approve(run);
        data.PayRuns.Add(run);

        await service.SaveCompanyDataAsync(temp, data);
        await service.SaveCompanyAsync(_file, temp);

        // A second open, which is what closing and reopening the app does.
        FileService reopened = Service();
        string temp2 = await Open(reopened);
        CompanyData loaded = await reopened.LoadCompanyDataAsync(temp2);

        Assert.Equal(2, loaded.Employees.Count);
        Assert.Contains(loaded.Employees, e => e.Name == "Dana Smith");

        PayRun saved = Assert.Single(loaded.PayRuns);
        Assert.Equal(PayRunStatus.Approved, saved.Status);
        Assert.Equal(2, saved.Lines.Count);
    }

    /// <summary>
    /// The figures, not just the records. An approved run's numbers are frozen so a stub
    /// reprinted next year still matches the one the employee was handed, which only holds if
    /// they are stored rather than recalculated on load.
    /// </summary>
    [Fact]
    public async Task AnApprovedRunsFigures_ComeBackUnchanged()
    {
        FileService service = Service();
        await service.CreateCompanyAsync(_file, "Payroll Co");

        string temp = await Open(service);
        CompanyData data = await service.LoadCompanyDataAsync(temp);
        data.Employees.Add(Person("EMP-001", "Dana Smith"));

        var payroll = new PayrollService();
        PayRun run = payroll.CreateDraft(
            data, new DateTime(2026, 8, 14), new DateTime(2026, 8, 1), new DateTime(2026, 8, 14))!;

        run.Lines[0].Bonus = 5_000m;
        payroll.Recalculate(data, run);
        payroll.Approve(run);
        data.PayRuns.Add(run);

        PayRunLine original = run.Lines[0];

        await service.SaveCompanyDataAsync(temp, data);
        await service.SaveCompanyAsync(_file, temp);

        FileService reopened = Service();
        CompanyData loaded = await reopened.LoadCompanyDataAsync(await Open(reopened));

        PayRunLine line = loaded.PayRuns[0].Lines[0];

        Assert.Equal(original.GrossPay, line.GrossPay);
        Assert.Equal(original.Bonus, line.Bonus);
        Assert.Equal(original.CppEmployee, line.CppEmployee);
        Assert.Equal(original.EiEmployee, line.EiEmployee);
        Assert.Equal(original.FederalTax, line.FederalTax);
        Assert.Equal(original.ProvincialTax, line.ProvincialTax);
        Assert.Equal(original.NetPay, line.NetPay);
    }

    /// <summary>
    /// A file written before payroll shipped has neither JSON file in it. That has to open as a
    /// company with no employees rather than throwing.
    /// </summary>
    [Fact]
    public async Task ACompanySavedBeforePayrollExisted_StillOpens()
    {
        FileService service = Service();
        await service.CreateCompanyAsync(_file, "Old Co");

        string temp = await Open(service);

        File.Delete(Path.Combine(temp, "employees.json"));
        File.Delete(Path.Combine(temp, "payRuns.json"));

        CompanyData loaded = await service.LoadCompanyDataAsync(temp);

        Assert.Empty(loaded.Employees);
        Assert.Empty(loaded.PayRuns);
    }

    public void Dispose()
    {
        foreach (string temp in _temps)
        {
            try { Directory.Delete(temp, recursive: true); } catch { /* best effort */ }
        }

        try { File.Delete(_file); } catch { /* best effort */ }

        GC.SuppressFinalize(this);
    }
}
