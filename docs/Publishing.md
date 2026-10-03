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

The Windows `.exe` installer is built using [Advanced Installer Professional Edition](https://www.advancedinstaller.com/). How the project is configured, and how to rebuild it if the `.aip` is ever lost, is in [Advanced Installer project setup](setup/AdvancedInstallerProjectSetup.md).

1. Open `packaging/windows/Argo Books.aip` in Advanced Installer.
2. In the **Product Details** tab, update the version number.
3. Click any other tab. A "Generate new product code?" message box appears: choose **Generate New**.
4. Click **Build** in the top left.

The project's synchronized folder is already pointed at `Argo-Books-Avalonia\publish\win-x64`, so it picks up whatever `dotnet publish` last wrote there.

Note that folder is the **publish** output, not `bin\Release\...`. Pointing it back at `bin\Release` still produces a working installer, so the mistake is silent: the only symptom is users waiting an extra 2.5 seconds on every launch.

The publish output is roughly 100MB larger than a plain build (about 508MB versus 400MB uncompressed) because of the precompiled native code. It also contains fewer files, since publish drops build artefacts that aren't needed at runtime.

## Linux

The Linux distribution is packaged as an [AppImage](https://appimage.org/). The build runs in the cloud via GitHub Actions.

### Build and package

1. Make sure the version branch with your changes is pushed to GitHub.
2. Go to the repo's **Actions** tab on github.com and select **Build Linux AppImage** in the left sidebar.
3. Click **Run workflow**, choose the branch to build from, and click the green **Run workflow** button.
4. Wait for the run to finish (about 5 minutes), then open the run's **Summary** page (not the job log) and scroll to the **Artifacts** section at the bottom. You may have to refresh the page. The artifact is a `.zip`; download then extract it to get `ArgoBooks-X.X.X-linux-x64.AppImage`.
5. To test it on a Linux VM, first make it executable:

   ```bash
   chmod +x ArgoBooks-X.X.X-linux-x64.AppImage
   ```

   Without this, double-clicking does nothing (silently). This only affects local testing; end users always have to mark downloaded AppImages executable regardless of how we build them, since browser downloads never preserve the executable bit.

To build it by hand instead, see [Linux manual build](setup/LinuxManualBuild.md).

### Linux runtime dependencies

The AppImage is self-contained (.NET runtime is bundled), but it depends on system libraries that are pre-installed on all standard desktop Linux distros (Ubuntu, Fedora, Linux Mint, etc.). End users should not need to install anything.

If you're testing on a minimal VM that's missing packages:

```bash
sudo apt install libgtk-3-0 libwebkit2gtk-4.0-37 libsecret-tools policykit-1
```

## macOS

Signing and notarizing needs setting up once first, see [One-time release setup](setup/OneTimeReleaseSetup.md).

**This whole section has to be done on a Mac.** The tools it needs only exist there, so none of it can be done from Windows the way the Linux build can. Clone the repo on the Mac and work there.

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

## Going Live

Everything here happens once all four files are built, in this order.

The app verifies an Ed25519 signature on every update it downloads, and refuses to install files that are unsigned or don't match, which is what step 1 is for. The signing key and tool need setting up once first, see [One-time release setup](setup/OneTimeReleaseSetup.md).

1. Put the **final** `.exe`, `.AppImage` and both `.zip` files together in one folder, then run the signing script:

   ```powershell
   powershell -File "C:\Users\evand\Desktop\Argo-Books-Avalonia\packaging\sign-release.ps1" "C:\Users\evand\Desktop\Argo Books versions\2.0.16" "C:\laragon\www\argo-books-website\avalonia-update.xml"
   ```

   The first path is the folder holding the four files, the second is the website repo's `avalonia-update.xml`. The script signs each file and updates the appcast: signatures, version, file sizes and publication date. It reads the version from the filenames, and stops without touching the appcast if anything is missing or inconsistent.

   Use the stapled macOS zips that `build-app.sh` produced last. Re-zipping a bundle afterwards changes its bytes and invalidates the signature.

   Check the file sizes and build times it prints, because it cannot tell a stale build from a fresh one, only that all four claim the same version. Then review the appcast with `git diff`.

2. Regenerate the translations for the new version's strings (see `tools/ArgoBooks.Translations/README.md`):

   ```powershell
   $env:AZURE_TRANSLATOR_REGION = "canadacentral"
   $env:AZURE_TRANSLATOR_KEY = "your-api-key"
   cd tools/ArgoBooks.Translations
   dotnet run -- --translate
   ```

   The JSON files land in `tools/ArgoBooks.Translations/languages/`, ready to upload in step 6.

3. In the website repo, add an entry for the new version to the What's New page (`whats-new/index.php`).

4. Before going live, run the freshly built Argo Books on all operating systems and test a couple of major features such as the receipt scanner to ensure things work.

5. Commit and push to `main` in Git so the `avalonia-update.xml` and What's New changes deploy.

6. Upload the release files via FileZilla into a new `resources/downloads/<version>/` folder on the server, matching the layout of the previous version:

   - `Argo Books Installer V.<version>.exe`
   - `ArgoBooks-<version>-linux-x64.AppImage`
   - `ArgoBooks-<version>-osx-arm64.zip`
   - `ArgoBooks-<version>-osx-x64.zip`
   - a `languages/` subfolder holding the JSON files from step 2

   The filenames matter: `get_avalonia_installer.php` builds the download links from those exact patterns, and the app fetches translations from `/resources/downloads/{version}/languages/{iso}.json`.

7. Check the website repo's **Verify release signatures** workflow on GitHub. The push in step 5 starts it, and it keeps rechecking for 45 minutes while the upload finishes. It downloads each file in `avalonia-update.xml` and fails if a file is missing or its signature doesn't match, which is what would make the app refuse the update.

The release is now live. The website download buttons serve the new version, and existing installs will show the "A new version is available" banner the next time they check for updates. Test the auto-update by opening the previous version of the app and letting it update, then confirm that the old version was uninstalled and the new one is installed. Once that works, the release is done.

## After the Release: The Stores

Neither store builds the app. Both point at the files already uploaded in step 6, so these only work once the release is live.

### Microsoft Store

The [listing](https://apps.microsoft.com/detail/xpdmdvrxj0xs0m?cid=PCCongratsBnr&hl=en-US&gl=CA) carries a URL to one specific installer, not a "latest" link, so every release needs it repointed.

1. Open the app in Partner Center and go to **Packages**.
2. Click the package ID in the list, then find the **Package URL** field, and point it at the new version.

   `https://argorobots.com/resources/downloads/<version>/Argo Books Installer V.<version>.exe`

   It has to be the static file and it has to name a version. The malware scan, the signing check and package validation all run against one specific binary, so a URL that moved would mean customers get a file Microsoft never reviewed. That is why there is no way to have it follow the newest build, and why this step exists.
3. Run **Package validation**. Silent install, the Add/Remove Programs entry and bundleware always come back "could not identify", because the installer is per-machine and raises a UAC prompt their sandbox cannot click. Not a failure, and Microsoft's own docs say UAC prompts are allowed. Malware and code signing are the two that must pass.
4. Submit. Review has run in under a day in practice against a stated three business days.

### Homebrew

The cask lives on GitHub in [Homebrew's own repo](https://github.com/Homebrew/homebrew-cask/blob/main/Casks/a/argo-books.rb) and pins the version and the SHA256 of both macOS archives, so a release needs a pull request. This has to be done from a Mac.

```
brew bump-cask-pr --version <version> argo-books
```

That reads the new archives, computes both hashes, commits and opens the PR by itself.

The cask file itself is not kept in this repo, because Homebrew's repo owns it and `brew bump-cask-pr` edits their clone, not ours. A second copy here would only drift.

Three things before touching it:

- `livecheck` uses a regex over `avalonia-update.xml`, not the `:sparkle` strategy. Sparkle expects `sparkle:os="macos"` and the appcast says `macos-arm64` and `macos-x64`, which is what NetSparkle writes and the app's updater needs. Leave it alone.
- Each push to a cask PR runs three macOS CI jobs that install and launch the app, so three phantom macOS devices appear in telemetry.
- The `zap trash:` list mirrors the app's own data locations. Renaming `ApplicationName` in `ArgoBooks.Core/Platform/MacPlatformService.cs`, or changing the bundle ID, means the cask needs a pull request of its own, separate from a version bump.

## Once a Year: Province and State Flags

The province and state pickers show a flag for each region, stored in `ArgoBooks/Assets/RegionFlags` and named by ISO code (for example `DE-BY.png`). Region flags rarely change: a US state redesigns one every year or two, and countries occasionally merge or split regions. Check them once a year, not every release.

1. Run the downloader. It reads the regions from `ArgoBooks/Data/Regions.cs`, looks up each one's current flag on Wikidata, and rewrites the PNGs to match the country flags:

   ```powershell
   dotnet run --project C:\Users\evand\Desktop\Argo-Books-Avalonia\tools\ArgoBooks.RegionFlags
   ```

2. Look through the changed images in the Git diff before committing. Wikidata is edited by the public, so a changed flag is usually a real redesign but can be a proposed design or vandalism.

Run it again whenever a region is added to or removed from `Regions.cs`. Where a region has no flag, the downloader takes its coat of arms, then its logo. Irish counties take their coats of arms first, because the flags listed for them are sports colours. A region with none of these shows its country's flag in the picker.

## Notes

The signature covers the file's exact bytes. If an installer file is rebuilt for any reason, put it back in the release folder and run `sign-release.ps1` again. Rerunning is safe: the version replace becomes a no-op and every signature and file size is rewritten from the files that are there now.

To check one file by hand, run `--verify` with the signature string from its enclosure in `avalonia-update.xml`:

```powershell
netsparkle-generate-appcast --verify "C:\path\to\ArgoBooks-2.0.8-linux-x64.AppImage" --signature "t4lRf5lP...8O9zCQ=="
```

Note it prints `Signature valid` or `Signature invalid` and exits 0 either way, so read the text rather than the exit code.
