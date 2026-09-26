using ArgoBooks.Core.Services;
using ArgoBooks.Desktop.Services;
using Avalonia;

namespace ArgoBooks.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        StartupTimeline.MarkMain();

        // Install crash handlers first so a failure anywhere in startup is captured.
        CrashReporter.InstallHandlers();

        // A factory, not an instance. Constructing NetSparkle here would run before
        // Avalonia is even configured, inside the window where the user is looking at
        // nothing. App builds it once the splash is on screen.
        App.UpdateServiceFactory = () => new NetSparkleUpdateService();

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
