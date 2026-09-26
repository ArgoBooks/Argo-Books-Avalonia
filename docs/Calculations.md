# Calculation Standards

This document is the single source of truth for how money is counted across the app: revenue, expenses, profit, tax, refunds, invoices, payments. Every dashboard stat card, chart, report, and analytics figure should follow these rules so that numbers agree wherever the user looks.

If you find a calculation that disagrees with this document, the calculation is wrong. Fix the code, not the doc. If a new requirement genuinely needs a different rule, update this doc first, then update the code.

Symbols used below: § means "section" (§4 is section 4), and Σ means "the sum of".

---

## 1. Vocabulary

| Term | Meaning |
|---|---|
| **Invoice** | A bill the business issued to a customer. Has a status (Draft, Sent, Paid, etc.) and a balance owed. |
| **Revenue** | Money the business earned, usually from a customer. It can be linked to the invoice it came from. Its `PaymentStatus` says whether the money has been collected. |
| **Expense** | Money the business spent, usually with a supplier. |
| **Payment** | A single payment received on an invoice. A refund (below) is a payment going back out. |
| **Refund** | A `Payment` row with `IsRefund=true` and a negative amount. It is tied to the invoice (and usually the payment) it gives money back on, and dated on the day it was issued. |
| **Subtotal** | The line items added up, each after its own discount, before any invoice-wide discount, fee, shipping or tax (§4). This is not the same as the pre-tax amount, which is `EffectiveSubtotalUSD` (§3). |
| **Tax amount** | The sales tax charged or paid. |
| **Total** | What the customer was charged or the business paid: Subtotal − discount + fees + shipping + tax, plus the security deposit on an invoice (§4). |

---

## 2. Foundational rules

### Rule 1: Revenue includes tax; profit doesn't.

Sales tax collected from customers is owed to the government, so it is shown as part of revenue but never counted as profit. Tax the business pays on its own purchases is a real cost, so expenses include it.

- **Total Revenue** card and charts, and customer billings, use `EffectiveTotalUSD` (tax included).
- **Net Profit**, **Profit Margin** and **Profit Over Time** use `EffectiveSubtotalUSD` (tax excluded) for revenue.
- **Expenses** use `EffectiveTotalUSD` (tax included).
- **Tracked stock** is the one exception, on the profit side only (§14).

```
Total Revenue  = Σ Revenue.EffectiveTotalUSD − Refunds(full)
Net Profit     = Σ Revenue.EffectiveSubtotalUSD − Σ OperatingExpenseUSD(expense) − Σ CostOfGoodsSoldUSD(revenue) − Refunds(pre-tax)
Profit Margin  = Net Profit / Σ Revenue.EffectiveSubtotalUSD
Tax Owed       = Σ Revenue.EffectiveTaxAmountUSD − Refunds(tax part) − Σ Expense.EffectiveTaxAmountUSD
```

The refund terms are defined in §8.

### Rule 2: The dashboard counts only money actually received.

Every revenue total on the dashboard, in Analytics and in `ReportChartDataService` first keeps only collected revenue (`RevenueAggregator.IsCollected`, §7). Tax figures follow the same rule.

Unpaid revenue counts only in:
- Outstanding and Overdue Invoices, which exist to show what hasn't been paid.
- The Revenue page list, which lists every revenue.
- Counts of rows or customers rather than money: Customer Growth, Active vs Inactive Customers, Customer Payment Status, Transactions Processed, Transactions by Accountant, Tax Rate Distribution.
- Formal reports (§10).

### Rule 3: Add up in USD, show in the company's currency.

Every row stores its amounts in USD as well as in its own currency (the `Effective*USD` properties), so totals across different currencies add up correctly. Every number the user sees or exports is then converted to the display currency. That is normally the company's currency, or USD for a report or Insights run that is missing an exchange rate it needs (Rule 3a).

### Rule 3a: Convert using the exchange rate for the transaction's own date.

Exchange rates come from the Argo server (`/api/exchange-rates*.php`), which returns every currency's rate against USD for a given day. A transaction is converted using the rate for the day it happened. There are two exceptions:

- **Online payments, their refunds, and a kept deposit** are stored in USD using their invoice's rate, so a fully paid invoice nets to zero in USD. If the invoice is still waiting for its rate, they wait too. Editing a kept deposit converts it at its invoice's rate again, as creating it did, unless the edit moves it to another currency. When shown, they convert from USD to the display currency at their own date, like any other row.
- **Forecasts and Past Predictions** describe future periods and have no transaction date, so they use today's rate (§10).

What this means in practice:

