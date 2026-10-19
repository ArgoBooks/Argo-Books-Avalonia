# The bridge release

Done once, for 2.0.21. Copies of Argo Books up to 2.0.20 update through the old updater. The new way is with Velopack. Once 2.0.21 is installed it moves each copy over to the new updater. Every release after it follows [Publishing](../Publishing.md) alone.

## Steps

1. Build all four installer files, following [Publishing](../Publishing.md).

2. Also build the old-style Windows installer, straight after `build-release.ps1` and from the same build:
   1. Open `packaging\windows\Argo Books.aip` in Advanced Installer.
   2. In the **Product Details** tab, set the version to 2.0.21.
   3. Click any other tab. When it asks "Generate new product code?", choose **Generate New**.
   4. Click **Build**.
   5. Copy `packaging\windows\Setup Files\Argo Books.exe` into `publish\` and rename it `Argo Books Installer V.2.0.21.exe`.

   [Advanced Installer project setup](AdvancedInstallerProjectSetup.md) has the details if anything looks different.

3. Follow steps 1 to 4 of **Going Live** in Publishing.

4. Sign the old update list. Put `Argo Books Installer V.2.0.21.exe`, the AppImage and both Mac `.zip` files into one folder, then run:

   ```powershell
   powershell -File packaging\sign-bridge-appcast.ps1 "<that folder>" "C:\laragon\www\argo-books-website\avalonia-update.xml"
   ```

5. Follow step 5 of Going Live, and also upload `Argo Books Installer V.2.0.21.exe` into `resources/downloads/2.0.21/`.

6. Follow steps 6 and 7 of Going Live. The push includes the `avalonia-update.xml` change, and also starts the **Verify release signatures** workflow, which should pass too.

7. In the Homebrew pull request for 2.0.21, change the cask's `livecheck` to read the new release file, since `avalonia-update.xml` stops changing after this release:

   ```ruby
   livecheck do
     url "https://argorobots.com/resources/updates/release.json"
     regex(/"version"\s*:\s*"(\d+(?:\.\d+)+)"/i)
   end
   ```

## What users see

- **Windows:** the old updater installs 2.0.21 as usual, with the admin prompt. When they next open Argo Books, it offers the update again. Clicking it installs the new copy, with no admin prompt, and opens it. Windows then asks once for permission to remove the old copy. If they say no, it asks again on the next two launches, then stops asking.
- **Mac and Linux:** the update installs as usual, and later updates use the new updater.

## Afterwards

- **Never edit `avalonia-update.xml` again.** It stays at 2.0.21 for good, so a copy nobody has opened for months still gets 2.0.21 first.
- Advanced Installer is no longer needed.
