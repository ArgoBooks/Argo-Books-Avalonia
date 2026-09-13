using System.Globalization;
using ArgoBooks.Core.Models.Payroll;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ArgoBooks.Core.Services.Payroll;

/// <summary>
/// Renders T4 slips and the T4 Summary as PDFs.
///
/// These are the employee's copy and the employer's record. They are laid out to be readable
/// rather than to imitate CRA's pre-printed form: a slip printed from here is not a
/// substitute for the filed return, which goes to CRA as XML. Every box carries its official
/// number so an employee can match it against their tax software, which is what they will
/// actually do with it.
/// </summary>
public static class T4PdfRenderer
{
    /// <summary>One employee's slip.</summary>
    public static byte[] RenderSlip(T4Return t4, T4Slip slip)
    {
        ArgumentNullException.ThrowIfNull(t4);
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
                        left.Item().Text("T4").FontSize(22).SemiBold().FontColor(Colors.Blue.Darken2);
                        left.Item().Text("Statement of Remuneration Paid").FontSize(11);
                    });

                    row.ConstantItem(160).AlignRight().Column(right =>
                    {
                        right.Item().AlignRight().Text(t4.TaxYear.ToString(CultureInfo.InvariantCulture))
                            .FontSize(22).SemiBold();
                        right.Item().AlignRight().Text("Tax year").FontSize(9).FontColor(Colors.Grey.Darken2);
                    });
                });
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
                        e.Item().PaddingTop(4).Text($"{slip.GivenName} {slip.Initial} {slip.Surname}".Replace("  ", " ").Trim()).SemiBold();

                        foreach (string line in AddressLines(slip.Address))
                        {
                            e.Item().Text(line);
                        }

                        e.Item().PaddingTop(4).Text($"Box 12  Social insurance number: {FormatSin(slip.Sin)}").FontSize(9);
                    });

                    row.ConstantItem(220).Column(d =>
                    {
                        d.Item().Text("EMPLOYER").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                        d.Item().PaddingTop(4).Text(t4.EmployerName).SemiBold();

                        foreach (string line in AddressLines(t4.EmployerAddress))
                        {
                            d.Item().Text(line);
                        }

                        d.Item().PaddingTop(4).Text($"Box 54  Account: {t4.PayrollAccountNumber}").FontSize(9);
                        d.Item().Text($"Box 10  Province of employment: {slip.ProvinceOfEmployment}").FontSize(9);
                    });
                });

                col.Item().Column(c =>
                {
                    c.Item().Text("AMOUNTS").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                    c.Item().PaddingTop(6).Column(rows =>
                    {
                        Box(rows, 14, "Employment income", slip.EmploymentIncome, bold: true);

                        // A Quebec employee contributes to QPP, which lives in different boxes.
                        // Printing it against box 16 would not merely be mislabelled, it would
                        // disagree with the slip filed for them.
                        Box(rows, slip.IsQuebec ? "17" : "16",
                            slip.IsQuebec ? "Employee's QPP contributions" : "Employee's CPP contributions",
                            slip.CppContributions);

                        if (slip.Cpp2Contributions > 0)
                        {
                            Box(rows, slip.IsQuebec ? "17A" : "16A",
                                slip.IsQuebec ? "Employee's second QPP contributions" : "Employee's second CPP contributions",
                                slip.Cpp2Contributions);
                        }

                        Box(rows, 18, "Employee's EI premiums", slip.EiPremiums);
                        Box(rows, 22, "Income tax deducted", slip.IncomeTaxDeducted);
                        Box(rows, 24, "EI insurable earnings", slip.InsurableEarnings);
                        Box(rows, 26, slip.IsQuebec ? "QPP pensionable earnings" : "CPP pensionable earnings",
                            slip.PensionableEarnings);

                        if (slip.IsQuebec)
                        {
                            Box(rows, 55, "QPIP premiums", slip.QpipPremiums);
                            Box(rows, 56, "QPIP insurable earnings", slip.QpipInsurableEarnings);
                        }
                    });
                });

                col.Item().Column(c =>
                {
                    c.Item().Text("OTHER").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                    c.Item().PaddingTop(6).Column(rows =>
                    {
                        Plain(rows, 28, "CPP exempt", slip.CppExemptAllYear ? "Yes" : "No");
                        Plain(rows, 28, "EI exempt", slip.EiExemptAllYear ? "Yes" : "No");
                        Plain(rows, 45, "Employer-offered dental benefits", DentalText(slip.DentalBenefit));
                    });
                });

                col.Item().PaddingTop(8).Text(
                        "Keep this slip for your records. The amounts above are reported to the Canada "
                        + "Revenue Agency and should match what you enter on your tax return.")
                    .FontSize(9).FontColor(Colors.Grey.Darken2);
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("Produced by Argo Books. Not a substitute for the return filed with CRA.")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        })).GeneratePdf(ms);

        return ms.ToArray();
    }

    /// <summary>The employer's T4 Summary: one page, every slip totalled.</summary>
    public static byte[] RenderSummary(T4Return t4)
    {
        ArgumentNullException.ThrowIfNull(t4);

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
                        left.Item().Text("T4 SUMMARY").FontSize(20).SemiBold().FontColor(Colors.Blue.Darken2);
                        left.Item().Text("Summary of Remuneration Paid").FontSize(11);
                    });

                    row.ConstantItem(160).AlignRight().Column(right =>
                    {
                        right.Item().AlignRight().Text(t4.TaxYear.ToString(CultureInfo.InvariantCulture))
                            .FontSize(20).SemiBold();
                        right.Item().AlignRight().Text("Tax year").FontSize(9).FontColor(Colors.Grey.Darken2);
                    });
                });
                col.Item().PaddingVertical(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
            });

            page.Content().PaddingVertical(10).Column(col =>
            {
                col.Spacing(16);

                col.Item().Column(e =>
                {
                    e.Item().Text("EMPLOYER").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                    e.Item().PaddingTop(4).Text(t4.EmployerName).SemiBold();

                    foreach (string line in AddressLines(t4.EmployerAddress))
                    {
                        e.Item().Text(line);
                    }

                    e.Item().PaddingTop(4).Text($"Payroll account: {t4.PayrollAccountNumber}").FontSize(9);
                    e.Item().Text($"Contact: {t4.ContactName}  {t4.ContactPhone}").FontSize(9);
                });

                col.Item().Column(c =>
                {
                    c.Item().Text("TOTALS").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                    c.Item().PaddingTop(6).Column(rows =>
                    {
                        Plain(rows, 88, "Total number of T4 slips filed", t4.Slips.Count.ToString(CultureInfo.InvariantCulture));
                        Box(rows, 14, "Total employment income", t4.TotalEmploymentIncome, bold: true);
                        Box(rows, 16, "Total employees' CPP contributions", t4.TotalEmployeeCpp);

                        if (t4.TotalEmployeeCpp2 > 0)
                        {
                            Box(rows, "16A", "Total employees' second CPP contributions", t4.TotalEmployeeCpp2);
                        }

                        Box(rows, 27, "Total employer's CPP contributions", t4.TotalEmployerCpp);
                        Box(rows, 18, "Total employees' EI premiums", t4.TotalEmployeeEi);
                        Box(rows, 19, "Total employer's EI premiums", t4.TotalEmployerEi);
                        Box(rows, 22, "Total income tax deducted", t4.TotalIncomeTax);
                    });
                });

                // What the employer should already have sent CRA across the year. Putting it
                // here is the point of the summary: a difference against what was actually
                // remitted is the thing that needs explaining, and it is easier to see now
                // than after CRA asks.
                decimal remittable = t4.TotalEmployeeCpp + t4.TotalEmployeeCpp2 + t4.TotalEmployerCpp
                                     + t4.TotalEmployerCpp2 + t4.TotalEmployeeEi + t4.TotalEmployerEi
                                     + t4.TotalIncomeTax;

                col.Item().Background(Colors.Grey.Lighten4).Padding(12).Row(r =>
                {
                    r.RelativeItem().Column(x =>
                    {
                        x.Item().Text("TOTAL DEDUCTIONS REPORTED").FontSize(11).SemiBold();
                        x.Item().Text("What should have been remitted to CRA over the year")
                            .FontSize(9).FontColor(Colors.Grey.Darken2);
                    });
                    r.ConstantItem(140).AlignRight().AlignMiddle().Text(Money(remittable)).FontSize(13).SemiBold();
                });
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("Produced by Argo Books. Not a substitute for the return filed with CRA.")
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

    private static void Box(ColumnDescriptor col, int box, string label, decimal value, bool bold = false) =>
        Box(col, box.ToString(CultureInfo.InvariantCulture), label, value, bold);

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

    private static void Plain(ColumnDescriptor col, int box, string label, string value) =>
        col.Item().PaddingTop(2).Row(r =>
        {
            r.ConstantItem(46).Text($"Box {box}").FontSize(9).FontColor(Colors.Grey.Darken2);
            r.RelativeItem().Text(label);
            r.ConstantItem(120).AlignRight().Text(value);
        });

    /// <summary>Grouped the way a SIN is normally written, so it can be checked at a glance.</summary>
    private static string FormatSin(string sin)
    {
        string digits = new(sin.Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 9 ? $"{digits[..3]} {digits[3..6]} {digits[6..]}" : "not provided";
    }

    private static string DentalText(DentalBenefitCode code) => code switch
    {
        DentalBenefitCode.PayeeOnly => "2 - Payee only",
        DentalBenefitCode.PayeeSpouseAndChildren => "3 - Payee, spouse and dependent children",
        DentalBenefitCode.PayeeAndSpouse => "4 - Payee and spouse",
        DentalBenefitCode.PayeeAndChildren => "5 - Payee and dependent children",
        _ => "1 - Not eligible",
    };

    private static string Money(decimal value) => $"${value:N2}";
}
