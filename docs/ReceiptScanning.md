# Receipt Scanning

Argo Books reads receipts with Google Gemini's vision model. The user takes a photo or drops in a PDF, and the AI returns the supplier, date, totals, currency and line items. A second AI call then matches the supplier and category to the company's existing ones. The user checks the result and saves it as an expense or revenue.

Every AI call goes through the Argo Books server, so the app never holds a Google API key. The server checks the license key and the monthly scan limit (see [LicenseKey](LicenseKey.md#usage-limits)).

## Supported files

JPEG, PNG, HEIC, WebP and PDF.

The list is kept in one place, `FilePickerTypes.ReceiptFormats`. The file picker, drag and drop, the saved file type and the wording on screen all come from it, so adding a format there is all it takes.

HEIC is what an iPhone takes photos in. Skia can't read HEIC on Windows or Linux, so these files are converted to JPEG separately first. If that conversion fails, the file is sent to the AI as it is, since the AI can read HEIC; only the preview on screen is missing.

Before scanning, images are rotated the right way up, given a little more contrast and sharpened, which helps with faded thermal receipts.

For a PDF with several pages, the preview shows page 1, but the AI reads every page.

## Scanning a receipt

1. Click **AI Scan** on the Receipts page and pick the file.
2. The image is prepared and sent to the AI.
3. A window opens with the supplier, date, totals, currency, payment method and line items filled in.
4. In the background, a second AI call suggests a matching supplier and category.
5. The user fixes anything the AI got wrong and saves.

Saving creates the expense or revenue with the receipt attached, and adds the receipt to the Receipts page.

## Scanning several at once

Select several receipts to scan them together. Three are scanned at a time while the rest wait. When all are done, the user goes through the results one by one, saving or skipping each.

## Supplier and category matching

After the receipt is read, a second AI call compares the supplier and line items with the company's existing suppliers and categories.

**Supplier:** it recognizes exact names, common variations ("Walmart" and "Walmart Inc.") and known abbreviations. If nothing matches well, it suggests creating a new supplier with a cleaned-up name.

**Category:** it is chosen from the line items and the kind of supplier. Vague categories like "General", "Expenses" or "Miscellaneous" are never picked; the AI suggests a specific existing category or a new one instead.

The user can change either suggestion before saving.

## Currency

The AI works out the currency from the address, language, currency symbol and tax names (GST means CAD, VAT means EUR or GBP, and so on) and returns a three-letter ISO 4217 code. If it can't tell, it uses USD. The user can change it before saving.

## Scan limit

Each scan counts toward the monthly limit, which is checked before every scan (see [LicenseKey](LicenseKey.md#usage-limits)). The dashboard's Quick Scan button also checks it before opening the file picker.

## Limitations

- **Scanning needs the internet.** There is no offline reading of receipts.
- **Failed scans are not retried automatically.** The error shows in the window and the user tries again.
- **Receipt images are stored inside the `.argo` file**, so a lot of receipts makes the file larger.
