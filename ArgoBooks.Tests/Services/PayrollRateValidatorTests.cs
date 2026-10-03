using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// The gate a downloaded rate table has to get through before it is allowed to decide anyone's
/// withholding.
///
/// A rate file delivered over the network is the one download in this app that must not be
/// taken on trust. Everything else degrades if it arrives corrupt; this decides how much tax
/// comes off a real person's pay, and a plausible-looking wrong number produces a plausible
/// -looking wrong deduction that nothing downstream questions.
///
/// The checks are the ones docs/PayrollRateUpdates.md already tells a human to run by hand:
/// every derived maximum has to reproduce from its own rate, and the bracket constants have to
/// be continuous at each boundary. Both are arithmetic on the file's own contents, so a table
/// that fails either one is self-contradictory and no amount of trusting the source fixes it.
/// </summary>
public class PayrollRateValidatorTests
{
    /// <summary>A fresh parse each time, so a test that mutates one cannot affect another.</summary>
    private static PayrollRateTable Shipped() =>
        new PayrollRateService().GetForDate(new DateTime(2026, 8, 15))!;

    [Fact]
    public void TheShippedEdition_PassesEveryCheck()
    {
        // The anchor. If this ever fails, the validator is wrong rather than the file: the July
        // 2026 edition was checked cell by cell against T4127's 123rd edition.
        Assert.Empty(PayrollRateValidator.Validate(Shipped()));
    }

    [Fact]
    public void ACppMaximumThatDoesNotFollowFromItsOwnRate_IsRejected()
    {
        // max = (YMPE - basic exemption) x rate. A fabricated or mistyped maximum cannot survive
        // this, and the maximum is what stops an employee contributing past the annual ceiling.
        PayrollRateTable table = Shipped();
        table.Cpp.MaxContributionEmployee += 100m;

        Assert.Contains(PayrollRateValidator.Validate(table), p => p.Contains("CPP maximum"));
    }

    [Fact]
    public void ACpp2MaximumThatDoesNotFollowFromItsOwnRate_IsRejected()
    {
        PayrollRateTable table = Shipped();
        table.Cpp2.MaxContributionEmployee += 100m;

        Assert.Contains(PayrollRateValidator.Validate(table), p => p.Contains("CPP2 maximum"));
    }

    [Fact]
    public void AnEiMaximumThatDoesNotFollowFromItsOwnRate_IsRejected()
    {
        PayrollRateTable table = Shipped();
        table.Ei.MaxPremiumEmployee += 100m;

        Assert.Contains(PayrollRateValidator.Validate(table), p => p.Contains("EI maximum"));
    }

    [Fact]
    public void ABracketConstantThatBreaksContinuity_IsRejected()
    {
        // At each boundary the bracket below and the bracket above must produce the same tax.
        // A wrong constant shifts every income above that boundary and looks entirely ordinary
        // on the page.
        PayrollRateTable table = Shipped();
        table.Federal.Brackets[1].ConstantK += 500m;

        Assert.Contains(PayrollRateValidator.Validate(table), p => p.Contains("Federal"));
    }

    [Fact]
    public void ABrokenProvincialBracket_NamesTheProvince()
    {
        // Thirteen jurisdictions in one file, so "a bracket is wrong" is not a usable message.
        PayrollRateTable table = Shipped();
        table.Provinces["ON"].Brackets[2].Rate += 0.05m;

        Assert.Contains(PayrollRateValidator.Validate(table), p => p.Contains("ON"));
    }

    [Fact]
    public void BracketsThatDoNotAscend_AreRejected()
    {
        PayrollRateTable table = Shipped();
        (table.Federal.Brackets[0].UpTo, table.Federal.Brackets[1].UpTo) =
            (table.Federal.Brackets[1].UpTo, table.Federal.Brackets[0].UpTo);

        Assert.NotEmpty(PayrollRateValidator.Validate(table));
    }

    [Fact]
    public void ATableWhoseTopBracketIsClosed_IsRejected()
    {
        // The last bracket has to be open-ended or the highest earners fall off the end of the
        // table, and BracketFor then charges them the top rate with no ceiling to check against.
        PayrollRateTable table = Shipped();
        table.Federal.Brackets[^1].UpTo = 999_999m;

        Assert.Contains(PayrollRateValidator.Validate(table), p => p.Contains("open"));
    }

    [Fact]
    public void ATableWithNoEditionId_IsRejected()
    {
        PayrollRateTable table = Shipped();
        table.EditionId = string.Empty;

        Assert.Contains(PayrollRateValidator.Validate(table), p => p.Contains("edition"));
    }

    [Fact]
    public void ATableWhoseDatesRunBackwards_IsRejected()
    {
        PayrollRateTable table = Shipped();
        (table.EffectiveFrom, table.EffectiveTo) = (table.EffectiveTo, table.EffectiveFrom);

        Assert.Contains(PayrollRateValidator.Validate(table), p => p.Contains("effective"));
    }

    [Fact]
    public void ATableWithNoProvinces_IsRejected()
    {
        PayrollRateTable table = Shipped();
        table.Provinces.Clear();

        Assert.Contains(PayrollRateValidator.Validate(table), p => p.Contains("province"));
    }

    [Fact]
    public void AQuebecBlockThatIsPresentButBroken_IsRejected()
    {
        // Quebec carries its own pension plan and its own brackets, so it needs the same
        // continuity check rather than being waved through for being optional.
        PayrollRateTable table = Shipped();
        table.Quebec!.Brackets[1].ConstantK += 500m;

        Assert.Contains(PayrollRateValidator.Validate(table), p => p.Contains("Quebec"));
    }

    [Fact]
    public void AQppMaximumThatDoesNotFollowFromItsOwnRate_IsRejected()
    {
        PayrollRateTable table = Shipped();
        table.Quebec!.Qpp.MaxContributionEmployee += 100m;

        Assert.Contains(PayrollRateValidator.Validate(table), p => p.Contains("QPP maximum"));
    }

    [Fact]
    public void AQpipMaximumThatDoesNotFollowFromItsOwnRate_IsRejected()
    {
        PayrollRateTable table = Shipped();
        table.Quebec!.Qpip.MaxPremiumEmployee += 100m;

        Assert.Contains(PayrollRateValidator.Validate(table), p => p.Contains("QPIP maximum"));
    }

    [Fact]
    public void AQuebecEiMaximumThatDoesNotFollowFromQuebecsOwnRate_IsRejected()
    {
        // Quebec pays EI at a lower rate because QPIP covers parental benefits, so its maximum
        // is checked against the Quebec rate rather than the federal one.
        PayrollRateTable table = Shipped();
        table.Quebec!.EiMaxPremiumEmployee += 100m;

        Assert.Contains(PayrollRateValidator.Validate(table), p => p.Contains("Quebec EI maximum"));
    }

    [Fact]
    public void AnEditionWithNoQuebecBlockAtAll_IsStillValid()
    {
        // Quebec is optional. An edition that carries none is refused at calculation time with a
        // message naming the province, which is a different thing from the file being malformed.
        PayrollRateTable table = Shipped();
        table.Quebec = null;

        Assert.Empty(PayrollRateValidator.Validate(table));
    }
}
