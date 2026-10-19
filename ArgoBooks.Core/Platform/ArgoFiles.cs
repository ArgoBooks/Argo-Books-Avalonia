using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ArgoBooks.Core.Platform;

/// <summary>
/// Opens .argo files in Argo Books for the current user, and cleans up the per-user file type entries that versions up to 2.0.13 left behind.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ArgoFiles
{
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private const uint SHCNE_ASSOCCHANGED = 0x8000000;
    private const uint SHCNF_IDLIST = 0x0;

    /// <summary>
    /// Differs from the ProgID the cleanup below deletes, so the cleanup leaves this registration in place.
    /// </summary>
    private const string CompanyFileProgId = "ArgoBooks.CompanyFile";

    /// <summary>
    /// Registers .argo for the current user, opening in <paramref name="exePath"/>. Velopack installs per user and registers no file types itself.
    /// </summary>
    public static void RegisterCompanyFiles(string exePath)
    {
        var extension = Models.FileFormatConstants.CompanyFileExtension;
        using (var progId = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{CompanyFileProgId}"))
        {
            progId.SetValue("", "Argo Books Company File");
            using var icon = progId.CreateSubKey("DefaultIcon");
            icon.SetValue("", $"\"{exePath}\",0");
            using var command = progId.CreateSubKey(@"shell\open\command");
            command.SetValue("", $"\"{exePath}\" \"%1\"");
        }
        using (var ext = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}"))
        {
            ext.SetValue("", CompanyFileProgId);
        }
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>
    /// Removes what <see cref="RegisterCompanyFiles"/> wrote, leaving .argo alone if the user has since given it to another application.
    /// </summary>
    public static void UnregisterCompanyFiles()
    {
        var removed = DeleteExtension(Models.FileFormatConstants.CompanyFileExtension, CompanyFileProgId);
        removed |= DeleteKey($@"Software\Classes\{CompanyFileProgId}");
        if (removed)
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private static readonly string[] LegacyExtensions = [Models.FileFormatConstants.CompanyFileExtension, Models.FileFormatConstants.BackupFileExtension, ".argotemplate"];

    /// <summary>
    /// Deletes the legacy registrations and returns true if anything was removed.
    /// </summary>
    public static bool RemoveLegacyRegistrations()
    {
        var removed = false;

        foreach (var extension in LegacyExtensions)
        {
            var progId = $"ArgoBooks{extension.Replace(".", "")}";
            removed |= DeleteExtension(extension, progId);
            removed |= DeleteKey($@"Software\Classes\{progId}");
        }

        if (removed)
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }

        return removed;
    }

    /// <summary>
    /// Removes an extension key only while it still points at our ProgID, so an association the
    /// user has since handed to another application is left alone.
    /// </summary>
    private static bool DeleteExtension(string extension, string progId)
    {
        var path = $@"Software\Classes\{extension}";

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            if (key?.GetValue("") as string != progId)
                return false;
        }
        catch
        {
            return false;
        }

        return DeleteKey(path);
    }

    private static bool DeleteKey(string path)
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(path))
            {
                if (key == null)
                    return false;
            }

            Registry.CurrentUser.DeleteSubKeyTree(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
