using System.Globalization;
using System.Text;
using System.Xml.Linq;
using ArgoBooks.Core.Models.Payroll;

namespace ArgoBooks.Core.Services.Payroll;

/// <summary>
/// Writes a Record of Employment as the XML ROE Web accepts as a payroll extract, following the
/// **Version 2.0** file layout in Appendix D of the ROE Web user requirements. Version 2.0 has
/// been mandatory since 1 September 2016.
///
/// Re-read Appendix D when preparing a January edition and update the version named here, the
/// same way T4XmlWriter names its specification.
///
/// Every length here comes from PayrollExtractXmlV2.xsd in ArgoBooks.Tests/Schemas/ServiceCanada,
/// not from Appendix D, because the two disagree: the prose describes MN as a middle name and the
/// schema caps it at four characters, and the address lines and surname differ too. RoeXmlTests
/// validates a generated file against that schema.
///
/// Two rules shape most of what follows.
///
/// Blocks 15C periods are numbered from the most recent, and the numbering is the meaning: PP 1
/// is the final pay period, PP 2 the one before it. Emitting them in storage order silently
/// reverses an employee's earnings history, which changes the benefit rather than failing.
///
/// Block 16 is never defaulted. The app knows somebody stopped being paid; it does not know why,
/// and quit, dismissal and leave are different legal statements. Build throws rather than guess.
///
/// This produces the file. It does not file anything: the employer uploads it through their own
/// ROE Web account, the same way the T4 XML goes through My Business Account.
/// </summary>
public static partial class RoeXmlWriter
{
    /// <summary>The only value ROE Web accepts for a version 2.0 payroll extract.</summary>
    private const string FileVersion = "W-2.0";

    private const string SoftwareVendor = "Argo Robots";

    private const string ProductName = "Argo Books";

    /// <summary>Block 15C tops out at 53 periods, the weekly case.</summary>
    private const int MaxPeriods = 53;

    // From PayrollExtractXmlV2.xsd, not from Appendix D: the prose and the schema disagree on
    // MN, LN and the address lines, and the schema is what rejects the file.
    private const int FirstNameMax = 20;
    private const int MiddleNameMax = 4;
    private const int LastNameMax = 28;
    private const int AddressLineMax = 35;
    private const int PostalCodeMax = 10;
    private const int ExtensionMax = 8;

    /// <summary>
    /// Block 17A's vacation pay code for "Paid because no longer working", the only one of the
    /// four this writes. Code 1, "Included with each pay", is never reported, and codes 3 and 4
    /// are future-dated payments pay runs know nothing about.
    /// </summary>
    private const string VacationPaidOnLeavingCode = "2";

    /// <summary>The schema's pattern: nine digits, R, then P (W is Appendix D's demo account), then four digits.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"^[0-9]{9}R[PW][0-9]{4}$")]
    private static partial System.Text.RegularExpressions.Regex BusinessNumber();

    /// <summary>
    /// Builds a submission carrying one ROE. ROE Web takes up to 1200 in a file, so the header
    /// holds a list rather than a single record even though a small payroll files one at a time.
    /// </summary>
    public static XDocument Build(RoeWorksheet sheet, string productVersion, bool draft = false)
        => Build([sheet], productVersion, draft);

