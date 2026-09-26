# Payment Portal

The payment portal lets customers view and pay invoices online through Stripe or Square.

**PayPal is not supported in the portal.** PayPal's "Log in with PayPal" sign-in won't connect Business accounts, and connecting them properly needs PayPal's Partner Referrals API, which is only open to enrolled platform partners. The PayPal option is hidden in Settings until then. This doesn't affect Argo Books Premium billing, which uses a separate PayPal setup that works.

![Payment Portal Overview](diagrams/payment-portal/portal-overview.svg)

## Registering the company

A company registers with the portal before it can send invoices online. Registration:

- Checks the registration key
- Issues the company an API key, which is saved in the company file (encrypted if the file has a password)
- Uploads the company logo (PNG, JPG, GIF, WebP, BMP or SVG)

![Company Registration Flow](diagrams/payment-portal/company-registration-flow.svg)

## Connecting a payment provider

Stripe (credit and debit cards) and Square are connected by signing in to them from Settings. Either can be connected or disconnected at any time.

![Provider Connection Flow](diagrams/payment-portal/provider-connect-flow.svg)

## Publishing an invoice

Publishing puts an invoice on the portal so the customer can see it and pay. At least one payment provider has to be connected first. Publishing:

- Shows the invoice using the same template as the desktop app
- Emails the customer
- Creates a payment link for the invoice

![Invoice Publish Flow](diagrams/payment-portal/invoice-publish-flow.svg)

## Bringing payments back into the app

Online payments are copied into the company file automatically, every 5 minutes unless changed in Settings.

- Each payment carries its portal payment ID, so it is never added twice.
- Payments in other currencies are converted to USD like any other.
- Online payments are marked as online, so they can be told apart from payments entered by hand.

![Payment Sync Flow](diagrams/payment-portal/payment-sync-flow.svg)

## Settings

| Setting | What it does |
|---------|-------------|
| **Auto-sync interval** | How often online payments are copied in (default: 5 minutes) |
| **Payment notifications** | Whether to show a notification when an online payment arrives |
| **Portal URL** | The address customers use |
| **Connected accounts** | Connect or disconnect Stripe and Square |
| **Company logo** | The logo shown on the portal |
