using System.Reflection;
using ArgoBooks.Core.Models.Assets;
using ArgoBooks.Core.Platform;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Supplies CRA's Capital Cost Allowance rates: the downloaded copy when it is newer than the one built into the app, otherwise the built-in one.
/// </summary>
public class CcaRateService
{
    public const string FileName = "cca.json";
    private const string ResourceName = "ArgoBooks.Core.Resources.Assets.cca.json";

    private readonly string _cacheDirectory;
    private CcaRateFile? _current;

    public CcaRateService(IPlatformService? platformService = null)
    {
        IPlatformService platform = platformService ?? PlatformServiceFactory.GetPlatformService();
        _cacheDirectory = Path.Combine(platform.GetCachePath(), "Assets");
    }

    /// <summary>Where a downloaded copy goes, shared with <see cref="CcaRateUpdateService"/> so both use the same path.</summary>
    public string CacheDirectory => _cacheDirectory;

    /// <summary>The rates to use, or null only if the built-in copy is unreadable and nothing was downloaded.</summary>
    public virtual CcaRateFile? Current => _current ??= Load();

    /// <summary>Drops the loaded copy so a newly downloaded one is picked up without a restart.</summary>
    public void Invalidate() => _current = null;

    private CcaRateFile? Load()
    {
        var embedded = ReadEmbedded();
        var cached = ReadCached();
        if (cached != null && (embedded == null || cached.Revision > embedded.Revision))
            return cached;
        return embedded;
    }

    private CcaRateFile? ReadCached()
    {
        try
        {
            var path = Path.Combine(_cacheDirectory, FileName);
            return File.Exists(path) ? ParseValid(File.ReadAllText(path)) : null;
        }
        catch
        {
            // An unreadable download must not stop the built-in rates from loading.
            return null;
        }
    }

    private static CcaRateFile? ReadEmbedded()
    {
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream == null)
                return null;
            using var reader = new StreamReader(stream);
            return ParseValid(reader.ReadToEnd());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The file, or null when it does not parse or fails <see cref="CcaRateValidator"/>.</summary>
    public static CcaRateFile? ParseValid(string json)
    {
        try
        {
            var file = JsonSerializer.Deserialize<CcaRateFile>(json);
            return CcaRateValidator.Validate(file).Count == 0 ? file : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
