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

Receipt scans, AI imports (spreadsheet and bank statement) and, on the free plan, invoice sends each have a monthly limit that depends on the plan. The server keeps the count for each license key, or for the device when there is no key. One service, `UsageLimitService`, handles all of them the same way; each limit only differs in the server address it calls and the names the server gives its fields.

Before each scan, import or send, the app decides whether it may go ahead:

- **A recent answer is reused.** An answer from the server less than 5 minutes old is used again, to save calls.
- **Limit reached:** the server says no more are left this month, so it is blocked and the user sees the date the count resets (the first of next month).
- **The server refuses the check:** the server's own usage endpoint answers with a 4xx status and a JSON body that says why (an `error`, `message` or `errorCode` field). It is blocked, and the user sees a short message. A refusal is an answer about this request, not a sign the server is down, and letting it through would give unlimited uncounted uses. This matters most for invoice sends on the free plan, which only the app enforces. The refusals the server gives are:
  - *Too many requests* (429, from the server's per-address rate limit): the user is asked to try again in a few minutes.
  - *License not recognised* (401): the key isn't known, or, for AI imports only, it is a Premium key whose subscription has ended (the other two limits fall back to the device's free count for an ended key). The message asks the user to restart, because the check at startup removes an ended license and the app then uses the free plan's count.
  - *Any other refusal*, such as a bad request (400): the user is told the server refused the check. The server's reason goes to the error log, because it is written for developers and can't be translated.
- **The count server is unavailable:** anything that isn't a readable answer from the endpoint. That is a server error (a 5xx status), a body that isn't the endpoint's JSON whatever the status (for example the hosting layer's HTML page for a firewall 403, a 404 during a deploy, or a 408), a failure with a 2xx status that carries no counts, a 4xx whose JSON gives no reason, or no answer at all while the internet works. It goes ahead, uncounted. The scan, import or send makes its own call to the server, which fails with its own message if the server is really down.
- **No internet:** it is blocked with a message saying the internet is down, because the scan, import or send can't work without it.
- **No license key and no device ID:** it is blocked, because the server has nothing to count against.

After a successful one, the app tells the server to add one to the count. If the server can't be reached at that moment, the result is still kept; it just isn't counted. Cancelling this call is not treated as a network problem.

The server's answer includes:

| Field | Meaning |
|-------|---------|
| `can_scan`, `can_import`, `can_send` | Whether one more may go ahead (the name depends on the limit) |
| `scan_count`, `import_count`, `send_count` | Used this month |
| `monthly_limit` | The limit for the plan, or -1 when there is no limit |
| `remaining` | Left this month |
| `tier` | The plan's name |
| `resets_at` | When the count resets |

## Connection problems

When a license or usage call fails, the app checks whether the internet works, then whether `argorobots.com` can be reached, and shows the matching message: no internet connection, Argo Books servers unreachable, or a general failure.

License calls time out after 30 seconds; usage calls after 15 seconds.

## Where the code is

| Service | File | What it does |
|---------|------|--------------|
| `LicenseService` | `ArgoBooks.Core/Services/LicenseService.cs` | Saving, encrypting and loading the license, checking it online, the device ID |
| `UsageLimitService` | `ArgoBooks.Core/Services/UsageLimitService.cs` | Receipt scan, AI import and invoice send limits |
| `UpgradeModalViewModel` | `ArgoBooks/ViewModels/UpgradeModalViewModel.cs` | The upgrade modal: entering and activating a key, prices |

## Clearing a license

Deleting the global settings file removes the license from the device. It also removes every other app setting, so to remove only the license, delete the license fields from the file instead.

| Platform | Settings file |
|----------|---------------|
| **Windows** | `%APPDATA%\ArgoBooks\settings.json` |
| **macOS** | `~/Library/Application Support/ArgoBooks/settings.json` |
| **Linux** | `$XDG_CONFIG_HOME/ArgoBooks/settings.json` (or `~/.config/ArgoBooks/settings.json`) |
