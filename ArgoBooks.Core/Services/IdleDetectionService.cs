using System.Timers;
using Timer = System.Timers.Timer;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Service that detects user inactivity and triggers auto-lock after a configurable timeout.
/// </summary>
public class IdleDetectionService : IDisposable
{
    private readonly Timer _idleTimer;
    private DateTime _lastActivityTime;
    private int _timeoutMinutes;
    private bool _isEnabled;
    private bool _isDisposed;

    /// <summary>
    /// Event raised when the idle timeout has been reached.
    /// </summary>
    public event EventHandler? IdleTimeoutReached;

    /// <summary>
    /// Gets or sets whether idle detection is enabled.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            if (value)
            {
                ResetIdleTimer();
                _idleTimer.Start();
            }
            else
            {
                _idleTimer.Stop();
            }
        }
    }

    /// <summary>
    /// Gets or sets the timeout in minutes before auto-lock triggers.
    /// </summary>
    public int TimeoutMinutes
    {
        get => _timeoutMinutes;
        set
        {
            _timeoutMinutes = value;
            if (_isEnabled)
            {
                ResetIdleTimer();
            }
        }
    }

    /// <summary>
    /// Creates a new IdleDetectionService.
    /// </summary>
    public IdleDetectionService()
    {
        _lastActivityTime = DateTime.UtcNow;
        _timeoutMinutes = 5;
        _isEnabled = false;

        // Check every 30 seconds
        _idleTimer = new Timer(30000);
        _idleTimer.Elapsed += OnIdleTimerElapsed;
        _idleTimer.AutoReset = true;
    }

    /// <summary>
    /// Records user activity (call this on mouse/keyboard events).
    /// </summary>
    public void RecordActivity()
    {
        _lastActivityTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Resets the idle timer (called when activity is detected or settings change).
    /// </summary>
    public void ResetIdleTimer()
    {
        RecordActivity();
    }

    /// <summary>
    /// Configures the idle detection from settings.
    /// </summary>
    /// <param name="enabled">Whether auto-lock is enabled.</param>
    /// <param name="timeoutMinutes">Timeout in minutes (0 = never).</param>
    public void Configure(bool enabled, int timeoutMinutes)
    {
        _timeoutMinutes = timeoutMinutes;
        IsEnabled = enabled && timeoutMinutes > 0;
    }

    private void OnIdleTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (!_isEnabled || _timeoutMinutes <= 0)
            return;

        var idleTime = DateTime.UtcNow - _lastActivityTime;
        if (idleTime.TotalMinutes >= _timeoutMinutes)
        {
            // Stop the timer to prevent multiple triggers
            _idleTimer.Stop();

            // Raise the event
            IdleTimeoutReached?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _idleTimer.Stop();
        _idleTimer.Dispose();
        _isDisposed = true;
    }
}
