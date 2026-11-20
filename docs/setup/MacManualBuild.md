# Building the macOS app by hand

The release build runs in GitHub Actions and needs no Mac, which is what [Publishing](../Publishing.md) describes. These are the same steps run by hand on a Mac, for when the workflow is unavailable or you are debugging what it does.

## One-time setup on the Mac

1. Install the .NET 10 SDK (arm64), the Xcode Command Line Tools, and `vpk` at the version in `Directory.Packages.props`:

   ```bash
   xcode-select --install
   dotnet tool install --global vpk --version 1.2.161
   ```

2. Install the **Developer ID Application** certificate into the login keychain, as described in [One-time release setup](OneTimeReleaseSetup.md#signing-and-notarizing-on-macos). Confirm it's there:

   ```bash
   security find-identity -v -p codesigning
   ```

   The full string it prints, `Developer ID Application: Your Name (TEAMID)`, is what the build script needs.

3. Create an app-specific password at appleid.apple.com, then store the notarization credentials once. They are saved in the keychain, so this is not repeated per release:

   ```bash
   xcrun notarytool store-credentials "argo-notary" \
     --apple-id you@example.com --team-id TEAMID --password <app-specific-password>
   ```

## Build

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
