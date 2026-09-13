using System.Globalization;
using System.Text;
using System.Xml.Linq;
using ArgoBooks.Core.Models.Payroll;

namespace ArgoBooks.Core.Services.Payroll;

/// <summary>
/// Writes a T4 return as the XML CRA accepts for electronic filing, following the **2026V4**
/// specification: T619 for the transmittal and T4 for the return.
///
/// That version is year-stamped and revised within the year, and both halves changed in ways
/// that would reject a previously working file: the T619 language code became required, and the
/// T4 gained a validation that the account number on the slip and on the summary must match.
/// Re-read the "What's new" section of both when preparing a January rate edition, and update
/// the version named here so the next person can tell whether anyone has. See
/// docs/Payroll rate updates.md, "What else needs a look each year".
///
/// Two of CRA's rules shape almost every decision here:
///
/// An optional element carrying no value causes the whole submission to be REJECTED, a
/// validation added in October 2025. So nothing is written speculatively: every optional
/// element is omitted unless it has content. Only the elements CRA marks required are always
/// present, and those are filled with a defined empty value rather than left blank.
///
/// The payroll account number on every slip must equal the one on the summary. It is
/// therefore taken from the return and written to both, never from anything per-employee.
/// </summary>
public static class T4XmlWriter
{
    /// <summary>
    /// Builds the whole submission: the T619 transmittal record, then the return.
    ///
    /// The T619 was left out once, on the reasoning that a transmittal belongs to whoever
    /// transmits rather than to the return. CRA's specification says otherwise in as many words,
    /// "T619 applicable to all return types", and shows it as the first child of Submission with
    /// the returns after it. A file without one is rejected on upload, which is a failure that
    /// arrives at the February deadline against a document whose every slip is correct.
    /// </summary>
    public static XDocument Build(T4Return t4)
    {
        ArgumentNullException.ThrowIfNull(t4);

        var slips = t4.Slips.Select(slip => BuildSlip(t4, slip));

        var root = new XElement("Submission",
            new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
            BuildTransmittal(t4),
            new XElement("Return",
                new XElement("T4",
                    slips,
                    BuildSummary(t4))));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    /// <summary>
    /// The T619. Says who is sending the submission and where CRA should write back.
    ///
    /// For this app's employer the transmitter and the employer are the same person, so the
    /// account number is the payroll account number from the return. CRA requires it to match
    /// the credentials used to sign in, which for a small employer filing their own return it
    /// does by definition.
    ///
    /// The field ORDER is the specification's own sequence rather than anything chosen here. It
    /// is an ordered schema, so a correct set of fields in a different order still rejects.
    /// </summary>
    private static XElement BuildTransmittal(T4Return t4)
    {
        string account = Upper(t4.PayrollAccountNumber, 15) ?? string.Empty;
        string phone = Digits(t4.ContactPhone, 10) ?? string.Empty;

        var contact = new XElement("CNTC",
            new XElement("cntc_nm", Name(t4.ContactName, 35) ?? string.Empty),
            new XElement("cntc_area_cd", phone.Length >= 10 ? phone[..3] : string.Empty),
            new XElement("cntc_phn_nbr", phone.Length >= 10 ? $"{phone[3..6]}-{phone[6..10]}" : string.Empty),
            new XElement("cntc_email_area", Text(t4.ContactEmail, 60) ?? string.Empty));

        var transmittal = new XElement("T619",
            new XElement("TransmitterAccountNumber", new XElement("bn15", account)),
            new XElement("sbmt_ref_id", SubmissionReference(t4)),

            // One return per submission, so one summary. This counts summaries and not slips:
            // sending the slip count would tell CRA to expect that many returns inside.
            new XElement("summ_cnt", "1"),
            new XElement("lang_cd", LanguageCode(t4.LanguageCode)),
            new XElement("TransmitterName", Required("l1_nm", Name(t4.EmployerName, 35))),
            new XElement("TransmitterCountryCode", TransmitterCountry(t4)),
            contact);

        return transmittal;
    }

    /// <summary>
    /// A short reference the transmitter makes up to tell one submission from another. CRA caps
    /// it at eight characters and rejects spaces, hyphens and punctuation, so it is built from
    /// the tax year and the account number rather than from anything free-form.
    /// </summary>
    private static string SubmissionReference(T4Return t4)
    {
        string digits = new(t4.PayrollAccountNumber.Where(char.IsAsciiDigit).ToArray());
        string tail = digits.Length >= 4 ? digits[^4..] : digits.PadLeft(4, '0');

        return $"{t4.TaxYear.ToString(CultureInfo.InvariantCulture)}{tail}";
    }

    /// <summary>E or F, and nothing else. Anything unrecognised is English.</summary>
    private static string LanguageCode(string? value) =>
        string.Equals(value?.Trim(), "F", StringComparison.OrdinalIgnoreCase) ? "F" : "E";

    /// <summary>
    /// Three letter ISO country code for where the transmitter is. Taken from the employer
    /// address, since they are the same party, and defaulting to Canada because an employer
    /// filing a T4 has a Canadian payroll account by definition.
    /// </summary>
    private static string TransmitterCountry(T4Return t4) =>
        CraFormat.Alpha3Country(t4.EmployerAddress.Country) ?? "CAN";

    public static string BuildString(T4Return t4)
    {
        var builder = new StringBuilder();
        using var writer = new Utf8StringWriter(builder);
        Build(t4).Save(writer, SaveOptions.None);
        return builder.ToString();
    }

    private static XElement BuildSlip(T4Return t4, T4Slip slip)
    {
        var name = new XElement("EMPE_NM", Required("snm", Name(slip.Surname, 20)));
        Add(name, "gvn_nm", Name(slip.GivenName, 12));
        Add(name, "init", Initial(slip.Initial));

        var address = new XElement("EMPE_ADDR");
        Add(address, "addr_l1_txt", Street(slip.Address.Street, 30));
        Add(address, "cty_nm", Street(slip.Address.City, 28));
        Add(address, "prov_cd", ProvinceCode(slip.Address.State, slip.Address.Country));
        Add(address, "cntry_cd", CraFormat.Alpha3Country(slip.Address.Country));
        Add(address, "pstl_cd", PostalCode(slip.Address.ZipCode, slip.Address.Country));

        var amounts = new XElement("T4_AMT");

        // Boxes 24 and 26 are required even when nil, which is why they are added
        // unconditionally while the others are not. CRA states explicitly that exempt
        // employment files 0.00 rather than omitting the element.
        Add(amounts, "empt_incamt", Money(slip.EmploymentIncome));

        // CRA: "Under no circumstances should amounts for both CPP and QPP appear on the same
        // slip." Quebec employees contribute to QPP, so their money goes in boxes 17 and 17A
        // and the CPP elements are omitted entirely rather than written as zero.
        if (slip.IsQuebec)
        {
            Add(amounts, "qpp_cntrb_amt", Money(slip.CppContributions));
            Add(amounts, "qppe_cntrb_amt", Money(slip.Cpp2Contributions));
        }
        else
        {
            Add(amounts, "cpp_cntrb_amt", Money(slip.CppContributions));
            Add(amounts, "cppe_cntrb_amt", Money(slip.Cpp2Contributions));
        }

        Add(amounts, "empe_eip_amt", Money(slip.EiPremiums));
        Add(amounts, "itx_ddct_amt", Money(slip.IncomeTaxDeducted));
        amounts.Add(new XElement("ei_insu_ern_amt", Money(slip.InsurableEarnings) ?? "0.00"));
        amounts.Add(new XElement("cpp_qpp_ern_amt", Money(slip.PensionableEarnings) ?? "0.00"));

        // Boxes 55 and 56, Quebec only. Optional, so omitted rather than zeroed elsewhere.
        Add(amounts, "prov_pip_amt", Money(slip.QpipPremiums));
        Add(amounts, "prov_insu_ern_amt", Money(slip.QpipInsurableEarnings));

        var element = new XElement("T4Slip", name);

        if (address.HasElements)
        {
            element.Add(address);
        }

        // All zeroes is CRA's defined value for a missing SIN. It is accepted and it is also a
        // flag: the guide warns that it stops the employee's CPP being credited to them.
        element.Add(new XElement("sin", Digits(slip.Sin, 9) is { Length: 9 } sin ? sin : "000000000"));
        Add(element, "empe_nbr", Text(slip.EmployeeNumber, 20));
        element.Add(new XElement("bn", Upper(t4.PayrollAccountNumber, 15) ?? string.Empty));
        element.Add(new XElement("cpp_qpp_xmpt_cd", slip.CppExemptAllYear ? "1" : "0"));
        element.Add(new XElement("ei_xmpt_cd", slip.EiExemptAllYear ? "1" : "0"));

        // Box 28's QPIP exemption. Only meaningful in Quebec, and optional, so it is written
        // only where it says something.
        if (slip.IsQuebec)
        {
            element.Add(new XElement("prov_pip_xmpt_cd", slip.QpipPremiums > 0 ? "0" : "1"));
        }
        element.Add(new XElement("rpt_tcd", ReportCode(t4.ReportType)));
        element.Add(new XElement("empt_prov_cd", Upper(slip.ProvinceOfEmployment, 2) ?? string.Empty));
        element.Add(new XElement("empr_dntl_ben_rpt_cd", ((int)slip.DentalBenefit).ToString(CultureInfo.InvariantCulture)));
        element.Add(amounts);

        return element;
    }

    private static XElement BuildSummary(T4Return t4)
    {
        var name = new XElement("EMPR_NM", Required("l1_nm", Name(t4.EmployerName, 30)));

        var address = new XElement("EMPR_ADDR");
        Add(address, "addr_l1_txt", Street(t4.EmployerAddress.Street, 30));
        Add(address, "cty_nm", Street(t4.EmployerAddress.City, 28));
        Add(address, "prov_cd", ProvinceCode(t4.EmployerAddress.State, t4.EmployerAddress.Country));
        Add(address, "cntry_cd", CraFormat.Alpha3Country(t4.EmployerAddress.Country));
        Add(address, "pstl_cd", PostalCode(t4.EmployerAddress.ZipCode, t4.EmployerAddress.Country));

        // CRA wants the area code separately from the rest, and the rest hyphenated as 3-4.
        string phone = Digits(t4.ContactPhone, 10) ?? string.Empty;
        var contact = new XElement("CNTC",
            new XElement("cntc_nm", Name(t4.ContactName, 22) ?? string.Empty),
            new XElement("cntc_area_cd", phone.Length >= 10 ? phone[..3] : string.Empty),
            new XElement("cntc_phn_nbr", phone.Length >= 10 ? $"{phone[3..6]}-{phone[6..10]}" : string.Empty));

        var totals = new XElement("T4_TAMT");
        Add(totals, "tot_empt_incamt", Money(t4.TotalEmploymentIncome));
        Add(totals, "tot_empe_cpp_amt", Money(t4.TotalEmployeeCpp));
        Add(totals, "tot_empe_cppe_amt", Money(t4.TotalEmployeeCpp2));
        Add(totals, "tot_empe_eip_amt", Money(t4.TotalEmployeeEi));
        Add(totals, "tot_itx_ddct_amt", Money(t4.TotalIncomeTax));
        Add(totals, "tot_empr_cpp_amt", Money(t4.TotalEmployerCpp));
        Add(totals, "tot_empr_cppe_amt", Money(t4.TotalEmployerCpp2));
        Add(totals, "tot_empr_eip_amt", Money(t4.TotalEmployerEi));

        var summary = new XElement("T4Summary",
            new XElement("bn", Upper(t4.PayrollAccountNumber, 15) ?? string.Empty),
            name);

        if (address.HasElements)
        {
            summary.Add(address);
        }

        summary.Add(contact);
        summary.Add(new XElement("tx_yr", t4.TaxYear.ToString(CultureInfo.InvariantCulture)));
        summary.Add(new XElement("slp_cnt", t4.Slips.Count.ToString(CultureInfo.InvariantCulture)));
        summary.Add(new XElement("rpt_tcd", SummaryReportCode(t4.ReportType)));

        // CRA accepts this for report type A only, and an optional element carrying no value
        // rejects the whole submission, so it is written only when there is both an amendment
        // and something to say.
        if (t4.ReportType == T4ReportType.Amendment)
        {
            Add(summary, "fileramendmentnote", Text(t4.AmendmentNote, 1309));
        }

        summary.Add(totals);

        return summary;
    }

    /// <summary>Adds the element only if there is something to put in it.</summary>
    private static void Add(XElement parent, string element, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            parent.Add(new XElement(element, value));
        }
    }