- **A row already in the display currency** shows its original amount and needs no rate, both on its own (`FormatWithOriginal`) and in `TrySumDisplayFromUSD` totals. One still waiting for its USD value counts as 0, as it does everywhere else (§3). `FormatTotalOrPending` totals and reports convert every row from USD.
- **A row whose rate isn't available** (offline, future-dated, or missed by import's check) is never converted at a nearby day's rate. It is saved as waiting (`IsPendingConversion`) with its USD amounts at 0, and converted automatically once its rate can be fetched (`PendingConversionService`). Import fetches the rates it needs first (`RateReadinessService.EnsureRatesAsync`) and, if it can't, stops with a prompt to retry or cancel. Opening a company fetches any rates its transactions are missing the same way (`CurrencyService.WarmCompanyRatesAsync`).
- **One way to store or wait.** Every save, import, sync and generated row stores its USD amounts through `UsdConversion.Apply`, including the draft invoice a quote converts into and each invoice a recurring schedule makes, which converts at its own issue date rather than keeping its template's USD amounts. It looks the rate up once and converts every amount field with it, or, with no rate, zeroes them, marks the row waiting and queues it. The queue converts with the same field writes, so a row converted straight away and one converted later are identical. The queue holds one entry per record, identified by the record's id and type together, because two records of different types can share an id (an imported sheet keeps its own ids). A record queued again replaces its entry, so its latest amounts are the ones converted. A pass converts only the entries it read before fetching the rate, and an entry replaced meanwhile waits for the next pass. When the pass ends it takes off the queue only the entries it converted, as they were when it converted them, so an entry an undo or redo put back meanwhile, even the same one, stays queued.
- **The company file holds the queue.** The queue is the company's own list (`CompanyData.PendingConversions`), saved in the company file together with the records it converts, so the two always agree. `PendingConversionService` works from a copy of it, which `UsdConversion` keeps in step, and opening a company makes that copy the file's list again, less any entry whose record has already converted. Nothing else is read: a queue kept anywhere else could hold an entry for an edit that was thrown away unsaved, and would convert the saved record with the discarded amounts.
- **Undo and redo.** A record's entry leaves the queue with the record and comes back with it. Whatever puts a record back queues it again if it is still waiting (`UsdConversion.Requeue`), or puts back the entry it held when it was removed (`UsdConversion.Snapshot` and `Restore`), which keeps the date whose rate it waits for. Nothing else is needed: a record converted before the undo keeps its USD amounts. Undo and redo take a record out and put it back by its id (`RecordLists`), not by the object they kept, because undoing or redoing an import restores the company from a snapshot. The restore keeps each record that is still there, and each queue entry it leaves as it was, the same object (`RecordLists.RestoreInPlace`), so an undo further back on the stack still changes what is in the books: a stock undo moves the live stock and removes the live adjustment, and reads off the queue entry it holds whether the stock's cost converted meanwhile. The restore puts back every id counter too (`IdCounters.CopyFrom`), and the bank, Stripe and Argo Books API imports' undo and redo move them with `IdCounters.RewindTo` and `RaiseTo`. This covers deleting revenue, expenses, payments and purchase orders, approving and voiding a pay run, switching a receipt between expense and revenue, recurring entries, and the bank, Stripe and Argo Books API imports.
- **Precision.** Stored USD amounts are kept at full precision and never rounded to cents (`ExchangeRateService.TryConvertToUsdBase`, `UsdConversion`). Only the displayed number is rounded, to the currency's decimal places (`ExchangeRateService.TryConvertExact`). This way a row converted straight away and one converted later store the same USD amount.
- **Totals.** Each row is converted at its own date before the total is added up, so a total always matches its rows to the cent. A card or total built with `FormatTotalOrPending` (or `TryComputeDisplay`, its core) or `FormatSumDisplayFromUSD` shows `CurrencyService.PendingMarker` ("Pending") while any of its rows is missing a rate. A chart can't show Pending, so a point with a missing rate plots the USD amount (`CurrencyService.GetDisplayAmount`), or 0 for a return or loss, which has no USD amount (§10).
- **Reports and Insights** use one currency for the whole document (`DisplayCurrency.Resolve` over `DisplayCurrency.ReportDates`): the company's currency when every date has a rate, otherwise USD throughout, so a printed report never mixes currencies. Insights forecasts and Past Predictions are the exception and always show the company's currency at today's rate (§10).

---

## 3. USD amounts (`Effective*USD`)

Totals that add up rows in different currencies use each row's `Effective*USD` properties. Never add up rows' own-currency fields (`Total`, `Amount`, `TaxAmount`) when the rows are in different currencies, because that would add dollars to euros.

| Property | Meaning |
|---|---|
| `EffectiveTotalUSD` | The total, tax included, in USD. |
| `EffectiveSubtotalUSD` (`Transaction` only) | The total without tax, in USD (Total − Tax). |
| `EffectiveTaxAmountUSD` (`Transaction` only) | The tax, in USD. |
| `EffectiveShippingCostUSD` (`Transaction` only) | The shipping, in USD. It is already part of the total. |
| `EffectiveAmountUSD` (`Payment`) | One payment or refund, in USD. Negative for a refund. |

A row with `IsPendingConversion = true` is still waiting for its exchange rate (Rule 3a) and has no USD amount yet, so every `Effective*USD` property returns 0 until it is converted.

---

## 4. Invoice math

How an invoice's total is worked out, step by step:

```
0. LineSubtotal     = max(0, Quantity × UnitPrice − Discount), rounded to 2 dp            (LineItem.Subtotal)
1. Subtotal         = Σ over LineItem of LineSubtotal
2. invoiceDiscount  = clamp(DiscountIsPercent ? Subtotal × DiscountAmount/100 : DiscountAmount, 0, Subtotal)
3. invoiceCustomFee = max(0, CustomFeeIsPercent ? Subtotal × CustomFeeAmount/100 : CustomFeeAmount)
4. TaxableBase      = max(0, Subtotal − invoiceDiscount + invoiceCustomFee + ShippingAmount)
5. TaxAmount        = max(0, TaxIsFixed ? TaxRate : TaxableBase × TaxRate/100)
6. Total            = max(0, TaxableBase + TaxAmount + max(0, SecurityDeposit))
```

`InvoiceMath` does steps 1 to 6, and all C# code that needs these figures calls it. The one other copy is the script inside the editable invoice preview (`InvoicePreviewControl`), which repeats the same steps in JavaScript.

- The invoice-wide discount and fee appear as their own lines on the invoice, separate from `Subtotal`. A percentage discount or fee is a percentage of `Subtotal`.
- A discount on a single line only reduces that line, so a line discount bigger than the line makes the line free rather than reducing other lines.
- The invoice-wide discount can't be more than `Subtotal`, so shipping is still charged even when the discount covers all the goods.
- Tax is always worked out from the invoice's tax rate. A line item's own `TaxRate` isn't used for the total.
- **Security deposit** is a refundable hold against damage. It is added to the total but isn't taxed and isn't revenue, so the revenue created from the invoice is `Total − SecurityDeposit`. §15 covers a deposit that is kept, and §8 covers how refunds treat it. Helper: `SecurityDeposits`.

---

## 5. Payment totals on an invoice

