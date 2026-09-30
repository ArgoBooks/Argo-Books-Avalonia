using ArgoBooks.Core.Models;
using ArgoBooks.Core.Models.Telemetry;

namespace ArgoBooks.Core.Services;

/// <summary>One kept copy of a company, as it exists on disk.</summary>
public sealed record BackupFile(string Path, DateTime TakenAtUtc, long SizeBytes);

/// <summary>
/// Keeps dated copies of a company so an earlier version can be restored. Writing the copy is
/// <see cref="CompanyManager.ExportBackupAsync"/>, which already packs the working directory and
/// encrypts with the company's own password; this decides when a copy is due, what it is called,
/// and which older ones to drop.
/// </summary>
public sealed class BackupService(IErrorLogger? errorLogger = null)
{
    public const string Extension = ".argobk";

    /// <summary>
    /// Between the company name and the extension, so copies of one company can be picked out of a
    /// folder shared with others: <c>Acme--backup-20260930-1432.argobk</c>.
    /// </summary>
    private const string Marker = "--backup-";

    /// <summary>
    /// Whether enough time has passed for the chosen frequency. Measured from the last copy actually
    /// written rather than from the app starting, so saving twice in a minute does not produce two.
    /// </summary>
    public static bool IsDue(BackupSettings settings, DateTime utcNow)
    {
        if (!settings.Enabled) return false;
        if (settings.LastBackupUtc is not { } last) return true;

        return settings.Frequency switch
        {
            BackupFrequency.OnSave => true,
            BackupFrequency.Daily => utcNow - last >= TimeSpan.FromDays(1),
            BackupFrequency.Weekly => utcNow - last >= TimeSpan.FromDays(7),
            _ => true
        };
    }

    /// <summary>
    /// Where copies go when the user has not chosen a folder: beside the company file. Documents is
    /// deliberately avoided, because Windows redirects it into OneDrive on most installs, which would
    /// sync every copy and can leave them online-only. A backup that is not on the disk is not one.
    /// </summary>
    public static string DefaultFolder(string? companyFilePath)
    {
        try
        {
            var beside = string.IsNullOrEmpty(companyFilePath)
                ? null
                : System.IO.Path.GetDirectoryName(companyFilePath);
            var root = string.IsNullOrEmpty(beside)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : beside;
            return System.IO.Path.Combine(root, "Argo Books Backups");
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string FolderFor(BackupSettings settings, string? companyFilePath) =>
        string.IsNullOrWhiteSpace(settings.Folder) ? DefaultFolder(companyFilePath) : settings.Folder;

    /// <summary>
    /// Writes a copy and drops the oldest beyond <see cref="BackupSettings.CopiesToKeep"/>. Returns
    /// the path written, or null if it could not be. Never throws: a backup that fails must not take
    /// down the close, import or save it was attached to.
    /// </summary>
    public async Task<string?> CreateAsync(
        CompanyManager companyManager,
        BackupSettings settings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var companyName = companyManager.CurrentCompanyName;
            if (string.IsNullOrWhiteSpace(companyName) || !companyManager.IsCompanyOpen)
                return null;

            var folder = FolderFor(settings, companyManager.CurrentFilePath);
            if (string.IsNullOrWhiteSpace(folder))
                return null;

            Directory.CreateDirectory(folder);

            var path = System.IO.Path.Combine(
                folder,
                $"{CompanyManager.ToCompanyFileName(companyName)}{Marker}{DateTime.Now:yyyyMMdd-HHmmss}{Extension}");

            await companyManager.ExportBackupAsync(path, cancellationToken);

            settings.LastBackupUtc = DateTime.UtcNow;

            // Only after the new copy exists, so a failed write never costs an older good one.
            Prune(folder, companyName, settings.CopiesToKeep);
            return path;
        }
        catch (Exception ex)
        {
            errorLogger?.LogError(ex, ErrorCategory.FileSystem, "Could not write a backup copy");
            return null;
        }
    }

    /// <summary>Copies of this company in <paramref name="folder"/>, newest first.</summary>
    public IReadOnlyList<BackupFile> List(string? folder, string? companyName)
    {
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(companyName) || !Directory.Exists(folder))
            return [];

        try
        {
            var prefix = CompanyManager.ToCompanyFileName(companyName) + Marker;
            return [.. new DirectoryInfo(folder)
                .EnumerateFiles("*" + Extension)
                .Where(f => f.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(f => new BackupFile(f.FullName, f.LastWriteTimeUtc, f.Length))
                .OrderByDescending(b => b.TakenAtUtc)];
        }
        catch (Exception ex)
        {
            errorLogger?.LogWarning($"Could not list backups in {folder}: {ex.Message}", "Backup");
            return [];
        }
    }

    private void Prune(string folder, string companyName, int keep)
    {
        if (keep <= 0) return;

        foreach (var old in List(folder, companyName).Skip(keep))
        {
            try
            {
                File.Delete(old.Path);
            }
            catch (Exception ex)
            {
                errorLogger?.LogWarning($"Could not remove an old backup: {ex.Message}", "Backup");
            }
        }
    }
}
