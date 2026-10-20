using System.Runtime.Versioning;
using ArgoBooks.Core.Services;
using ArgoBooks.Desktop.Services;
using Avalonia;
using Velopack;

namespace ArgoBooks.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any SynchronizationContext-reliant code before AppMain is called: things aren't initialized yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack starts this executable to run each install, update and uninstall step, and Run handles that and exits before anything else starts.
        var velopack = VelopackApp.Build()
            // An update installs only when the user clicks Update, so a downloaded one they have not accepted stays uninstalled.
            .SetAutoApplyOnStartup(false);
        if (OperatingSystem.IsWindows())
            AddWindowsInstallHooks(velopack);
        velopack.Run();

        StartupTimeline.MarkMain();

        // Install crash handlers first so a failure anywhere in startup is captured.
        CrashReporter.InstallHandlers();

        // A factory, so the updater is built once the first window is showing rather than while the screen is still blank.
        App.UpdateServiceFactory = () => new VelopackUpdateService(App.ErrorLogger);

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Velopack only runs install hooks on Windows, and a method marked for Windows covers the calls inside its callbacks too.
    [SupportedOSPlatform("windows")]
    private static void AddWindowsInstallHooks(VelopackApp velopack) =>
        velopack
            .OnAfterInstallFastCallback(_ => WindowsInstallHooks.AfterInstall())
            .OnAfterUpdateFastCallback(_ => WindowsInstallHooks.AfterUpdate())
            .OnBeforeUninstallFastCallback(version => WindowsInstallHooks.BeforeUninstall(version.ToString()));

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
