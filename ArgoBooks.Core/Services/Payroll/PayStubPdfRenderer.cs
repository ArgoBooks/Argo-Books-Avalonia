using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Payroll;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ArgoBooks.Core.Services.Payroll;

/// <summary>
/// Renders one employee's pay statement to a PDF.
///
/// Everything printed comes off the stored <see cref="PayRunLine"/> rather than being
/// recalculated, so a stub reprinted next year is identical to the one handed over on the
/// day. Year-to-date figures are passed in for the same reason.
/// </summary>
public static class PayStubPdfRenderer
{
    /// <summary>One employee's stub, which is what gets handed to them.</summary>
    public static byte[] Render(PayRun run, PayRunLine line, PayrollYearToDate ytd,
                                CompanyData companyData, string currencySymbol = "$")
    {
        QuestPDF.Settings.License = LicenseType.Community;

        using var ms = new MemoryStream();

        Document.Create(container => container.Page(page =>
            Compose(page, run, line, ytd, companyData, currencySymbol))).GeneratePdf(ms);

        return ms.ToArray();
    }

    private static void Compose(PageDescriptor page, PayRun run, PayRunLine line,
                                PayrollYearToDate ytd, CompanyData companyData, string currencySymbol)
    {
        var company = companyData.Settings.Company;

        page.Size(PageSizes.A4);
        page.Margin(40);
        page.DefaultTextStyle(t => t.FontSize(10).FontFamily("Helvetica"));

        page.Header().Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text(company.Name).FontSize(18).SemiBold();
                    if (!string.IsNullOrWhiteSpace(company.Address))
                        left.Item().Text(company.Address);
                    var cityLine = string.Join(", ", new[] { company.City, company.ProvinceState, company.Country }
                        .Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (!string.IsNullOrWhiteSpace(cityLine))
                        left.Item().Text(cityLine);
                });

                row.ConstantItem(180).AlignRight().Column(right =>
                {
                    right.Item().AlignRight().Text("PAY STATEMENT")
                        .FontSize(20).SemiBold().FontColor(Colors.Blue.Darken2);
                    right.Item().AlignRight().Text(run.Id)
                        .FontSize(12).FontColor(Colors.Grey.Darken2);
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
                    e.Item().PaddingTop(4).Text(line.EmployeeName).SemiBold();
                    if (!string.IsNullOrWhiteSpace(line.Province))
                        e.Item().Text($"Province of employment: {line.Province}");
                });

