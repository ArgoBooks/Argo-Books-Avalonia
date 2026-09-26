# Integrations

The outside services Argo Books uses, and how it reaches them.

![Integrations Overview](diagrams/integrations/integrations-overview.svg)

## The Argo Books server

`argorobots.com` runs a PHP server that sits between the app and almost every outside service. It does two jobs:

1. **Passing calls on to other services.** The app never talks to Gemini, Google or the exchange rate provider itself; the server makes those calls. That keeps the API keys, cost tracking and each license's monthly limits in one place.
2. **Running Argo Books' own features** that have no outside service behind them: licenses, usage counts, the payment portal's customer pages, emails, and more.

Endpoints follow the pattern `/api/<area>/<action>.php`. The exact URLs are spread across the Core services; the tables below are the full list of what the app uses the server for.

Most endpoints check the user's license key, sent in an `Authorization: Bearer` header, plus an `X-Device-Id` header. The payment portal's customer pages are the exception: customers don't have a license, so they use short-lived codes sent by email.

### Services the server calls for the app

| Service | Used for |
|---|---|
| Google Gemini | Reading receipts, matching suppliers and categories, and AI spreadsheet import. See [ReceiptScanning](ReceiptScanning.md) and [AISpreadsheetImport](AISpreadsheetImport.md). |
| Google sign-in and Sheets | Signing in with Google, and exporting charts to Google Sheets. |
| Exchange rate provider | Current and past exchange rates against USD, cached on the server. |

![Gemini Integration](diagrams/integrations/gemini.svg)

![Google Sheets Integration](diagrams/integrations/google-sheets.svg)

![Exchange Rates Integration](diagrams/integrations/exchange-rates.svg)

### Argo Books' own server features

| Area | What it does |
|---|---|
| License and subscription | Checks license keys, activates purchased keys, and gets current prices. See [LicenseKey](LicenseKey.md). |
| Usage limits | Monthly counts for each license: receipt scans, AI imports and published invoices. |
| Invoice email | Sends invoice emails to customers. |
| Payment portal | Customer refund requests, email checks and email change confirmations. See [PaymentPortal](PaymentPortal.md). |

## Stripe payment import

Stripe is the one service the app talks to directly. `StripeApiClient` calls `api.stripe.com` using a restricted API key the business creates in its own Stripe dashboard and pastes into Settings. Nothing goes through `argorobots.com`, and Argo Books never holds the business's Stripe login.

The key is saved in the company file's settings (`integrations.stripe.apiKey`), so it has the same protection as the rest of the file: it is encrypted only if the file has a password. See [Security](SecurityArchitecture.md).

This brings in payments taken through Stripe so they don't have to be typed in. It was added in 2.0.11, and the key needs read access to Balance transactions, Charges and Payouts.

| Part | What it does |
|---|---|
| `StripeApiClient` | Talks to the Stripe API, handles paging, and checks the key |
| `StripeSyncService` | Gets the balance transactions since the last sync and builds a preview |
| `StripeDetailImporter` | Turns each charge into a sale, creating the product and customer if needed |
| `StripeImportCreation` | Adds the whole import as a single step that can be undone |
| `IntegrationImportFlow` | The preview, the question and the import, the same whether the sync starts from the Revenue page's banner or from Settings. The Argo Books API import uses it too |

Good to know:

- **Sales come in with all their detail.** Tax, discounts and Stripe's processing fee are all brought in, not just the amount paid out.
- **Refunds become returns** against the original sale. When there's no sale to attach one to, it is recorded as an expense instead, and that expense is never added twice.
- **The question before importing shows totals in the company's currency.** Each sale and fee is converted at its own date, the same rate the import then uses, so a sync with sales in several currencies shows the right total rather than the numbers added together. The rates are fetched before the question is asked, and a total with a rate that still can't be had shows Pending.
- **Syncing again doesn't create duplicates.** The last imported charge is remembered, so the next sync only gets what came after it.
- **Payouts are remembered** in `importedPayouts`. When a bank statement is imported later, `BankMatchingService` automatically ignores a deposit within 1 cent or 1% of a payout and inside the matching date window. That stops the Stripe payout being counted on top of the sales already imported from Stripe.

Payments made through the payment portal are separate; see [PaymentPortal](PaymentPortal.md).
