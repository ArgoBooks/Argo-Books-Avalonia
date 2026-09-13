using System.Globalization;
using ArgoBooks.Core.Models.Payroll;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ArgoBooks.Core.Services.Payroll;

/// <summary>
/// Renders the Record of Employment worksheet.
///
/// Laid out in ROE Web's block order so it can be worked through top to bottom without hunting.
/// It is deliberately not a facsimile of the ROE form: an ROE is issued by Service Canada and a
/// printed sheet is not one, so looking like the real thing would be worse than useless.
///
/// Block 16, the reason for issuing, is absent on purpose. It is chosen on the export screen
/// rather than derived, and whoever is reading this sheet into ROE Web has just chosen it, so
/// printing it back at them adds a field to check without adding anything to know.
/// </summary>
public static class RoePdfRenderer
{
    public static byte[] Render(RoeWorksheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        QuestPDF.Settings.License = LicenseType.Community;

        using var ms = new MemoryStream();

        Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(t => t.FontSize(10).FontFamily("Helvetica"));

            page.Header().Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        left.Item().Text("RECORD OF EMPLOYMENT").FontSize(19).SemiBold()
                            .FontColor(Colors.Blue.Darken2);
                        left.Item().Text("Worksheet for ROE Web").FontSize(11);
                    });

                    row.ConstantItem(190).AlignRight().Column(right =>
                    {
                        right.Item().AlignRight().Text(sheet.EmployeeName).FontSize(13).SemiBold();
                        right.Item().AlignRight().Text(Frequency(sheet.PayFrequency)).FontSize(9)
                            .FontColor(Colors.Grey.Darken2);
                    });
                });
                col.Item().PaddingVertical(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
            });

            page.Content().PaddingVertical(10).Column(col =>
            {
                col.Spacing(14);

                // The deadline first, because it is five days and it is the thing most often
                // missed. It runs from the end of the pay period, not from the last day worked.
                if (sheet.Deadline is { } due)
                {
                    col.Item().Background(Colors.Grey.Lighten4).Padding(10).Column(d =>
                    {
                        d.Item().Text($"Due to Service Canada by {Date(due)}").FontSize(12).SemiBold();
                        d.Item().Text("Five calendar days after the end of the final pay period.")
                            .FontSize(9).FontColor(Colors.Grey.Darken2);
                    });
                }

                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(e =>
                    {
                        e.Item().Text("EMPLOYEE").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                        e.Item().PaddingTop(4).Text(sheet.EmployeeName).SemiBold();

                        foreach (string line in AddressLines(sheet.Address))
                        {
                            e.Item().Text(line);
                        }

                        e.Item().PaddingTop(4).Text($"Block 9  SIN: {FormatSin(sheet.Sin)}").FontSize(9);
                    });

                    row.ConstantItem(220).Column(d =>
                    {
                        d.Item().Text("EMPLOYER").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                        d.Item().PaddingTop(4).Text(sheet.EmployerName).SemiBold();
                        d.Item().PaddingTop(4).Text($"Block 5  Account: {sheet.PayrollAccountNumber}").FontSize(9);
                    });
                });

                col.Item().Column(c =>
                {
                    c.Item().Text("DATES").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                    c.Item().PaddingTop(6).Column(rows =>
                    {
                        Line(rows, "10", "First day worked", Date(sheet.FirstDayWorked));
                        Line(rows, "11", "Last day for which paid", Date(sheet.LastDayPaid));
                        Line(rows, "12", "Final pay period ending date", Date(sheet.FinalPeriodEnd));
                    });
                });

                col.Item().Column(c =>
                {
                    c.Item().Text("TOTALS").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                    c.Item().PaddingTop(6).Column(rows =>
                    {
                        Line(rows, "15A", $"Total insurable hours, last {sheet.HoursPeriodCount} pay periods",
                            sheet.TotalInsurableHours is { } h
                                ? h.ToString("N2", CultureInfo.CurrentCulture)
                                : "see note below",
                            bold: true);

                        Line(rows, "15B", $"Total insurable earnings, last {sheet.EarningsPeriodCount} pay periods",
                            Money(sheet.TotalInsurableEarnings), bold: true);

                        Line(rows, "17A", "Vacation pay on separation", Money(sheet.VacationPay));
                    });

                    // Block 17A is the one figure here the pay runs cannot settle on their own.
                    c.Item().PaddingTop(6).Text(
                            "Block 17A is vacation pay paid because of the separation. This shows the "
                            + "vacation pay in the final period only: vacation pay included with every "
                            + "cheque must NOT be reported, and pay for a granted leave period or an "
                            + "anniversary date after the interruption must be added by hand. Confirm "
                            + "it before filing.")
                        .FontSize(9).FontColor(Colors.Grey.Darken2);

                    // The two windows are different lengths, which reads like a mistake on the
                    // page unless it is said out loud.
                    c.Item().PaddingTop(6).Text(
                            $"Blocks 15A and 15C cover {sheet.HoursPeriodCount} pay periods and block 15B covers "
                            + $"{sheet.EarningsPeriodCount}. They are different windows, not a typo.")
                        .FontSize(9).FontColor(Colors.Grey.Darken2);
                });

                if (sheet.HoursUnavailableReason is { } reason)
                {
                    col.Item().Background(Colors.Grey.Lighten4).Padding(10).Text(reason)
                        .FontSize(9).FontColor(Colors.Red.Darken1);
                }

                col.Item().Column(c =>
                {
                    c.Item().Text($"BLOCK 15C  INSURABLE EARNINGS BY PAY PERIOD")
                        .FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                    c.Item().Text("Most recent pay period first, which is the order ROE Web asks for.")
                        .FontSize(9).FontColor(Colors.Grey.Darken2);

                    c.Item().PaddingTop(6).Row(r =>
                    {
                        r.ConstantItem(40).Text("P.P.").FontSize(9).SemiBold();
                        r.RelativeItem().Text("Period ending").FontSize(9).SemiBold();
                        r.ConstantItem(90).AlignRight().Text("Hours").FontSize(9).SemiBold();
                        r.ConstantItem(110).AlignRight().Text("Earnings").FontSize(9).SemiBold();
                    });

                    int n = 1;
                    foreach (RoePayPeriod period in sheet.Periods)
                    {
                        int index = n++;
                        c.Item().PaddingTop(2).Row(r =>
                        {
                            r.ConstantItem(40).Text(index.ToString(CultureInfo.InvariantCulture)).FontSize(9);
                            r.RelativeItem().Text(Date(period.PeriodEnd)).FontSize(9);
                            r.ConstantItem(90).AlignRight().Text(period.InsurableHours is { } ph
                                ? ph.ToString("N2", CultureInfo.CurrentCulture)
                                : "-").FontSize(9);

                            // Nil periods are printed as 0.00 rather than skipped, because ROE
                            // Web wants a value in every field and a skipped row shifts every
                            // period after it into the wrong slot.
                            r.ConstantItem(110).AlignRight().Text(Money(period.InsurableEarnings)).FontSize(9);
                        });
                    }
                });

                col.Item().PaddingTop(6).Text(
                        "Block 16, the reason for issuing, is not filled in here. Argo Books knows the employee "
                        + "stopped being paid; it does not know whether they quit, were dismissed, or went on "
                        + "leave. Choose it in ROE Web.")
                    .FontSize(9).FontColor(Colors.Grey.Darken2);
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("Produced by Argo Books. This is a worksheet, not a Record of Employment. "
                       + "Issue the ROE through ROE Web.")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        })).GeneratePdf(ms);

        return ms.ToArray();
    }

    private static IEnumerable<string> AddressLines(Models.Common.Address address)
    {
        if (!string.IsNullOrWhiteSpace(address.Street))
        {
            yield return address.Street;
        }

        string cityLine = string.Join(", ",
            new[] { address.City, address.State, address.ZipCode }.Where(s => !string.IsNullOrWhiteSpace(s)));

        if (!string.IsNullOrWhiteSpace(cityLine))
        {
            yield return cityLine;
        }
    }

    private static void Line(ColumnDescriptor col, string block, string label, string value, bool bold = false) =>
        col.Item().PaddingTop(2).Row(r =>
        {
            r.ConstantItem(52).Text($"Block {block}").FontSize(9).FontColor(Colors.Grey.Darken2);

            var left = r.RelativeItem().Text(label);
            var right = r.ConstantItem(130).AlignRight().Text(value);

            if (bold)
            {
                left.SemiBold();
                right.SemiBold();
            }
        });

    private static string Frequency(PayFrequency frequency) => frequency switch
    {
        PayFrequency.Weekly => "Weekly",
        PayFrequency.SemiMonthly => "Semi-monthly",
        PayFrequency.Monthly => "Monthly",
        _ => "Biweekly",
    };

    private static string Date(DateTime? value) =>
        value?.ToString("d MMMM yyyy", CultureInfo.CurrentCulture) ?? "not recorded";

    private static string FormatSin(string sin)
    {
        string digits = new(sin.Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 9 ? $"{digits[..3]} {digits[3..6]} {digits[6..]}" : "not provided";
    }

    private static string Money(decimal value) => $"${value:N2}";
}
