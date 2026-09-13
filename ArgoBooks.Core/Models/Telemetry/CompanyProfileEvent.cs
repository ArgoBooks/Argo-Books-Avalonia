namespace ArgoBooks.Core.Models.Telemetry;

/// <summary>
/// Describes the business the user is keeping books for.
///
/// <para>
/// This is the one telemetry event that is NOT anonymous, and it is deliberate: knowing
/// which industries and currencies people actually run on is worth more than any number of
/// feature counters. A sole trader's company name is frequently their own name, so treat
/// everything here as personal data. It is disclosed in /legal/privacy.php under "Business
/// Profile Data". Adding a field here means changing that page in the same commit, and adding
/// it to the allowlist in api/data/telemetry_filter.php, which drops anything it does not
/// recognise.
/// </para>
///
/// <para>
/// Emitted once per session for the company that is open, not on every change, so a user
/// who works all day produces one of these rather than a stream of near-identical rows.
/// </para>
/// </summary>
public class CompanyProfileEvent : TelemetryEvent
{
    /// <inheritdoc />
    public override TelemetryDataType DataType => TelemetryDataType.CompanyProfile;

    /// <summary>Company name as the user typed it.</summary>
    public string? CompanyName { get; set; }

    /// <summary>Sole proprietorship, partnership, corporation and so on.</summary>
    public string? BusinessType { get; set; }

    /// <summary>Retail, consulting, construction and so on.</summary>
    public string? Industry { get; set; }

    /// <summary>Country the business operates in, which need not match the IP country.</summary>
    public string? Country { get; set; }

    /// <summary>ISO 4217 code the books are kept in.</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// The language the app is displayed in, as its English name, which is how the setting
    /// stores it. Not the same question as Country: plenty of people run an English app in a
    /// country whose language is not English, and that gap is exactly what tells us which
    /// translations are actually being used rather than merely available.
    /// </summary>
    public string? Language { get; set; }
}
