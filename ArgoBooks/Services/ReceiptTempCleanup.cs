namespace ArgoBooks.Services;

/// <summary>
/// Removes receipt and invoice preview files from the temp directory. Rendered PDF pages, receipt
/// previews, bulk-scan thumbnails and invoice previews accumulate under %TEMP%/ArgoBooks and are
/// regenerated on demand. Recent ones are kept so reopening a receipt is quick, unless they came
/// from a password-protected company: those are readable copies of what the password is meant to
/// guard, so they go when the company closes.
/// </summary>
public static class ReceiptTempCleanup
{
    private static readonly string[] Subdirectories = ["Receipts", "ScanPreview", "BulkScanPreview", "InvoicePreviews"];

    /// <summary>Files not modified within this window are considered stale and deleted.</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    private static string Root => Path.Combine(Path.GetTempPath(), "ArgoBooks");

    // Present while the previews may hold a protected company's files. It outlives a crash, so
    // the next launch knows to clear them.
    private static string ProtectedMarker => Path.Combine(Root, ".protected-previews");

    /// <summary>
    /// Deletes cached preview files older than <see cref="MaxAge"/>, or all of them when the last
    /// run ended with a protected company's previews still on disk. Safe to call fire-and-forget
    /// at startup; never throws.
    /// </summary>
    public static Task CleanOldFilesAsync() => Task.Run(() =>
    {
        if (File.Exists(ProtectedMarker))
            ClearProtected();
        else
            DeleteFiles(DateTime.UtcNow - MaxAge);
    });

    /// <summary>Notes that a password-protected company is open, so its previews are not kept.</summary>
    public static void MarkProtected()
    {
        try
        {
            if (File.Exists(ProtectedMarker)) return;
            Directory.CreateDirectory(Root);
            File.WriteAllBytes(ProtectedMarker, []);
        }
        catch
        {
            // Without the marker the previews fall back to the age limit.
        }
    }

    /// <summary>
    /// Deletes every preview if a protected company has been open. Called when a company closes
    /// and when the app exits.
    /// </summary>
    public static void ClearProtected()
    {
        if (!File.Exists(ProtectedMarker)) return;

        DeleteFiles(DateTime.MaxValue);
        try { File.Delete(ProtectedMarker); } catch { /* cleared again next time */ }
    }

    private static void DeleteFiles(DateTime olderThanUtc)
    {
        foreach (var sub in Subdirectories)
        {
            var dir = Path.Combine(Root, sub);
            if (!Directory.Exists(dir))
                continue;

            try
            {
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < olderThanUtc)
                            File.Delete(file);
                    }
                    catch
                    {
                        // File in use or already gone, skip it.
                    }
                }
            }
            catch
            {
                // Directory enumeration failed, non-critical, skip this subdirectory.
            }
        }
    }
}
