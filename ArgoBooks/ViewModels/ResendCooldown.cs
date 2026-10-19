using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgoBooks.ViewModels;

/// <summary>
/// The wait before a verification code can be sent again, shared by every modal that emails one.
/// It ticks on a one second timer and counts down on the UI thread.
/// </summary>
public sealed partial class ResendCooldown : ObservableObject, IDisposable
{
    /// <summary>Seconds the Resend button stays disabled after a code is sent.</summary>
    public const int Seconds = 60;

    private System.Timers.Timer? _timer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(ButtonText))]
    private int _secondsLeft;

    public bool IsRunning => SecondsLeft > 0;

    public string ButtonText => IsRunning ? $"Resend code ({SecondsLeft}s)" : "Resend code";

    /// <summary>Starts the wait again from the full length, as after each code sent.</summary>
    public void Start()
    {
        Stop();
        SecondsLeft = Seconds;
        _timer = new System.Timers.Timer(1000) { AutoReset = true };
        _timer.Elapsed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(Tick);
        _timer.Start();
    }

    private void Tick()
    {
        if (SecondsLeft > 0) SecondsLeft--;
        if (SecondsLeft == 0) Stop();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => Stop();
}