    private static XElement Required(string element, string? value) =>
        new(element, value ?? string.Empty);

    /// <summary>The slip's code. O, A or C.</summary>
    private static string ReportCode(T4ReportType type) => type switch
    {
        T4ReportType.Amendment => "A",
        T4ReportType.Cancel => "C",
        _ => "O",
    };

    /// <summary>
    /// The summary's code, which is NOT the same set. CRA lists only O and A for the summary
    /// while the slip also takes C, so a cancellation return carries C on its slips and A on
    /// the summary: the return as a whole is a correction to what was already filed.
    /// </summary>
    private static string SummaryReportCode(T4ReportType type) =>
        type == T4ReportType.Original ? "O" : "A";

    /// <summary>
    /// Dollars and cents, no separators. Negatives are not permitted by the specification, so
    /// anything below zero is written as nil rather than silently sign-flipped: a negative
    /// total means a voided run was mishandled upstream and should be found, not papered over.
    /// </summary>
    private static string? Money(decimal value) =>
        value <= 0 ? null : value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string? Text(string? value, int max)
    {
        string trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed[..Math.Min(trimmed.Length, max)];
    }

    /// <summary>
    /// A name in the characters CRA lists, truncated to the field width.
    ///
    /// The cleaning is a last resort rather than the intended fix: the employee form refuses a
    /// character CRA will not take, and year end validation lists any employee entered before it
    /// did. This is here so that a file is never written containing one, since a single comma in
    /// a name rejects the entire submission and the employer finds out at the deadline.
    /// </summary>
    private static string? Name(string? value, int max) => Text(CraFormat.CleanName(value), max);

