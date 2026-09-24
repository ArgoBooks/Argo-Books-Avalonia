# Advanced Installer project setup

Recovery guide for the Windows installer project. The `.aip` file lives at
`Argo-Books-Avalonia/packaging/windows/Argo Books.aip` and is tracked in git, so
recovering it is a checkout, not a rebuild. This doc is mostly here for reference.
A full rebuild from scratch (about an hour) is only needed if the project file is
lost from the repo as well.

> FYI: "AI" in this doc always means **Advanced Installer**, not artificial intelligence.

## Path conventions

Every file the `.aip` references is a path relative to `packaging/windows/`, and
Advanced Installer resolves those against the `.aip`'s own location. Moving the
file breaks all of them at once, silently, so it needs to stay where it is.

| Reference | Resolves to |
|---|---|
| `..\..\publish\win-x64\` | the `dotnet publish` output, gitignored |
| `..\..\ArgoBooks\Assets\argo-logo.ico` | the app icon, shared with `ArgoBooks.Desktop.csproj` |
| bare filenames | the dialog logos sitting beside the `.aip` |

Nothing points outside the repo. A fresh clone plus a `dotnet publish` is enough
to build the installer.

Advanced Installer regenerates `Setup Files/`, `Argo Books-cache/` and its own
`Advanced Installer/` folder beside the project. All three are gitignored.

## Step 1: Install required tooling

Skip any you already have.

### Advanced Installer
Download from https://www.advancedinstaller.com/download.html, install, and activate using your Caphyon license key (**Help → Activate License**).

Pricing on https://www.advancedinstaller.com/purchase.html is **$399 USD/user/year** for the Professional tier (which is what this project needs). I originally bought it as a perpetual one-time license, but that option is no longer shown on the public pricing page.

### Windows SDK (provides `signtool.exe`)
Download from https://developer.microsoft.com/windows/downloads/windows-sdk/. During setup you only need the **Windows SDK Signing Tools for Desktop Apps** component. Advanced Installer auto-resolves `<AI_SIGNTOOL_FOLDER>signtool.exe` to whatever SDK version is installed.

### .NET 10 SDK (for `dotnet publish`)
Download from https://dotnet.microsoft.com/download. Verify in a new terminal with `dotnet --version`.

### Microsoft Trusted Signing CLI tools
Follow https://learn.microsoft.com/azure/trusted-signing/how-to-signing-integrations to download the client tools package, then extract to `C:\Users\<you>\AppData\Local\Microsoft\MicrosoftTrustedSigningClientTools\`. Confirm `Azure.CodeSigning.Dlib.dll` lands in that folder.

### Azure Trusted Signing account
Azure Trusted Signing needs to be set up. See `Argo-Books-Avalonia/docs/setup/AzureSetup.md` for the full walkthrough.

### Trusted Signing metadata JSON
It should already exist at `Argo-Books-Avalonia/packaging/windows/trusted-signing-metadata.json`. Double-check it's there. If missing, see Step 13 in `Argo-Books-Avalonia/docs/setup/AzureSetup.md` for the contents and how to recreate it.

### Avalonia release build
Run `dotnet publish`, do not use a Rider build:

```bash
dotnet publish ArgoBooks.Desktop -c Release -f net10.0-windows10.0.17763.0 -r win-x64 --self-contained -o publish/win-x64
```

The project sets `PublishReadyToRun`, which precompiles the app to native code and cuts cold start from roughly 6.5 seconds to 3.7 seconds. That setting only takes effect during `publish`. A Rider Release build silently ignores it and produces a working but noticeably slower installer. See `Argo-Books-Avalonia/docs/Publishing.md` for the full publishing flow.

Run the command from the solution root (`Argo-Books-Avalonia`), because `-o` is relative to the current directory rather than to the project folder.

Publish output ends up at `Argo-Books-Avalonia/publish/win-x64/`, alongside `ArgoBooks.Desktop`, not inside it. The folder is gitignored. Advanced Installer's synchronized folder (set up in Step 4) points at this path.

### Icon and logo files
All three are tracked in the repo, so a clone already has them:

| File | Used for |
|---|---|
| `ArgoBooks/Assets/argo-logo.ico` | setup.exe icon, Control Panel entry, shortcuts, and the `.argo` file association |
| `packaging/windows/Argo Books icon transparent square.png` | install dialog logo, light theme |
| `packaging/windows/Argo Books icon white background.png` | install dialog logo, dark theme |

The `.ico` is the same file `ArgoBooks.Desktop.csproj` sets as its
`ApplicationIcon`, referenced rather than duplicated so the installer and the
built exe can never drift apart.

**Both dialog logos must be square.** The AppInstaller theme draws the logo into
a fixed 70x70 box and stretches whatever it is given to fill it, without
preserving proportions, so a wide image comes out visibly stretched vertically.
The masters in the brand library are 480x355, which is why the light-theme file
here is a separate 590x590 version with the artwork centered on a transparent
square. It matches the proportions of the dark-theme file, so the logo is the
same size in both themes.

These are copies of files in the brand library at
`Desktop\Argo Books assets\Third\Logo\`, which is not version controlled. The
repo copies are what ship, so treat them as authoritative and re-copy
deliberately if the artwork changes.

## Step 2: Create the project

1. Open Advanced Installer. Make sure **MSI Installer** is selected in the left sidebar.
2. Click **.NET Application**.
3. On the right, set **Project Language** to **English**.
4. Click **Create New Project**. A "**New .NET Application project**" wizard opens.

The wizard has nine screens. Click **Next** between each.

**Screen 1 - Enter details about your product:**
- Product name: `Argo Books`
- Organization (manufacturer): `Argo Books`

**Screen 2 - Select a distribution type:**
- Choose **EXE setup file** (a single `.exe` with everything bundled inside). This is the format argorobots.com serves to users.

**Screen 3 - Add files to your project:**
- **Browse** to the Avalonia publish output folder: `C:\Users\<you>\Desktop\Argo-Books-Avalonia\publish\win-x64`.
- This must be the **publish** folder, not `bin\Release\...`. Both produce a working installer, so pointing at the wrong one fails silently: the only symptom is every user waiting an extra 2.5 seconds at launch.
- Check **Synchronized folder: recheck the folder and update the package every time the project is loaded or built**. This makes AI re-sync new builds automatically so you don't have to re-add files after every release.

**Screen 4 - Create shortcuts for your applications:**
- Keep **Argo Books.exe** checked. Leave **createdump.exe** unchecked.
- Keep both **Create shortcut on Desktop** and **Create shortcut in Programs group from Start menu** checked too.

**Screen 5 - Configure .NET assemblies in your package:**
- Leave the default **Install as a regular file (no registration)** selected.

**Screen 6 - Configure Launch Conditions:**
- Keep **Add launch conditions** checked.
- **Minimum .NET version**: `.NET Framework 4.0` (this is for the installer UI itself; Argo Books's own .NET 10 runtime is bundled separately by `dotnet publish`).
- **Supported Operating Systems**: leave all **64-bit Windows versions** checked (Win 7 x64 through Win 11 x64).

**Screen 7 - Configure Prerequisites:**
- Keep **Add .NET Framework prerequisite** checked.
- **.NET Framework**: change the dropdown to **.NET Framework 4.0** (matches the minimum version you set on Screen 6).
- **Location**: leave the default **Download single file from URL**. The installer won't bundle .NET Framework into the `.exe` (keeps the download smaller; modern Win 10/11 already has .NET Framework so the download rarely actually runs).
- Leave **Silent Install** unchecked.

**Screen 8 - Application execution:**
- Check **Launch application after install** so users see a pre-checked "Launch Argo Books now?" option on the final install screen.
- Select `Argo Books.exe`.
- Leave the default radio **Optional launch when pressing Finish button** selected.

**Screen 9 - Configure installation UI:**
- **Main installation dialog**: leave the default **Browse installation folder dialog** selected.
- **Dialog Theme**: pick **App Installer**.
- Click **Finish**.

When prompted to save, save the project as `Argo Books.aip` inside
`Argo-Books-Avalonia/packaging/windows/`. The location matters: every file
reference in the project is relative to that folder, and Advanced Installer
resolves them against wherever the `.aip` sits.

## Step 3: Product Details

In the **Product Details** page:

| Field | Value |
|---|---|
| Name | `Argo Books` |
| Product Version | current release (e.g. `2.0.7`) |
| Publisher | `Argo Books` |
| Support link | `https://argorobots.com/documentation/` |
| Contact | `https://argorobots.com/contact-us/` |
| Control Panel Icon | `..\..\ArgoBooks\Assets\argo-logo.ico` (this is the icon Windows shows in Apps & features / Add or Remove Programs) |

