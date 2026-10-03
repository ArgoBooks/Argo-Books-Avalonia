namespace ArgoBooks.Core.Services;

/// <summary>
/// Atomic file replacement that survives transient interference from antivirus,
/// search indexers, and backup agents. These tools briefly lock or scan a freshly
/// written file, which makes a plain <see cref="File.Move(string, string, bool)"/>
/// fail intermittently, most visibly on a brand-new install the AV hasn't whitelisted
/// yet. Callers write their data to a temp path first, then call <see cref="ReplaceAsync"/>
/// to swap it onto the final path.
/// </summary>
public static class AtomicFile
{
    private const int MaxAttempts = 4;

    /// <summary>
    /// A scratch path unique to the calling process, for callers that write-then-replace a
    /// file shared by every running instance (global settings, telemetry).
    /// <para>
    /// Multiple instances are supported deliberately, so a user can work in two companies at
    /// once. That means a predictable "&lt;file&gt;.tmp" is written concurrently by every
    /// instance: one opens it exclusively while another is mid-write, or renames it away
    /// before another gets to its own rename. Those surface as IOException on create and
    /// FileNotFoundException on move, the latter of which
    /// <see cref="ReplaceAsync"/> deliberately does not retry because it normally means the
    /// file was quarantined. Giving each process its own scratch name removes the collision
    /// rather than papering over it.
    /// </para>
    /// </summary>
    public static string TempPathFor(string finalPath) =>
        $"{finalPath}.{Environment.ProcessId}.tmp";

    /// <summary>
    /// Best-effort cleanup of a scratch file left behind by a failed write.
    /// </summary>
    public static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch
        {
            // Nothing useful to do: the file is either locked or already gone, and the caller
            // is already handling a failure.
        }
    }

    /// <summary>
    /// Opens a file for reading, retrying the same way <see cref="ReplaceAsync"/> does when it is
    /// transiently locked.
    /// <para>
    /// The lock that matters is the scanner's, not ours: antivirus opens a file it has not seen
    /// before with no sharing while it scans, and our open fails outright whatever FileShare we
    /// ask for. It is most likely on a file that has just appeared, which is exactly what opening
    /// a downloaded company or one restored from a backup does.
    /// </para>
    /// <para>
    /// Only the open is retried. A lock that outlasts the attempts rethrows, so a genuinely
    /// in-use file still surfaces as an error rather than hanging.
    /// </para>
    /// </summary>
    public static async Task<FileStream> OpenReadAsync(
        string path,
        int bufferSize = 4096,
        FileOptions options = FileOptions.Asynchronous,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, options);
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException)
                                       && ex is not FileNotFoundException
                                       && ex is not DirectoryNotFoundException
                                       && attempt < MaxAttempts)
            {
                await Task.Delay(50 * attempt, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Moves <paramref name="tempPath"/> onto <paramref name="finalPath"/>, retrying with a
    /// short async backoff when the destination is transiently locked, then giving up and
    /// rethrowing if the lock persists.
    /// <para>
    /// The rename is the only swap used: on the same volume <see cref="File.Move(string, string, bool)"/>
    /// is atomic, so <paramref name="finalPath"/> is always either the old file or the new one,
    /// never a half-written mix. A non-atomic copy fallback is deliberately NOT used: it would
    /// truncate the destination and risk corrupting a company file if interrupted, and it rarely
    /// helps anyway because a lock strong enough to fail the rename also fails a copy's open-for-write.
    /// Callers that hold real user data should let the rethrow surface as a "couldn't save" error.
    /// </para>
    /// </summary>
    public static async Task ReplaceAsync(
        string tempPath,
        string finalPath,
        bool overwrite = true,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                File.Move(tempPath, finalPath, overwrite);
                return;
            }
            catch (FileNotFoundException)
            {
                // The temp source is gone (e.g. antivirus quarantined it between the
                // write and the move). Retrying can't bring it back; surface to the caller.
                throw;
            }
            catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < MaxAttempts)
            {
                // Destination briefly locked by AV / indexer / backup. Back off (without
                // blocking the calling thread) and retry. The last attempt's failure is not
                // caught here, so it propagates to the caller.
                await Task.Delay(50 * attempt, cancellationToken);
            }
        }
    }
}
