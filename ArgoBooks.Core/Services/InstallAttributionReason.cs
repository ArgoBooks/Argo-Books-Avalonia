namespace ArgoBooks.Core.Services;

/// <summary>
/// Reads the FirstRunReporter marker file so the rest of the app can tell whether
/// the install came in with a referral token, without depending on FirstRunReporter itself.
/// </summary>
public static class InstallAttributionReason
{
    private const string MarkerFileName = "first_run_reported.marker";

    /// <summary>
    /// Returns the <c>reason=</c> value from the first-run marker:
    /// <c>"token"</c>, <c>"no_token"</c>, <c>"gave_up_after_retries"</c>, or
    /// <c>null</c> if the marker doesn't exist yet (FirstRunReporter hasn't finished).
    /// </summary>
    public static string? ReadFirstRunMarkerReason()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ArgoBooks",
                MarkerFileName);
            if (!File.Exists(path)) return null;

            foreach (var line in File.ReadAllLines(path))
            {
                if (line.StartsWith("reason=", StringComparison.Ordinal))
                {
                    return line.Substring("reason=".Length).Trim();
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The id FirstRunReporter gave this computer, which is what the website files the install
    /// and its survey answers under. Null until the first run has been reported.
    /// </summary>
    public static string? ReadMachineUuid()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ArgoBooks",
                "machine_uuid.txt");
            if (!File.Exists(path)) return null;
            var raw = File.ReadAllText(path).Trim();
            return Guid.TryParse(raw, out _) ? raw : null;
        }
        catch
        {
            return null;
        }
    }
}
