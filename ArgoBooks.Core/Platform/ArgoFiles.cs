using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ArgoBooks.Core.Platform;

/// <summary>
/// Removes the per-user file type registrations that Argo Books wrote on every launch up to
/// 2.0.13. The installer now owns the .argo association, and an entry under
/// HKCU\Software\Classes takes precedence over the installer's per-machine one, so the old
/// entries have to go or they keep pointing the extension at whichever build ran last.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ArgoFiles
{
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private const uint SHCNE_ASSOCCHANGED = 0x8000000;
    private const uint SHCNF_IDLIST = 0x0;

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
