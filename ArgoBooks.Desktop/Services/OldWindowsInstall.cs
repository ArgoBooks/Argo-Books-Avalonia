using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace ArgoBooks.Desktop.Services;

/// <summary>
/// The copy of Argo Books installed for every user by the Advanced Installer installer, which the Velopack copy replaces.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class OldWindowsInstall
{
    /// <summary>
    /// The upgrade code every version built with Advanced Installer carries, so it finds the old copy whichever version it is.
    /// </summary>
    private const string UpgradeCode = "{56B4BFD1-ED8C-4FBE-9562-14EB8B82623C}";

    private const int ErrorCancelled = 1223;

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiEnumRelatedProducts(string upgradeCode, uint reserved, uint productIndex, StringBuilder productCode);

    /// <summary>
    /// The product codes of every old install still on the PC.
    /// </summary>
    public static IReadOnlyList<string> ProductCodes()
    {
        var codes = new List<string>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var code = new StringBuilder(39);
                var result = MsiEnumRelatedProducts(UpgradeCode, 0, index, code);
                if (result != 0)
                    break;
                codes.Add(code.ToString());
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // No Windows Installer means nothing it could have installed.
        }
        return codes;
    }

    public static bool IsPresent() => ProductCodes().Count > 0;

    /// <summary>
    /// Uninstalls the old copy, which Windows asks permission for because it lives in Program Files. Returns true once it is gone, false when the user said no or the uninstall failed.
    /// </summary>
    public static bool Remove()
    {
        foreach (var code in ProductCodes())
        {
            try
            {
                // UPGRADINGPRODUCTCODE tells the old installer this is a replacement rather than a goodbye, which skips the uninstall survey it would otherwise open.
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "msiexec.exe",
                    Arguments = $"/x {code} /qb! /norestart UPGRADINGPRODUCTCODE={code}",
                    UseShellExecute = true,
                    Verb = "runas"
                });
                process?.WaitForExit();
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                return false;
            }
        }
        return !IsPresent();
    }
}