Each invoice keeps these running totals, worked out from its payments. After changing an invoice's payments, the payment form, portal sync, currency conversion and recurring invoices call `InvoiceTotalsService.Recalculate(invoice, allPayments)`. The spreadsheet importer is different: it takes `AmountPaid` from the sheet, and sets `Balance` to `max(0, Total − AmountPaid)` when the sheet gives an amount paid, and to the sheet's balance when it gives only that. When it gives neither, a row whose status is Paid, Refunded or PartiallyRefunded was paid in full, so `AmountPaid` is the total and `Balance` is 0, unless the invoice already has payments recorded against it. Then its amounts are worked out from those payments with `InvoiceTotalsService.RecalculateFromPayments`, so they agree with its payments; the invoice's currency is set first, since only payments in its currency count. Any other row owes the total less what was already paid. It then works out the status from them (§6). Both imports, the column one and the AI one, set the balance this way.

**Updating an existing record.** Both imports change only what the sheet gives. The column import changes the fields it has columns for, and the AI import the fields its row gives a value for (`RecordLists.FillAbsent`), whatever the record: an invoice, a revenue, an expense, a payment, a customer, and so on. So a sheet that only updates the status of existing invoices leaves their amounts as they are, one that gives only a new total keeps what was already paid, and a record's USD amounts are converted again only when something they are worked out from (a date, an amount or the currency) is given.

| Field | How it's worked out | Notes |
|---|---|---|
| `AmountPaid` | Σ `Amount` of the payments that aren't refunds, in the invoice's currency | Refunds don't reduce it. |
| `AmountRefunded` | Σ of the refund amounts as positive numbers, in the invoice's currency | Never negative. |
| `Balance` / `BalanceUSD` | `max(0, Total − AmountPaid)`. In USD: `max(0, TotalUSD − Σ EffectiveAmountUSD of the payments that aren't refunds)` | What the customer still owes. A refund doesn't increase it. |

When a company opens, these totals are recalculated once for every invoice that has payments, to repair any old errors (`CompanyManager.HealInvoiceTotalsIfNeeded`). It only runs again when that repair logic changes. An invoice imported without payments keeps the `AmountPaid` the import gave it.

A payment can't be recorded on a Draft invoice, because a draft has no revenue until it is sent.

A refund never changes the payment it gives money back on. The original payment keeps the full `Amount` received, including any processing fee the customer paid. Refunds are subtracted from totals only, never from the amount shown for a single payment.

---

## 6. Invoice status

| Status | Meaning | How it's set |
|---|---|---|
| `Draft` | Being prepared; never sent. | Saved without sending. |
| `Pending` | Ready but not sent yet. | Only by a spreadsheet import. |
| `Sent` | Sent to the customer, waiting for payment. | When the invoice is sent. |
| `Viewed` | The customer opened it. | Only by a spreadsheet import. |
| `Partial` | The customer paid some and still owes the rest. | `0 < AmountPaid < Total`. |
| `Paid` | Paid in full (`AmountPaid >= Total`). | When a payment covers the balance. |
| `Overdue` | Past its due date and still owed (`Balance > 0`), and not a Draft, Paid, Refunded or Cancelled invoice. | Always worked out when shown (`Invoice.IsOverdue`), never saved. A spreadsheet import reads a sheet's Overdue as Sent. An old file can still hold a saved Overdue; it counts for nothing, and the invoice shows as Sent until it really is overdue. |
| `Cancelled` | The invoice was voided. | Only by a spreadsheet import. |
| `PartiallyRefunded` | Paid, then refunded less than `Total`, or refunded in full and then paid again. | The refund status rule below. |
| `Refunded` | Paid, then refunded in full with no later payment. | The refund status rule below. |

`InvoiceTotalsService.RecalculateStatus` sets the four payment statuses: Paid, Partial, PartiallyRefunded and Refunded. An invoice keeps any other status until its first payment or refund arrives, and that earlier status is saved in `Invoice.StatusBeforePayment`. If every payment is later removed (deleted, undone, or moved to another invoice), the invoice goes back to that status, so it is owed again and can become overdue. An invoice that got a payment status some other way, such as an import marking it Paid, has no earlier status to go back to and keeps the one it has.

**Imported invoices.** The spreadsheet importer takes the sheet's status, reading Overdue as Sent. Draft, Cancelled, Refunded and PartiallyRefunded can't be worked out from an amount paid, so one the sheet gives is taken at its word whatever the amounts, and so is one an existing invoice already has when the sheet gives no status: a cancelled invoice with a payment stays Cancelled, and so is never overdue. Every other status is one the amounts decide. The invoice starts from the status it had before any payment (`StatusBeforePayment`, or Sent when it has none), and `RecalculateStatus` moves it on with the imported amounts, as if the amount paid had arrived as a payment. So an Overdue or Sent invoice with 50 of 100 paid becomes Partial, one paid in full becomes Paid, an unpaid Pending, Sent or Viewed invoice keeps its status, and one marked Paid or Partial with nothing paid is Sent and owes its total. A row for a new invoice that gives no status is read as Draft, but only until its amounts are worked out, so one paid in full still becomes Paid. A row that updates an existing invoice and gives no status keeps that invoice's own status, which its amounts then work out the same way when the sheet gives any. A status counts as given only when it is one of the statuses above, in any case and ignoring spaces, hyphens and underscores, or one of a few other spellings: Canceled, Void and Voided are Cancelled, Paid in full is Paid, and Partially paid is Partial. Anything else, such as Open or Unpaid, is read as giving no status, so the amounts decide it (`SpreadsheetImportService.ParseImportedInvoiceStatus`). A sheet that updates existing invoices with a Status column and no amount columns, such as one marking a batch paid, sets the status as given, except that Overdue is still worked out from the stored amounts.

**Refund status rule** (`InvoiceTotalsService.RefundedStatus`). If less than `Total` has been refunded, the status is PartiallyRefunded. If at least `Total` has been refunded, it is Refunded, unless the customer then paid again so that net paid (`AmountPaid − AmountRefunded`) is still at least `Total`, in which case it is PartiallyRefunded. A processing fee the customer paid isn't refunded, so a $100 invoice paid with a $3 fee and refunded $100 is Refunded.

Screens and report tables show `InvoiceTotalsService.DisplayStatus`: Overdue if the invoice is overdue; otherwise, if it has refunds, the refund status rule worked out fresh; otherwise the saved status, with a saved Overdue shown as Sent. Never show the saved `Status` on its own. The Invoices page's status filter matches this displayed status.

