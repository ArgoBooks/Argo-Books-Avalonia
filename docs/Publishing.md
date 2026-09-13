# Publishing Argo Books

## Before You Build

1. Update the version number in `Directory.Build.props`
2. Run all tests: `dotnet test ArgoBooks.Tests`

## Windows

### Build

**Use `dotnet publish`, not a Rider build.** The project sets `PublishReadyToRun`, which precompiles IL to native code and cuts cold start from about 6.5 seconds to about 3.7 seconds. That property applies only to `publish`; a plain Rider Release build ignores it and ships the slower output.

Run it from the solution root (`Argo-Books-Avalonia`):

```bash
dotnet publish ArgoBooks.Desktop -c Release -f net10.0-windows10.0.17763.0 -r win-x64 --self-contained -o publish/win-x64
```

Output lands at `Argo-Books-Avalonia\publish\win-x64`.

A Rider Release build is still fine for local testing, it just won't have the startup improvement.

### Package

The Windows `.exe` installer is built using [Advanced Installer Professional Edition](https://www.advancedinstaller.com/).

1. Open `packaging/windows/Argo Books.aip` in Advanced Installer.
2. In the **Product Details** tab, update the version number.
3. Click any other tab. A "Generate new product code?" message box appears: choose **Generate New**.
4. Click **Build** in the top left.

The project's synchronized folder is already pointed at `Argo-Books-Avalonia\publish\win-x64`, so it picks up whatever `dotnet publish` last wrote there.

Note that folder is the **publish** output, not `bin\Release\...`. Pointing it back at `bin\Release` still produces a working installer, so the mistake is silent: the only symptom is users waiting an extra 2.5 seconds on every launch.

The publish output is roughly 100MB larger than a plain build (about 508MB versus 400MB uncompressed) because of the precompiled native code. It also contains fewer files, since publish drops build artefacts that aren't needed at runtime.

## Linux

The Linux distribution is packaged as an [AppImage](https://appimage.org/). The build runs in the cloud via GitHub Actions, so no Linux VM is needed.

### Build and package (GitHub Actions, recommended)

1. Make sure the version branch with your changes is pushed to GitHub.
2. Go to the repo's **Actions** tab on github.com and select **Build Linux AppImage** in the left sidebar.
3. Click **Run workflow**, choose the branch to build from, and click the green **Run workflow** button.
4. Wait for the run to finish (about 5 minutes), then open the run's **Summary** page (not the job log) and scroll to the **Artifacts** section at the bottom. You may have to refresh the page. The artifact is a `.zip`; download then extract it to get `ArgoBooks-X.X.X-linux-x64.AppImage`.
5. To test it on a Linux VM, first make it executable:

   ```bash
   chmod +x ArgoBooks-X.X.X-linux-x64.AppImage
   ```

   Without this, double-clicking does nothing (silently). This only affects local testing; end users always have to mark downloaded AppImages executable regardless of how we build them, since browser downloads never preserve the executable bit.

### Manual build (reference only)

The commands below are what the workflow runs. Use them only if you need to build without GitHub Actions.

#### Step 1: Build (on Windows)

.NET cross-compiles, so this produces Linux binaries without needing a Linux machine:

```bash
dotnet publish ArgoBooks.Desktop -c Release -f net10.0 -r linux-x64 --self-contained -o publish/linux-x64
```

#### Step 2: Copy to Linux VM

Copy these to your Linux VM (e.g. via shared folder, Google Drive, or USB):
- The `publish/linux-x64/` folder (the build output)
- The `packaging/linux/` folder (desktop entry, MIME type, build script)

#### Step 3: Package as AppImage (on Linux VM)

One-time setup: install FUSE (required to run AppImage tools) and [appimagetool](https://github.com/AppImage/appimagetool):

```bash
sudo apt install libfuse2
wget https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
chmod +x appimagetool-x86_64.AppImage
sudo mv appimagetool-x86_64.AppImage /usr/local/bin/appimagetool
```

Then `cd` into the folder that contains both `publish/` and `packaging/`, and run the script with the version number from `Directory.Build.props`:

```bash
cd ~/Downloads
chmod +x packaging/linux/build-appimage.sh
sed -i 's/\r$//' packaging/linux/*.sh packaging/linux/*.desktop packaging/linux/*.xml
./packaging/linux/build-appimage.sh 2.0.8
```

This produces `publish/ArgoBooks-2.0.8-linux-x64.AppImage`.

### Linux runtime dependencies

The AppImage is self-contained (.NET runtime is bundled), but it depends on system libraries that are pre-installed on all standard desktop Linux distros (Ubuntu, Fedora, Linux Mint, etc.). End users should not need to install anything.

If you're testing on a minimal VM that's missing packages:

```bash
sudo apt install libgtk-3-0 libwebkit2gtk-4.0-37 libsecret-tools policykit-1
```

## macOS

**Everything in this section runs on the Mac itself, both the setup and the build.** Unlike Linux, none of it cross-compiles from Windows: the bundle, the icon, the signature and the notarization all need macOS-only tools (`iconutil`, `sips`, `codesign`, `notarytool`). Clone the repo on the Mac and work there.

### One-time setup

Requires an **Apple Developer Program membership** ($99 USD per year). Without it macOS rejects a downloaded copy with "Argo Books is damaged and can't be opened", so this is not skippable for a public release.

1. Install the .NET 10 SDK (arm64) and the Xcode Command Line Tools:

   ```bash
   xcode-select --install
   ```

2. In the Apple Developer portal, create a **Developer ID Application** certificate and install it into the login keychain. Confirm it's there:

   ```bash
   security find-identity -v -p codesigning
   ```

   The full string it prints, `Developer ID Application: Your Name (TEAMID)`, is what the build script needs.

3. Create an app-specific password at appleid.apple.com, then store the notarization credentials once. They are saved in the keychain, so this is not repeated per release:

   ```bash
   xcrun notarytool store-credentials "argo-notary" \
     --apple-id you@example.com --team-id TEAMID --password <app-specific-password>
   ```

### Build and package

`packaging/macos/build-app.sh` does the whole thing: publishes, assembles the `.app`, generates the `.icns` from `ArgoBooks/Assets/argo-logo.png`, signs, notarizes, staples and zips.

Both Apple Silicon and Intel ship, so there are two builds. The script builds Apple Silicon by default, and setting `RID=osx-x64` makes it build Intel instead. From the repo root:

```bash
export APPLE_SIGN_IDENTITY="Developer ID Application: Your Name (TEAMID)"
export APPLE_NOTARY_PROFILE="argo-notary"
./packaging/macos/build-app.sh
RID=osx-x64 ./packaging/macos/build-app.sh
```

Output is `publish/ArgoBooks-<version>-osx-arm64.zip` and `publish/ArgoBooks-<version>-osx-x64.zip`. Notarization normally takes a few minutes per build and the script waits for it.

With neither environment variable set the script still produces a bundle. That is fine for testing on the build Mac itself, but a downloaded copy is blocked on every other machine, so never ship one.

### What the .app bundle is

A `.app` is a directory that Finder displays as one icon:

```
Argo Books.app/
  Contents/
    Info.plist              bundle id, version, icon name, minimum macOS
    MacOS/                  the dotnet publish output, unchanged
      Argo Books
    Resources/
      argo-logo.icns
```

It is the macOS counterpart of the AppDir the Linux script builds. `Info.plist` and `entitlements.plist` live in `packaging/macos/`; the version placeholder in `Info.plist` is substituted at build time.

### Why a .zip and not a .dmg

The exact names `ArgoBooks-{version}-osx-arm64.zip` and `ArgoBooks-{version}-osx-x64.zip` are already expected by `GetInstallerFileName()` in `NetSparkleUpdateService.cs`, which picks by the Mac's processor, and by `$platformPatterns` in the website's `get_avalonia_installer.php`. The updater unzips the archive, finds the `.app` inside and swaps it into place. A `.dmg` would mean rewriting that path for no benefit, since the bundle is self-contained and there is nothing to install.

### Gotchas

- **Sign the final stapled zip, not an earlier one.** Stapling rewrites the bundle, so an archive made before it has different bytes and its NetSparkle signature fails on every user's machine. The script already zips last; this only bites if you rebuild the archive by hand.
- **The entitlements are load-bearing.** Notarization requires the hardened runtime, which by default blocks the JIT, unsigned executable memory, and loading the bundled native libraries. Drop any entry from `packaging/macos/entitlements.plist` and the app is killed at launch with no useful message.
- **Every nested native library is signed individually.** `codesign --deep` silently misses them.
- **The bundle name and `CFBundleExecutable` must both stay `Argo Books`.** The updater walks up from the running executable looking for a `.app` parent, and falls back to `/Applications/Argo Books.app`.

## Sign the Release Files

The app verifies an Ed25519 signature on every update it downloads, and refuses to install files that are unsigned or don't match. Every file referenced by the appcast must therefore be signed with our private key.

### One-time setup

- The signing key pair needs to be added to `C:\Users\evand\AppData\Local\netsparkle`. **Back this folder up**. If the private key is lost, shipped versions of the app can't verify future updates; if it leaks, someone who also compromised the website could forge updates. It must never be committed to a repo.
- The matching public key is embedded in the app at `NetSparkleUpdateService.UpdatePublicKey`.
- Intall the signing tool: `dotnet tool install --global NetSparkleUpdater.Tools.AppCastGenerator`

### Each release

1. After producing the **final** `.exe`, `.AppImage` and both `.zip` files, generate a signature for each file:

   ```powershell
   netsparkle-generate-appcast --generate-signature "C:\path\to\Argo Books Installer V.2.0.8.exe"
   netsparkle-generate-appcast --generate-signature "C:\path\to\ArgoBooks-2.0.8-linux-x64.AppImage"
   netsparkle-generate-appcast --generate-signature "C:\path\to\ArgoBooks-2.0.8-osx-arm64.zip"
   netsparkle-generate-appcast --generate-signature "C:\path\to\ArgoBooks-2.0.8-osx-x64.zip"
   ```

   Each command prints a base64 signature string.

   For the macOS zips these must be the stapled archives that `build-app.sh` produced last. Re-zipping a bundle afterwards changes its bytes and invalidates the signature.

2. In the website repo, update `avalonia-update.xml`
   - On each `<enclosure>`, update `sparkle:edSignature`. The signature is the long base64 string printed in step 1. The `.exe`'s signature goes on the `sparkle:os="windows"` enclosure, the `.AppImage`'s on the `sparkle:os="linux"` one, the `osx-arm64.zip`'s on `sparkle:os="macos-arm64"`, and the `osx-x64.zip`'s on `sparkle:os="macos-x64"`. Don't swap the two Mac signatures: each Mac would reject its own download.
   - All the version numbers in the file. For example, do a replace all for `2.0.11` and update it to `2.0.12`, or whatever the version is.

3. Regenerate the translations for the new version's strings (see `tools/ArgoBooks.Translations/README.md`):

   ```powershell
   $env:AZURE_TRANSLATOR_REGION = "canadacentral"
   $env:AZURE_TRANSLATOR_KEY = "your-api-key"
   cd tools/ArgoBooks.Translations
   dotnet run -- --translate
   ```

   The JSON files land in `tools/ArgoBooks.Translations/languages/`, ready to upload in step 7.

4. In the website repo, add an entry for the new version to the What's New page (`whats-new/index.php`).

5. Before going live, run the freshly built Argo Books on all operating systems and test a couple of major features such as the receipt scanner to ensure things work.

6. Commit and push to `main` in Git so the `avalonia-update.xml` and What's New changes deploy.

7. Upload the release files via FileZilla into a new `resources/downloads/<version>/` folder on the server, matching the layout of the previous version:

   - `Argo Books Installer V.<version>.exe`
   - `ArgoBooks-<version>-linux-x64.AppImage`
   - `ArgoBooks-<version>-osx-arm64.zip`
   - `ArgoBooks-<version>-osx-x64.zip`
   - a `languages/` subfolder holding the JSON files from step 3

   The filenames matter: `get_avalonia_installer.php` builds the download links from those exact patterns, and the app fetches translations from `/resources/downloads/{version}/languages/{iso}.json` (`LanguageService.DownloadUrlTemplate`).

The release is now live. The website download buttons serve the new version, and existing installs will show the "A new version is available" banner the next time they check for updates. Test the auto-update by opening the previous version of the app and letting it update, then confirm that the old version was uninstalled and the new one is installed. Once that works, the release is done.

## Notes

The signature covers the file's exact bytes. If an installer file is rebuilt for any reason, re-sign it and update `avalonia-update.xml`.

If you want to double-check an installer file before publishing, run `--verify` with that same file's signature string (the text printed by step 1):

```powershell
netsparkle-generate-appcast --verify "C:\path\to\ArgoBooks-2.0.8-linux-x64.AppImage" --signature "t4lRf5lP...8O9zCQ=="
```
