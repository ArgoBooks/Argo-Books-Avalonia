# Security

How Argo Books protects a company file. [Data Storage](DataStorage.md) describes the file itself.

![Security Architecture](diagrams/security/security-architecture.svg)

## When a file is encrypted

**Setting a password is what turns encryption on.** The encryption key is made from the password, so a file without a password is not encrypted at all. It is only compressed, and anyone who has the file can read it.

Once a password is set, every save encrypts the whole file.

## Encryption

The whole compressed file is encrypted with AES-256-GCM, so there is no choosing which fields to protect and nothing to miss.

- A new random 96-bit nonce on every save
- A 128-bit authentication tag, so a file that was tampered with fails to open instead of showing altered data

![Encryption Flow](diagrams/security/encryption-flow.svg)

![Decryption Flow](diagrams/security/decryption-flow.svg)

## Turning the password into a key

PBKDF2-SHA256 turns the password into a 64-byte key, using 600,000 iterations (the OWASP recommendation for SHA-256) and a new random 256-bit salt on every save. The key is split in half, and the halves are never used for the same thing:

| Bytes | Used for |
|---|---|
| 0 to 31 | Unlocking the file's data key (see below) |
| 32 to 63 | A check value stored in the footer, to tell a wrong password apart from a damaged file |

Getting both halves from one run means PBKDF2, which is slow on purpose, only runs once per save.

![Key Derivation](diagrams/security/key-derivation.svg)

## Envelope encryption

Since 2.0.11 (format version 2 and later), the password doesn't encrypt the file directly:

1. A random 256-bit data key encrypts the file.
2. The data key is stored in the footer twice: once locked with the password's key, and once locked with the Argo Books recovery public key (RSA-4096, OAEP-SHA256).

Either copy unlocks the same data key, so the file is only encrypted once however many ways there are to unlock it. Adding another way later only adds a footer field, and changing the password only re-locks the data key instead of re-encrypting the whole file.

This is what lets support recover a file without ever being able to recover the password. See [Password recovery](../tools/ArgoBooks.Recovery/README.md).

Format version 1 files have no data key; the password's key decrypts them directly. They still open, and get the recovery copy the next time they are saved.

## What is not encrypted

The footer at the end of the file is stored as plain JSON. It has to be, because it holds what is needed to start decrypting. It also lets the app list recent companies without asking for a password.

Anyone with the file can read these from the footer:

- The company name and the names of any accountants
- When the file was created and last changed, and the app and format versions
- The company logo thumbnail
- Whether the file is encrypted, and whether biometric unlock is on

The footer also holds the encryption settings, but none of them help an attacker:

- The salt, nonce and password check value are not secrets. Salts and nonces only need to be unique, and the check value is the output of 600,000 rounds of PBKDF2.
- The data keys are stored locked, so they are unreadable on their own.

No financial data can be read from the footer.

## Opening a file

![Authentication Flow](diagrams/security/authentication-flow.svg)

## Biometric unlock

Biometrics don't replace the password; they unlock a saved copy of it. When the user turns biometric unlock on, the password is handed to the operating system's protected storage (DPAPI, under the current user account, on Windows), which ties it to that computer and that user. Argo Books never sees the fingerprint or face. The operating system confirms who the user is and hands back the password.

This means:

- It only works on that computer, under that user account. On any other computer the password is needed.
- It is only as strong as the operating system login.
- The saved copy follows the password. Changing the password updates it. Removing the password, or adding one to a file that had none, turns biometric unlock off so the user chooses again.

Security settings: auto-lock after a set idle time, biometric unlock on or off, and adding, changing or removing the password.

## Rules the code follows

| Rule | How |
|----------|----------------|
| **The password is never stored** | It is turned into a key and thrown away |
| **The data key is never stored readable** | It only exists locked; nothing on disk holds it in the clear |
| **Keys are wiped from memory** | Password keys, check values and data keys are zeroed in `finally` blocks straight after use |
| **Tampering is caught** | The GCM tag makes a changed file fail to open instead of showing altered data |
| **Never save unencrypted by mistake** | Saving a file that has a password is refused if the encryption service is missing, rather than writing plain data under a footer that says it is encrypted |
| **Idle sessions lock** | After the auto-lock time |
| **Data stays local** | Nothing is sent to the cloud without consent. The recovery private key is kept offline and never ships with the app |
