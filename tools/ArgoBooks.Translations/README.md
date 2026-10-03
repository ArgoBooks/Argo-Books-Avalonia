# Translation Tool

Finds every translatable string in the app and builds the language files, sending anything new to Azure Translator.

## Setup
```powershell
$env:AZURE_TRANSLATOR_REGION = "canadacentral"
$env:AZURE_TRANSLATOR_KEY = "your-api-key"

cd tools/ArgoBooks.Translations
```

## Commands

**Rebuild `en.json` only (no API call, no key needed):**
```
dotnet run -- --languages en
```
English is skipped by the translation loop, so this rewrites `en.json` from the source strings and stops. Run it after changing any English text, or the change will not appear in the app.

**Translate to all languages:**
```
dotnet run -- --translate
```

**Translate to specific languages:**
```
dotnet run -- --languages fr,de,es,ja
```

## Output
Files are saved to `./languages/` (e.g., `en.json`, `fr.json`, `de.json`).
