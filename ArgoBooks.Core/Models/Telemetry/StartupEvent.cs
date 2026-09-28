namespace ArgoBooks.Core.Models.Telemetry;

/// <summary>
/// How long a launch actually took, split at the first moment the app could draw anything.
///
/// <para>
/// The split is the whole point. Users who click the shortcut a second time are reacting to
/// a screen with nothing on it, and a splash window can only appear once managed code is
/// already running. Everything before that (loading the runtime, mapping assemblies, the
/// first-run antivirus scan of a fresh install) is invisible to any in-app timer that starts
/// at Main. Measuring from the OS process start time is what separates "our startup is slow"
/// from "the machine took eight seconds to get to our first line", and those need completely
/// different fixes.
/// </para>
/// </summary>
public class StartupEvent : TelemetryEvent
{
    /// <inheritdoc />
    public override TelemetryDataType DataType => TelemetryDataType.Startup;

    /// <summary>
    /// Process start to the splash window being on screen. This is the dead time a splash
    /// cannot cover, and the number to look at before optimising anything else.
    /// </summary>
    public long? ToFirstPaintMs { get; set; }

    /// <summary>
    /// Process start to the main window opening, so it includes
    /// <see cref="ToFirstPaintMs"/> rather than continuing from it.
    /// </summary>
    public long? ToReadyMs { get; set; }

    /// <summary>
    /// Milliseconds from process start to the service graph being built, before any view
    /// model exists. Sits between <see cref="ToFirstPaintMs"/> and
    /// <see cref="ToViewModelsReadyMs"/>; like every mark here it is measured from process
    /// start, so the segments are differences rather than sums.
    /// </summary>
    public long? ToServicesReadyMs { get; set; }

    /// <summary>
    /// Milliseconds from process start to the view models being built, immediately before
    /// the main window is constructed.
    /// </summary>
    public long? ToViewModelsReadyMs { get; set; }

    /// <summary>
    /// Milliseconds from process start to Main running: the runtime starting and loading the
    /// app's assemblies, before any of its own code.
    /// </summary>
    public long? ToMainMs { get; set; }

    /// <summary>
    /// Milliseconds from process start to the app shell's view model being built. Falls
    /// between <see cref="ToServicesReadyMs"/> and <see cref="ToViewModelsReadyMs"/>.
    /// </summary>
    public long? ToShellViewModelMs { get; set; }

    /// <summary>
    /// Milliseconds from process start to the main window being constructed, before it is
    /// shown. The gap from <see cref="ToViewModelsReadyMs"/> is building the views.
    /// </summary>
    public long? ToWindowBuiltMs { get; set; }

    /// <summary>
    /// True when this launch had to read from disk rather than from a warm OS file cache,
    /// decided by how long ago the previous launch was. See
    /// <see cref="Services.StartupTimeline.IsColdStart"/>. Relaunching soon after quitting is
    /// not comparable to a first launch, and mixing the two flatters the average exactly when
    /// relaunches are what is being investigated.
    /// </summary>
    public bool ColdStart { get; set; }
}
