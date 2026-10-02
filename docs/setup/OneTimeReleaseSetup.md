# One-time release setup

Done once, not per release. [Publishing](../Publishing.md) assumes all of this is already in place.

## Signing and notarizing on macOS

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

## Signing the release files for the updater

- The signing key pair needs to be added to `C:\Users\evand\AppData\Local\netsparkle`. **Back this folder up**. If the private key is lost, shipped versions of the app can't verify future updates; if it leaks, someone who also compromised the website could forge updates. It must never be committed to a repo.
- The matching public key is embedded in the app at `NetSparkleUpdateService.UpdatePublicKey`.
- Intall the signing tool: `dotnet tool install --global NetSparkleUpdater.Tools.AppCastGenerator`
