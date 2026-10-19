namespace ArgoBooks.Core.Services;

/// <summary>
/// Works out, on the launch after an accepted update, whether that update installed, so an update that did not take is reported rather than unseen.
/// </summary>
public static class UpdateOutcome
{
    /// <summary>The website keeps this much of a feature event's context and drops the rest.</summary>
    private const int MaxContextLength = 64;

    /// <summary>
    /// True when the version running is the one the update was installing, or newer.
    /// </summary>
    public static bool IsInstalled(string runningVersion, string targetVersion)
    {
        if (Version.TryParse(runningVersion, out var running) && Version.TryParse(targetVersion, out var target))
            return running >= target;
        return string.Equals(runningVersion, targetVersion, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The telemetry context for an update: "2.0.19>2.0.20", then any detail, cut to what the website keeps.
    /// </summary>
    public static string Context(string fromVersion, string targetVersion, string? detail = null)
    {
        var text = string.IsNullOrEmpty(detail) ? $"{fromVersion}>{targetVersion}" : $"{fromVersion}>{targetVersion} {detail}";
        return text.Length <= MaxContextLength ? text : text[..MaxContextLength];
    }
}
