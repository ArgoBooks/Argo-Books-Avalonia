using ArgoBooks.Core.Data;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;

namespace ArgoBooks.Core.Services;

/// <summary>
/// The periods a business files sales tax for, and what it owes for each: tax collected on sales, less tax
/// handed back on refunds, less the tax on purchases it can claim back, as the Tax Summary report works it
/// out (docs/Calculations.md §17).
/// </summary>
public static class SalesTaxPeriods
{
    /// <summary>One filing period, with the last day its return and payment are due.</summary>
    public sealed record Period(DateTime Start, DateTime End, DateTime DueDate);

    /// <summary>What a period owes. A negative net is a refund due from the government.</summary>
    public sealed record Amounts(decimal Collected, decimal Refunded, decimal ClaimedBack)
    {
        public decimal Net => Collected - Refunded - ClaimedBack;
    }

    /// <summary>A period on the Sales Tax page, with the return filed for it if there is one.</summary>
    public sealed record Row(Period Period, SalesTaxFrequency Frequency, SalesTaxFiling? Filing);

    private static int MonthsIn(SalesTaxFrequency frequency) => frequency switch
    {
        SalesTaxFrequency.Monthly => 1,
        SalesTaxFrequency.Quarterly => 3,
        _ => 12
    };

    /// <summary>
    /// The month the business's tax year starts in, which its yearly and quarterly periods count from: the one
    /// it chose, or July in Australia, whose financial year runs July to June, and January anywhere else.
    /// </summary>
    public static int YearStartMonth(CompanyData data) =>
        data.Settings.Company.SalesTaxYearStartMonth is >= 1 and <= 12 and var month
            ? month
            : Countries.NormalizeCountryOrKeep(data.Settings.Company.Country) == "Australia" ? 7 : 1;

    /// <summary>
    /// The period that holds <paramref name="date"/>, counted from a tax year starting in <paramref name="yearStartMonth"/>,
    /// and due as returns are in <paramref name="country"/>.
    /// </summary>
    public static Period Containing(DateTime date, SalesTaxFrequency frequency, string? country = null, int yearStartMonth = 1)
    {
        var months = MonthsIn(frequency);
        var sinceYearStart = (date.Month - yearStartMonth + 12) % 12;
        var yearStart = new DateTime(date.Month >= yearStartMonth ? date.Year : date.Year - 1, yearStartMonth, 1);
        var start = yearStart.AddMonths(sinceYearStart / months * months);
        var end = start.AddMonths(months).AddDays(-1);
        return new Period(start, end, DueDate(end, frequency, country));
    }

    /// <summary>
    /// When a return for a period ending on <paramref name="end"/> is due, by the usual deadline where the
    /// business is: Canada's GST/HST a month after (three for a yearly return), most US states the 20th of
    /// the next month, UK VAT a month and seven days after (two months for a yearly return), and Australia's
    /// BAS the 28th of the next month for a quarter (February for the December quarter), the 21st for a month
    /// and 31 October after a year. Anywhere else, the end of the next month.
    /// </summary>
    public static DateTime DueDate(DateTime end, SalesTaxFrequency frequency, string? country)
    {
        var nextMonth = end.AddDays(1);
        DateTime EndOfMonth(DateTime month) => new DateTime(month.Year, month.Month, 1).AddMonths(1).AddDays(-1);

        switch (Countries.NormalizeCountryOrKeep(country))
        {
            case "United States":
                return new DateTime(nextMonth.Year, nextMonth.Month, 20);
            case "United Kingdom":
                return frequency == SalesTaxFrequency.Yearly ? EndOfMonth(nextMonth.AddMonths(1)) : EndOfMonth(nextMonth).AddDays(7);
            case "Australia":
                if (frequency == SalesTaxFrequency.Monthly)
                    return new DateTime(nextMonth.Year, nextMonth.Month, 21);
                if (frequency == SalesTaxFrequency.Yearly)
                    return EndOfMonth(end.AddMonths(4));
                var dueMonth = end.Month == 12 ? nextMonth.AddMonths(1) : nextMonth;
                return new DateTime(dueMonth.Year, dueMonth.Month, 28);
            default:
                return EndOfMonth(nextMonth.AddMonths(frequency == SalesTaxFrequency.Yearly ? 2 : 0));
        }
    }

