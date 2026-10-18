# Publishing Argo Books

Tools, keys and accounts need setting up once first, see [One-time release setup](setup/OneTimeReleaseSetup.md).

Every build lands in the repo's `publish\` folder. All four platforms end up there on the Windows PC before going live.

## Before You Build

1. Update the version number in `Directory.Build.props`
2. Run all tests: `dotnet test ArgoBooks.Tests`

## Windows

From the repo root:

```powershell
powershell -File packaging\windows\build-release.ps1
```

This builds, signs and packages the app, and puts the installer at `publish\ArgoBooks-<version>-win-Setup.exe`.

## Linux

The Linux build runs in the cloud via GitHub Actions.

### Build and package

1. Make sure the version branch with your changes is pushed to GitHub.
2. Go to the repo's **Actions** tab on github.com and select **Build Linux AppImage** in the left sidebar.
3. Click **Run workflow**, choose the branch to build from, and click the green **Run workflow** button.
4. Wait for the run to finish (about 5 minutes), then open the run's **Summary** page (not the job log) and scroll to the **Artifacts** section at the bottom. You may have to refresh the page.
5. Download the artifact `.zip` and extract it into the repo's `publish\` folder.
6. To test it on a Linux VM, first make it executable:

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

**This whole section has to be done on a Mac.** The tools it needs only exist there, so none of it can be done from Windows the way the Linux build can. Clone the repo on the Mac and work there.

### Build and package

`packaging/macos/build-app.sh` does the whole thing: publishes, assembles the `.app`, generates the `.icns` from `ArgoBooks/Assets/argo-logo.png`, signs, notarizes and zips it.

Both Apple Silicon and Intel ship, so there are two builds. The script builds Apple Silicon by default, and setting `RID=osx-x64` makes it build Intel instead. From the repo root:

```bash
export APPLE_SIGN_IDENTITY="Developer ID Application: Your Name (TEAMID)"
export APPLE_NOTARY_PROFILE="argo-notary"
./packaging/macos/build-app.sh
RID=osx-x64 ./packaging/macos/build-app.sh
```

Notarization normally takes a few minutes per build and the script waits for it.

When both are done, copy the two `.zip` files and the `velopack` folder from the Mac's `publish/` into `publish\` on the Windows PC.

With neither environment variable set the script still produces a bundle. That is fine for testing on the build Mac itself, but a downloaded copy is blocked on every other machine, so never ship one.

### What the .app bundle is

A `.app` is a directory that Finder displays as one icon:

```
Argo Books.app/
  Contents/
    Info.plist              bundle id, version, icon name, minimum macOS
    MacOS/                  the dotnet publish output, plus Velopack's updater
      Argo Books
    Resources/
      argo-logo.icns
```

It is the macOS counterpart of the AppDir the Linux script builds. `Info.plist` and `entitlements.plist` live in `packaging/macos/`; the version placeholder in `Info.plist` is substituted at build time.

### Gotchas

- **The entitlements are load-bearing.** Notarization requires the hardened runtime, which by default blocks the JIT, unsigned executable memory, and loading the bundled native libraries. Drop any entry from `packaging/macos/entitlements.plist` and the app is killed at launch with no useful message.
- **Every nested native library is signed individually.** `codesign --deep` silently misses them.
- **The bundle name and `CFBundleExecutable` must both stay `Argo Books`.** The website, the Homebrew cask and the update package all expect that name.

## Going Live

