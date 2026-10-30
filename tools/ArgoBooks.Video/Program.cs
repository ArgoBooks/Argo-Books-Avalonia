using System.Reflection;
using System.Text.Json.Nodes;
using ArgoBooks.Core.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;

namespace ArgoBooks.Video;

/// <summary>
/// Runs the real app without a window and records a scripted take of it, frame by frame, for the
/// marketing videos. make_video.py calls this and does the edit; see README.md.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("usage: ArgoBooks.Video <take> <out folder> <receipt image> <scan cache folder> [bank statement]");
            return 2;
        }
        var options = new TakeOptions(args[0], args[1], args[2], args[3], args.Length > 4 ? args[4] : null);
        Directory.CreateDirectory(options.OutFolder);

        Sandbox.Prepare(Path.Combine(AppContext.BaseDirectory, "sandbox-appdata"));

        var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, Fps = Recorder.Fps })
            .WithInterFont()
            .SetupWithLifetime(lifetime);
        Sandbox.SilenceTelemetry();

        var exitCode = 0;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Takes.RunAsync(lifetime.MainWindow!, options);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                exitCode = 1;
            }
            lifetime.Shutdown(exitCode);
        });
        lifetime.Start();
        return exitCode;
    }
}

internal sealed record TakeOptions(string Take, string OutFolder, string ReceiptImage, string ScanCache, string? Statement);

/// <summary>
/// Keeps a recording away from the person's own settings and usage statistics.
/// </summary>
internal static class Sandbox
{
    public static void Prepare(string appData)
    {
        if (Directory.Exists(appData))
            Directory.Delete(appData, recursive: true);
        Directory.CreateDirectory(appData);
        File.WriteAllText(Path.Combine(appData, "settings.json"), Settings().ToJsonString());

        // The app asks this factory for its settings folder, so swapping the instance moves every settings and telemetry file into the sandbox.
        typeof(PlatformServiceFactory).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, new SandboxPlatformService(appData));
    }

    /// <summary>A returning user with every tutorial, survey and prompt already dealt with.</summary>
    private static JsonObject Settings() => new()
    {
        ["recentCompanies"] = new JsonArray(),
        ["ui"] = new JsonObject { ["theme"] = "Light", ["language"] = "English", ["sidebarCollapsed"] = false },
        ["tutorial"] = new JsonObject
        {
            ["hasCompletedWelcomeTutorial"] = true,
            ["hasCompletedAppTour"] = true,
            ["hasSkippedTutorial"] = true,
            ["showSetupChecklist"] = false,
            ["showFirstVisitHints"] = false,
            ["hasShownSourceSurvey"] = true,
            ["isSourceSurveyDismissed"] = true,
            ["hasAskedExitSurvey"] = true,
            ["firstLaunchDate"] = DateTime.UtcNow.AddMonths(-6).ToString("O"),
        },
        ["updateEmail"] = new JsonObject { ["dismissed"] = true },
        ["reviewPrompt"] = new JsonObject { ["dismissed"] = true },
        ["backups"] = new JsonObject { ["enabled"] = false },
    };

    public static void SilenceTelemetry() =>
        typeof(App).GetProperty(nameof(App.TelemetryManager))!.SetValue(null, null);

    private sealed class SandboxPlatformService(string appData) : WindowsPlatformService
    {
        public override string GetAppDataPath() => appData;
    }
}