    /// <summary>Every period from the one holding <paramref name="from"/> to the one holding <paramref name="to"/>, newest first.</summary>
    public static List<Period> Between(DateTime from, DateTime to, SalesTaxFrequency frequency, string? country = null, int yearStartMonth = 1)
    {
        var periods = new List<Period>();
        for (var period = Containing(from, frequency, country, yearStartMonth);
             period.Start <= to;
             period = Containing(period.End.AddDays(1), frequency, country, yearStartMonth))
            periods.Add(period);
        periods.Reverse();
        return periods;
    }

    /// <summary>
    /// The periods to show, newest first: every return filed, whatever frequency it was filed at, and the
    /// periods at <paramref name="frequency"/> no filed return covers. They start at the first sale or purchase,
    /// but no further back than two years, so the list keeps to the periods still worth filing.
    /// </summary>
    public static List<Row> Rows(CompanyData data, SalesTaxFrequency frequency, DateTime today)
    {
        var dates = data.Revenues.Select(r => r.Date).Concat(data.Expenses.Select(e => e.Date)).ToList();
        var from = dates.Count > 0 ? dates.Min() : today;
        if (from < today.AddYears(-2)) from = today.AddYears(-2);

        var filings = data.SalesTaxFilings;
        return Between(from, today, frequency, data.Settings.Company.Country, YearStartMonth(data))
            .SelectMany(p => Unfiled(p, filings))
            .Select(p => new Row(p, frequency, null))
            .Concat(filings.Select(f => new Row(new Period(f.PeriodStart, f.PeriodEnd, f.DueDate), f.Frequency, f)))
            .OrderByDescending(r => r.Period.Start)
            .ToList();
    }

    /// <summary>
    /// The parts of <paramref name="period"/> no filed return covers. A return filed at another frequency can
    /// cover part of it, such as January of a quarter after switching from monthly, and the rest is still owed.
    /// </summary>
    private static IEnumerable<Period> Unfiled(Period period, List<SalesTaxFiling> filings)
    {
        var start = period.Start;
        foreach (var filing in filings.Where(f => f.PeriodStart <= period.End && f.PeriodEnd >= period.Start).OrderBy(f => f.PeriodStart))
        {
            if (filing.PeriodStart > start)
                yield return period with { Start = start, End = filing.PeriodStart.AddDays(-1) };
            if (filing.PeriodEnd >= start)
                start = filing.PeriodEnd.AddDays(1);
        }
        if (start <= period.End)
            yield return period with { Start = start };
    }

    /// <summary>
    /// The unfiled return the dashboard reminds about: the earliest due of the periods that have ended,
    /// owe or are owed something, and are due within <paramref name="withinDays"/> or overdue. None until the
    /// business has chosen how often it files, since one that doesn't collect sales tax never does.
    /// </summary>
    public static Row? ComingDue(CompanyData data, DateTime today, int withinDays = 14)
    {
        if (data.Settings.Company.SalesTaxFrequency is not { } frequency)
            return null;

        return Rows(data, frequency, today)
            .Where(r => r.Filing == null && r.Period.End < today && r.Period.DueDate <= today.AddDays(withinDays))
            .Where(r => For(data, r.Period, (usd, _) => usd).Net != 0)
            .OrderBy(r => r.Period.DueDate)
            .FirstOrDefault();
    }