Everything here happens once all four platforms are built and sitting in `publish\` on the Windows PC, in this order.

1. Sign the release, so installed copies of the app trust the update:

   ```powershell
   powershell -File packaging\sign-release.ps1 -Website "C:\laragon\www\argo-books-website"
   ```

   It also puts the files the website needs into the website repo. It stops if anything is missing or the builds are from different versions.

   Check the build times it prints are all from today's builds.

2. Regenerate the translations for the new version's strings (see `tools/ArgoBooks.Translations/README.md`):

   ```powershell
   $env:AZURE_TRANSLATOR_REGION = "canadacentral"
   $env:AZURE_TRANSLATOR_KEY = "your-api-key"
   cd tools/ArgoBooks.Translations
   dotnet run -- --translate
   ```

   The JSON files land in `tools/ArgoBooks.Translations/languages/`, ready to upload in step 5.

3. In the website repo, add an entry for the new version to the What's New page (`whats-new/index.php`).

4. Before going live, run the freshly built Argo Books on all operating systems and test a couple of major features such as the receipt scanner to ensure things work.

5. Upload with FileZilla, using the list the signing script printed in step 1. Add a `languages/` subfolder holding the JSON files from step 2 to the new `resources/downloads/<version>/` folder.

   Upload before pushing. Otherwise installed copies are told about an update that isn't on the server yet.

6. Commit and push the website repo to `main`. The website's download buttons switch to the new version once this deploys.

7. Check the website repo's **Verify Velopack release** workflow on GitHub. The push in step 6 starts it, and it fails if any file is missing, damaged, or not signed properly.

The release is now live. Existing installs will show the "A new version is available" banner the next time they check for updates. Test the auto-update by opening the previous version of the app and letting it update, then confirm the new version opens. Once that works, the release is done.

The previous release's `.nupkg` files in `resources/updates/` on the server can be deleted a few days later.

2.0.21 was released with extra one-time steps to move everyone off the old installer, described in [The bridge release](setup/BridgeRelease.md).

## After the Release: The Stores

Neither store builds the app. Both point at the files already uploaded in step 5, so these only work once the release is live.

### Microsoft Store

The [listing](https://apps.microsoft.com/detail/xpdmdvrxj0xs0m?cid=PCCongratsBnr&hl=en-US&gl=CA) carries a URL to one specific installer, not a "latest" link, so every release needs it repointed.

1. Open the app in Partner Center and go to **Packages**.
2. Click the package ID in the list, then find the **Package URL** field, and point it at the new version.

   `https://argorobots.com/resources/downloads/<version>/ArgoBooks-<version>-win-Setup.exe`

   It has to be the static file and it has to name a version. The malware scan, the signing check and package validation all run against one specific binary, so a URL that moved would mean customers get a file Microsoft never reviewed. That is why there is no way to have it follow the newest build, and why this step exists.

   The silent install parameter is `--silent`.
3. Run **Package validation**. Malware and code signing are the two that must pass.
4. Submit. Review has run in under a day in practice against a stated three business days.

### Homebrew

The cask lives on GitHub in [Homebrew's own repo](https://github.com/Homebrew/homebrew-cask/blob/main/Casks/a/argo-books.rb) and pins the version and the SHA256 of both macOS archives, so a release needs a pull request. This has to be done from a Mac.

```
brew bump-cask-pr --version <version> argo-books
```

That reads the new archives, computes both hashes, commits and opens the PR by itself.

The cask file itself is not kept in this repo, because Homebrew's repo owns it and `brew bump-cask-pr` edits their clone, not ours. A second copy here would only drift.

Three things before touching it:

- `livecheck` reads the version from `https://argorobots.com/resources/updates/release.json`. Leave it alone.
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

If anything is rebuilt after signing, run `sign-release.ps1` again. Rerunning it is always safe.

To check one file's signature by hand, pass the contents of its `.sig` file to `--verify`:

```powershell
netsparkle-generate-appcast --verify "publish\velopack\linux\ArgoBooksApp-2.0.22-linux-full.nupkg" --signature (Get-Content "publish\velopack\linux\ArgoBooksApp-2.0.22-linux-full.nupkg.sig")
```

Note it prints `Signature valid` or `Signature invalid` and exits 0 either way, so read the text rather than the exit code.

To test an update without real users seeing it, build with `build-release.ps1 -Sandbox`, sign with `sign-release.ps1 -Sandbox -KeyPath <sandbox key folder>`, and upload to `resources/updates-sandbox/` on dev.argorobots.com. Only Debug builds check there.
