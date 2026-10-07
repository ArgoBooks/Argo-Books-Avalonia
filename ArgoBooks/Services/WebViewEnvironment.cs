using ArgoBooks.Core.Models.Telemetry;
using ArgoBooks.Core.Services;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace ArgoBooks.Services;

/// <summary>
/// Shared setup for every <see cref="NativeWebView"/> the app creates.
///
/// WebView2 defaults its user data folder to a directory beside the host executable. In an
/// installed build that is Program Files, which a standard user cannot write to, so creating
/// the environment fails with UnauthorizedAccessException (E_ACCESSDENIED). During development
/// the executable runs from bin/, which is writable, so this only ever reproduces against an
/// installed build. <see cref="Configure"/> moves the folder somewhere always writable.
///
/// <see cref="InstallDispatcherGuard"/> covers what configuration cannot. The environment is
/// created from NativeWebView.OnAttached(), which is async void, so a failure is re-thrown onto
/// the dispatcher rather than returned to the caller: it bypasses every try/catch around the
/// call site and terminates the process. A machine with no WebView2 runtime installed fails the
/// same way, and no user data folder can prevent that one.
/// </summary>
public static class WebViewEnvironment
{
    /// <summary>Telemetry code for a web view that could not start, so it groups on the dashboard.</summary>
    private const string FailureCode = "WebViewEnvironmentFailed";

    /// <summary>The assembly that hosts the whole web view stack, as it appears in Exception.Source.</summary>
    private const string WebViewAssemblyName = "Avalonia.Controls.WebView";

    /// <summary>
    /// Frames that appear only when the web view stack itself is at fault. Deliberately narrow:
    /// the guard suppresses an exception that would otherwise end the process, so it must not
    /// match one that merely passed through a view that happens to host a web view.
    /// </summary>
    private static readonly string[] WebViewFrameMarkers =
    [
        "Avalonia.Controls.NativeWebView",
        "Avalonia.Controls.NativeWebDialog",
        "Avalonia.Controls.WebViewAdapter",
        "Avalonia.Controls.Win.WebView2",
        "Microsoft.Web.WebView2",
    ];

    private static readonly string? UserDataFolder = ResolveUserDataFolder();

    private static int _guardInstalled;

    /// <summary>
    /// True once a web view has failed to start in this session. A second attempt fails the
    /// same way, so a view that can show something else should stop asking and show it.
    /// Written and read on the UI thread only.
    /// </summary>
    public static bool HasFailed { get; private set; }

    /// <summary>
    /// Raised on the UI thread when a web view fails to start, so a view already showing an
    /// empty web view can swap itself for a fallback.
    /// </summary>
    public static event EventHandler? Failed;

    /// <summary>
    /// Applies the shared environment settings to a web view. Must run before the view is
    /// attached to a visual tree: attaching is what builds the environment, and these settings
    /// are read while it is being built.
    /// </summary>
    public static void Configure(NativeWebView webView)
    {
        webView.EnvironmentRequested += OnEnvironmentRequested;
    }

    /// <summary>
    /// Stops a web view that failed to start from taking the process down with it. Idempotent;
    /// call once at startup, before any window exists.
    /// </summary>
    public static void InstallDispatcherGuard(IErrorLogger? errorLogger)
    {
        // Atomic check-and-set, matching CrashReporter.InstallHandlers, so a re-entrant or
        // concurrent call cannot double-subscribe and log every failure twice.
        if (Interlocked.Exchange(ref _guardInstalled, 1) == 1)
        {
            return;
        }

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            if (!IsWebViewFailure(e.Exception))
            {
                return;
            }

            // Handling it leaves the app running with a dead web view, which costs a PDF thumbnail or an inline invoice preview.
            e.Handled = true;
            HasFailed = true;

            errorLogger?.LogWarning(
                $"WebView could not start: {e.Exception.GetType().Name}: {e.Exception.Message}",
                "WebView",
                ErrorCategory.UI,
                FailureCode);

            // Raised inside a handler that has just stopped the process from ending, so a
            // subscriber that throws would put the app straight back where it started.
            try
            {
                Failed?.Invoke(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                errorLogger?.LogWarning(
                    $"WebView fallback notification failed: {ex.Message}",
                    "WebView",
                    ErrorCategory.UI,
                    FailureCode);
            }
        };
    }

    private static void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e)
    {
        if (UserDataFolder != null && e is WindowsWebView2EnvironmentRequestedEventArgs windows)
        {
            windows.UserDataFolder = UserDataFolder;
        }
    }

    /// <summary>
    /// A location the user can always write to. LocalApplicationData rather than the app's
    /// roaming data folder because this holds a Chromium profile: it is a cache, it grows to
    /// hundreds of megabytes, and roaming it between machines is what Chromium expects not to
    /// happen. It also matches where every other WebView2 host on Windows puts its profile.
    /// </summary>
    private static string? ResolveUserDataFolder()
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localAppData))
            {
                return null;
            }

            string folder = Path.Combine(localAppData, "ArgoBooks", "WebView2");
            Directory.CreateDirectory(folder);
            return folder;
        }
        catch
        {
            // Leave WebView2 on its default, which is the behaviour that shipped before this.
            // The dispatcher guard keeps the resulting failure non-fatal either way.
            return null;
        }
    }

    private static bool IsWebViewFailure(Exception? exception)
    {
        for (Exception? ex = exception; ex != null; ex = ex.InnerException)
        {
            if (ex is AggregateException aggregate)
            {
                foreach (Exception inner in aggregate.InnerExceptions)
                {
                    if (IsWebViewFailure(inner))
                    {
                        return true;
                    }
                }
            }

            // Source is only the assembly name, so it is matched whole rather than by substring.
            if (ex.Source == WebViewAssemblyName || MentionsWebViewFrame(ex.StackTrace))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MentionsWebViewFrame(string? stackTrace)
    {
        if (string.IsNullOrEmpty(stackTrace))
        {
            return false;
        }

        foreach (string marker in WebViewFrameMarkers)
        {
            if (stackTrace.Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
