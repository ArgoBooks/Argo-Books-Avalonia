namespace ArgoBooks.Mobile.Services;

/// <summary>
/// Launches Google ML Kit's Document Scanner (GmsDocumentScanning) full-screen UI: it edge-detects,
/// crops, and straightens a photographed page live, then hands back a JPEG. Configured for a single
/// page with gallery import allowed, so the same UI also serves as the "import from photos" path
/// (the capture screen calls this from both the shutter and the "import from photos" button).
///
/// Unlike QrScanner's turnkey GmsBarcodeScanning (a coroutine-style API that returns the decoded
/// result directly from StartScan()), the document scanner only exposes
/// GmsDocumentScanner.GetStartScanIntent(Activity), which resolves (via Android.Gms.Tasks.Task) to
/// an IntentSender. That has to be launched with Activity.StartIntentSenderForResult and collected
/// back through the hosting Activity's OnActivityResult override - there's no
/// ActivityResultLauncher/androidx.activity.result binding available in this package, so this uses
/// the classic request-code + OnActivityResult plumbing instead (MainActivity.OnActivityResult
/// forwards to HandleActivityResult below).
///
/// Not unit-tested: requires a live Activity, Google Play Services, and (outside the gallery-import
/// path) a camera.
/// </summary>
public static class DocumentScanner
{
    // Arbitrary request code; only needs to be distinct from other StartActivityForResult callers
    // sharing MainActivity (none, at the time of writing).
    private const int RequestCode = 4210;

    private static TaskCompletionSource<byte[]?>? _pending;

    /// <summary>
    /// Shows the ML Kit document scanner (or, if the user picks "import from photos" inside its
    /// own UI, the gallery picker) and returns the cropped/straightened page as JPEG bytes, or null
    /// if the user cancelled, there's no current activity, a scan is already in flight, or the scan
    /// otherwise failed.
    /// </summary>
    public static async Task<byte[]?> ScanAsync()
    {
        var activity = Platform.CurrentActivity;
        if (activity == null || _pending != null)
        {
            return null;
        }

        var options = new GmsDocumentScannerOptions.Builder()
            .SetGalleryImportAllowed(true)
            .SetPageLimit(1)
            .SetResultFormats(GmsDocumentScannerOptions.ResultFormatJpeg)
            .SetScannerMode(GmsDocumentScannerOptions.ScannerModeFull)
            .Build();

        var scanner = GmsDocumentScanning.GetClient(options);

        IntentSender intentSender;
        try
        {
            intentSender = await scanner.GetStartScanIntent(activity).AsAsync<IntentSender>();
        }
        catch (Java.Lang.Exception)
        {
            // Play Services, or the on-device document-scanning module, isn't available.
            return null;
        }

        _pending = new TaskCompletionSource<byte[]?>();
        activity.StartIntentSenderForResult(intentSender, RequestCode, null, 0, 0, 0);
        return await _pending.Task;
    }

    /// <summary>
    /// Called from MainActivity.OnActivityResult; completes the pending ScanAsync() call if the
    /// request code matches. No-op for any other request code (or if no scan is in flight).
    /// </summary>
    public static void HandleActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != RequestCode)
        {
            return;
        }

        var pending = _pending;
        _pending = null;
        if (pending == null)
        {
            return;
        }

        if (resultCode != Result.Ok)
        {
            // User backed out of the scanner (or the gallery picker it launched internally).
            pending.TrySetResult(null);
            return;
        }

        try
        {
            var result = GmsDocumentScanningResult.FromActivityResultIntent(data);
            var pages = result?.Pages;
            var imageUri = pages != null && pages.Count > 0 ? pages[0].ImageUri : null;
            if (imageUri == null)
            {
                pending.TrySetResult(null);
                return;
            }

            var activity = Platform.CurrentActivity;
            using var stream = activity?.ContentResolver?.OpenInputStream(imageUri);
            if (stream == null)
            {
                pending.TrySetResult(null);
                return;
            }

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            pending.TrySetResult(buffer.ToArray());
        }
        catch (Exception)
        {
            pending.TrySetResult(null);
        }
    }
}
