using System.Runtime.InteropServices;

namespace ArgoBooks.Core.Platform.Mac;

/// <summary>
/// Credential storage in the macOS login keychain, the counterpart to
/// <see cref="Linux.LinuxSecretStorage"/>.
///
/// Uses the file-based keychain rather than the newer data protection keychain. The latter
/// needs the app to carry a keychain-access-groups entitlement matching its signing team,
/// which would tie a local unsigned build and a released signed one to different stores; this
/// one works either way, and the app is not sandboxed, so nothing else is gained by it.
///
/// The password is passed as bytes straight into the C API and never becomes a command line
/// argument, which is what keeps it off the process list.
/// </summary>
internal static class MacKeychain
{
    private const string SecurityFramework = "/System/Library/Frameworks/Security.framework/Security";

    /// <summary>Groups the app's items so they are recognisable in Keychain Access.</summary>
    private const string ServiceName = "Argo Books";

    private const int Success = 0;
    private const int ItemNotFound = -25300;

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainAddGenericPassword(
        IntPtr keychain,
        uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName,
        uint passwordLength, byte[] passwordData,
        out IntPtr itemRef);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainFindGenericPassword(
        IntPtr keychainOrArray,
        uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName,
        out uint passwordLength, out IntPtr passwordData,
        out IntPtr itemRef);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    /// <summary>
    /// The keychain is part of the OS, so there is nothing to install and nothing to probe for.
    /// Present so this reads like the Linux and Windows stores beside it.
    /// </summary>
    public static bool IsAvailable() => OperatingSystem.IsMacOS();

    /// <summary>
    /// Stores a password against a company file, replacing any password already held for it.
    /// </summary>
    public static void Store(string fileId, string password)
    {
        if (string.IsNullOrEmpty(fileId))
            return;

        var service = Utf8(ServiceName);
        var account = Utf8(fileId);
        var secret = Utf8(password);

        try
        {
            // Adding over an existing item fails with errSecDuplicateItem rather than
            // replacing it, so the old one goes first. Clear() ignores a missing item.
            Clear(fileId);

            SecKeychainAddGenericPassword(
                IntPtr.Zero,
                (uint)service.Length, service,
                (uint)account.Length, account,
                (uint)secret.Length, secret,
                out var item);

            if (item != IntPtr.Zero)
                CFRelease(item);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not a Mac, or Security.framework is missing. Nothing is stored, and the caller
            // already treats a later empty lookup as "biometric login is not set up".
        }
        finally
        {
            Array.Clear(secret);
        }
    }

    /// <summary>
    /// Reads the password held for a company file, or null when there is none.
    /// </summary>
    public static string? Lookup(string fileId)
    {
        if (string.IsNullOrEmpty(fileId))
            return null;

        var service = Utf8(ServiceName);
        var account = Utf8(fileId);

        try
        {
            var status = SecKeychainFindGenericPassword(
                IntPtr.Zero,
                (uint)service.Length, service,
                (uint)account.Length, account,
                out var length, out var data, out var item);

            try
            {
                if (status != Success || data == IntPtr.Zero)
                    return null;

                return Marshal.PtrToStringUTF8(data, (int)length);
            }
            finally
            {
                if (data != IntPtr.Zero)
                    SecKeychainItemFreeContent(IntPtr.Zero, data);
                if (item != IntPtr.Zero)
                    CFRelease(item);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Removes the password held for a company file. A file with none stored is not an error.
    /// </summary>
    public static void Clear(string fileId)
    {
        if (string.IsNullOrEmpty(fileId))
            return;

        var service = Utf8(ServiceName);
        var account = Utf8(fileId);

        try
        {
            var status = SecKeychainFindGenericPassword(
                IntPtr.Zero,
                (uint)service.Length, service,
                (uint)account.Length, account,
                out _, out var data, out var item);

            if (data != IntPtr.Zero)
                SecKeychainItemFreeContent(IntPtr.Zero, data);

            if (status == Success && item != IntPtr.Zero)
            {
                SecKeychainItemDelete(item);
                CFRelease(item);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Nothing to remove if the framework is not there.
        }
    }

    /// <summary>True when a password is held for this file, without reading it back.</summary>
    public static bool HasPassword(string fileId) => Lookup(fileId) != null;

    private static byte[] Utf8(string value) => System.Text.Encoding.UTF8.GetBytes(value);
}
