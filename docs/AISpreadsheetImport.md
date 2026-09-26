# AI Spreadsheet Import

Argo Books uses AI to import spreadsheets in any layout. It works out what each sheet holds (customers, products, sales and so on), matches the columns to the fields in Argo Books, and reshapes the data when needed.

![AI Import Overview](diagrams/ai-spreadsheet-import/ai-import-overview.svg)

## Supported files

| Format | Extension | Details |
|--------|-----------|---------|
| **Excel workbook** | `.xlsx` | Each sheet is analyzed separately |
| **CSV** | `.csv` | The separator (comma, tab, semicolon or pipe) is detected automatically |

## How an import works

There are five steps: analyze, review, check, import, and categorize.

### 1. Analyze

![AI Analysis Flow](diagrams/ai-spreadsheet-import/analysis-flow.svg)

`SpreadsheetAnalysisService` reads the file and sends a sample to Gemini, through the argorobots.com server. For large files it sends 13 rows rather than all of them: the first 5, the last 3, and 5 from the middle. The middle rows are picked at random, but with a fixed seed, so the same file always gives the same sample.

Along with the sample, the AI gets a description of every kind of record Argo Books stores and its fields. It replies with:

- What each sheet holds, with a confidence score
- Which source column goes to which field, each with a confidence score
- Whether the import needs Tier 1 or Tier 2 (see step 4)
- The source columns it couldn't place, and the fields nothing was found for

Address fields follow the user's country. For example, US users see "State" and "ZIP Code", while UK users see "County" and "Postcode".

### 2. Review

`ImportMappingDialog` shows the AI's findings before anything is imported. The user can:

- Choose which sheets to import
- Look over the column matches and their confidence
- Choose whether existing records are skipped or overwritten
- See how many imports are left this month

### 3. Check

The data is then checked against the company's existing records:

- **Missing links**, such as a sale for a customer that doesn't exist. Missing suppliers, customers, categories and similar records can be created automatically as placeholders.
- **Bad data** that can't be fixed automatically, which the user has to correct in the spreadsheet.

`ImportValidationDialog` lists the problems by sheet. The user either cancels, or creates the missing records and imports.

### 4. Import

There are two ways to import, depending on how much the data needs changing.

#### Tier 1: renaming columns

![Tier 1 Processing](diagrams/ai-spreadsheet-import/tier-1-processing.svg)

Used when the spreadsheet already has the right shape and only the column names differ, for example "Client Name" instead of "Name", or different words for the same thing. The columns are renamed using the AI's matches, and the normal importer does the rest. No more AI calls are needed.

#### Tier 2: the AI rewrites the rows

![Tier 2 Processing](diagrams/ai-spreadsheet-import/tier-2-processing.svg)

Used when renaming columns isn't enough:

- One sheet mixes several kinds of record
- Several rows make one record, such as an invoice with one line per row
- Pivot tables, cross-tabs or other layouts that aren't one record per row
- Columns need splitting or combining

Every row is read and split into batches of 100. Up to 10 batches go to the AI at once, and it turns each one into records in Argo Books' format. It is told to:

- Make up sensible IDs when the file has none
- Write dates in ISO 8601 format
- Read amounts correctly, removing currency symbols and handling each region's separators
- Skip subtotal rows, repeated headers and empty rows
- Join several rows into one record where that makes sense

Records that appear in more than one batch are merged, and when two share an ID the last one wins. The records are then added one batch at a time, because company data can't safely be changed from several threads at once.

### 5. Categorize

After the import, any product without a category is sent to the AI, which picks a category from its name and description (for example "Industrial Drill Press" goes under "Power Tools").

## When the AI can't make sense of a file

If step 1 finds nothing it can use, the import doesn't just stop. It runs a second, more thorough pass (`SpreadsheetAnalysisService.RescueAsync`) that either pulls out records or explains why the file can't be imported.

Instead of a 13-row sample, this pass reads every sheet in full, and the AI decides for each sheet:

- **Extract:** the sheet has one record per row. The AI picks the best kind of record (any `SpreadsheetSheetType`, bank statements included), and the sheet goes through the normal Tier 2 import. Bank statement rows go to Bank Matching, the same as in a normal import.
- **Reject:** the sheet can't be turned into Argo Books records. The AI returns one of a fixed set of reason codes rather than its own words, so the user always sees wording we wrote.

| Code | Used when |
|------|-----------|
| `SummaryOrReport` | The file is a summary or report of totals (such as a QuickBooks Profit and Loss statement), not individual records |
| `NotArgoData` | Nothing in the file is something Argo Books tracks |
| `UnsupportedStructure` | It looks like records, but the layout can't be matched. Also used for any code the app doesn't recognize |
| `EmptyOrUnreadable` | There are no readable rows |
| `TooLarge` | The file is over the row limit below. Set by the app, not the AI |

The app owns the wording shown for each code (`ImportRescueMessages.ForReason`). A reply from the AI that is broken or unrecognized gets the `UnsupportedStructure` message. A file that can't be read gets the `EmptyOrUnreadable` message instead of a raw error, and the error is logged.

**Size limits.** Rows are sent in batches, so size limits are about time and the number of AI calls, not what the AI can handle:

- Files over **10,000 rows** are rejected as `TooLarge` before any AI call.
- Files over **1,000 rows** show a "this may take a while" message first.
- For wide sheets, batches get smaller than 100 rows (aiming for about 2,500 cells each) so the AI's reply isn't cut off.

**Usage.** This pass only counts as an import when it actually extracts records; a rejected file isn't counted. The usual monthly limit check still runs first.

## Monthly limit

AI imports have a monthly limit, handled by `UsageLimitService`. The count goes up after each successful import. See [LicenseKey](LicenseKey.md#usage-limits) for how the limit is checked, including what happens when the server can't be reached.

## Where it is run from

`PerformAiImportAsync` in `ArgoBooks/App.axaml.cs` runs the whole import: it calls the services, shows the dialogs and progress, takes the undo snapshot and records telemetry.
