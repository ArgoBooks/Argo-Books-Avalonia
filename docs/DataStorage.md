# Data Storage

Each company is saved as a single `.argo` file instead of in a database. The whole company is loaded into memory when it opens, so everything runs fast. The data stays on the user's computer, and the file can be copied, emailed or backed up like any other file. No database or server has to be installed.

A file is always compressed. It is encrypted only once the user sets a password (see [Security](SecurityArchitecture.md)).

## CompanyManager

`CompanyManager` handles everything to do with company files:

- Opening, saving and closing files
- The temporary folder a company is unpacked into while it is open
- Encryption, through the encryption service
- Auto-save
- Locking the file so two copies of the app can't edit it at once

Opening a file:

![Company Manager Load File](diagrams/data-storage/company-manager-load-file.svg)

Saving a file:

![Company Manager Save File](diagrams/data-storage/company-manager-save-file.svg)

## The `.argo` file

While a company is open, it is a folder of JSON files and attachments. Saving packs that folder into a TAR archive, compresses it with GZip, encrypts it if there is a password, and adds a footer at the end:

```
[ content: gzip(tar(company folder)), encrypted if a password is set ]
[ footer JSON (UTF-8, not encrypted)                                 ]
[ footer length (4 bytes, little-endian)                             ]
[ magic bytes "ARGO"                                                 ]
```

Opening reads the file from the end: first the magic bytes, then the length, then the footer, and only then the content. The footer can't be encrypted, because it holds what is needed to start decrypting. [Security](SecurityArchitecture.md#what-is-not-encrypted) lists what it contains.

![Argo File Format](diagrams/data-storage/argo-file-format.svg)

### Format versions

The footer records a format version:

| Version | What changed |
|---|---|
| **1** | The content is encrypted directly with the key made from the password |
| **2** | Added in 2.0.11. A random data key encrypts the content, and that key is stored locked by the password and, separately, by the recovery key ([details](SecurityArchitecture.md#envelope-encryption)) |
| **3** | Added with decimal stock quantities and cost of goods sold. Same layout as version 2 |

Version 1 files still open, and are upgraded to the current version the next time they are saved.

Older versions of the app can't open newer files. `FileService` checks the format version **before** trying to decrypt, so an old app says it needs updating instead of wrongly saying the password is incorrect. That is the only reason version 3 exists: its layout is the same as version 2, but an older app would misread a stock quantity like 2.5 as a whole number.

## Global settings

Settings that belong to the app rather than to one company, such as the recent files list, user preferences and the license, are kept in a separate `settings.json` file. [LicenseKey](LicenseKey.md#clearing-a-license) lists where it is on each platform.
