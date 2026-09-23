namespace ArgoBooks.Core.Models.Telemetry;

/// <summary>
/// Session start or end event.
/// </summary>
public class SessionEvent : TelemetryEvent
{
    /// <inheritdoc />
    public override TelemetryDataType DataType => TelemetryDataType.Session;

    /// <summary>
    /// Session action type.
    /// </summary>
    public SessionAction Action { get; set; }

    /// <summary>
    /// Wall clock seconds from launch to quit (only for SessionEnd events).
    ///
    /// Counts a window left open overnight as use, which is why the longest sessions on
    /// record run to days. Kept because every session ever recorded is measured this way,
    /// but <see cref="ActiveSeconds"/> is the one worth charting.
    /// </summary>
    public long? DurationSeconds { get; set; }

    /// <summary>
    /// Seconds the app was actually being driven (only for SessionEnd events).
    ///
    /// Accumulated between inputs, skipping any gap longer than the idle threshold, so a
    /// long unattended stretch adds nothing. Null on SessionStart, on ends reconstructed
    /// after a force-quit where no input history survived, and on builds predating the
    /// field, where a reader must treat it as "not measured" rather than zero.
    /// </summary>
    public long? ActiveSeconds { get; set; }

    /// <summary>
    /// The page on screen when the session ended (only for SessionEnd events). Null on
    /// SessionStart, on ends reconstructed after a force-quit, and on older builds.
    /// </summary>
    public string? LastPage { get; set; }

    /// <summary>
    /// Whether the session shut down normally. False marks a SessionEnd reconstructed on
    /// the next launch from a leftover <see cref="Services.SessionSentinel"/>, meaning the
    /// run was force-quit, cut off by an OS restart, or lost to a power failure.
    /// <para>
    /// A flag rather than a third <see cref="SessionAction"/> so that a client running
    /// ahead of the server degrades safely: the upload filter drops an unknown field but
    /// coerces an unknown enum value to "Unknown", which would break session pairing
    /// outright. Null on SessionStart, and on ends from builds predating this field, where
    /// the reader should assume clean.
    /// </para>
    /// </summary>
    public bool? Clean { get; set; }

    /// <summary>
    /// Highest managed heap size seen during the session, in MB, sampled once a minute. Null on
    /// SessionStart, on ends reconstructed after a force-quit, and on builds predating the field.
    /// </summary>
    public int? PeakManagedMemoryMb { get; set; }

    /// <summary>
    /// Highest process working set seen during the session, in MB, sampled alongside
    /// <see cref="PeakManagedMemoryMb"/>. Counts what the managed figure cannot: decoded
    /// bitmaps, Skia surfaces and the web view's own processes. Null in the same cases.
    /// </summary>
    public int? PeakWorkingSetMb { get; set; }
}

/// <summary>
/// Session action types.
/// </summary>
public enum SessionAction
{
    /// <summary>
    /// Application started.
    /// </summary>
    SessionStart,

    /// <summary>
    /// Application closed.
    /// </summary>
    SessionEnd
}
