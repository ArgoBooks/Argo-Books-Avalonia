using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for how a CRA rate edition is found and loaded.
///
/// The behaviour that matters is what happens when an edition is missing or broken. Payroll is
/// the one part of the app that must refuse to guess: calculating a 2027 pay run against 2026
/// rates produces deductions that look right, are wrong, and are only found when CRA assesses
/// the return. So a missing edition has to come back as null, and a corrupt delivered file has
/// to be stepped over rather than taking every other edition down with it.
/// </summary>
public class PayrollRateServiceTests : IDisposable
{
    private readonly string _cache = Path.Combine(
        Path.GetTempPath(), "argo-rates-" + Guid.NewGuid().ToString("N"));

    private PayrollRateService Service() => new(new CachePlatformService(_cache));

    public void Dispose()
    {
        if (Directory.Exists(_cache))
        {
            Directory.Delete(_cache, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private void WriteCached(string fileName, string json)
    {
        Directory.CreateDirectory(Path.Combine(_cache, "Payroll"));
        File.WriteAllText(Path.Combine(_cache, "Payroll", fileName), json);
    }

    private static string Edition(string id, string from, string to) => $$"""
        {
          "editionId": "{{id}}",
          "effectiveFrom": "{{from}}",
          "effectiveTo": "{{to}}",
          "federal": {
            "brackets": [ { "upTo": null, "rate": 0.5, "constantK": 0 } ],
            "basicPersonalAmount": { "maximum": 0 },
            "canadaEmploymentAmount": 0,
            "lowestRateForCredits": 0.14
          },
          "cpp": { "rateEmployee": 0, "baseRateEmployee": 0, "basicExemptionAnnual": 0,
                   "ympeCeiling": 0, "maxContributionEmployee": 0 },
          "cpp2": { "rateEmployee": 0, "yampeCeiling": 0, "maxContributionEmployee": 0 },
          "ei": { "rateEmployee": 0, "employerMultiplier": 1.4, "maxInsurableEarnings": 0,
                  "maxPremiumEmployee": 0 },
          "provinces": { "AB": { "brackets": [ { "upTo": null, "rate": 0.1, "constantK": 0 } ],
                                 "basicPersonalAmount": { "maximum": 0 } } }
        }
        """;

    #region Finding the edition for a pay date

    [Fact]
    public void APayDateInsideAnEdition_FindsIt()
    {
        PayrollRateTable? table = Service().GetForDate(new DateTime(2026, 8, 15));

        Assert.NotNull(table);
        Assert.Equal("2026-07", table.EditionId);
    }

    [Fact]
    public void APayDateWithNoEdition_ComesBackNullRatherThanTheNearestOne()
    {
        // The important refusal. There is deliberately no fallback to the most recent edition:
        // a run calculated on the wrong year's rates is wrong in a way nobody notices until
        // CRA assesses it.
        Assert.Null(Service().GetForDate(new DateTime(2031, 3, 1)));
        Assert.Null(Service().GetForDate(new DateTime(2019, 3, 1)));
    }

    [Fact]
    public void TheEditionBoundaries_AreInclusive()
    {
        PayrollRateTable table = Service().GetForDate(new DateTime(2026, 8, 15))!;

        Assert.NotNull(Service().GetForDate(table.EffectiveFrom));
        Assert.NotNull(Service().GetForDate(table.EffectiveTo));
        Assert.Null(Service().GetForDate(table.EffectiveTo.AddDays(1)));
    }

    [Fact]
    public void TheTwoHalvesOf2026_MeetWithNoGapAndNoOverlap()
    {
        // CRA publishes twice a year and the halves have to abut exactly. A gap leaves a pay date
        // that cannot be calculated at all; an overlap makes which edition applies depend on the
        // order the files happened to load.
        PayrollRateTable january = Service().GetForDate(new DateTime(2026, 3, 15))!;
        PayrollRateTable july = Service().GetForDate(new DateTime(2026, 8, 15))!;

        Assert.Equal("2026-01", january.EditionId);
        Assert.Equal("2026-07", july.EditionId);
        Assert.Equal(january.EffectiveTo.Date.AddDays(1), july.EffectiveFrom.Date);
        Assert.Equal("2026-01", Service().GetForDate(new DateTime(2026, 6, 30))!.EditionId);
        Assert.Equal("2026-07", Service().GetForDate(new DateTime(2026, 7, 1))!.EditionId);
    }

    [Fact]
    public void TheJanuaryEdition_CarriesTheUnproratedFiguresForTheThreeProvincesThatChanged()
    {
        // BC, NL and PE all changed retroactively to 1 January 2026, so the JULY edition carries
        // prorated figures to catch the year up and the January one carries the originals, which
        // are what was actually withheld from January to June. Getting these the wrong way round
        // is invisible: both sets are plausible numbers for the same province in the same year.
        PayrollRateTable jan = Service().GetForDate(new DateTime(2026, 3, 15))!;
        PayrollRateTable jul = Service().GetForDate(new DateTime(2026, 8, 15))!;

        // T4127 122nd edition vs 123rd: BC's lowest rate, prorated up for the second half.
        Assert.Equal(0.0506m, jan.Provinces["BC"].Brackets[0].Rate);
        Assert.Equal(0.0614m, jul.Provinces["BC"].Brackets[0].Rate);

        // BC's tax reduction basic amount, and the income at which it runs out.
        Assert.Equal(575m, jan.Provinces["BC"].TaxReduction!.Basic);
        Assert.Equal(805m, jul.Provinces["BC"].TaxReduction!.Basic);
        Assert.Equal(41722m, jan.Provinces["BC"].TaxReduction!.PhaseOutEnd);
        Assert.Equal(44952m, jul.Provinces["BC"].TaxReduction!.PhaseOutEnd);

        // Newfoundland and Labrador's basic personal amount.
        Assert.Equal(11188m, jan.Provinces["NL"].BasicPersonalAmount.Maximum);
        Assert.Equal(15000m, jul.Provinces["NL"].BasicPersonalAmount.Maximum);

        // Prince Edward Island's 21% band over $200,000 exists only in the second half.
        Assert.Equal(5, jan.Provinces["PE"].Brackets.Count);
        Assert.Equal(6, jul.Provinces["PE"].Brackets.Count);
        Assert.Equal(0.19m, jan.Provinces["PE"].Brackets[^1].Rate);
        Assert.Equal(0.21m, jul.Provinces["PE"].Brackets[^1].Rate);
    }

    [Fact]
    public void TheAnnualFigures_AreIdenticalInBothHalves()
    {
        // CPP, CPP2, EI, QPP and QPIP are set once a year. A July edition that disagreed with the
        // January one about a ceiling would make an employee's year-to-date stop adding up at the
        // halfway point.
        PayrollRateTable jan = Service().GetForDate(new DateTime(2026, 3, 15))!;
        PayrollRateTable jul = Service().GetForDate(new DateTime(2026, 8, 15))!;

        Assert.Equal(jul.Cpp.YmpeCeiling, jan.Cpp.YmpeCeiling);
        Assert.Equal(jul.Cpp.MaxContributionEmployee, jan.Cpp.MaxContributionEmployee);
        Assert.Equal(jul.Cpp2.YampeCeiling, jan.Cpp2.YampeCeiling);
        Assert.Equal(jul.Cpp2.MaxContributionEmployee, jan.Cpp2.MaxContributionEmployee);
        Assert.Equal(jul.Ei.MaxInsurableEarnings, jan.Ei.MaxInsurableEarnings);
        Assert.Equal(jul.Ei.MaxPremiumEmployee, jan.Ei.MaxPremiumEmployee);
        Assert.Equal(jul.Quebec!.Qpp.MaxContributionEmployee, jan.Quebec!.Qpp.MaxContributionEmployee);
        Assert.Equal(jul.Quebec.Qpip.MaxPremiumEmployee, jan.Quebec.Qpip.MaxPremiumEmployee);
        Assert.Equal(jul.Quebec.EiMaxPremiumEmployee, jan.Quebec.EiMaxPremiumEmployee);

        // And the federal side, which did not change mid-year for 2026.
        Assert.Equal(jul.Federal.BasicPersonalAmount.Maximum, jan.Federal.BasicPersonalAmount.Maximum);
        Assert.Equal(jul.Federal.CanadaEmploymentAmount, jan.Federal.CanadaEmploymentAmount);
        Assert.Equal(jul.Federal.Brackets.Count, jan.Federal.Brackets.Count);
    }

    #endregion

    #region Delivered editions

    [Fact]
    public void ADeliveredEdition_IsUsedWithoutAReleaseGoingOut()
    {
        // The whole point of the cache directory: CRA publishes twice a year on a fixed
        // deadline, and a rate change has to be a file upload rather than a build.
        WriteCached("2027-01.json", Edition("2027-01", "2027-01-01", "2027-06-30"));

        PayrollRateTable? table = Service().GetForDate(new DateTime(2027, 3, 1));

        Assert.NotNull(table);
        Assert.Equal("2027-01", table.EditionId);
    }

    [Fact]
    public void ADeliveredEdition_SupersedesTheEmbeddedCopyOfTheSameId()
    {
        // A correction to an edition already shipped inside the app has to win, otherwise it
        // could only be fixed by a release, which is the situation this exists to avoid.
        WriteCached("2026-07.json", Edition("2026-07", "2026-07-01", "2026-12-31"));

        PayrollRateTable table = Service().GetForDate(new DateTime(2026, 8, 15))!;

        Assert.Equal(0.5m, table.Federal.Brackets[0].Rate);
    }

    [Fact]
    public void ACorruptDeliveredFile_IsSteppedOverRatherThanTakingTheRestDown()
    {
        WriteCached("broken.json", "{ this is not json");

        Assert.NotNull(Service().GetForDate(new DateTime(2026, 8, 15)));
    }

    [Fact]
    public void AFileWithNoEditionId_IsNotAnEdition()
    {
        // Valid JSON that parses into an empty table. Accepting it would put a nameless edition
        // in the list that covers no date and shadows nothing, which is harmless, and a second
        // one would then collide with it on the empty id, which is not.
        WriteCached("nameless.json", "{ }");

        Assert.NotNull(Service().GetForDate(new DateTime(2026, 8, 15)));
    }

    [Fact]
    public void NoCacheDirectoryAtAll_LeavesTheEmbeddedEditionsWorking()
    {
        Assert.False(Directory.Exists(_cache));
        Assert.NotNull(Service().GetForDate(new DateTime(2026, 8, 15)));
    }

    #endregion

    #region Reloading

    [Fact]
    public void ADeliveredEditionArrivingWhileTheAppIsOpen_IsPickedUpAfterInvalidate()
    {
        var service = Service();
        Assert.Null(service.GetForDate(new DateTime(2027, 3, 1)));

        WriteCached("2027-01.json", Edition("2027-01", "2027-01-01", "2027-06-30"));

        // Still nothing: the list is held in memory so that a pay run does not read the disk
        // once per employee.
        Assert.Null(service.GetForDate(new DateTime(2027, 3, 1)));

        service.Invalidate();

        Assert.NotNull(service.GetForDate(new DateTime(2027, 3, 1)));
    }

    #endregion

    private sealed class CachePlatformService(string cachePath) : IPlatformService
    {
        public PlatformType Platform => PlatformType.Linux;
        public string GetAppDataPath() => cachePath;
        public string GetTempPath() => Path.GetTempPath();
        public string GetCachePath() => cachePath;
        public void EnsureDirectoryExists(string path) => Directory.CreateDirectory(path);
        public bool SupportsFileSystem => true;
        public bool SupportsNativeDialogs => false;
        public bool SupportsBiometrics => false;
        public Task<bool> IsBiometricAvailableAsync() => Task.FromResult(false);
        public Task<string> GetBiometricAvailabilityDetailsAsync() => Task.FromResult("Not supported");
        public Task<bool> AuthenticateWithBiometricAsync(string reason) => Task.FromResult(false);
        public void StorePasswordForBiometric(string fileId, string password) { }
        public string? GetPasswordForBiometric(string fileId) => null;
        public void ClearPasswordForBiometric(string fileId) { }
        public bool SupportsAutoUpdate => false;
        public int MaxRecentCompanies => 10;
        public string NormalizePath(string path) => path;
        public string CombinePaths(params string[] paths) => Path.Combine(paths);
        public string GetMachineId() => "test-machine-id";
        public StringComparer PathComparer => StringComparer.Ordinal;
    }
}
