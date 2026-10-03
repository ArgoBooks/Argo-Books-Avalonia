# Argo Books Mobile (Android)

The Argo Books companion app for Android: pair with the desktop app over an end-to-end-encrypted channel, view your books (dashboard, data, analytics) read-only, and capture receipts with the camera. Built with Avalonia (not native Android / not MAUI), sharing code with the desktop app through `ArgoBooks.Shared`.

- **Project type:** Avalonia 12 Android head (`net10.0-android`).
- **References:** `ArgoBooks.Shared` only (never `ArgoBooks.Core`, which pulls in desktop-only deps).
- **App id:** `com.argorobots.argobooks`.

## Prerequisites

- **.NET 10 SDK** with the Android workload (`dotnet workload install android`).
- **JDK 21** (the Android build needs it). Installed at `C:\Program Files\Microsoft\jdk-21.x-hotspot`.
- **Android SDK** at `%LOCALAPPDATA%\Android\Sdk` (installed by Android Studio or the workload).
- **A Play-enabled emulator or a physical device.** ML Kit (the receipt scanner) and the biometric lock both need Google Play services, so use a `google_apis_playstore` emulator image or a real phone. The `Pixel_9a` AVD on this machine is already Play-enabled.

You do **not** build this app in Android Studio. Android Studio only supplies the SDK, emulator, and `adb`. The app is built by Rider or the `dotnet` CLI.

## Running it

The emulator (or device) must be running **before** you deploy. Rider does not reliably auto-boot it.

### 1. Start the emulator

```
"C:\Users\evand\AppData\Local\Android\Sdk\emulator\emulator.exe" -avd Pixel_9a
```

Wait for the home screen. Confirm it is connected:

```
"C:\Users\evand\AppData\Local\Android\Sdk\platform-tools\adb.exe" devices
```

### 2. Deploy the app

**From the CLI (repo root):**

```
dotnet build ArgoBooks.Mobile/ArgoBooks.Mobile.csproj -c Debug -f net10.0-android -t:Run
```

`-t:Run` builds, installs, and launches on the running emulator. The first build is slow (it pulls down dependencies); later builds are fast.

**From Rider:** pick `ArgoBooks.Mobile` as the startup project, select `Pixel_9a` in the device dropdown, and press Run. If the device shows as "unavailable", the emulator is not booted yet, start it first (step 1).

### 3. Relaunch it without rebuilding

If you close the app while the emulator is still running, it is still installed, so there is nothing to redeploy. Start it again with:

```
"C:\Users\evand\AppData\Local\Android\Sdk\platform-tools\adb.exe" shell monkey -p com.argorobots.argobooks -c android.intent.category.LAUNCHER 1
```

`monkey` with the LAUNCHER category is used here because it does not need the activity name, which for a .NET Android build is a generated `crc64...` class rather than something stable you can type.

Confirm it came up:

```
"C:\Users\evand\AppData\Local\Android\Sdk\platform-tools\adb.exe" shell pidof com.argorobots.argobooks
```

A PID means it is running. No output means it is not.

## Environment (which server it talks to)

The server base URL is chosen by build config in `Services/MobileApiConfig.cs`:

- **Debug build -> `https://dev.argorobots.com`** (sandbox).
- **Release build -> `https://argorobots.com`** (production).

The QR/pairing payload carries no host, so **the phone, the desktop, and the server must all be on the same environment**. To test end to end: run this app in Debug, run the desktop app in Debug (so it also targets the dev subdomain), and make sure the sync API is deployed to `dev.argorobots.com` with the sync tables present.

## Pairing on an emulator

The emulator's back camera is a fake virtual scene, not a real camera, so QR scanning is awkward. On an emulator, pair using the **"paste pairing data" / manual code** path on the Connect screen instead of "Scan QR code". Generate the code on the desktop: Settings > Mobile app > Connect a phone.

Receipt capture is the same story: use **"Import from photos"** (drag a receipt image onto the emulator window so it lands in the gallery) rather than the live shutter. On a real phone, the camera paths work normally.

## Screenshots and screen recording

The app sets `FLAG_SECURE` so the ledger cannot leak into the Android recents thumbnail or a screenshot, **but only in Release builds** (see `MainActivity.cs`). Debug builds leave it off so you can screenshot and screen-record during development:

```
"C:\Users\evand\AppData\Local\Android\Sdk\platform-tools\adb.exe" -s emulator-5554 exec-out screencap -p > shot.png
```

If a screenshot comes back solid black, you are on a Release build; switch to Debug.