    /// <summary>
    /// The period as people name it: March 2026, Q1 2026, 2026, or 2025-26 for a year starting after January.
    /// A quarter that isn't a calendar quarter, or part of a period, is named by its months, such as Feb - Apr 2026.
    /// </summary>
    public static string NameOf(Period period, SalesTaxFrequency frequency)
    {
        var whole = period.Start.Day == 1
                    && period.End == period.Start.AddMonths(MonthsIn(frequency)).AddDays(-1);
        if (whole && frequency == SalesTaxFrequency.Monthly)
            return period.Start.ToString("MMMM yyyy");
        if (whole && frequency == SalesTaxFrequency.Quarterly && (period.Start.Month - 1) % 3 == 0)
            return $"Q{(period.Start.Month - 1) / 3 + 1} {period.Start.Year}";
        if (whole && frequency == SalesTaxFrequency.Yearly)
            return period.Start.Month == 1 ? period.Start.Year.ToString() : $"{period.Start.Year}-{period.End.Year % 100:D2}";

        return period.Start.Month == period.End.Month && period.Start.Year == period.End.Year
            ? period.Start.ToString("MMMM yyyy")
            : period.Start.Year == period.End.Year
                ? $"{period.Start:MMM} - {period.End:MMM yyyy}"
                : $"{period.Start:MMM yyyy} - {period.End:MMM yyyy}";
    }

    /// <summary>A filed return as a bank statement or list names it: "Sales tax Q1 2026".</summary>
    public static string Describe(SalesTaxFiling filing) =>
        $"Sales tax {NameOf(new Period(filing.PeriodStart, filing.PeriodEnd, filing.DueDate), filing.Frequency)}";

    /// <summary>
    /// What <paramref name="period"/> owes, each amount converted at its own date by <paramref name="toDisplay"/>,
    /// and rounded to the cent as a return reports it unless <paramref name="toCents"/> is false.
    /// </summary>
    public static Amounts For(CompanyData data, Period period, Func<decimal, DateTime, decimal> toDisplay, bool toCents = true)
    {
        bool InPeriod(DateTime date) => date.Date >= period.Start && date.Date <= period.End;

        var collected = data.Revenues
            .Where(r => InPeriod(r.Date))
            .Sum(r => toDisplay(r.EffectiveTotalUSD - r.EffectiveSubtotalUSD, r.Date));
        var refunded = RefundAggregator.GetRefundedTaxInDateRangeDisplay(
            data.Payments, ProfitCalculator.BuildInvoiceLookup(data.Invoices), period.Start, period.End.AddDays(1).AddTicks(-1), toDisplay);
        var rule = PurchaseTax.RuleFor(data);
        var claimedBack = data.Expenses
            .Where(e => InPeriod(e.Date))
            .Sum(e => toDisplay(PurchaseTax.ClaimableUSD(e, rule), e.Date));

        decimal Cents(decimal amount) => toCents ? Math.Round(amount, 2, MidpointRounding.AwayFromZero) : amount;
        return new Amounts(Cents(collected), Cents(refunded), Cents(claimedBack));
    }

    /// <summary>
    /// Whether any sale, purchase or refund in the period is still waiting for its exchange rate, so what the
    /// period owes isn't known yet and it can't be filed.
    /// </summary>
    public static bool IsWaitingForRate(CompanyData data, Period period)
    {
        bool InPeriod(DateTime date) => date.Date >= period.Start && date.Date <= period.End;
        return data.Revenues.Any(r => r.IsPendingConversion && InPeriod(r.Date))
               || data.Expenses.Any(e => e.IsPendingConversion && InPeriod(e.Date))
               || data.Payments.Any(p => p.IsRefund && p.IsPendingConversion && InPeriod(p.Date));
    }

    /// <summary>
    /// What the returns paid by <paramref name="end"/> paid, less refunds received, in <paramref name="displayCode"/>,
    /// each at the rate on the day it was paid. Pending is true when a rate is missing, and that return counts as 0.
    /// </summary>
    public static (decimal Paid, bool Pending) PaidBetween(CompanyData data, DateTime start, DateTime end, string displayCode)
    {
        decimal paid = 0m;
        var pending = false;
        foreach (var filing in data.SalesTaxFilings.Where(f => f.PaidDate.Date >= start.Date && f.PaidDate.Date <= end.Date))
        {
            if (DisplayCurrency.FromNative(filing.Amount, filing.Currency, displayCode, filing.PaidDate.Date) is { } amount)
                paid += amount;
            else
                pending = true;
        }
        return (paid, pending);
    }
}
