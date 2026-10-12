using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;

namespace ArgoBooks.Core.Services;

/// <summary>
/// Captures unhandled exceptions (crashes) to a local file the instant they
/// happen, and uploads any pending crash files to the server on the next launch.
///
/// Design goals:
///   - The capture path NEVER throws and uses only static state, so it works even
///     if a crash happens before the app's services are constructed.
///   - Crashes are written locally and synchronously (a dying process can't be
///     trusted to finish an async network call), then delivered on next startup,
///     the same persist-then-upload model the telemetry pipeline already relies on.
/// </summary>
public static class CrashReporter
{
    private const string CrashFolderName = "crashes";

    private static IErrorLogger? _breadcrumbSource;
    private static int _handlersInstalled;
    private static int _capturing; // re-entrancy guard

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Installs global unhandled-exception handlers. Safe to call very early (e.g.
    /// from Program.Main) before any services exist. Idempotent.
    /// </summary>
    public static void InstallHandlers()
    {
        // Atomic check-and-set so concurrent or re-entrant calls can't both pass
        // the guard and double-subscribe the handlers.
        if (Interlocked.Exchange(ref _handlersInstalled, 1) == 1)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Capture(ex, "AppDomain");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Capture(e.Exception, "TaskScheduler");
            // Mark observed so an unobserved task fault doesn't itself escalate.
            e.SetObserved();
        };
    }

    /// <summary>
    /// Provide a source of recent log entries to attach as breadcrumbs. Call once
    /// the ErrorLogger exists. Optional, capture still works without it.
    /// </summary>
    public static void SetBreadcrumbSource(IErrorLogger errorLogger) => _breadcrumbSource = errorLogger;

    /// <summary>
    /// Write a crash report to disk. Never throws.
    /// </summary>
    public static void Capture(Exception exception, string handler)
    {
        // Block concurrent captures (multiple handlers can fire for one dying process).
        if (Interlocked.Exchange(ref _capturing, 1) == 1)
        {
            return;
        }

        try
        {
            string? dir = GetCrashDirectory();
            if (dir == null)
            {
                return;
            }

            Directory.CreateDirectory(dir);
            CrashReportDto report = BuildReport(exception, handler);
            string fileName = $"crash_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.json";
            File.WriteAllText(Path.Combine(dir, fileName), JsonSerializer.Serialize(report, JsonOptions));
        }
        catch
        {
            // A crash handler must never throw. Swallow everything.
        }
        finally
        {
            Interlocked.Exchange(ref _capturing, 0);
        }
    }

    /// <summary>
    /// Uploads any pending crash files to the server, then deletes the ones that
    /// were accepted. Best-effort: never throws, never blocks launch. Call on
    /// startup once auth (device id / license) is available.
    /// </summary>
    public static async Task UploadPendingAsync(HttpClient httpClient, string appVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!LicenseAuthHelper.IsConfigured)
            {
                return;
            }

            string? dir = GetCrashDirectory();
            if (dir == null || !Directory.Exists(dir))
            {
                return;
            }

            string[] files = Directory.GetFiles(dir, "crash_*.json");
            if (files.Length == 0)
            {
                return;
            }

            var reports = new List<JsonElement>();
            var includedFiles = new List<string>();

            foreach (string file in files.Take(50))
            {
                try
                {
                    string text = await File.ReadAllTextAsync(file, cancellationToken);
                    using var doc = JsonDocument.Parse(text);
                    reports.Add(doc.RootElement.Clone());
                    includedFiles.Add(file);
                }
                catch
                {
                    // Corrupt or locked file: skip and leave it for a later attempt.
                }
            }

            if (reports.Count == 0)
            {
                return;
            }

            var payload = new
            {
                appVersion,
                platform = GetPlatform(),
                crashes = reports,
            };

            byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));

            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(jsonBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            content.Add(fileContent, "file", $"crash_{DateTime.UtcNow:yyyyMMddHHmmss}.json");

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiConfig.BaseUrl}/api/data/crash");
            request.Content = content;
            LicenseAuthHelper.AddAuthHeaders(request);
            request.Headers.UserAgent.ParseAdd($"ArgoSalesTracker/{appVersion}");

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                foreach (string file in includedFiles)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // Leave it; the next startup will retry the delete after re-upload.
                    }
                }
            }
        }
        catch
        {
            // Startup upload is best-effort and must never disrupt launch.
        }
    }

    private static CrashReportDto BuildReport(Exception exception, string handler)
    {
        return new CrashReportDto
        {
            DataId = Guid.NewGuid().ToString("N")[..16],
            Timestamp = DateTime.UtcNow.ToString("o"),
            Handler = handler,
            ExceptionType = exception.GetType().FullName,
            Message = Truncate(exception.Message, 2000),
            Source = GetSource(exception),
            StackTrace = Truncate(exception.StackTrace, 8000),
            InnerException = exception.InnerException != null
                ? Truncate($"{exception.InnerException.GetType().FullName}: {exception.InnerException.Message}", 2000)
                : null,
            OsVersion = GetOsVersion(),
            AppVersion = GetAppVersion(),
            Platform = GetPlatform(),
            Breadcrumbs = GetBreadcrumbs(),
        };
    }

    /// <summary>Best-effort "file:line" (or method name) of where the exception was thrown.</summary>
    private static string? GetSource(Exception exception)
    {
        try
        {
            var trace = new System.Diagnostics.StackTrace(exception, true);
            var frame = trace.GetFrame(0);
            if (frame == null)
            {
                return null;
            }

            string? file = frame.GetFileName();
            if (!string.IsNullOrEmpty(file))
            {
                return $"{Path.GetFileName(file)}:{frame.GetFileLineNumber()}";
            }

            return frame.GetMethod()?.Name;
        }
        catch
        {
            return null;
        }
    }

    private static List<string>? GetBreadcrumbs()
    {
        try
        {
            IErrorLogger? source = _breadcrumbSource;
            if (source == null)
            {
                return null;
            }

            IReadOnlyList<ErrorLogEntry> entries = source.GetRecentErrors(20);
            if (entries.Count == 0)
            {
                return null;
            }

            var crumbs = new List<string>(entries.Count);
            foreach (ErrorLogEntry e in entries)
            {
                string location = e.SourceFile != null ? $" ({e.SourceFile}:{e.LineNumber})" : string.Empty;
                crumbs.Add($"[{e.Timestamp:HH:mm:ss}] {e.Level} {e.Category}: {Truncate(e.Message, 300)}{location}");
            }

            return crumbs;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetCrashDirectory()
    {
        try
        {
            string appData = Platform.PlatformServiceFactory.GetPlatformService().GetAppDataPath();
            return Path.Combine(appData, CrashFolderName);
        }
        catch
        {
            return null;
        }
    }

    private static string GetPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "Windows";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "macOS";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "Linux";
        }

        return "Unknown";
    }

    private static string? GetOsVersion()
    {
        try
        {
            return Truncate(RuntimeInformation.OSDescription, 120);
        }
        catch
        {
            return null;
        }
    }

    // Captured at crash time so a report stays attributed to the version that
    // actually crashed, even if the app updates before the next-launch upload.
    private static string? GetAppVersion()
    {
        try
        {
            return AppInfo.VersionNumber;
        }
        catch
        {
            return null;
        }
    }

    private static string? Truncate(string? value, int max)
    {
        if (value == null)
        {
            return null;
        }

        return value.Length <= max ? value : value[..max];
    }

    private sealed class CrashReportDto
    {
        public string? DataId { get; set; }
        public string? Timestamp { get; set; }
        public string? Handler { get; set; }
        public string? ExceptionType { get; set; }
        public string? Message { get; set; }
        public string? Source { get; set; }
        public string? StackTrace { get; set; }
        public string? InnerException { get; set; }
        public string? OsVersion { get; set; }
        public string? AppVersion { get; set; }
        public string? Platform { get; set; }
        public List<string>? Breadcrumbs { get; set; }
    }
}
