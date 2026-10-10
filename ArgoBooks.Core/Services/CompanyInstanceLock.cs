using System.Security.Cryptography;
using System.Text;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Prevents the same company (<c>.argo</c>) file from being open in two running instances of the
/// app at once. Two instances editing one company would race their auto-saves and could overwrite
/// each other's changes or corrupt the file, so a company can only be held by one instance.
///
/// The lock is an exclusive OS file handle (<see cref="FileShare.None"/> +
/// <see cref="FileOptions.DeleteOnClose"/>) on a small per-company lock file, keyed by the
/// company's canonical path. Because the lock *is* the open handle (not merely the file's
/// existence), it is self-healing: an instance that crashes without releasing leaves no open
/// handle, so the next launch acquires cleanly. A leftover lock file with no handle never blocks.
/// </summary>
public sealed class CompanyInstanceLock : IDisposable
{
    private FileStream? _handle;

    /// <summary>The canonical path currently locked by this instance, or null if none.</summary>
    public string? LockedPath { get; private set; }

    /// <summary>
    /// Tries to take the exclusive lock for <paramref name="companyFilePath"/>. Releases any lock
    /// this instance already holds first. Returns true if the lock was acquired (this instance now
    /// owns the company), or false if another running instance already holds it.
    /// </summary>
    public bool TryAcquire(string companyFilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(companyFilePath);

        Release();

        var canonical = Canonicalize(companyFilePath);
        var lockFilePath = GetLockFilePath(canonical);

        try
        {
            var dir = Path.GetDirectoryName(lockFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _handle = new FileStream(
                lockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            LockedPath = canonical;
            return true;
        }
        catch (IOException)
        {
            // Sharing violation: another instance holds the exclusive handle. This is the expected
            // "already open elsewhere" signal.
            _handle = null;
            LockedPath = null;
            return false;
        }
        catch (Exception)
        {
            // Any other problem (permissions, a broken temp dir, etc.) must NOT block a legitimate open. Fail open: report the lock as acquired so the user can still work.
            _handle = null;
            LockedPath = null;
            return true;
        }
    }

    /// <summary>
    /// Checks whether another running instance currently holds the lock for
    /// <paramref name="companyFilePath"/>, WITHOUT disturbing any lock this instance already holds.
    /// Used to fail an open fast (before closing the current company) so a blocked open doesn't drop
    /// the user back to the welcome screen. Returns false for a path this instance itself holds.
    /// </summary>
    public bool IsHeldByAnotherInstance(string companyFilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(companyFilePath);

        var canonical = Canonicalize(companyFilePath);

        // A path we already hold is ours, not "another instance".
        if (LockedPath == canonical)
        {
            return false;
        }

        var lockFilePath = GetLockFilePath(canonical);
        try
        {
            var dir = Path.GetDirectoryName(lockFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // A throwaway exclusive handle: if we can take it, nobody else holds it. Disposed
            // immediately (DeleteOnClose tidies the probe file) so this only tests, never claims.
            using var probe = new FileStream(
                lockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            return false;
        }
        catch (IOException)
        {
            // Sharing violation: another instance holds it.
            return true;
        }
        catch (Exception)
        {
            // Infra problem (permissions, broken temp dir): don't block a legitimate open.
            return false;
        }
    }

    /// <summary>Releases the lock, if held.</summary>
    public void Release()
    {
        try
        {
            _handle?.Dispose();
        }
        catch
        {
            // Best effort; the OS releases the handle on process exit regardless.
        }

        _handle = null;
        LockedPath = null;
    }

    public void Dispose() => Release();

    private static string Canonicalize(string path)
    {
        var full = Path.GetFullPath(path);
        // Windows and macOS file systems are case-insensitive by default, so normalize case there to map the same file (opened via different-case paths) onto one lock.
        return OperatingSystem.IsLinux() ? full : full.ToLowerInvariant();
    }

    private static string GetLockFilePath(string canonicalPath)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPath)));
        var dir = Path.Combine(Path.GetTempPath(), "ArgoBooks", "locks");
        return Path.Combine(dir, hash + ".lock");
    }
}
