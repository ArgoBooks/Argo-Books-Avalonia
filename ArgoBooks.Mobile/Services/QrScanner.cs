using System.Threading.Tasks;
using Android.Gms.Extensions;
using Microsoft.Maui.ApplicationModel;
using Xamarin.Google.MLKit.Vision.Barcode.Common;
using Xamarin.Google.MLKit.Vision.CodeScanner;

namespace ArgoBooks.Mobile.Services;

/// <summary>
/// Launches Google ML Kit's turnkey barcode-scanning UI (GmsBarcodeScanning - a full-screen
/// scan activity provided by Google Play Services, no custom camera pipeline required) and
/// returns the decoded payload string.
///
/// Unlike ZXing.Net.MAUI (it hard-requires the full
/// Microsoft.Maui/Microsoft.Maui.Controls assembly, which a pure-Avalonia Android head doesn't
/// reference), this is a plain Android binding (same family as Net.Google.MLKit.DocumentScanner,
/// already integrated for receipt scanning) and coexists with Avalonia fine.
///
/// Restricted to the QR format since that's the only barcode type the pairing flow produces.
/// Not unit-tested (device/Play-Services dependent); PairingCoordinator, which this feeds into,
/// is unit-tested independently of how the payload string was obtained.
/// </summary>
public static class QrScanner
{
    /// <summary>
    /// Shows the ML Kit scan-code UI and returns the decoded string, or null if the user
    /// cancelled, there's no current activity, or the scan otherwise failed.
    /// </summary>
    public static async Task<string?> ScanAsync()
    {
        var activity = Platform.CurrentActivity;
        if (activity == null)
        {
            return null;
        }

        var options = new GmsBarcodeScannerOptions.Builder()
            .SetBarcodeFormats(Barcode.FormatQrCode)
            .AllowManualInput()
            .Build();

        var scanner = GmsBarcodeScanning.GetClient(activity, options);

        try
        {
            var barcode = await scanner.StartScan().AsAsync<Barcode>();
            return barcode?.RawValue;
        }
        catch (Java.Lang.Exception)
        {
            // User cancelled the scan, denied camera permission, or Play Services / the
            // on-device scanning module isn't available - all surface as an MlKitException
            // (a Java.Lang.Exception subclass). Treat all of these as "no result".
            return null;
        }
    }
}