    /// <summary>As <see cref="Name"/>, for the fields that may also carry / and #.</summary>
    private static string? Street(string? value, int max) => Text(CraFormat.CleanAddress(value), max);

    /// <summary>
    /// One alpha, as the specification requires. Taken from the second given name, which may
    /// begin with something that is not a letter once a name has been split on spaces.
    /// </summary>
    private static string? Initial(string? value)
    {
        string cleaned = CraFormat.CleanName(value);
        char? letter = cleaned.FirstOrDefault(char.IsLetter);

        return letter is > '\0' ? letter.Value.ToString() : null;
    }

    /// <summary>
    /// The address province.
    ///
    /// CRA: "when the employee's country code is neither CAN nor USA, enter ZZ in this field".
    /// A country the app cannot identify is left alone rather than forced to ZZ, since the most
    /// common reason for one is that no country was recorded at all and the address is Canadian.
    /// </summary>
    private static string? ProvinceCode(string? province, string? country)
    {
        if (CraFormat.Alpha3Country(country) is { } code && code is not ("CAN" or "USA"))
        {
            return "ZZ";
        }

        return Upper(province, 2);
    }

    /// <summary>
    /// The postal code with its separators removed, because a Canadian one is six characters and
    /// the specification allows a dash only for a USA or foreign code. Nearly everyone types the
    /// space.
    /// </summary>
    private static string? PostalCode(string? value, string? country) =>
        Upper(CraFormat.NormalizePostalCode(value, country), 10);

    private static string? Upper(string? value, int max) => Text(value, max)?.ToUpperInvariant();

    private static string? Digits(string? value, int max)
    {
        string digits = new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : digits[..Math.Min(digits.Length, max)];
    }
}
