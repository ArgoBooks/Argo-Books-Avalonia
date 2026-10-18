using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using ArgoBooks.Core.Platform;
using ArgoBooks.Core.Services;

namespace ArgoBooks.Desktop.Services;

/// <summary>
/// What runs while Velopack installs, updates or uninstalls Argo Books on Windows. Each one runs in a short-lived copy of the app with no window, and Velopack ends it after 30 seconds.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsInstallHooks
{
    public static void AfterInstall()
    {
        Try(() => ArgoFiles.RegisterCompanyFiles(CurrentExePath()));
        Try(SaveInstallToken);
    }

    public static void AfterUpdate() => Try(() => ArgoFiles.RegisterCompanyFiles(CurrentExePath()));

    public static void BeforeUninstall(string version)
    {
        Try(ArgoFiles.UnregisterCompanyFiles);
        Try(() => Process.Start(new ProcessStartInfo($"{ApiConfig.BaseUrl}/uninstall/?v={Uri.EscapeDataString(version)}&p=windows") { UseShellExecute = true })?.Dispose());
    }

    private static string CurrentExePath() =>
        Environment.ProcessPath ?? throw new InvalidOperationException("The app's own path is unknown.");

    /// <summary>
    /// The website adds a referral token to the name of the Setup file it serves, and Setup is the process that runs this step, so its name is where the token is read from.
    /// </summary>
    private static void SaveInstallToken()
    {
        var setupPath = ParentProcessPath();
        var token = setupPath == null ? null : InstallToken.FromPath(setupPath);
        if (token == null)
            return;

        Directory.CreateDirectory(AppPaths.LocalData);
        var finalPath = Path.Combine(AppPaths.LocalData, InstallToken.FileName);
        var tempPath = AtomicFile.TempPathFor(finalPath);
        File.WriteAllText(tempPath, token, Encoding.ASCII);
        AtomicFile.Replace(tempPath, finalPath);
    }

    /// <summary>
    /// A failure here must never fail the install, so it is skipped and the rest goes ahead.
    /// </summary>
    private static void Try(Action action)
    {
        try { action(); }
        catch (Exception ex) { Debug.WriteLine($"Install step failed: {ex}"); }
    }

    private static string? ParentProcessPath()
    {
        var info = new ProcessBasicInformation();
        using var self = Process.GetCurrentProcess();
        if (NtQueryInformationProcess(self.Handle, 0, ref info, Marshal.SizeOf(info), out _) != 0)
            return null;

        var parent = OpenProcess(ProcessQueryLimitedInformation, false, (uint)info.InheritedFromUniqueProcessId.ToInt64());
        if (parent == IntPtr.Zero)
            return null;
        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            return QueryFullProcessImageName(parent, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            CloseHandle(parent);
        }
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, ref ProcessBasicInformation info, int length, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder exeName, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