| **Upgrade Code** | `{56B4BFD1-ED8C-4FBE-9562-14EB8B82623C}`. This GUID is how Windows recognizes Argo Books across versions. Use this exact value so future installs can upgrade existing copies. |

## Step 4: Verify Files and Folders

The wizard in Step 2 already added the synchronized folder. Just sanity-check it landed in the right place:

1. Go to **Resources → Files and Folders** in the left sidebar.
2. Expand the tree on the left. Under **Application Folder** you should see your build output synced in, with `Argo Books.exe`, `ArgoBooks.dll`, the Avalonia DLLs, etc. listed on the right.

If it's missing, or right-click **Application Folder** → **Add Folder** and point at the Avalonia build path.

## Step 5: Shortcut icons

The shortcuts need their icons pointed at the Argo Books `.ico`:

1. In **Resources → Files and Folders**, look at the tree on the left.
2. Click **Application Shortcut Folder**. In the file list on the right, right-click the `Argo Books` shortcut → **Properties** → set the **Icon** to `..\..\ArgoBooks\Assets\argo-logo.ico`.
3. Click **Desktop** in the tree. Do the same for the shortcut here.

## Step 6: AppInstaller theme logos

Set the two logos that the AppInstaller theme shows on the install dialog:

1. Go to **User Interface → Themes** in the left sidebar.
2. On the right side of the page you'll see two fields: **App Logo Icon** and **App Logo Icon Dark**.
3. Click the **...** button next to **App Logo Icon** and pick `Argo Books icon transparent square.png` from `packaging/windows/`.
4. Click the **...** button next to **App Logo Icon Dark** and pick `Argo Books icon white background.png` from the same folder.

