namespace ArgoBooks.Core.Services;

/// <summary>
/// Creates the per-company temp directories that hold a company's decrypted files. Centralizes the
/// creation and owner-only permission hardening so <see cref="CompanyManager"/> and
/// <see cref="FileService"/> can't drift apart.
/// </summary>
internal static class SecureTempDirectory
{
    /// <summary>
    /// Creates a unique ArgoBooks temp directory under the OS temp path. On Unix the directory is
    /// restricted to the owner so other local users on a shared machine can't read the decrypted
    /// company files; this is a no-op on Windows (temp is already per-user).
    /// </summary>
    public static string Create()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "ArgoBooks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
            catch { /* best effort; permissions hardening only */ }
        }
        return tempPath;
    }

    /// <summary>
    /// The marker file that keeps an open company's directory from being deleted underneath it.
    /// Excluded from the archive by <see cref="CompressionService"/>, so it never reaches a
    /// <c>.argo</c> file.
    /// </summary>
    public const string InUseFileName = ".inuse";

    /// <summary>
    /// Marks <paramref name="directory"/> as belonging to an open company by holding an exclusive
    /// handle on a marker file inside it. Windows refuses to delete a directory containing an open
    /// file, so the cleanup tools that empty the temp folder skip this one instead of taking a
    /// company out from under the person editing it. The handle deletes the marker when it closes,
    /// so nothing is left behind, and on Unix, where an open handle does not prevent unlinking,
    /// this is merely inert. Returns null when the marker cannot be taken; callers carry on, since
    /// the directory is still usable.
    /// </summary>
    public static FileStream? Hold(string directory)
    {
        try
        {
            return new FileStream(
                Path.Combine(directory, InUseFileName),
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// A directory younger than this is left alone. One being filled by a company that is still
    /// opening in another window has no marker yet.
    /// </summary>
    private static readonly TimeSpan AbandonedAfter = TimeSpan.FromHours(1);

    /// <summary>
    /// Deletes the working directories no running copy of the app is using. A crash, a forced
    /// shutdown or the power going leaves one behind, and it holds the whole company unencrypted,
    /// password or not. Returns how many were deleted.
    /// </summary>
    public static int DeleteAbandoned(string? root = null, DateTime? nowUtc = null)
    {
        root ??= Path.Combine(Path.GetTempPath(), "ArgoBooks");
        var cutoff = (nowUtc ?? DateTime.UtcNow) - AbandonedAfter;
        var deleted = 0;

        try
        {
            if (!Directory.Exists(root)) return 0;

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                try
                {
                    if (!IsWorkingDirectoryName(Path.GetFileName(directory))
                        || Directory.GetCreationTimeUtc(directory) > cutoff
                        || IsHeld(directory))
                        continue;

                    Directory.Delete(directory, recursive: true);
                    deleted++;
                }
                catch
                {
                    // In use after all, or not ours to delete. The next launch tries again.
                }
            }
        }
        catch
        {
            // The temp folder could not be listed. Nothing to clean this time.
        }

        return deleted;
    }

    // Create() names a directory with a GUID in "N" form: 32 hex digits. The other folders the
    // app keeps beside them (Receipts, locks, Sample) do not match and are left alone.
    private static bool IsWorkingDirectoryName(string name)
        => name.Length == 32 && name.All(Uri.IsHexDigit);

    private static bool IsHeld(string directory)
    {
        var marker = Path.Combine(directory, InUseFileName);
        if (!File.Exists(marker)) return false;

        try
        {
            // The owner holds the marker with no sharing, so this only opens once it has gone.
            using var probe = new FileStream(marker, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch
        {
            return true;
        }
    }
}
