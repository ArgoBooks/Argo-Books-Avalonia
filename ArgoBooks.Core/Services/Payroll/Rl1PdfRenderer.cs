using System.Globalization;
using ArgoBooks.Core.Models.Payroll;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ArgoBooks.Core.Services.Payroll;

/// <summary>
/// Renders the RL-1 worksheets and summary as PDFs.
///
/// Deliberately NOT filable slips, and they say so on the page. Revenu Quebec accepts a paper
/// slip printed by software only when it carries an authorization number, two letters and seven
/// digits like FS9999999, which it issues to a developer per taxation year after certifying the
/// software, plus a two-dimensional barcode on copy 1. Argo Books holds neither, and "the RL
/// slip does not have an authorization number" heads Revenu Quebec's own list of the most common
/// reasons a slip is rejected.
///
/// So this takes the same position as <see cref="RoePdfRenderer"/> rather than
/// <see cref="T4PdfRenderer"/>: a sheet that carries every figure in its official box, laid out
/// to be read and re-keyed into My Account for businesses. Looking more like the real form would
/// make it likelier to be mailed, which is the one outcome that wastes the employer's deadline.
/// </summary>
public static class Rl1PdfRenderer
{
    /// <summary>One employee's slip.</summary>
    public static byte[] RenderSlip(Rl1Return rl1, Rl1Slip slip)
    {
        ArgumentNullException.ThrowIfNull(rl1);
        ArgumentNullException.ThrowIfNull(slip);

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
                        left.Item().Text("RL-1 worksheet").FontSize(22).SemiBold().FontColor(Colors.Blue.Darken2);
                        left.Item().Text("Employment and Other Income").FontSize(11);
                    });

                    row.ConstantItem(160).AlignRight().Column(right =>
                    {
                        right.Item().AlignRight().Text(rl1.TaxYear.ToString(CultureInfo.InvariantCulture))
                            .FontSize(22).SemiBold();
                        right.Item().AlignRight().Text("Tax year").FontSize(9).FontColor(Colors.Grey.Darken2);
                    });
                });
                // On the page rather than only in the app, because the PDF is what gets saved,
                // emailed and looked at again in February, by which time whatever the export
                // screen said is long gone.
                col.Item().PaddingTop(8).Background(Colors.Amber.Lighten4).Padding(8)
                    .Text(Rl1Service.FilingNotice).FontSize(8).FontColor(Colors.Grey.Darken4);

                col.Item().PaddingVertical(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
            });

            page.Content().PaddingVertical(10).Column(col =>
            {
                col.Spacing(16);

                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(e =>
                    {
                        e.Item().Text("EMPLOYEE").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                        e.Item().PaddingTop(4).Text($"{slip.GivenName} {slip.Surname}".Trim()).SemiBold();

                        foreach (string line in AddressLines(slip.Address))
                        {
                            e.Item().Text(line);
                        }

                        e.Item().PaddingTop(4).Text($"Social insurance number: {FormatSin(slip.Sin)}").FontSize(9);
                    });

                    row.ConstantItem(220).Column(d =>
                    {
                        d.Item().Text("EMPLOYER").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                        d.Item().PaddingTop(4).Text(rl1.EmployerName).SemiBold();

                        foreach (string line in AddressLines(rl1.EmployerAddress))
                        {
                            d.Item().Text(line);
                        }

                        d.Item().PaddingTop(4).Text($"Identification number: {rl1.QuebecIdentificationNumber}").FontSize(9);
                        d.Item().Text($"Slip type: {SlipCodeText(rl1.SlipCode)}").FontSize(9);
                    });
                });

                col.Item().Column(c =>
                {
                    c.Item().Text("AMOUNTS").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                    c.Item().PaddingTop(6).Column(rows =>
                    {
                        Box(rows, "A", "Employment income", slip.EmploymentIncome, bold: true);
                        Box(rows, "B.A", "QPP contribution", slip.QppContribution);

                        if (slip.AdditionalQppContribution > 0)
                        {
                            Box(rows, "B.B", "Additional QPP contribution", slip.AdditionalQppContribution);
                        }

                        Box(rows, "C", "Employment insurance premium", slip.EiPremium);

                        // Boxes D and F only appear when there is something in them. Argo Books
                        // does not collect registered pension plan contributions or union dues,
                        // so in practice they never do, and printing two permanent zeroes would
                        // suggest the employer has both and contributed nothing.
                        if (slip.RppContribution > 0)
                        {
                            Box(rows, "D", "Registered pension plan contribution", slip.RppContribution);
                        }

                        // Quebec tax alone. The federal tax withheld in the same pay run is on
                        // the employee's T4, in box 22, and is not repeated here.
                        Box(rows, "E", "Quebec income tax withheld", slip.QuebecIncomeTax);

                        if (slip.UnionDues > 0)
                        {
                            Box(rows, "F", "Union dues", slip.UnionDues);
                        }

                        Box(rows, "G", "Pensionable salary or wages under the QPP", slip.QppPensionableSalary);
                        Box(rows, "H", "QPIP premium", slip.QpipPremium);

                        // Revenu Quebec requires box I to read 0 rather than be left blank when
                        // there is no eligible salary, so it is always printed.
                        Box(rows, "I", "Eligible salary or wages under the QPIP", slip.QpipEligibleSalary);
                    });
                });

                col.Item().PaddingTop(8).Text(
                        "Keep this slip for your records. The amounts above are reported to Revenu Quebec "
                        + "and should match what you enter on your Quebec income tax return. Your federal "
                        + "amounts are on your T4 slip, which is separate.")
                    .FontSize(9).FontColor(Colors.Grey.Darken2);
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("Produced by Argo Books.").FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        })).GeneratePdf(ms);

        return ms.ToArray();
    }

    /// <summary>The employer's RL-1 Summary: one page, every slip totalled.</summary>
    public static byte[] RenderSummary(Rl1Return rl1)
    {
        ArgumentNullException.ThrowIfNull(rl1);

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
                        left.Item().Text("RL-1 SUMMARY WORKSHEET").FontSize(20).SemiBold().FontColor(Colors.Blue.Darken2);
                        left.Item().Text("Summary of Source Deductions and Employer Contributions").FontSize(11);
                    });

                    row.ConstantItem(160).AlignRight().Column(right =>
                    {
                        right.Item().AlignRight().Text(rl1.TaxYear.ToString(CultureInfo.InvariantCulture))
                            .FontSize(20).SemiBold();
                        right.Item().AlignRight().Text("Tax year").FontSize(9).FontColor(Colors.Grey.Darken2);
                    });
                });
                // On the page rather than only in the app, because the PDF is what gets saved,
                // emailed and looked at again in February, by which time whatever the export
                // screen said is long gone.
                col.Item().PaddingTop(8).Background(Colors.Amber.Lighten4).Padding(8)
                    .Text(Rl1Service.FilingNotice).FontSize(8).FontColor(Colors.Grey.Darken4);

                col.Item().PaddingVertical(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
            });

            page.Content().PaddingVertical(10).Column(col =>
            {
                col.Spacing(16);

                col.Item().Column(e =>
                {
                    e.Item().Text("EMPLOYER").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                    e.Item().PaddingTop(4).Text(rl1.EmployerName).SemiBold();

                    foreach (string line in AddressLines(rl1.EmployerAddress))
                    {
                        e.Item().Text(line);
                    }

                    e.Item().PaddingTop(4).Text($"Identification number: {rl1.QuebecIdentificationNumber}").FontSize(9);
                });

                col.Item().Column(c =>
                {
                    c.Item().Text("TOTALS").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                    c.Item().PaddingTop(6).Column(rows =>
                    {
                        Plain(rows, "Number of RL-1 slips filed", rl1.Slips.Count.ToString(CultureInfo.InvariantCulture));
                        Box(rows, "A", "Total employment income", rl1.TotalEmploymentIncome, bold: true);
                        Box(rows, "B", "Total QPP contributions withheld", rl1.TotalQpp);
                        Box(rows, "B", "Total employer QPP contributions", rl1.TotalEmployerQpp);
                        Box(rows, "H", "Total QPIP premiums withheld", rl1.TotalQpip);
                        Box(rows, "H", "Total employer QPIP premiums", rl1.TotalEmployerQpip);
                        Box(rows, "E", "Total Quebec income tax withheld", rl1.TotalQuebecIncomeTax);
                    });
                });

                col.Item().Background(Colors.Grey.Lighten4).Padding(12).Row(r =>
                {
                    r.RelativeItem().Column(x =>
                    {
                        x.Item().Text("TOTAL DEDUCTIONS REPORTED").FontSize(11).SemiBold();
                        x.Item().Text("What should have been remitted to Revenu Quebec over the year")
                            .FontSize(9).FontColor(Colors.Grey.Darken2);
                    });
                    r.ConstantItem(140).AlignRight().AlignMiddle().Text(Money(rl1.TotalRemittable))
                        .FontSize(13).SemiBold();
                });

                // Said plainly because it is the single most likely way this summary is wrong.
                // The health services fund contribution is a real employer liability that Argo
                // Books does not calculate, so the figure above is not the whole remittance.
                col.Item().Text(
                        "This total covers QPP, QPIP and Quebec income tax only. It does NOT include the "
                        + "contribution to the health services fund, the contribution related to labour "
                        + "standards, or the workforce skills development contribution, which Argo Books does "
                        + "not calculate. Add those before comparing this against what you remitted.")
                    .FontSize(9).FontColor(Colors.Grey.Darken2);

                col.Item().Text(
                        "Employment insurance is federal. It is reported to the Canada Revenue Agency on the "
                        + "T4 Summary and is deliberately absent here.")
                    .FontSize(9).FontColor(Colors.Grey.Darken2);
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("Produced by Argo Books.").FontSize(8).FontColor(Colors.Grey.Darken1);
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

    private static void Box(ColumnDescriptor col, string box, string label, decimal value, bool bold = false) =>
        col.Item().PaddingTop(2).Row(r =>
        {
            r.ConstantItem(46).Text($"Box {box}").FontSize(9).FontColor(Colors.Grey.Darken2);

            var left = r.RelativeItem().Text(label);
            var right = r.ConstantItem(120).AlignRight().Text(Money(value));

            if (bold)
            {
                left.SemiBold();
                right.SemiBold();
            }
        });

    private static void Plain(ColumnDescriptor col, string label, string value) =>
        col.Item().PaddingTop(2).Row(r =>
        {
            r.ConstantItem(46).Text(string.Empty);
            r.RelativeItem().Text(label);
            r.ConstantItem(120).AlignRight().Text(value);
        });

    private static string FormatSin(string sin)
    {
        string digits = new((sin).Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 9 ? $"{digits[..3]} {digits[3..6]} {digits[6..]}" : "not provided";
    }

    private static string SlipCodeText(Rl1SlipCode code) => code switch
    {
        Rl1SlipCode.Amended => "A - Amended",
        Rl1SlipCode.Cancelled => "D - Cancelled",
        _ => "R - Original",
    };

    private static string Money(decimal value) => $"${value:N2}";
}