Both must be square. See "Icon and logo files" in Step 1 for why: the theme
stretches the image to fill a square box, so a wide logo comes out distorted.

## Step 7: File associations

This is what makes double-clicking a `.argo` file open that company.

1. Go to **Resources → File Associations** in the left sidebar.
2. Click **New File Association Wizard** in the toolbar. It creates the ProgID, extension and verb together, so there is no separate ProgID field to fill in.
3. Extension: `argo`.
4. Description: `Argo Books Company File`. This is the text Explorer shows in its **Type** column.
5. When the wizard asks you to select the application in your package that should be associated with this type, choose `Argo Books.exe`.
6. Verb: `open`, with **Arguments** set to `"%1"`, including the quotes. Without them, company files stored under a path containing a space fail to open.
7. Select the new ProgID in the tree and rename it to `ArgoBooks.Company`. The default is `[|Manufacturer].[|ProductName].argo`, which resolves to `Argo Books.Argo Books.argo`. ProgIDs are conventionally alphanumeric with periods and no spaces.
8. In **ProgId Properties**, tick **Be the default application associated with these file types**, and check that **Display → Icon** already shows the Argo Books icon. The wizard sets that icon from the executable picked in step 5.

`.argobk` is deliberately not registered. Backup files have no open path, so an open verb would produce a file that looks handleable and then lands the user on the welcome screen.

To confirm the association after installing:

```powershell
Get-ItemProperty "Registry::HKEY_CLASSES_ROOT\$((Get-ItemProperty 'Registry::HKEY_CLASSES_ROOT\.argo').'(default)')\shell\open\command"
```

