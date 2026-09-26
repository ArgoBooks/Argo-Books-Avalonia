# Localization

Argo Books shows its text in the user's language. Translations are downloaded from the server and saved on the computer. The list of supported languages is on the website [here](https://www.argorobots.com/documentation/pages/reference/supported_languages.php).

![Localization Overview](diagrams/localization/localization-overview.svg)

## Translating text

### In XAML

Wrap the English text in the `{loc:Loc}` markup extension:

```xml
<TextBlock Text="{loc:Loc 'Save Changes'}" />
<Button Content="{loc:Loc 'Cancel'}" />
```

Write an apostrophe twice:

```xml
<TextBlock Text="{loc:Loc 'Don''t save'}" />
```

Prefer `{loc:Loc}` over translating in code, because it updates by itself when the user changes language.

For bound data, such as the items in a ComboBox, use `TranslateConverter`:

```xml
<ComboBox ItemsSource="{Binding Options}">
    <ComboBox.ItemTemplate>
        <DataTemplate>
            <TextBlock Text="{Binding Converter={StaticResource TranslateConverter}}" />
        </DataTemplate>
    </ComboBox.ItemTemplate>
</ComboBox>
```

### In code

```csharp
using ArgoBooks.Localization;

var message = Loc.Tr("Operation completed successfully");
var formatted = Loc.Tr("Saved {0} items", count);

if (Loc.IsEnglish) { /* ... */ }
var isoCode = Loc.CurrentIsoCode;  // e.g., "fr"
var name = Loc.CurrentLanguage;     // e.g., "French"
```

Put changing values in placeholders (`{0}`, `{1}`) and translate the whole sentence. Don't join two translated pieces together, because word order differs between languages.

## How a string is looked up

![Translation Flow](diagrams/localization/translation-flow.svg)

1. The English text is turned into a key.
2. The key is looked up in the saved translations for the current language.
3. If there is a translation it is shown; otherwise the English text is shown.

The key is made from the English text (`LanguageService.GetStringKey`): it is lowercased, `&` becomes `amp`, everything except letters, digits, underscores and the braces of `{0}`-style placeholders is removed, it is cut to 50 characters, and `str_` is put in front. So `"Save Changes"` becomes `str_savechanges` and `"Saved {0} items"` becomes `str_saved{0}items`.

