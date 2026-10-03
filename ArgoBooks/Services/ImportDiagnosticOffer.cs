using ArgoBooks.Core.Services;

namespace ArgoBooks.Services;

/// <summary>
/// Sends a file that would not import, so the reason can be found.
///
/// Sent without asking. An import failing for nobody's benefit is the thing this
/// exists to stop, and a dialog in front of someone whose import has just failed gets
/// dismissed. What stands in place of asking is disclosure: the behaviour is named in
/// Settings, on by default and switchable off, and described in the privacy policy, so
/// a person who does not want their statement sent can see that it happens and stop it.
///
/// Sent only for the failures where the file itself is the open question. Running out
/// of imports, a usage check that errored, a busy server and the user closing the
/// screen all produce no rows as well, and in none of those is there anything to
/// examine, so nothing leaves the machine.
/// </summary>
public static class ImportDiagnosticOffer
{
    /// <summary>Failure reasons where seeing the file would actually tell us something.</summary>
    private static bool IsAboutTheFile(string reason) =>
        reason.Contains("extract-empty", StringComparison.Ordinal)
        || reason.Contains("no-rows", StringComparison.Ordinal)
        || reason.Contains("unreadable", StringComparison.Ordinal);

    /// <summary>
    /// Sends the file in the background and returns at once. The person has already been
    /// told the import did not work, and nothing about this should hold them up or appear
    /// on screen. Does nothing when the setting is off, when the failure is not about the
    /// file, or when the file has gone.
    /// </summary>
    public static Task SendAsync(string filePath, string reason, int? pageCount = null)
    {
        if (!IsAboutTheFile(reason)) return Task.CompletedTask;
        if (App.SettingsService?.GlobalSettings.SendFailedImportsForDiagnosis != true) return Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            var sent = await ImportDiagnosticUploader.SendAsync(
                filePath, reason, App.ErrorLogger, pageCount);

            _ = App.TelemetryManager?.TrackFeatureAsync(
                Shared.Telemetry.FeatureName.ImportDiagnosticSent, sent ? reason : "failed");
        });

        return Task.CompletedTask;
    }
}
