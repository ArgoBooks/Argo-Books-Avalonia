using System.Diagnostics;
using ArgoBooks.Core.Platform.Mac;

namespace ArgoBooks.Core.Platform;

/// <summary>
/// macOS-specific platform service implementation.
/// </summary>
public class MacPlatformService : BasePlatformService
{
    /// <inheritdoc />
    public override PlatformType Platform => PlatformType.MacOS;

    /// <inheritdoc />
    public override string GetAppDataPath()
    {
        // ~/Library/Application Support/ArgoBooks
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return CombinePaths(homeDir, "Library", "Application Support", ApplicationName);
    }

    /// <inheritdoc />
    public override string GetCachePath()
    {
        // ~/Library/Caches/ArgoBooks
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return CombinePaths(homeDir, "Library", "Caches", ApplicationName);
    }

    /// <inheritdoc />
    public override bool SupportsBiometrics => true;

    /// <inheritdoc />
    public override async Task<bool> IsBiometricAvailableAsync() =>
        await Task.Run(() => MacKeychain.IsAvailable() && MacAuthenticator.IsAvailable());

    /// <inheritdoc />
    public override async Task<string> GetBiometricAvailabilityDetailsAsync() =>
        await Task.Run(MacAuthenticator.DescribeAvailability);

    /// <inheritdoc />
    public override async Task<bool> AuthenticateWithBiometricAsync(string reason) =>
        await MacAuthenticator.AuthenticateAsync(reason);

    /// <summary>
    /// Stores the password in the login keychain, which is what the Touch ID prompt is
    /// guarding: the prompt proves the person is there, and this is what they get access to.
    ///
    /// The keychain binds an item to the code signature of the app that wrote it, so a
    /// password saved by a local unsigned build and then read by a signed one prompts for
    /// permission the first time rather than failing outright.
    /// </summary>
    public override void StorePasswordForBiometric(string fileId, string password) =>
        MacKeychain.Store(fileId, password);

    /// <inheritdoc />
    public override string? GetPasswordForBiometric(string fileId) =>
        MacKeychain.Lookup(fileId);

    /// <inheritdoc />
    public override void ClearPasswordForBiometric(string fileId) =>
        MacKeychain.Clear(fileId);

    /// <inheritdoc />
    public override bool SupportsAutoUpdate => true;

    /// <inheritdoc />
    public override string GetMachineId()
    {
        try
        {
            // Use ioreg to get the hardware UUID (IOPlatformUUID)
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "/usr/sbin/ioreg",
                Arguments = "-rd1 -c IOPlatformExpertDevice",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000); // 5 second timeout

            // Parse the IOPlatformUUID from the output
            // Format: "IOPlatformUUID" = "XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX"
            const string uuidKey = "\"IOPlatformUUID\"";
            var keyIndex = output.IndexOf(uuidKey, StringComparison.Ordinal);
            if (keyIndex >= 0)
            {
                var startQuote = output.IndexOf('"', keyIndex + uuidKey.Length + 1);
                if (startQuote >= 0)
                {
                    var endQuote = output.IndexOf('"', startQuote + 1);
                    if (endQuote > startQuote)
                    {
                        var uuid = output.Substring(startQuote + 1, endQuote - startQuote - 1);
                        if (!string.IsNullOrEmpty(uuid))
                        {
                            return uuid;
                        }
                    }
                }
            }
        }
        catch
        {
            // Process execution failed
        }

        return base.GetMachineId();
    }
}