**English is looked up too.** It comes from the saved `en.json` file, not straight from the source code. So after changing any English text, rebuild `en.json` (see [Rebuilding English](#rebuilding-english)), or the app keeps showing the old wording.

## Key collisions

Because capitals, punctuation and anything past 50 characters are dropped, different strings can end up with the same key:

- `"Save Changes"` and `"Save changes"` both become `str_savechanges`.
- `"Supplier"` and `"Supplier..."` both become `str_supplier`.
- Two long strings that start with the same 50 characters share a key.

Only one of them can exist. The translation tool keeps whichever it finds first and silently drops the others, so the wrong text can appear wherever the key is used. A table header reading `Supplier...` is the usual sign.

The tool reads **all AXAML files before any C# file**, so text in `{loc:Loc}` always wins over a C# string with the same key. That keeps screen text safe from strings that only exist as spreadsheet column names or import aliases.

Each run prints a collision report, but it doesn't help with this kind of problem. It only lists strings that still differ after lowercasing and removing punctuation, which catches two different long strings cut to the same 50 characters. Strings that differ only in capitals or punctuation are counted as harmless and shown as a single number. That is fine for a menu item and wrong for a table header, so look into that number when it changes, or check the screen.

The rules below avoid most collisions.

### Capitalization

Use sentence case: capitalize the first word only.

> Clear all, Sync now, Street address, Units sold

Names and acronyms keep their capitals (`Argo Books`, `Stripe`, `PDF`, `GST/HST`). Since the key is lowercased, fixing capitalization never changes the key, so the existing translations stay attached and nothing needs translating again.

When a string must appear in all capitals, translate it once and change the case afterwards, instead of adding a second string:

```csharp
var upper = Loc.Tr("Save Changes").ToUpperInvariant();
```

In XAML, use `UpperCaseConverter`.

### Punctuation

Keep punctuation out of translated text. Translate the word, then add the punctuation in a separate `Run`:

```xml
<!-- Instead of Text="{loc:Loc 'Quantity:'}", which collides with 'Quantity' -->
<TextBlock><Run Text="{loc:Loc 'Quantity'}" /><Run Text=":" /></TextBlock>
```

Only the `Run` with `{loc:Loc}` is translated. The other is plain text, which is right, because `:`, `...`, `#` and `($)` are the same in every language. It looks the same as a single `Text` attribute.

This only works for the text inside a `TextBlock`. It doesn't work for text in an attribute, such as `ToolTip.Tip`, `Placeholder`, `PlaceholderText` or a custom control's `Header`. There, either leave the punctuation out or use a different string that doesn't collide (`Select category...` rather than `Category...`).

## Changing language

![Language Change Flow](diagrams/localization/language-change-flow.svg)

1. The user picks a language in Settings, which calls `LanguageService.SetLanguageAsync()`.
2. `LanguageService` downloads the translations if they aren't saved yet, then raises `LanguageChanged`.
3. `LocalizationManager` receives it and refreshes every translated binding on screen.

## Downloading and saving translations

![Download Flow](diagrams/localization/download-flow.svg)

Each app version downloads its translations from:

```
https://argorobots.com/resources/downloads/{version}/languages/{isoCode}.json
```

They are saved here:

| Platform | Folder |
|----------|--------|
| **Windows** | `%LOCALAPPDATA%\ArgoBooks\Languages\` |
| **macOS** | `~/Library/Caches/ArgoBooks/Languages/` |
| **Linux** | `~/.cache/ArgoBooks/Languages/` |

The folder holds one `{isoCode}.json` file per downloaded language, with `en.json` for English.

Each file maps keys to text:

```json
{
  "str_savechanges": "Enregistrer les modifications",
  "str_cancel": "Annuler",
  "str_saved{0}items": "Enregistré {0} éléments"
}
```

## Making the translation files

The files are made by the tool in `tools/ArgoBooks.Translations`, which uses the **Azure Translator API**.

Set the Azure details, then go to the tool's folder:

```powershell
$env:AZURE_TRANSLATOR_REGION = "canadacentral"
$env:AZURE_TRANSLATOR_KEY = "your-api-key"

cd tools/ArgoBooks.Translations
```

| Command | What it does |
|---------|-------------|
| `dotnet run -- --languages en` | Rebuilds `en.json` and prints the collision report. No Azure calls, no key needed |
| `dotnet run -- --translate` | Translates into every language |
| `dotnet run -- --languages fr,de,es,ja` | Translates into the listed languages |
| `dotnet run -- --output C:\MyTranslations` | Writes the files to another folder |

Files are written to `./languages/` unless `--output` says otherwise.

### What a run does

1. Collects every translatable string, from AXAML first and then C#.
2. Rebuilds `en.json` from those strings. This happens on every run, whatever the options.
3. For each language asked for, keeps every key that already has a translation and sends only the missing ones to Azure, in batches.
4. Writes one `{isoCode}.json` file per language.

Keeping existing translations saves money and keeps any translations that were corrected by hand. For this to work, copy the current translation files into the output folder before running.

### Rebuilding English

Run `dotnet run -- --languages en` after changing any English text. It is free and takes seconds. English is skipped when translating, so the run rebuilds `en.json`, says "Nothing to do", and stops without reading the Azure key or touching the other language files.

Deleting a key from `en.json` does nothing, because the file is rebuilt every run.

### Translating a string again

Delete its key from the other language's file (`fr.json` and so on) and run `--translate` again.
