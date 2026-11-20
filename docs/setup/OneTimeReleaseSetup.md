# One-time release setup

Done once, not per release. [Publishing](../Publishing.md) assumes all of this is already in place.

## Velopack's packaging tool

Every build script packs its release with `vpk`. Install it on the Windows PC:

```bash
dotnet tool install --global vpk --version 1.2.161
```

The version has to match the `Velopack` package in `Directory.Packages.props`, and the build scripts stop if it doesn't. When that package is upgraded, upgrade the tool too:

```bash
dotnet tool update --global vpk --version <new version>
```

The Linux and macOS builds install the matching version by themselves in GitHub Actions.

## Code signing on Windows

`build-release.ps1` signs through Azure Trusted Signing, set up in [Azure setup](AzureSetup.md). It signs as whoever is signed in to the Azure CLI (installed in step 12 there), so sign in once:

```powershell
az login
```

If a build ever fails because the sign-in has expired, run it again.

That account needs the **Artifact Signing Certificate Profile Signer** role, which Azure setup step 9 grants.

## Signing and notarizing on macOS

Requires an **Apple Developer Program membership** ($99 USD per year). Without it macOS rejects a downloaded copy with "Argo Books is damaged and can't be opened", so this is not skippable for a public release.

The macOS build runs in GitHub Actions and signs with six repository secrets. They have the same names as in the game repositories, so the values saved there can be reused. Until they exist, the build comes out unsigned and the run says so; if only some are set, the run stops and names the missing ones.

| Secret | What it holds |
|---|---|
| `MACOS_CERTIFICATE_P12` | The Developer ID Application certificate with its private key, as a base64 .p12 |
| `MACOS_CERTIFICATE_PASSWORD` | The password chosen when exporting that .p12 |
| `APPLE_API_KEY_P8` | The App Store Connect API key, the .p8 file in base64 |
| `APPLE_API_KEY_ID` | That key's Key ID |
| `APPLE_API_ISSUER_ID` | The Issuer ID shown above the keys |
| `APPLE_TEAM_ID` | The Apple Developer team ID, ten letters and digits |

### 1. Export the Developer ID certificate as a .p12

On the Mac:

1. Open **Keychain Access** (Applications, Utilities). Choose the **login** keychain on the left and the **My Certificates** tab at the top.
2. Find **Developer ID Application: (your name) (TEAMID)**. Press the arrow beside it: a private key must be listed under it. Without the key the certificate cannot sign, so it has to be exported from the Mac that made the certificate request.
   - If there is no Developer ID Application certificate yet, make one: in Keychain Access choose Keychain Access, Certificate Assistant, **Request a Certificate From a Certificate Authority**, enter your email, choose **Saved to disk**, then at developer.apple.com go to Certificates, IDs & Profiles, Certificates, press +, choose **Developer ID Application**, upload the request and download the certificate. Double-click the downloaded `.cer` to add it to the login keychain beside its key.
3. Right-click the certificate (the certificate line, not the key) and choose **Export "Developer ID Application: ..."**.
4. Choose the format **Personal Information Exchange (.p12)**, name it `developer_id.p12`, save it to the Desktop and press Save.
5. Type a strong password for the file and note it: this is `MACOS_CERTIFICATE_PASSWORD`. macOS then asks for the Mac's login password to allow the export.
6. Open **Terminal** and copy the file as base64:

   ```bash
   base64 -i ~/Desktop/developer_id.p12 | pbcopy
   ```

   The clipboard now holds `MACOS_CERTIFICATE_P12`. Paste it into GitHub straight away (step 3 below).
7. The team ID is the ten characters in brackets at the end of the certificate's name, also shown at developer.apple.com under Account, Membership details. That is `APPLE_TEAM_ID`.
8. Delete `developer_id.p12` from the Desktop once the secrets are saved, or keep it only in a password manager.

### 2. Create the App Store Connect API key

The build notarizes with this key rather than an Apple ID password.

1. Sign in at appstoreconnect.apple.com and open **Users and Access**.
2. Choose the **Integrations** tab, then **App Store Connect API** on the left, then **Team Keys**. The first time, the Account Holder presses **Request Access** and accepts the terms.
3. Press **+** (Generate API Key). Name it `GitHub notarization` and choose the **Developer** access. Press Generate.
4. Copy the **Issuer ID** shown above the list: that is `APPLE_API_ISSUER_ID`.
5. Copy the new key's **Key ID** from the list: that is `APPLE_API_KEY_ID`.
6. Press **Download** beside the key. Apple only allows this once, so keep the file `AuthKey_KEYID.p8` safe until the secret is saved.
7. Copy the file as base64, in PowerShell on Windows:

   ```powershell
   [Convert]::ToBase64String([IO.File]::ReadAllBytes("$HOME\Downloads\AuthKey_KEYID.p8")) | Set-Clipboard
   ```

   or on the Mac:

   ```bash
   base64 -i ~/Downloads/AuthKey_KEYID.p8 | pbcopy
   ```

   The clipboard now holds `APPLE_API_KEY_P8`.

The key stays valid until it is revoked on that page. A key already made for the games works here too.

### 3. Add the secrets on GitHub

1. Open the repository on GitHub, then **Settings**, **Secrets and variables**, **Actions**.
2. On the **Secrets** tab, press **New repository secret**.
3. Type the name exactly as in the table above, paste the value, and press **Add secret**.
4. Repeat for all six.

The GitHub command line does the same from PowerShell. It asks for each value, so nothing lands in the shell history:

```powershell
gh secret set APPLE_TEAM_ID --repo ArgoBooks/Argo-Books-Avalonia
```

GitHub never shows a secret again once saved, and the workflow keeps them out of its logs. To replace one, add it again with the same name.

Run **Build macOS app** once afterwards. The artifact's name no longer ends in `-UNSIGNED`, and the log of each build step ends with Apple accepting the notarization.

Building on a Mac by hand needs its own keychain setup instead, described in [macOS manual build](MacManualBuild.md).

## Signing the release files for the updater

- The signing key pair needs to be added to `C:\Users\evand\AppData\Local\netsparkle`. **Back this folder up**. If the private key is lost, shipped versions of the app can't verify future updates; if it leaks, someone who also compromised the website could forge updates. It must never be committed to a repo.
- The matching public key is embedded in the app at `VelopackUpdateService.UpdatePublicKey`.
- Install the signing tool: `dotnet tool install --global NetSparkleUpdater.Tools.AppCastGenerator`. The app no longer uses NetSparkle, but this tool still makes the Ed25519 signatures, and copies of the app already out there trust the keys it made.
