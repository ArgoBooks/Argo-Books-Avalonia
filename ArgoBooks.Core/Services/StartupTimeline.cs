using System.Diagnostics;
using System.Globalization;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Measures a launch from the moment the OS created the process, not from the moment our
/// code started running.
///
/// <para>
/// The distinction is the entire reason this exists. A stopwatch started in <c>Main</c> or
/// in <c>OnFrameworkInitializationCompleted</c> cannot see the runtime being loaded, the
/// assemblies being mapped, or antivirus scanning a freshly installed binary the first time
/// it is touched, and on a slow disk that is most of the wait. It is also the part a splash
/// window can never cover, because the splash is drawn by the very code that is waiting to
/// start. A user staring at nothing and clicking the shortcut again is reacting to exactly
/// this window, so measuring anything else answers the wrong question.
/// </para>
///
/// <para>
/// <see cref="Process.StartTime"/> is the OS's own record and needs no cooperation from us.
/// It can throw where the platform will not hand over process details, so every read is
/// guarded and the whole thing degrades to null rather than failing a launch. A null here
/// means "we could not time this run", never zero, which would quietly read as instant.
/// </para>
/// </summary>
public static class StartupTimeline
{
    private static readonly DateTime? ProcessStartUtc = ReadProcessStartUtc();

    private static long? _toFirstPaintMs;
    private static long? _toServicesReadyMs;
    private static long? _toViewModelsReadyMs;
    private static bool _reported;

    /// <summary>
    /// True when this launch had to read from disk rather than from a warm OS file cache.
    ///
    /// Decided by how long ago the previous launch was, recorded on disk. The earlier test
    /// asked whether a sibling instance was already running, which is a different question
    /// and one whose answer is almost always the same: two copies at once effectively never
    /// happens, so every launch on record was reported as cold and the split measured
    /// nothing. Relaunching soon after quitting is the case that reads from cache, and it
    /// is now the case this detects.
    /// </summary>
    public static bool IsColdStart { get; } = ReadIsColdStart();

    /// <summary>
    /// A relaunch within this window is treated as warm. Generous, because the file cache
    /// survives well past a quick restart and the point is to separate "opened it again"
    /// from "first launch of the day".
    /// </summary>
    private static readonly TimeSpan WarmWindow = TimeSpan.FromHours(4);

    /// <summary>
    /// Call the instant the splash window is actually on screen. Records the dead time the
    /// user spent looking at nothing. Ignores repeat calls.
    /// </summary>
    public static void MarkFirstPaint()
    {
        _toFirstPaintMs ??= ElapsedSinceProcessStartMs();
    }

    /// <summary>
    /// Milliseconds from process start to the splash appearing, or null if it was never
    /// marked (the splash failed to open) or the process start time was unreadable.
    /// </summary>
    public static long? ToFirstPaintMs => _toFirstPaintMs;

    /// <summary>
    /// Call once the service graph is constructed, before any view model is built.
    /// </summary>
    public static void MarkServicesReady()
    {
        _toServicesReadyMs ??= ElapsedSinceProcessStartMs();
    }

    /// <summary>
    /// Milliseconds from process start to the services being ready. Null if the launch
    /// failed before that point.
    /// </summary>
    public static long? ToServicesReadyMs => _toServicesReadyMs;

    /// <summary>
    /// Call once the view models exist, immediately before the main window is built.
    /// </summary>
    public static void MarkViewModelsReady()
    {
        _toViewModelsReadyMs ??= ElapsedSinceProcessStartMs();
    }

    /// <summary>
    /// Milliseconds from process start to the view models being ready.
    /// </summary>
    public static long? ToViewModelsReadyMs => _toViewModelsReadyMs;

    /// <summary>
    /// Milliseconds from process start to now. Called when the main window opens, so it
    /// spans the whole launch and includes <see cref="ToFirstPaintMs"/> rather than
    /// continuing from it.
    /// </summary>
    public static long? ToReadyMs() => ElapsedSinceProcessStartMs();

    /// <summary>
    /// True the first time it is called and false afterwards, so the one-event-per-launch
    /// rule holds even if the main window's Opened handler fires more than once.
    /// </summary>
    public static bool TryClaimReport()
    {
        if (_reported)
        {
            return false;
        }

        _reported = true;
        return true;
    }

    private static long? ElapsedSinceProcessStartMs()
    {
        if (ProcessStartUtc is not { } start)
        {
            return null;
        }

        var elapsed = (DateTime.UtcNow - start).TotalMilliseconds;

        // A machine suspended mid-launch, or a clock corrected by NTP between process start
        // and now, produces a figure that describes the clock rather than the app. Drop
        // those instead of letting them drag an average around.
        if (elapsed < 0 || elapsed > TimeSpan.FromMinutes(10).TotalMilliseconds)
        {
            return null;
        }

        return (long)elapsed;
    }

    private static DateTime? ReadProcessStartUtc()
    {
        try
        {
            return Process.GetCurrentProcess().StartTime.ToUniversalTime();
        }
        catch (Exception)
        {
            // Some sandboxed and containerised environments refuse process details.
            // Timing is a diagnostic, never a requirement.
            return null;
        }
    }

    /// <summary>
    /// Reads the previous launch time, decides warm or cold from it, then stamps this
    /// launch for the next one to read.
    ///
    /// A plain file with a timestamp in it, because this runs before any service exists:
    /// IsColdStart is a static initialiser and fires long before settings are loaded.
    /// </summary>
    private static bool ReadIsColdStart()
    {
        try
        {
            var stampPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ArgoBooks",
                "last-launch.txt");

            var isCold = true;

            if (File.Exists(stampPath)
                && DateTime.TryParse(
                       File.ReadAllText(stampPath).Trim(),
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.RoundtripKind,
                       out var previous))
            {
                // A clock moved backwards would otherwise make every launch look warm
                // forever, so a negative gap is treated as cold rather than trusted.
                var sincePrevious = DateTime.UtcNow - previous.ToUniversalTime();
                isCold = sincePrevious < TimeSpan.Zero || sincePrevious > WarmWindow;
            }

            var directory = Path.GetDirectoryName(stampPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(stampPath, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));

            return isCold;
        }
        catch (Exception)
        {
            // No stamp readable or writable, so the split is unknowable for this launch.
            // Cold is the safer default: it keeps an unmeasurable launch out of the warm
            // bucket, where it would drag the warm figure up and hide a real regression.
            return true;
        }
    }
}
