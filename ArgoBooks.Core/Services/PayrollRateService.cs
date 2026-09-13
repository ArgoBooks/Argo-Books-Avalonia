using System.Reflection;
using ArgoBooks.Core.Models.Payroll;
using ArgoBooks.Core.Platform;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Supplies the CRA rate table in force on a given pay date.
///
/// Tables are versioned by edition, not by year. CRA publishes twice a year and a July
/// edition can carry prorated amounts that apply only to the second half, so a pay run in
/// February and one in August of the same year need different tables. Asking for "2026" is
/// always the wrong question; the pay date is the only correct input.
///
/// Editions are looked for in the cache directory first, so a new edition can be delivered
/// without shipping a release, then fall back to the ones embedded in the assembly.
/// </summary>
public class PayrollRateService
{
    private const string ResourcePrefix = "ArgoBooks.Core.Resources.Payroll.";

    private readonly string _cacheDirectory;
    private List<PayrollRateTable>? _all;

    public PayrollRateService(IPlatformService? platformService = null)
    {
        IPlatformService platform = platformService ?? PlatformServiceFactory.GetPlatformService();
        _cacheDirectory = Path.Combine(platform.GetCachePath(), "Payroll");
    }

    /// <summary>
    /// Where a delivered edition goes. Exposed so <see cref="PayrollRateUpdateService"/> writes
    /// to the same place this reads from, rather than rebuilding the path and drifting from it.
    /// </summary>
    public string CacheDirectory => _cacheDirectory;

    /// <summary>
    /// The edition covering <paramref name="payDate"/>, or null when none is available.
    ///
    /// Null is a real answer and callers must handle it by refusing to calculate. There is
    /// deliberately no fallback to the nearest or most recent edition: quietly calculating a
    /// 2027 pay run with 2026 rates produces deductions that look plausible and are wrong,
    /// which is worse than producing nothing.
    /// </summary>
    /// <remarks>
    /// Virtual only so a test can stand in an app with no editions loaded at all. That state is
    /// real, it is what the day after the last edition expires looks like, and the only other
    /// way to reach it is to wait for it.
    /// </remarks>
    public virtual PayrollRateTable? GetForDate(DateTime payDate)
    {
        return LoadAll().FirstOrDefault(t => t.Covers(payDate));
    }

    /// <summary>
    /// Drops the in-memory copies so a newly downloaded edition is picked up without a
    /// restart.
    /// </summary>
    public void Invalidate()
    {
        _all = null;
    }

    private List<PayrollRateTable> LoadAll()
    {
        if (_all != null)
        {
            return _all;
        }

        var tables = new List<PayrollRateTable>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Cache first, so a delivered edition supersedes the embedded copy of the same id.
        foreach (string path in CachedFiles())
        {
            PayrollRateTable? table = ReadFile(path);
            if (table != null && seen.Add(table.EditionId))
            {
                tables.Add(table);
            }
        }

        foreach (string name in EmbeddedNames())
        {
            PayrollRateTable? table = ReadEmbedded(name);
            if (table != null && seen.Add(table.EditionId))
            {
                tables.Add(table);
            }
        }

        _all = tables;
        return _all;
    }

    private IEnumerable<string> CachedFiles()
    {
        if (!Directory.Exists(_cacheDirectory))
        {
            return [];
        }

        try
        {
            return Directory.GetFiles(_cacheDirectory, "*.json");
        }
        catch
        {
            // An unreadable cache must not stop the embedded editions from loading.
            return [];
        }
    }

    private static IEnumerable<string> EmbeddedNames() =>
        Assembly.GetExecutingAssembly()
            .GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                        && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

    private PayrollRateTable? ReadFile(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch
        {
            // A corrupt file is skipped rather than thrown, so one bad edition cannot make
            // every other pay date uncalculable. The absent edition surfaces as a null from
            // GetForDate, which callers already have to handle.
            return null;
        }
    }

    private PayrollRateTable? ReadEmbedded(string resourceName)
    {
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                return null;
            }

            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch
        {
            return null;
        }
    }

    private PayrollRateTable? Parse(string json)
    {
        PayrollRateTable? table = JsonSerializer.Deserialize<PayrollRateTable>(json);
        if (table == null || string.IsNullOrWhiteSpace(table.EditionId))
        {
            return null;
        }

        return table;
    }
}
