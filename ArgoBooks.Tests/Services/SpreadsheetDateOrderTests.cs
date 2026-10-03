using System.Globalization;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// A column of numeric dates whose fields are all 12 or under says nothing about its own order, and
/// a statement covering only the 1st to the 12th of a month is exactly that. Reading those
/// month-first turns one month of a British or Canadian statement into twelve, so the computer's own
/// regional order decides instead. A column that settles itself still wins, whatever the region says.
/// </summary>
public class SpreadsheetDateOrderTests
{
    private static SpreadsheetRowReader.DateOrder Detect(CultureInfo culture, params string[] dates)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            var rows = dates.Select(d => new List<object?> { d, "10.00" }).ToList();
            return SpreadsheetRowReader.DetectDateOrder(rows, ["Date", "Amount"], "Date");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void EveryFieldUnderThirteen_OnADayFirstMachine_ReadsDayFirst()
    {
        Assert.Equal(SpreadsheetRowReader.DateOrder.DayFirst,
            Detect(new CultureInfo("en-GB"), "05/03/2026", "06/04/2026", "11/04/2026"));
    }

    [Fact]
    public void EveryFieldUnderThirteen_OnAMonthFirstMachine_StaysUnknown()
    {
        // Unknown keeps the per-value parse, which reads month-first, which is what a US machine wants.
        Assert.Equal(SpreadsheetRowReader.DateOrder.Unknown,
            Detect(new CultureInfo("en-US"), "05/03/2026", "06/04/2026", "11/04/2026"));
    }

    [Fact]
    public void AFieldOverTwelve_SettlesTheColumnWhateverTheRegionSays()
    {
        // 15 can only be a day, so the column is day-first even on a month-first machine.
        Assert.Equal(SpreadsheetRowReader.DateOrder.DayFirst,
            Detect(new CultureInfo("en-US"), "15/03/2026", "06/04/2026"));

        // 15 in the second field can only be a day, so the column is month-first on a day-first machine.
        Assert.Equal(SpreadsheetRowReader.DateOrder.MonthFirst,
            Detect(new CultureInfo("en-GB"), "03/15/2026", "04/06/2026"));
    }

    [Fact]
    public void AColumnThatContradictsItself_StaysUnknown()
    {
        Assert.Equal(SpreadsheetRowReader.DateOrder.Unknown,
            Detect(new CultureInfo("en-GB"), "15/03/2026", "03/15/2026"));
    }

    [Fact]
    public void AYearFirstDateIsNeverAmbiguous_AndDoesNotSettleTheColumn()
    {
        // Four leading digits are skipped, so these rows say nothing and the region decides.
        Assert.Equal(SpreadsheetRowReader.DateOrder.Unknown,
            Detect(new CultureInfo("en-US"), "2026-03-05", "2026-04-06"));
    }
}
