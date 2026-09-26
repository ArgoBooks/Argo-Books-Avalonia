# License Key

A license key unlocks the premium features. The key is checked with the server, tied to one device, and saved on that device in encrypted form.

## Key format

Keys look like `XXXX-XXXX-XXXX-XXXX-XXXX`: 20 letters and digits in groups of four, 24 characters with the dashes. Capitals don't matter; keys are turned into capitals before use.

## Checking a key

![License Validation Flow](diagrams/license-key/license-validation-flow.svg)

### Activating

When the user enters a key in the upgrade modal:

1. The app checks the key has the right format.
2. It sends the key and the device ID to the server (`/api/license/redeem.php`).
3. The server marks the key as used and ties it to that device.
4. The app saves the license, encrypted, on the device.
5. Premium features unlock straight away.

### Checking again later

The app sends the saved key and device ID to `/api/license/validate.php` to check the subscription is still active. The answer is one of:

- `Valid`: active, and tied to this device
- `InvalidKey`: the server doesn't recognize the key
- `ExpiredSubscription`: the subscription has ended
- `WrongDevice`: the key is now tied to a different device
- `NetworkError`: the server couldn't be reached

### Moving to a new device

Entering the key on a new device works at any time. The server moves the key to the new device straight away, and nothing has to be done on the old one first.

The next time the old device starts, the check returns `WrongDevice`. The app removes the saved license there and tells the user: *"Your license key has been activated on a different device."*

## How the license is saved

The license is saved in the app's global settings file, encrypted so it only works on the computer it was activated on:

| Field | What it holds |
|-------|---------------|
| `LicenseData` | The encrypted license: premium status, key and activation date |
| `Salt` | A random salt used when making the encryption key |
| `Iv` | The random IV for AES-256-GCM |

The encryption password is made from the computer's own ID (`IPlatformService.GetMachineId()`) plus the fixed text `ArgoBooks_License_v2`, hashed with SHA-256. The encryption service turns that and the salt into an AES-256-GCM key. Because the machine ID is part of it, the license can't be copied to another computer. If it can't be decrypted, for example because the machine ID changed, the app treats it as missing and goes back to the free plan.

The **device ID** sent to the server is that same SHA-256 hash. It stays the same across restarts and is different on every computer. The server uses it to stop one key being used on several devices, and to record which device activated each key.

## Buying and cancelling

The upgrade modal gets current prices from `/api/pricing/plans.php`. Users buy a subscription at `argorobots.com/pricing/premium/`, enter the key they receive in the upgrade modal, and cancel at `argorobots.com/community/users/subscription.php`.

## Usage limits

Receipt scans and AI spreadsheet imports each have a monthly limit that depends on the plan. The server keeps the count for each license key. `ReceiptUsageService` and `AiImportUsageService` handle it the same way:

- **Before each scan or import**, the app asks the server how many are left. The answer is reused for 5 minutes to save calls.
- **After a successful one**, the app tells the server to add one to the count. If the server can't be reached at that moment, the result is still kept; it just isn't counted.
- **If the limit is reached**, the scan or import is blocked and the user sees the date the count resets (the first of next month).
- **If the server can't be reached** and there is no answer from the last 5 minutes, it is blocked with a message saying whether the internet or the Argo Books server is down. Scans and imports need the internet anyway, because the AI is reached through the server.

One difference: when the server replies with an error other than "limit reached", an AI import is allowed to go ahead, but a receipt scan is blocked.

The server's answer includes:

| Field | Meaning |
|-------|---------|
| `ScanCount` | Used this month (`ImportCount` for AI imports) |
| `MonthlyLimit` | The limit for the plan |
| `Remaining` | Left this month |
| `Tier` | The plan's name |
| `ResetsAt` | When the count resets |

## Connection problems

When a license or usage call fails, the app checks whether the internet works, then whether `argorobots.com` can be reached, and shows the matching message: no internet connection, Argo Books servers unreachable, or a general failure.

License calls time out after 30 seconds; usage calls after 15 seconds.

## Where the code is

| Service | File | What it does |
|---------|------|--------------|
| `LicenseService` | `ArgoBooks.Core/Services/LicenseService.cs` | Saving, encrypting and loading the license, checking it online, the device ID |
| `ReceiptUsageService` | `ArgoBooks.Core/Services/ReceiptUsageService.cs` | Receipt scan limits |
| `AiImportUsageService` | `ArgoBooks.Core/Services/AiImportUsageService.cs` | AI import limits |
| `UpgradeModalViewModel` | `ArgoBooks/ViewModels/UpgradeModalViewModel.cs` | The upgrade modal: entering and activating a key, prices |

## Clearing a license

Deleting the global settings file removes the license from the device. It also removes every other app setting, so to remove only the license, delete the license fields from the file instead.

| Platform | Settings file |
|----------|---------------|
| **Windows** | `%APPDATA%\ArgoBooks\settings.json` |
| **macOS** | `~/Library/Application Support/ArgoBooks/settings.json` |
| **Linux** | `$XDG_CONFIG_HOME/ArgoBooks/settings.json` (or `~/.config/ArgoBooks/settings.json`) |
