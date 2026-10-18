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
        VelopackApp.Build()
            // An update installs only when the user clicks Update, so a downloaded one they have not accepted stays uninstalled.
            .SetAutoApplyOnStartup(false)
            .OnAfterInstallFastCallback(_ => { if (OperatingSystem.IsWindows()) WindowsInstallHooks.AfterInstall(); })
            .OnAfterUpdateFastCallback(_ => { if (OperatingSystem.IsWindows()) WindowsInstallHooks.AfterUpdate(); })
            .OnBeforeUninstallFastCallback(version => { if (OperatingSystem.IsWindows()) WindowsInstallHooks.BeforeUninstall(version.ToString()); })
            .Run();

        StartupTimeline.MarkMain();

        // Install crash handlers first so a failure anywhere in startup is captured.
        CrashReporter.InstallHandlers();

        // A factory, so the updater is built once the first window is showing rather than while the screen is still blank.
        App.UpdateServiceFactory = () => new VelopackUpdateService(App.ErrorLogger);

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
