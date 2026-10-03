# Password recovery

How support can open a customer's company file when they have lost the password.

> Internal document. Do not publish this to customers. It describes how the recovery path
> works and what proof of ownership is required, which is exactly what someone would need to
> know in order to talk their way into a file that isn't theirs.

## How it works

From Argo Books v2.0.11, an encrypted company file is no longer encrypted directly with a key derived from the password. Instead:

1. A random data key encrypts the archive.
2. That data key is stored in the footer twice: once wrapped under the user's password, and once wrapped under the Argo Books recovery public key.

Either wrap yields the same data key, so the file can be opened with the password or with the recovery private key. The password itself is never stored and stays unrecoverable.

The recovery key is configured, under key id `argo-recovery-1`, so builds made from this repository attach a recovery path to every newly encrypted file.

Internally this is file format version 2, which is the number the `inspect` command reports. Files written by v2.0.10 and earlier are format version 1 and have no recovery path.

Relevant code:

| File | Role |
|---|---|
| `ArgoBooks.Shared/Security/KeyEnvelope.cs` | Generates and wraps data keys |
| `ArgoBooks.Shared/Security/RecoveryKeyProvider.cs` | Holds the embedded public key |
| `ArgoBooks.Core/Services/FileService.cs` | Builds the envelope on save, opens it on load |
| `tools/ArgoBooks.Recovery/` | Offline support tool |

## Before you start

Every command below is run from the repository root (`C:\Users\evand\Desktop\Argo-Books-Avalonia`) in PowerShell, and can be copied and pasted exactly as written. The paths are relative, so they will not work from anywhere else.

The recovery tool is not committed as an executable, so build it first. Do this once, and again after any code change:

```powershell
dotnet build tools/ArgoBooks.Recovery
```

## Generating or rotating the key

Only needed once, and again if the key is ever rotated. Not part of a normal release.

1. Generate a key pair. The path at the end is where the private key is written:

   ```powershell
   .\tools\ArgoBooks.Recovery\bin\Debug\net10.0\argo-recovery.exe keygen "$HOME\Desktop\argo-recovery-private.pem"
   ```

2. Paste the printed public key into `RecoveryKeyProvider.PublicKeyPem`. The tool prints it already formatted as a C# raw string literal, ready to paste as-is.

3. Bump `RecoveryKeyProvider.CurrentKeyId` if this replaces an earlier key, and keep the old private key forever. Files already in the wild are still wrapped under it.

4. Move `argo-recovery-private.pem` to offline storage. Do not commit it, do not put it on the server, and do not leave a copy on your working machine.

5. Ship a build. Only files saved by that build onward will have a recovery path.

## Check it works after generating a key

Do this once after generating a key, not on every release. Wrapping is exercised on every save, but unwrapping only ever runs in the recovery tool, so a mistake in key custody stays invisible until a customer is already in trouble.

1. Create a test company in the app and set a password on it.

2. Confirm it has a recovery path. Replace the path with wherever you saved the test company:

   ```powershell
   .\tools\ArgoBooks.Recovery\bin\Debug\net10.0\argo-recovery.exe inspect "C:\path\to\Test.argo"
   ```

   It should print `Recovery: available`.

3. Decrypt it with the private key:

   ```powershell
   .\tools\ArgoBooks.Recovery\bin\Debug\net10.0\argo-recovery.exe unlock "C:\path\to\Test.argo" "$HOME\Desktop\argo-recovery-private.pem"
   ```

4. Open the recovered file in the app. It should open with no password and the data should be intact. If it does, the feature works end to end.

## Handling a recovery request

1. **Verify who is asking.** This is the entire security boundary. Anyone who steals a laptop also has the file, so possession of the file is not proof of ownership. Check the request against the licence and purchase records, and against the email on the account.

2. Have the customer send the `.argo` file.

3. Check what you are dealing with before doing anything:

   ```powershell
   .\tools\ArgoBooks.Recovery\bin\Debug\net10.0\argo-recovery.exe inspect "C:\path\to\Their Company.argo"
   ```

   This reads metadata only and decrypts nothing. If it reports `Recovery: NOT AVAILABLE`, the file was encrypted before recovery shipped and the password is the only way in. Say so plainly rather than leaving them hoping.

4. Decrypt it:

   ```powershell
   .\tools\ArgoBooks.Recovery\bin\Debug\net10.0\argo-recovery.exe unlock "C:\path\to\Their Company.argo" "$HOME\Desktop\argo-recovery-private.pem"
   ```

   This writes `Their Company (recovered).argo` next to the original and never modifies the original. The recovered file has **no password**.

5. Send the recovered file back and tell them to set a new password immediately.

6. Delete your copies of both files once they confirm it opens.

## Key custody

- If the private key is lost, every recovery path for every file wrapped under it dies. There is no way to re-derive it.
- If the private key leaks, anyone holding both it and a customer's file can read that file. The key alone is useless without the file, which is why it must never sit anywhere that also receives customer files.
- To rotate: run `keygen` again, paste the new public key, and bump `RecoveryKeyProvider.CurrentKeyId`. Keep the old private key forever, since files already in the wild are still wrapped under it. `inspect` reports which key id a given file needs.
