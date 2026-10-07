using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using ArgoBooks.Mobile.Services;
using Avalonia.Android;
using Microsoft.Maui.ApplicationModel;

namespace ArgoBooks.Mobile;

// Avalonia 12's Android head moved app-builder customization onto the Application subclass (see MainApplication.cs); AvaloniaMainActivity is now non-generic.
[Activity(
    Label = "Argo Books",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Platform.Init(this, savedInstanceState);

        // This is a biometric-locked accounting app: mark the window secure so the ledger never
        // leaks into the Android recents thumbnail or a screenshot. FLAG_SECURE also blanks the
        // app-switcher preview, so financial data isn't visible while the app is "locked" in the
        // background. Release only: FLAG_SECURE blocks screenshots/screen recording, which we need
        // for development, demos, and support captures, so Debug builds leave it off.
#if !DEBUG
        Window?.SetFlags(WindowManagerFlags.Secure, WindowManagerFlags.Secure);
#endif
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
    }

    // Driven off the real Activity lifecycle, because OnPause fires whenever the app goes to the background, screen-off and app switching included.
    protected override void OnPause()
    {
        base.OnPause();
        App.NotifyBackgrounded();
    }

    protected override void OnResume()
    {
        base.OnResume();
        App.NotifyForegrounded();
    }

    // Collects the result of the scanner's StartIntentSenderForResult, since ML Kit's document scanner has no ActivityResultLauncher binding.
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        DocumentScanner.HandleActivityResult(requestCode, resultCode, data);
    }
}