Every overdue count and total (the Invoices page card and Overdue filter, the dashboard's Overdue Invoices card and Upcoming Invoices list, customer payment standings, the overdue notification and the Insights card) uses `Invoice.IsOverdue` and nothing else. A draft is never overdue, because it was never sent and nobody owes it yet.

The invoice document (the preview, the email and the page on the payment portal, all drawn by `InvoiceHtmlRenderer`) marks the due date as overdue from `Invoice.IsOverdue` too. An invoice being sent is drawn with the Sent status it goes out with, so a draft whose due date has already passed shows as overdue, and the create form's preview does the same. A cancelled, refunded or fully paid invoice never shows as overdue. A quote is drawn as a draft invoice, so it never shows as overdue.

---

## 7. Revenue `PaymentStatus`: which revenue counts as collected

`Revenue.PaymentStatus` is the `RevenuePaymentStatus` enum. When a company file loads, a blank or unrecognised value is read as `Paid`.

| Value | Counts as collected? |
|---|---|
| `Paid` | ✅ Yes (the default for new rows) |
| `Complete` | ✅ Yes (an old name for Paid, from earlier imports) |
| `Partial` | ❌ No |
| `Pending` | ❌ No |
| `Unpaid` | ❌ No |
| `Overdue` | ❌ No |

Always check with `RevenueAggregator.IsCollected(revenue)` rather than comparing the enum yourself. The spreadsheet importer's `NormalizePaymentStatus` turns free-form text into the enum.

**Revenue created from an invoice** counts as collected once the invoice is paid in full. It stays collected after a refund, because the refund is subtracted separately, on its own date (§8). `InvoiceTotalsService.SyncLinkedRevenueStatus` updates it whenever a payment is recorded or removed, whether by hand or through the payment portal.

---

## 8. Refunds

A refund is subtracted from revenue, profit and tax owed on the day the refund was issued, not on the day of the original payment.

### Effect on revenue

Revenue is reduced by the refund, except for any part of the refund that gave back a security deposit (below):

```
Revenue in period = Σ Revenue.EffectiveTotalUSD (paid only, in the date range)
                  − Σ |Payment.EffectiveAmountUSD| × Payment.RevenueShare for refunds in the date range
```

Helper: `RefundAggregator.GetRefundedInDateRangeUSD(payments, start, end)`.

### Effect on profit and tax

Part of every refund is tax being handed back. Profit only loses the part that was revenue before tax, and tax owed loses the rest:

```
Per refund:   pre-tax part = |Payment.EffectiveAmountUSD| × Payment.RevenueShare
                             × ((Invoice.Total − Invoice.SecurityDeposit − Invoice.TaxAmount) / (Invoice.Total − Invoice.SecurityDeposit))
              tax part     = |Payment.EffectiveAmountUSD| × Payment.RevenueShare − pre-tax part
Fallback:     if the refund isn't linked to an invoice, or the invoice is all deposit, the whole amount counts as pre-tax
```

The pre-tax share is taken from the invoice's total less its deposit and tax, not from `Subtotal / Total`, because `Subtotal` leaves out the invoice-wide discount, shipping and fees (§4). Helpers: `RefundAggregator.PreTaxShare`, `PreTaxPortionUSD`, `TaxPortionUSD`, `GetRefundedPreTaxInDateRangeUSD`.

### Refunds that give back a deposit

A deposit was never revenue (§4), so the part of a refund that returns it doesn't reduce revenue. Each refund records that part in `Payment.DepositAmount`, and the sums above use `Payment.RevenueShare`, the share of the refund that isn't deposit.

- A refund made in Argo Books says how much of it is deposit: the refund form shows the deposit as its own line, and the payment sync records that line's amount.
- A refund made directly in the payment provider's dashboard doesn't say, so it is taken from the deposit first, since that is usually what such a refund is for.
- Either way, the deposit part is never more than the deposit still held: the deposit less earlier refunds of it and any part the business kept. Refunding a deposit the business had kept does reduce revenue, because keeping it had made it revenue.

`DepositAmount` is set when the refund syncs (`PaymentPortalService`), and for older refunds by the one-time repair when a company opens. Helper: `SecurityDeposits.RefundPortion`.

---

## 9. Expenses

Expenses have no paid or unpaid status, so every expense counts. Expense totals include tax (`EffectiveTotalUSD`, Rule 1). Profit uses `OperatingExpenseUSD` instead, which leaves out tracked stock (§14).

**Payroll.** Approving a pay run records each employee's net pay as an expense dated on the pay date (`PayrollService.ApproveAndRecord`). Wages are worked out in the company's currency, and each wage expense is stored in USD like any other expense, at the pay date's rate (Rule 3a). A pay run approved ahead of its pay date has no rate yet, so its expenses wait for that date's rate and convert once it can be fetched; until then they count as 0 (§3) and totals that include them show Pending. Voiding a pay run removes its expenses, and undoing the void puts them back waiting again if they still are.

---

## 10. Formal reports and other screens

`AccountingReportDataService` builds the formal reports (Income Statement, Balance Sheet, Cash Flow, General Ledger, and similar), and `ReportTableDataService` fills the transaction tables in the Report Builder. Both deliberately follow accounting conventions instead of the dashboard rules:

- The Income Statement, General Ledger and Report Builder revenue and expense tables show amounts without tax (`EffectiveSubtotalUSD`), because the Balance Sheet shows tax separately as Sales Tax Payable. Cash figures and Sales by Product (§13) include tax (`EffectiveTotalUSD`).
- They count all revenue in the date range, paid or not. Accountants call this the accrual basis.
- Cash (on the Cash Flow Statement and the Balance Sheet's Cash line) includes tax: paid revenue that has no invoice, plus every payment (refunds count as negative), minus every expense.
- The Balance Sheet lists **Inventory** as a current asset (`InventoryValuationService.TotalValueAsOf`). It counts each item's stock on hand as of the report's end date, placing each stock change on the date of its sale or purchase, or on its own date when it has neither. Stock is valued at the item's current `UnitCost` in USD, because the app doesn't keep a history of costs, and converted at the end date's rate (§14, Stock value). The Income Statement shows a Cost of Goods Sold line and Gross Profit whenever a sale in the date range has a cost (§14).
- The Balance Sheet lists **Security Deposits** as a liability: the deposit on every invoice issued by the end date, less what refunds by then gave back and any part kept by then (`SecurityDeposits.StillHeld`).

### Insights tab (`InsightsService`)

The Insights tab (trends, anomalies, forecasts, recommendations) uses collected revenue and expenses, both with tax included (`IsCollected`, `EffectiveTotalUSD`). It does not subtract refunds (§8). Forecast profit is simply forecast revenue minus forecast expenses, not the Rule 1 net profit.

**Trend comparisons** compare against the period `ComparisonPeriod.For` gives (§12). It works this out from the date preset's name (`AnalysisDateRange.PresetName`), so the page must build its range with `AnalysisDateRange.FromPreset`. A range built with `AnalysisDateRange.Custom` is compared with the same number of days just before it. A forecast range such as "Next Month" is first mapped to this month, quarter or year so far.

**Currency.** Insights does its analysis in USD and converts only the amounts it shows. Like a report, each run picks one currency for everything (Rule 3a), except that it ignores dates after today, since it never converts anything at a future date. Amounts in descriptions, averages included, convert each row at its own date, and the overdue total converts each invoice's balance at its issue date. Forecast cards, ranges and Past Predictions convert their stored USD figures to the company's currency at today's rate (`InsightsPageViewModel.FormatForecastAmount`). The sample numbers shown to free users are for illustration only and are never converted.

**Top Performing Product** compares each product's collected revenue with its `CostPrice` converted to USD at each sale's date. A sale still waiting for its rate, or a line whose cost price can't be converted yet, is left out of the comparison rather than counted as having no revenue or no cost. A product with under a cent of revenue in the period has no meaningful margin and is left out too.

**Forecast accuracy.** A forecast is saved under the future period it covers. Once that period ends, it is checked against actual results worked out the same way the forecast was (`ForecastAccuracyService.ValidatePastForecasts`, `RunBacktestAsync`).

### Returns and Losses

Returns (items returned by customers or sent back to suppliers) and Losses (lost or damaged stock) don't change revenue, profit or expenses; it is the refund payment (§8) that reduces revenue and profit. The Financial Impact charts and stat cards add up `Return.RefundAmount` and `LostDamaged.ValueLost`. The Returns page shows `Return.NetRefund`, which is the refund less any restocking fee.

Neither record stores a USD amount or its own currency. The amount is in the currency of the sale or purchase it came from (`Return.OriginalTransactionId`, `LostDamaged.InventoryItemId`), or in the company's currency if there is none, and it is converted from that currency at the record's own date (`ReturnLossAmounts.CurrencyOf`, `DisplayCurrency.FromNative`). An amount already in the display currency is used as it is. If the rate is missing, the amount counts as 0 on a chart and shows Pending on a stat card or page total.

### Integration import previews

Before a Stripe or Argo Books API sync imports anything, it asks the user with totals of the sales, fees or expenses it is about to bring in (`IntegrationImportFlow`). Those rows aren't in the books yet, so they have no USD amount. Each total converts every amount from its own currency at its own date (`DisplayCurrency.TrySumFromNative`), the way Returns and Losses are converted, and shows Pending while a rate is missing. Amounts in different currencies are never added together as they stand.

### Bank matching

Bank Matching (`BankMatchingService`) only marks revenue, expenses, invoices and payments as matched (`BankMatched`); it never changes an amount. It compares each row's amount in its own currency (`Total`, or a payment's `Amount`) with the bank line, not a USD amount.

### Mobile app

The phone shows a snapshot the desktop builds (`SnapshotBuilder`), covering all time. Money In is worked out the same way as the dashboard's Total Revenue card: collected revenue less refunds (Rule 2, §8). Money Out is every expense, and Profit is Money In minus Money Out. Like a report, the snapshot picks one currency for everything (`DisplayCurrency.Resolve`, Rule 3a) and converts each row at its own date. It sends the numbers together with that currency, and the phone writes them out with the currency's symbol. Each invoice row carries the status the desktop shows (`InvoiceTotalsService.DisplayStatus`, §6), as text the phone displays as it is.

---

## 11. Quick reference: where each number comes from

Use this table to check where a number on screen comes from. For a new chart or stat card, find the closest row and copy a screen that already follows it.

| Screen | Revenue field | Expense field | Paid sales only? | Refunds subtracted? |
|---|---|---|---|---|
| Total Revenue card (dashboard, Analytics and the Revenue page's Monthly Revenue card), Revenue Over Time, Revenue Growth (Analytics) | `EffectiveTotalUSD` | — | Yes | Yes, in full |
| Top Customers (the Analytics chart and the dashboard widget, both over the selected date range, `TopCustomers.Rank`) | `EffectiveTotalUSD` | — | Yes | Yes, in full, the refunds issued in the range. A customer left at or below 0 is not listed |
| Total Expenses card (dashboard, Analytics and the Expenses page's Monthly Expenses card), Expenses Over Time | — | `EffectiveTotalUSD` | n/a | n/a |
| Net Profit card, Profit Margin (Analytics), Profit Over Time | `EffectiveSubtotalUSD` | `OperatingExpenseUSD`, plus cost of goods sold (§14) | Yes | Yes, the pre-tax part |
| Revenue vs Expenses | `EffectiveTotalUSD` | `EffectiveTotalUSD` | Yes (revenue) | Yes, in full (revenue) |
| Report Summary box total | The same as the Total Revenue card (Revenue reports), the Total Expenses card (Expenses reports) or the Net Profit card (all other reports) | | | |
| Tax charts and Analytics tax cards | `EffectiveTaxAmountUSD` | `EffectiveTaxAmountUSD` | Yes (revenue) | Yes, the tax part (except the by-category, by-product and Effective Tax Rate figures) |
| Avg Shipping Cost (Analytics) | `EffectiveShippingCostUSD` | `EffectiveShippingCostUSD` | Yes (revenue) | n/a |
| Return / Loss Financial Impact | `Return.RefundAmount` (§10) | `LostDamaged.ValueLost` (§10) | n/a | n/a |
| Forecasts, backtests and accuracy checks (Insights) | `EffectiveTotalUSD` | `EffectiveTotalUSD` | Yes (revenue) | No |
| Geographic / Country charts | `EffectiveTotalUSD` | `EffectiveTotalUSD` | Yes (revenue) | No |
| Outstanding and Overdue Invoices cards | `BalanceUSD` | — | **No** (they show what's unpaid) | n/a |
| Revenue page list | `EffectiveTotalUSD` | — | **No** (it lists everything) | n/a |
| Income Statement / General Ledger | `EffectiveSubtotalUSD` | `EffectiveSubtotalUSD` (Income Statement: without tracked stock, plus cost of goods sold, §14) | No (all revenue) | Yes, the pre-tax part |
| Sales by Product (Analytics tab) | `EffectiveTotalUSD`, shared out per line | — | Yes | No (§13) |
| Sales by Product (report) | `EffectiveTotalUSD`, shared out per line | — | No (all revenue) | No (§13) |

---

## 12. Implementation pointers

Helpers for adding things up, in `ArgoBooks.Core/Services/` unless the entry says otherwise:

- `RevenueAggregator.SumCollectedRevenueUSD(revenues, start, end)`: collected revenue, tax included, in USD.
- `RevenueAggregator.SumCollectedRevenuePreTaxUSD(revenues, start, end)`: collected revenue without tax, in USD (for profit, not for display).
- `ExpenseAggregator.SumExpensesUSD(expenses, start, end)`: expenses, tax included, in USD.
- `RefundAggregator.GroupRefundsByDayUSD(payments, start, end)`: refunds by day, for time-series charts.
- `ProfitCalculator.CalculateNetProfitUSD(data, start, end)` and `CalculateNetProfitByDayUSD`: the Rule 1 net profit, as a total and by day. Every screen that shows profit calls these rather than working it out again.
- `CostOfGoodsAggregator`: cost of goods sold on sales (`SumCostOfGoodsSoldUSD`, paid only or all), and expenses without the tracked stock they bought (`SumOperatingExpensesUSD`). `ProfitCalculator` uses both.
- `DatePresetNames.GetDateRange(preset)` (in `ArgoBooks.Core/Models/Reports/`): the dates every preset covers, and the only code that works them out (the dashboard, Analytics, Insights, report templates, and the Revenue and Expenses pages' monthly cards). This week, month, quarter and year run from their first day to the end of today, not to the end of the period, so a future-dated row isn't counted yet and the comparison below compares like with like. Last month, quarter and year cover the whole period. The preset names are `DatePresetNames`; the `DateRangePreset` enum names the same presets for code, and both custom spellings, "Custom Range" (dashboard and Analytics settings) and "Custom" (report templates), read as a custom range.
- `DashboardCalculations.FormatRevenue` and `FormatExpenses` (in `ArgoBooks/ViewModels/Dashboard/`): the Total Revenue and Total Expenses cards, on the dashboard and in Analytics, so the two agree for the same range. The Revenue and Expenses pages' monthly cards call them with This Month's dates, so they always match the dashboard set to This Month.
- `ComparisonPeriod.For(preset, start, end)`: the period every "vs previous period" figure compares against (the dashboard, Analytics, Insights trends, and the report Summary box's growth rate). This month, quarter or year so far is compared with the same days of the previous one, cut short if the previous one is shorter (This Month on Sep 11 compares with Aug 1 to Aug 11; This Year on Feb 29 compares with Jan 1 to Feb 28). Last month, quarter or year is compared with the whole period before it. Every other range is compared with the same number of days just before it.

---

## 13. Sales by product

The Analytics Products tab and the Report Builder's Sales by Product template both show **revenue and units sold** for each product, using `ProductSalesService`. They differ only in which revenue they count (below).

**Neither shows profit per product.** Cost of goods sold only exists for products that track inventory, and it reads low while stock from before it began is still selling (§14), so a per-product profit margin would be misleading.

### Revenue per product (tax included, USD)

A transaction's USD total isn't stored per line, so each line gets a share of it in proportion to the line's own subtotal. Tax is shared out the same way:

```
lineItemsTotal = Σ over LineItem of li.Subtotal          (in the transaction's own currency, before tax)
revenueUSD(li) = lineItemsTotal != 0
               ? li.Subtotal / lineItemsTotal × txn.EffectiveTotalUSD     (the last line with a subtotal takes what is left)
               : 0
```

Lines with no product are grouped under "Unknown". A transaction with no lines at all is left out, and one whose lines add up to 0 (every line discounted to nothing) counts its units but no revenue.

**One way to share out a transaction.** `LineAllocation` is the only code that splits a transaction's amount across its lines, for every screen that shows money per product, per category or per tax rate: Sales by Product, Insights' Top Performing Product, the revenue and expense by-category charts, the Income Statement and General Ledger, the tax by-category and by-product charts, the Tax Summary report and refunds by product. An amount is shared by each line's subtotal. Tax is shared by each line's own tax (`LineItem.TaxAmount`, its subtotal times its own rate) when any line carries one, so an untaxed line takes none of it, and otherwise by subtotal like any other amount, since the tax was worked out on the whole invoice (§4) (`LineAllocation.AllocateTax`). The shares are kept at full precision (Rule 3) and the last line with a weight takes whatever is left, so the lines always add up to the transaction's amount exactly and a free line always gets exactly 0. Only the number shown is rounded. What each screen shares out, and where a transaction goes when its lines can't take it (no lines, or lines that add up to 0), differs by screen:

| Screen | Amount shared out | When the lines can't take it |
|---|---|---|
| Sales by Product (both), Top Performing Product (Insights) | `EffectiveTotalUSD` | Left out, since it can't be put on any product |
| Revenue and expense by-category charts | `EffectiveTotalUSD` | "Other" |
| Income Statement categories, General Ledger | `EffectiveSubtotalUSD` | The transaction's own line ("Uncategorized" on the Income Statement, the Revenue or Expenses heading in the General Ledger), so the report still balances |
| Tax by category and tax by product charts | `EffectiveTaxAmountUSD`, as tax | "Other" |
| Tax Summary report, by rate | `EffectiveTaxAmountUSD`, as tax, grouped by each line's own rate | The transaction's own rate, which is also where its tax goes when no line carries a rate of its own |
| Refunds by product | Each invoice's refunds, in the display currency | Left out |

On the Income Statement, an expense that bought tracked stock shares out only what is left after the stock (§14) across its other lines. When those lines can't take it, as when every line bought stock and only shipping or fees are left, it goes under "Uncategorized" like any transaction whose lines can't.

Units sold is the sum of the lines' quantities. Average sale price is revenue ÷ units sold (0 when no units were sold), rounded to 2 decimals.

### Which revenue each one counts

- **Analytics Products tab** (`cashBasis: true`) counts paid sales only (`RevenueAggregator.IsCollected`). Its total can differ from the Total Revenue card in two ways: it is higher by any refunds, because it doesn't subtract them (below), and lower by any revenue that has no lines or whose lines add up to 0.
- **Report Builder template** (`cashBasis: false`) counts all revenue in the date range, paid or not, like the Income Statement.

Refunds are recorded against a whole invoice (§8), not against individual lines, so per-product revenue can't subtract them. Returns by Product and Losses by Product show returned and lost stock instead.

---

## 14. Cost of goods sold

Products with **Track Inventory** turned on are treated differently for profit: what their stock cost is subtracted when a sale uses it, not as an expense on the day it was bought. Products that don't track inventory aren't affected.

`InventoryStockService` is the only code that moves stock: for a purchase, a sale, editing or deleting either, a Stripe or Argo Books API import, a transfer, or receiving a purchase order. It is also the only code that sets a sale line's cost of goods sold. `CostOfGoodsAggregator` is the only code that adds those figures up.

### Buying tracked stock

A purchase line for a tracked product adds its quantity to stock and is marked `LineItem.IsStockPurchase`. Its price before tax counts as stock, not as an expense:

```
stockPurchaseUSD(expense)    = min( Σ over stock lines of li.Subtotal × (expense.EffectiveTotalUSD / expense.Total),
                                    expense.EffectiveTotalUSD )
operatingExpenseUSD(expense) = expense.EffectiveTotalUSD − stockPurchaseUSD(expense)
```

Tax, shipping and fees on the purchase are still expenses. The stock record's `UnitCost` is set to the line's price per unit before tax, in USD, at the purchase's exchange rate. While that rate is missing, the cost is pending (see Stock value below). If the purchase is in the company's currency, the product's `CostPrice` is set to the line's unit price too.

### Selling tracked stock

A sale line for a tracked product takes its quantity out of stock and saves its cost on the line, in USD:

```
openingUsed(li)   = min(item.OpeningUnits, li.Quantity)
li.CostOfGoodsUSD = (li.Quantity − openingUsed(li)) × item.UnitCost
```

That cost is saved and not recalculated later. Editing a sale keeps the unit cost its lines were saved with, for each product and location. Only a product or location added by the edit, one whose earlier lines were all opening stock, or one whose earlier lines were still waiting for their cost (below), uses the current `UnitCost`. A sale still waiting for its exchange rate has no cost, just as it has no revenue yet.

**A sale made while its stock's cost is pending.** When the stock record's cost is still waiting for its rate (Stock value, below), the line is marked `LineItem.IsCostOfGoodsPending` and its `CostOfGoodsUSD` is 0 for now, as any amount waiting for its rate counts as 0 (§3). It is not left at 0: when the stock record's cost converts, every line waiting on that record (same product and location) gets `(li.Quantity − li.OpeningUnitsUsed) × the converted unit cost`, and from then on it is fixed like any other. This happens even if a later purchase with a known rate has already given the record a new cost, so the sale keeps the cost the stock had when it sold. A record holds one pending cost at a time: if another purchase waiting for its rate comes first, its cost replaces the earlier one, and the waiting sales take that.

Undoing a change puts the stock record's cost back as it was before the change. If that cost was waiting for its rate and has converted since, it comes back converted rather than waiting again. A sale the undo puts back, such as one deleted or edited while its lines were waiting, was out of the books when the cost converted, so its waiting lines get that converted cost as the undo puts the stock back (`InventoryStockService.Revert`). This happens even when a later purchase has since given the stock a new cost.

### Profit

Net profit is the Rule 1 formula. The dashboard counts only paid sales, for both revenue and cost of goods sold (Rule 2). The Income Statement counts every sale in the date range, shows **Cost of Goods Sold** and **Gross Profit** under revenue whenever that cost isn't zero, and leaves tracked stock out of its expense categories. The Expenses card, the Expenses page and cash flow still count every purchase in full, because that money really was spent.

**Profit while a sale waits for its stock's cost.** A sale waiting for its stock's cost counts that cost as 0, so a figure that subtracts cost of goods sold would read high. While any sale it counts is waiting (`CostOfGoodsAggregator.IsCostOfGoodsPending`), such a figure follows the all-or-nothing Pending rule of Rule 3a and shows Pending until the cost converts. That covers the dashboard's Net Profit card, the Analytics Net Profit and Profit Margin cards (`CurrencyService.FormatNetProfitOrPending`), and the report Summary box's net profit. The cards' change against the previous period is left blank, and the Summary box's net profit growth rate reads Pending, while a sale in either period is waiting (`CostOfGoodsAggregator.IsProfitChangePending`), since a period that reads high makes the change wrong either way. On the Income Statement the Cost of Goods Sold, Gross Profit and Net Income lines read Pending, while its revenue and expense lines still show their amounts. Profit Over Time is a chart and can't show Pending, so it plots the cost as 0.

### Stock on hand when this began (opening units)

Stock that was already on hand when cost of goods sold was introduced had already been counted as an expense when it was bought, so it must not reduce profit a second time when it sells. The first time a company opens in a version with cost of goods sold, `CompanyManager.StartCostOfGoodsIfNeeded` records each stock record's units on hand as `InventoryItem.OpeningUnits`. Sales use these opening units first, at no cost (`LineItem.OpeningUnitsUsed`), and editing or deleting a sale gives them back. A transfer moves opening units in proportion to the stock it moves. A purchase or sale saved before cost of goods sold was introduced keeps the old treatment when it is edited: it moves stock, stays a full expense (or has no cost), and uses no opening units.

### Stock value

A stock record's `UnitCost` is always in USD. A product's `CostPrice` is in the company's currency. They are set as follows:

- A purchase of tracked stock sets `UnitCost` from the purchase line (above).
- A new stock record starts at the product's `CostPrice` converted to USD at one day's rate (`InventoryStockService.StartAtCostPrice`). The day is the date of the sale or purchase that created the record, the order date for a purchase order being received, and today for a record added on the Stock Levels page. When receiving a purchase order creates the record, its cost comes from the order line instead, converted at the order's own rate. Receiving into an existing record doesn't change its cost. A transfer gives the new record the cost of the stock it came from, pending if that cost is pending.
- **A missing rate leaves the cost pending**, following Rule 3a. The record is marked `InventoryItem.IsPendingConversion`, its `UnitCost` is 0 for now, and the cost in its own currency waits in the conversion queue (`PendingConversionService`, type `InventoryItem`) with the date whose rate it needs. When that rate can be fetched, the queue converts it into `UnitCost` and fills in the sales that were waiting on it (above). A purchase with a known rate that sets the cost first ends the wait for the record, but the queued cost still converts for those sales.
- The spreadsheet Inventory sheet's Unit Cost column is the stored USD value, both on export and on import. Both imports, the column one and the AI one, update a stock record through `InventoryStockService.ImportStockRecord`, which changes only the fields the sheet gives. A pending cost is exported as 0, so a Unit Cost of 0 leaves a pending cost waiting, and a different, non-zero Unit Cost replaces it. Sales already waiting on the queued cost still take that one.
- Sales waiting on a record's pending cost find the record by its product and location. An import therefore leaves the product and location of a record with such sales as they are, and changes its other fields.

Stock value is `InStock × UnitCost`, added up in USD and converted to the display currency once, at the date the stock is valued at:

- **On screens** (the dashboard's Inventory Value card and the Locations page), stock is valued as it stands now, at today's rate (`CurrencyService.FormatStockValue`). It shows Pending while today's rate is missing, or while a record holding stock has a pending cost.
- **In reports** (the Balance Sheet and the Report Builder's inventory table), at the report's end date, the date `DisplayCurrency.ReportDates` makes sure has a rate. A record with a pending cost counts at 0 there.

Cost of goods sold uses `UnitCost` directly, since it is already in USD.

### Locations

Each line takes stock from, or adds it to, the stock record at its location (`LineItem.LocationId`). If the line has no location, or the product has no stock record there, the product's first stock record is used. If the product has no stock record at all, a new one is created at the line's location, or at the company's first location. Editing or deleting a transaction gives back exactly what its own stock adjustments show it moved, to the same records. So a transaction saved before its product tracked stock, or brought in by the spreadsheet import, has nothing to give back.

### Not covered

- **Returns** only record the return (§10). They don't put stock back or reverse the sale's cost of goods sold.
- **Receipt scans and revenue created from invoices** don't move stock.

---

## 15. Rentals

`RentalBookings` does the rental math. A rental has one or more lines, each with an item, a quantity, a rate type (daily, weekly or monthly) and a deposit per unit. A rental saved by the old Rent Out action has no lines; its single item and total deposit are stored on the rental itself, and `RentalRecord.EffectiveLineItems()` turns them into a line.

### Charges

```
days            = max(1, returnDate.Date − startDate.Date)
line (Daily)    = rate × days × quantity
line (Weekly)   = rate × ceil(days / 7) × quantity
line (Monthly)  = rate × ceil(days / 30) × quantity
TotalCost       = Σ lines + ExtraCharges
```

Days are counted by date, not time, so out Monday and back Wednesday is two days, and every rental is at least one day. Estimates are worked out to the due date. An invoice made from a rental has one line per item, charged up to the return date (or the due date if the rental is still out), plus a line for any extra charges.

### Deposits and extra charges

The rental's `SecurityDeposit` is the per-unit deposit × quantity, added up over its lines. At return, any part of it can be refunded (`DepositRefunded`) and the rest is kept. A kept deposit becomes revenue on the return date:

- On a rental with an invoice, it becomes its own revenue row (`Revenue.IsKeptDeposit`), linked to the invoice and converted at the invoice's rate (Rule 3a).
- On a rental without an invoice, it is added to the revenue recorded when the rental is marked paid (below).

If the invoice was paid online, the refunded deposit goes back through the payment provider, and the Balance Sheet stops counting it as held once that refund syncs. Extra charges, such as a late fee or damage, are billed on top of the rental and don't come out of the deposit.

### Paid without an invoice

Marking a rental paid when it has no invoice records a revenue row for `TotalCost` plus any kept deposit, in the company's currency and converted at its own date (Rule 3a). It is dated on the return date if marked paid at return, otherwise on the day it was marked paid. `RentalRecord.RevenueId` links the two, so marking the rental unpaid, or deleting it, removes that revenue. A rental with an invoice is counted through its invoice instead, and a rental already marked paid can't be invoiced, so its money is never counted twice.

### Stock and reservations

A rental that starts after today is **Reserved** and doesn't touch stock. Checking it out makes it Active and takes its units out of stock; returning it puts them back. Before a rental is saved or checked out, each item must have enough units free on every day the rental covers:

```
owned  = InStock + units out on active and overdue rentals
booked = the most units other rentals need on any one day of this rental's dates
free   = owned − booked
```

A reservation needs its units from its start date to its due date. A rental that is out needs them for its dates and, once it's late, every day up to today. A rental that takes stock now also can't take more than is in stock, plus what it already has out when it is being edited.
