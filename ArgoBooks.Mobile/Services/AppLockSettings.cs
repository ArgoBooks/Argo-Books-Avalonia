namespace ArgoBooks.Mobile.Services;

/// <summary>
/// Stores the "biometric app lock" on/off preference (default ON, free for all users - no
/// premium gate) and tracks when the app was last sent to the background, for the grace-period
/// check in <see cref="ArgoBooks.Shared.Mobile.AppLockPolicy"/>.
///
/// The enabled flag uses Microsoft.Maui.Storage.Preferences rather than the encrypted
/// ISecureStore: it's a UI toggle, not a secret, and Preferences already persists it across app
/// restarts. The "last backgrounded" timestamp is intentionally in-memory only (static field) -
/// if the OS kills the process while backgrounded, the next launch is a cold start and
/// AppLockPolicy.ShouldLock already locks unconditionally in that case (lastBackgroundedUtc is
/// null), so nothing needs to survive process death.
/// </summary>
public static class AppLockSettings
{
    private const string EnabledKey = "applock_enabled";

    /// <summary>Resume within this long after backgrounding does not re-show the lock screen.</summary>
    public static readonly TimeSpan GracePeriod = TimeSpan.FromMinutes(1);

    private static DateTime? _lastBackgroundedUtc;

    /// <summary>When the app was last sent to the background this process lifetime, or null.</summary>
    public static DateTime? LastBackgroundedUtc => _lastBackgroundedUtc;

    /// <summary>Whether the biometric app lock is turned on. Defaults to true.</summary>
    public static Task<bool> IsEnabledAsync() => Task.FromResult(Preferences.Default.Get(EnabledKey, true));

    /// <summary>Turns the biometric app lock on or off.</summary>
    public static Task SetEnabledAsync(bool enabled)
    {
        Preferences.Default.Set(EnabledKey, enabled);
        return Task.CompletedTask;
    }

    /// <summary>Call when the app is sent to the background (e.g. from the Activity's OnPause).</summary>
    public static void RecordBackgrounded() => _lastBackgroundedUtc = DateTime.UtcNow;

    /// <summary>Call after a successful unlock, so the just-unlocked session gets a fresh grace period.</summary>
    public static void RecordUnlocked() => _lastBackgroundedUtc = DateTime.UtcNow;
}