                // Wide enough that the period fits on one line. At 220 it wrapped mid
                // range, which reads as two dates rather than one span.
                row.ConstantItem(260).Column(d =>
                {
                    Field(d, "Pay date:", run.PayDate.ToString("yyyy-MM-dd"));
                    Field(d, "Period:", $"{run.PeriodStart:yyyy-MM-dd} to {run.PeriodEnd:yyyy-MM-dd}");
                    Field(d, "Pay periods a year:", line.PayPeriodsPerYear.ToString());
                });
            });

            // Earnings
            col.Item().Column(c =>
            {
                c.Item().Text("EARNINGS").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                c.Item().PaddingTop(6).Column(rows =>
                {
                    // Hours belong on the base pay line rather than above it, so every
                    // line in this section is an amount and they visibly sum to gross.
                    Amount(rows, line.HoursWorked != 0
                            ? $"Base pay ({line.HoursWorked:0.##} hours)"
                            : "Base pay",
                        Money(line.BasePay, currencySymbol));

                    if (line.Bonus != 0)
                    {
                        Amount(rows, "Bonus", Money(line.Bonus, currencySymbol));
                    }

                    if (line.VacationPay != 0)
                    {
                        Amount(rows, "Vacation pay", Money(line.VacationPay, currencySymbol));
                    }

                    rows.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                    Amount(rows, "Gross pay", Money(line.GrossPay, currencySymbol), bold: true);
                });
            });

            // Deductions
            col.Item().Column(c =>
            {
                // A Quebec employee contributes to QPP, not CPP, and to QPIP, which has no
                // equivalent anywhere else. Both matter here rather than being cosmetic: the
                // total below is gross minus net, so a withheld premium with no line of its own
                // leaves the section not adding up in front of the person being paid.
                bool quebec = string.Equals(line.Province, "QC", StringComparison.OrdinalIgnoreCase);

                c.Item().Text("DEDUCTIONS").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                c.Item().PaddingTop(6).Column(rows =>
                {
                    Amount(rows, quebec ? "QPP" : "CPP", Money(line.CppEmployee, currencySymbol));

                    if (line.Cpp2Employee != 0)
                    {
                        Amount(rows, quebec ? "QPP2" : "CPP2", Money(line.Cpp2Employee, currencySymbol));
                    }

                    Amount(rows, "EI", Money(line.EiEmployee, currencySymbol));

                    if (line.QpipEmployee != 0)
                    {
                        Amount(rows, "QPIP", Money(line.QpipEmployee, currencySymbol));
                    }

                    Amount(rows, "Federal tax", Money(line.FederalTax, currencySymbol));
                    Amount(rows, $"{line.Province} tax", Money(line.ProvincialTax, currencySymbol));

                    rows.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                    Amount(rows, "Total deductions",
                        Money(line.GrossPay - line.NetPay, currencySymbol), bold: true);
                });
            });

            col.Item().Background(Colors.Grey.Lighten4).Padding(12).Row(r =>
            {
                r.RelativeItem().Text("NET PAY").FontSize(12).SemiBold();
                r.ConstantItem(140).AlignRight().Text(Money(line.NetPay, currencySymbol))
                    .FontSize(14).SemiBold();
            });

            // Year to date, which is what an employee checks a stub against.
            col.Item().Column(c =>
            {
                bool quebec = string.Equals(line.Province, "QC", StringComparison.OrdinalIgnoreCase);

                c.Item().Text("YEAR TO DATE").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                c.Item().PaddingTop(6).Column(rows =>
                {
                    Amount(rows, "Gross pay", Money(ytd.PensionableEarnings + line.GrossPay, currencySymbol));
                    Amount(rows, quebec ? "QPP" : "CPP",
                        Money(ytd.CppEmployee + line.CppEmployee, currencySymbol));

                    if (ytd.Cpp2Employee + line.Cpp2Employee != 0)
                    {
                        Amount(rows, quebec ? "QPP2" : "CPP2",
                            Money(ytd.Cpp2Employee + line.Cpp2Employee, currencySymbol));
                    }

                    Amount(rows, "EI", Money(ytd.EiEmployee + line.EiEmployee, currencySymbol));

                    if (ytd.QpipEmployee + line.QpipEmployee != 0)
                    {
                        Amount(rows, "QPIP", Money(ytd.QpipEmployee + line.QpipEmployee, currencySymbol));
                    }
                });
            });
        });

        page.Footer().AlignCenter().Text(t =>
        {
            t.Span("Calculated using CRA payroll deductions edition ").FontSize(8).FontColor(Colors.Grey.Darken1);
            t.Span(run.RateEditionId).FontSize(8).SemiBold().FontColor(Colors.Grey.Darken1);
        });
    }

    private static void Field(ColumnDescriptor col, string label, string value) =>
        col.Item().Row(r =>
        {
            r.RelativeItem().Text(label).FontColor(Colors.Grey.Darken2);
            r.ConstantItem(150).AlignRight().Text(value);
        });

    private static void Amount(ColumnDescriptor col, string label, string value,
                               bool bold = false, bool plain = false) =>
        col.Item().PaddingTop(2).Row(r =>
        {
            var left = r.RelativeItem().Text(label);
            var right = r.ConstantItem(140).AlignRight().Text(value);

            if (bold)
            {
                left.SemiBold();
                right.SemiBold();
            }
            else if (!plain)
            {
                left.FontColor(Colors.Grey.Darken3);
            }
        });

    private static string Money(decimal value, string symbol) => $"{symbol}{value:N2}";
}