It should name the executable under `C:\Program Files (x86)\Argo Books\`.

## Step 8: Custom Action `WriteInstallToken`

On the website admin page there's a **funnel** that tracks each user through their journey: first website visit → download → install → using the app → paying for Premium. This custom action is what plugs in the **install** part.

How it works: when a user downloads the installer from argorobots.com, the website server renames the `.exe` to embed a unique code in the filename (e.g. `Argo Books Installer V.2.0.7_6548910d.exe`). This custom action reads that code during install and saves it to disk. On first launch, Argo Books sends the code back to the server, so we know that specific download became an install.

If the user renames the installer before running it (for whatever reason), the code in the filename gets lost and we can't tie the install back to the original download. The install still works fine; the funnel just logs it as an unattributed installation.

1. Go to **Custom Behavior → Custom Actions** and click **New Custom Action**.
2. In the wizard, select: `Execute a VB, Java or PowerShell script` → `PowerShell` → `Script source embedded directly in the project (script inline)`.
3. Name: `WriteInstallToken`.
4. **Parameter values** field at top of script editor:
   ```
   "[AI_SETUPEXEPATH]"
   ```
5. **Script body**:
   ```powershell
   Param([string]$setupExePath)

   try {
       if ($setupExePath -match '_([0-9a-f]{8})\.(exe|msi)$') {
           $token = $matches[1]
           $appDataDir = Join-Path $env:LOCALAPPDATA 'ArgoBooks'
           New-Item -ItemType Directory -Force -Path $appDataDir | Out-Null
           Set-Content -Path (Join-Path $appDataDir 'install_token.txt') `
                       -Value $token -Encoding ASCII -Force
       }
   }
   catch {
       # Never fail the install over a missing token
   }
   ```
6. **Execution Time**: `When the system is being modified (deferred)`.
7. **Execution Options**:
   - Leave `Run under the LocalSystem account with full privileges` **unchecked**. Needs to run as the installing user so `$env:LOCALAPPDATA` resolves to *their* AppData, not the SYSTEM profile.
   - Leave `Wait for custom action to finish before proceeding` **checked**.
   - **Uncheck** `Fail installation if custom action returns an error`. Never break an install over a telemetry feature.
8. **Execution Stage Condition**: 
- Install: **checked**
- Uninstall: **unchecked**
- Maintenance: **unchecked**

## Step 9: Custom Action `OpenUrl`

Opens the uninstall survey at [argorobots.com/uninstall/](https://argorobots.com/uninstall/) in the person's browser when they remove Argo Books. The page asks one question about why they left. Nothing is sent from the machine: the browser opens, and answering is their choice.

Windows only, because Windows is the only platform with an uninstaller.

1. Go to **Custom Behavior → Custom Actions**, search for `url` and add **Open URL**.
2. **URL**:
   ```
   https://argorobots.com/uninstall/?v=[ProductVersion]&p=windows
   ```
   `[ProductVersion]` expands at runtime, so the link always names the build being removed and never needs editing per release.
3. **Execution Time**: `Immediately`. The action has to run as the logged-in user to reach their browser.
4. **Execution Options**: **uncheck both**.
   - `Wait for custom action to finish before proceeding`: with this on, the uninstall waits for the process it spawned to exit. If their browser was not already running, that process *is* the browser, so the uninstall hangs until they close it.
   - `Fail installation if custom action returns an error`: a survey must never be able to block someone removing the software.
5. **Execution Stage Condition**:
   - Install: **unchecked**
   - Uninstall: **checked**
   - Maintenance: **unchecked**
   - Condition: `UPGRADINGPRODUCTCODE = ""`

The condition matters: an update uninstalls the old build and runs its custom actions, so without it every update opens "Argo Books has been removed" in the browser of someone who just updated.

### Two Advanced Installer bugs to expect (23.7)

**The URL field does not show what you paste.** Paste the URL and the box still looks empty. It took the value. Save the project and check the `OpenUrl` row in the `.aip` to confirm.

**Both text fields append a newline when you press Enter.** In the Edit Condition dialog this produces `Invalid operator at position N`, where N is always two past the end of what you can see, because a carriage return and line feed are sitting after it. Type the condition and click **Validate** with the mouse. Never press Enter.

## Step 10: Build settings

In the left sidebar under **Package Definition**:

1. Open **Install Parameters** and check **Run as administrator** (so Windows prompts for UAC elevation; required because Argo Books installs to `C:\Program Files`).
2. Open **Builds → DefaultBuild** and set:

| Setting | Value |
|---|---|
| Package type | Single EXE setup (resources inside) |
| EXE icon | `..\..\ArgoBooks\Assets\argo-logo.ico` |

## Step 11: Digital Signature

In **Digital Signature**:

1. **Enable signing: checked**
2. **Sign Tool**: `Custom`
3. **Path**: `<AI_SIGNTOOL_FOLDER>signtool.exe`
4. **Command line** (single line, no breaks):
   ```
   sign /fd SHA256 /tr "http://timestamp.acs.microsoft.com" /td SHA256 /dlib "C:\Users\<you>\AppData\Local\Microsoft\MicrosoftTrustedSigningClientTools\Azure.CodeSigning.Dlib.dll" /dmdf "C:\Users\<you>\Desktop\Argo-Books-Avalonia\packaging\windows\trusted-signing-metadata.json" /d "[|ProductName]"
   ```

This signs against the Azure-hosted Microsoft Trusted Signing certificate so SmartScreen reputation accrues against the publisher identity over time.

## Step 12: Verify a build

1. Save the project and click **Build**.
2. Output lands at `Argo-Books-Avalonia/packaging/windows/Setup Files/Argo Books Installer V.{version}.exe`. That folder is gitignored.
3. Rename to add a fake token, e.g. `Argo Books Installer V.{version}_abcdef12.exe`.
4. Delete `%LOCALAPPDATA%\ArgoBooks\first_run_reported.marker` if present (the desktop app skips reporting if the marker exists).
5. Run the installer.
6. Check `%LOCALAPPDATA%\ArgoBooks\install_token.txt`. Should contain `abcdef12`.
7. Launch the app then wait a few seconds.
8. Re-check `%LOCALAPPDATA%\ArgoBooks\first_run_reported.marker`. Should contain `reason=token`. If it says `reason=no_token`, the custom action either didn't run or `AI_SETUPEXEPATH` came back empty.

## Rotating the token secret

Only rotate `REFERRAL_TOKEN_SECRET` in the website's `.env` if it leaks (e.g. accidentally committed to git). Do it during a low-traffic period: any download that's already out there stops being attributable for about 2 weeks after the rotation.

## Troubleshooting

**If the token file isn't created during install:** The custom action may not be running, or `AI_SETUPEXEPATH` may be substituting as empty. Temporarily extend the PowerShell script to write a debug log alongside the token file:

```powershell
Set-Content -Path (Join-Path $appDataDir 'install_token_debug.txt') `
            -Value "setupExePath=$setupExePath" -Encoding ASCII -Force
```

Reinstall, then inspect `%LOCALAPPDATA%\ArgoBooks\install_token_debug.txt`. Most failures come from the installer being renamed without a `_xxxxxxxx` suffix.

**If the token file exists but `FirstRunReporter` doesn't report it:** Check `%LOCALAPPDATA%\ArgoBooks\first_run_reported.marker`. If it exists, the app has already attempted reporting and won't retry. Delete the marker to force a re-report on next app launch.

## Notes

- The uninstall survey's page, table and admin tab live in the website repo: `uninstall/index.php`, `uninstall_feedback` and the Uninstalls tab on `admin/app-stats/`.
- For details on the desktop-app side of the token flow (`FirstRunReporter`, marker file, retry logic), see `ArgoBooks.Core/Services/FirstRunReporter.cs` in the Avalonia repo.
- For the server side of how `_xxxxxxxx` tokens are generated, see `referral_install_token()` in [`track_referral_event.php`](../../track_referral_event.php). It is the single shared recipe: [`get_avalonia_installer.php`](../../get_avalonia_installer.php) embeds the token in the served filename and `api/track-app-event.php` verifies it on first run.
