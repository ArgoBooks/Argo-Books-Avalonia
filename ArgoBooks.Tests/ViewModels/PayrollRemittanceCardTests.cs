using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Services;
using ArgoBooks.Services;
using ArgoBooks.ViewModels.Dashboard;
using Xunit;

namespace ArgoBooks.Tests.ViewModels;

/// <summary>
/// The dashboard's Next Remittance Due card, against the Pay runs page.
///
/// The two answered the same question two ways: the page followed CRA's remitter type, and the
/// card always showed last month's total due on the 15th. For an accelerated or quarterly
/// remitter that names the wrong amount and a date that can be after the real deadline, and CRA
/// charges up to 10% on a late remittance.
/// </summary>
public class PayrollRemittanceCardTests
{
    private static PayRun Run(DateTime payDate) => new()
    {
        Id = $"PR-{payDate:yyyyMMdd}",
        PayDate = payDate,
        Status = PayRunStatus.Approved,
        Lines =
        {
            new PayRunLine
            {
                EmployeeId = "EMP-001",
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
    };

    /// <summary>
    /// 20 September 2026, a date on which every schedule disagrees with "last month, due the
    /// 15th": regular is past 15 September and owes September by 15 October, quarterly owes the
    /// whole third quarter, and both accelerated types owe part of September within days.
    /// </summary>
    [Theory]
    [InlineData(RemitterType.Regular)]
    [InlineData(RemitterType.Quarterly)]
    [InlineData(RemitterType.AcceleratedThreshold1)]
    [InlineData(RemitterType.AcceleratedThreshold2)]
    public void TheCard_ShowsWhatThePayRunsPageShows(RemitterType remitter)
    {
        DateTime today = new(2026, 9, 20);
        var data = new CompanyData();
        data.Settings.Company.RemitterType = remitter;

        for (DateTime day = new(2026, 5, 1); day <= today; day = day.AddDays(3))
        {
            data.PayRuns.Add(Run(day));
        }

        (string value, string secondary) = StatCardWidgetViewModel.PayrollRemittanceCard(data, today);
        (decimal amount, DateTime due) = PayrollService.NextRemittance(data.PayRuns, today, remitter);

        Assert.Equal(CurrencyService.Format(amount), value);
        Assert.Contains($"{due:d MMM}", secondary);
    }

    [Fact]
    public void TheCard_ShowsCrasShare_AndNamesWhatGoesToRevenuQuebec()
    {
        // Quebec income tax, QPP and QPIP go to Revenu Quebec as a separate payment. Folding them
        // into the CRA figure overstated what CRA was owed by the whole Quebec side.
        var data = new CompanyData();
        PayRun run = Run(new DateTime(2026, 8, 14));
        run.Lines[0].Province = "QC";
        run.Lines[0].QpipEmployee = 9m;
        run.Lines[0].QpipEmployer = 12.60m;
        data.PayRuns.Add(run);

        (string value, string secondary) = StatCardWidgetViewModel.PayrollRemittanceCard(data, new DateTime(2026, 8, 16));

        // Federal tax and EI to CRA; Quebec tax, QPP and QPIP to Revenu Quebec.
        Assert.Equal(CurrencyService.Format(200m + 30m + 42m), value);
        Assert.Contains(CurrencyService.Format(90m + 100m + 100m + 9m + 12.60m), secondary);
    }
}