    public static XDocument Build(IReadOnlyList<RoeWorksheet> sheets, string productVersion, bool draft = false)
    {
        ArgumentNullException.ThrowIfNull(sheets);

        if (sheets.Count == 0)
        {
            throw new ArgumentException("A payroll extract needs at least one ROE.", nameof(sheets));
        }

        var header = new XElement("ROEHEADER",
            new XAttribute("FileVersion", FileVersion),
            new XAttribute("SoftwareVendor", Text(SoftwareVendor, 100)!),
            new XAttribute("ProductName", Text(ProductName, 100)!));

        // Optional, so it is written only when there is a version to write. An attribute
        // present and empty is the kind of thing a validator rejects for no visible reason.
        string? version = Text(productVersion, 10);
        if (version != null)
        {
            header.Add(new XAttribute("ProductVersion", version));
        }

        foreach (RoeWorksheet sheet in sheets)
        {
            header.Add(BuildRoe(sheet, draft));
        }

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), header);
    }

    /// <summary>UTF-8 without a byte order mark, which ROE Web does not expect.</summary>
    public static string BuildString(RoeWorksheet sheet, string productVersion, bool draft = false)
        => BuildString([sheet], productVersion, draft);

    public static string BuildString(IReadOnlyList<RoeWorksheet> sheets, string productVersion, bool draft = false)
    {
        var builder = new StringBuilder();
        using var writer = new Utf8StringWriter(builder);
        Build(sheets, productVersion, draft).Save(writer, SaveOptions.None);
        return builder.ToString();
    }

    /// <summary>
    /// Everything that has to be true before a file is worth producing.
    ///
    /// Returned rather than thrown so the export screen can show all of them at once, which is
    /// the difference between filling the form in once and filling it in five times.
    /// </summary>
    public static IReadOnlyList<string> Validate(RoeWorksheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var problems = new List<string>();

        if (Digits(sheet.Sin, 9) is not { Length: 9 })
        {
            problems.Add("Block 8 needs a nine digit social insurance number.");
        }

        // The schema pins the shape, not just the length: nine digits, R, P or W, four digits.
        if (!BusinessNumber().IsMatch(PayrollAccount(sheet.PayrollAccountNumber) ?? string.Empty))
        {
            problems.Add("Block 5 needs the CRA payroll account number in the form 123456789RP0001, set on the company.");
        }

        // FN and LN are both minOccurs="1", so one token is not a usable name here even though
        // it reads like one. Reported rather than padded: a blank FN would validate against the
        // schema and then be a Record of Employment with no first name on it.
        if (SplitName(sheet.EmployeeName) is null)
        {
            problems.Add("Block 9 needs the employee's first and last name.");
        }

        // A1, A2 and PC are mandatory in the schema; A3 is not.
        if (Text(sheet.Address.Street, AddressLineMax) == null)
        {
            problems.Add("Block 9 needs the employee's street address.");
        }

        if (Text(sheet.Address.City, AddressLineMax) == null)
        {
            problems.Add("Block 9 needs the employee's city.");
        }

        if (PostalCode(sheet.Address.ZipCode) == null)
        {
            problems.Add("Block 9 needs the employee's postal code.");
        }

        if (sheet.FirstDayWorked == null)
        {
            problems.Add("Block 10 needs the first day the employee worked.");
        }

        if (sheet.LastDayPaid == null)
        {
            problems.Add("Block 11 needs the last day for which they were paid.");
        }

        if (sheet.FinalPeriodEnd == null)
        {
            problems.Add("Block 12 needs the end of the final pay period.");
        }

        // Service Canada checks this one, and it is easy to produce: an end date after the last
        // pay period is what a final unpaid week looks like.
        if (sheet.LastDayPaid != null && sheet.FinalPeriodEnd != null && sheet.FinalPeriodEnd < sheet.LastDayPaid)
        {
            problems.Add("Block 12 cannot be earlier than block 11.");
        }

        if (sheet.TotalInsurableHours == null)
        {
            problems.Add("Block 15A needs the total insurable hours. "
                         + (sheet.HoursUnavailableReason ?? "They could not be worked out from the pay runs."));
        }

        if (sheet.Periods.Count == 0)
        {
            problems.Add("Block 15C needs at least one pay period.");
        }

        if (sheet.Reason == null)
        {
            problems.Add("Block 16 needs a reason for issuing this ROE.");
        }

        if (sheet.Recall == RoeRecall.ExpectedDate && sheet.RecallDate == null)
        {
            problems.Add("Block 14 needs the expected date of recall.");
        }

        if (Name(sheet.ContactFirstName, 20) == null || Name(sheet.ContactLastName, 20) == null)
        {
            problems.Add("Block 16 needs the name of the person Service Canada should contact.");
        }

        if (NationalPhone(sheet.ContactPhone) == null)
        {
            problems.Add("Block 16 needs a ten digit contact telephone number.");
        }

        // Appendix D's range for the amount.
        if (VacationPayOnLeaving(sheet) is < 0.01m or > 9_999_999.99m)
        {
            problems.Add("Block 17A needs an amount between 0.01 and 9,999,999.99.");
        }

        return problems;
    }

    private static XElement BuildRoe(RoeWorksheet sheet, bool draft)
    {
        IReadOnlyList<string> problems = Validate(sheet);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "This ROE cannot be exported yet: " + string.Join(" ", problems));
        }

        var roe = new XElement("ROE",
            new XAttribute("PrintingLanguage", RoeCodes.Language(sheet.Language)),
            new XAttribute("Issue", draft ? "D" : "S"));

        // Block 3, the employer's own reference. Optional.
        Add(roe, "B3", Text(sheet.PayrollReferenceNumber, 26));

        roe.Add(new XElement("B5", PayrollAccount(sheet.PayrollAccountNumber)));
        roe.Add(new XElement("B6", RoeCodes.PayPeriodType(sheet.PayPeriodType)));
        roe.Add(new XElement("B8", Digits(sheet.Sin, 9)));
        roe.Add(BuildEmployee(sheet));
        roe.Add(new XElement("B10", Date(sheet.FirstDayWorked)));
        roe.Add(new XElement("B11", Date(sheet.LastDayPaid)));
        roe.Add(new XElement("B12", Date(sheet.FinalPeriodEnd)));

        Add(roe, "B13", Text(sheet.Occupation, 40));

        roe.Add(BuildRecall(sheet));

        // Block 15A is whole hours. Service Canada's own layout caps the field at four digits,
        // which is why a decimal is rounded rather than truncated to fit.
        roe.Add(new XElement("B15A", Hours(sheet.TotalInsurableHours)));
        roe.Add(BuildPeriods(sheet));
        roe.Add(BuildReason(sheet));

        // Block 17A comes only from a figure the employer confirmed on the form, never from the
        // worksheet's final period alone: see RoeWorksheet.VacationPayOnLeaving.
        if (VacationPayOnLeaving(sheet) is { } vacation)
        {
            roe.Add(new XElement("B17A",
                new XElement("VP",
                    new XAttribute("nbr", "1"),
                    new XElement("CD", VacationPaidOnLeavingCode),
                    new XElement("AMT", Money(vacation)))));
        }

        Add(roe, "B18", Text(sheet.Comments, 160));

        roe.Add(new XElement("B20", RoeCodes.Language(sheet.Language)));

        return roe;
    }

    /// <summary>
    /// Block 9. The name is stored as one string and the schema wants three elements, so it is
    /// split on whitespace: first token to FN, last to LN, anything between to MN.
    ///
    /// MN is four characters, not a middle name: Appendix D calls it a middle name and the
    /// schema caps it at an initial. Anything longer is cut rather than refused, because a
    /// middle name is optional and losing it costs nothing.
    /// </summary>
    private static XElement BuildEmployee(RoeWorksheet sheet)
    {
        (string first, string? middle, string last) = SplitName(sheet.EmployeeName)!.Value;

        var employee = new XElement("B9", new XElement("FN", first));
        Add(employee, "MN", middle);
        employee.Add(new XElement("LN", last));

        // A1, A2 and PC are mandatory, A3 is not, so only A3 is conditional.
        employee.Add(new XElement("A1", Text(sheet.Address.Street, AddressLineMax)));
        employee.Add(new XElement("A2", Text(sheet.Address.City, AddressLineMax)));
        Add(employee, "A3", Text(sheet.Address.State, AddressLineMax));
        employee.Add(new XElement("PC", PostalCode(sheet.Address.ZipCode)));

        return employee;
    }

    /// <summary>
    /// First, optional middle, last. Null when there is no usable pair, which the caller reports
    /// rather than papering over: both FN and LN are mandatory in the schema.
    /// </summary>
    private static (string First, string? Middle, string Last)? SplitName(string? name)
    {
        string[] parts = (name ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 2)
        {
            return null;
        }

        string? first = Name(parts[0], FirstNameMax);
        string? last = Name(parts[^1], LastNameMax);

        if (first == null || last == null)
        {
            return null;
        }

        string? middle = parts.Length > 2 ? Name(string.Join(' ', parts[1..^1]), MiddleNameMax) : null;

        return (first, middle, last);
    }

    /// <summary>
    /// Block 14. Mandatory as a block even when there is nothing to say, which is what the
    /// Unknown code is for. The date is written only under Y, because a date under N or U is
    /// a contradiction rather than extra information.
    /// </summary>
    private static XElement BuildRecall(RoeWorksheet sheet)
    {
        var recall = new XElement("B14", new XElement("CD", RoeCodes.Recall(sheet.Recall)));

        if (sheet.Recall == RoeRecall.ExpectedDate)
        {
            recall.Add(new XElement("DT", Date(sheet.RecallDate)));
        }

        return recall;
    }

    /// <summary>
    /// Block 15C. PP 1 is the final pay period and the numbering runs backwards from there,
    /// which is why the worksheet stores them most recent first and they are emitted in that
    /// order rather than sorted.
    /// </summary>
    private static XElement BuildPeriods(RoeWorksheet sheet)
    {
        var periods = new XElement("B15C");
        int number = 1;

        foreach (RoePayPeriod period in sheet.Periods.Take(MaxPeriods))
        {
            periods.Add(new XElement("PP",
                new XAttribute("nbr", number.ToString(CultureInfo.InvariantCulture)),
                new XElement("AMT", Money(period.InsurableEarnings))));
            number++;
        }

        return periods;
    }

    /// <summary>
    /// Block 16. Carries the separation code and the person Service Canada calls about it, which
    /// is the same block on the paper form and catches people out because the two look unrelated.
    /// </summary>
    private static XElement BuildReason(RoeWorksheet sheet)
    {
        string phone = NationalPhone(sheet.ContactPhone)!;

        var reason = new XElement("B16",
            new XElement("CD", RoeCodes.Separation(sheet.Reason!.Value)),
            new XElement("FN", Name(sheet.ContactFirstName, 20)),
            new XElement("LN", Name(sheet.ContactLastName, 20)),
            new XElement("AC", phone[..3]),
            new XElement("TEL", phone[3..]));

        Add(reason, "EXT", Digits(sheet.ContactPhoneExtension, ExtensionMax));

        return reason;
    }

    /// <summary>Adds the element only when there is something to put in it.</summary>
    private static void Add(XElement parent, string element, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            parent.Add(new XElement(element, value));
        }
    }

    /// <summary>CCYY-MM-DD, which is the only date format the layout accepts.</summary>
    private static string Date(DateTime? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>Two decimal places, always, with a dot. Negatives are not a thing an ROE carries.</summary>
    private static string Money(decimal value) =>
        Math.Max(0m, Math.Round(value, 2, MidpointRounding.AwayFromZero))
            .ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Whole hours, capped at the four digit field.</summary>
    private static string Hours(decimal? value) =>
        Math.Clamp(Math.Round(value ?? 0m, 0, MidpointRounding.AwayFromZero), 0m, 9999m)
            .ToString("0", CultureInfo.InvariantCulture);

    private static string? Text(string? value, int max)
    {
        string trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed[..Math.Min(trimmed.Length, max)];
    }

    private static string? Name(string? value, int max) => Text(value, max);

    /// <summary>
    /// Block 5 without its spaces. The T4 accepts the number typed as "123456789 RP 0001", so a
    /// number that files a T4 has to export an ROE too.
    /// </summary>
    private static string? PayrollAccount(string? value)
    {
        string cleaned = new string((value ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>Block 17A's amount as it will be written, or null when there is none to write.</summary>
    private static decimal? VacationPayOnLeaving(RoeWorksheet sheet)
    {
        decimal amount = Math.Round(sheet.VacationPayOnLeaving ?? 0m, 2, MidpointRounding.AwayFromZero);
        return amount == 0m ? null : amount;
    }

    /// <summary>
    /// Block 16 wants a bare ten digit number. A pasted or dial-code-prefixed North American
    /// number arrives as eleven digits starting with 1, and taking the first ten of that shifts
    /// every digit one place left, which produces a valid looking wrong phone number.
    /// </summary>
    internal static string? NationalPhone(string? value)
    {
        string digits = new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

        if (digits.Length == 11 && digits[0] == '1')
        {
            digits = digits[1..];
        }

        return digits.Length == 10 ? digits : null;
    }

    private static string? Digits(string? value, int max)
    {
        string digits = new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : digits[..Math.Min(digits.Length, max)];
    }

    /// <summary>Canadian postal codes go up with no space, which is how the layout wants them.</summary>
    private static string? PostalCode(string? value)
    {
        string cleaned = new((value ?? string.Empty).Where(char.IsAsciiLetterOrDigit).ToArray());
        return cleaned.Length == 0 ? null : cleaned[..Math.Min(cleaned.Length, PostalCodeMax)].ToUpperInvariant();
    }
}
