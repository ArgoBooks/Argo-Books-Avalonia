# Calculation Standards

This document is the single source of truth for how money is counted across the app: revenue, expenses, profit, tax, refunds, invoices, payments. Every dashboard stat card, chart, report, and analytics figure should follow these rules so that numbers agree wherever the user looks.

If you find a calculation that disagrees with this document, the calculation is wrong. Fix the code, not the doc. If a new requirement genuinely needs a different rule, update this doc first, then update the code.

---

## 1. Vocabulary

| Term | Meaning |
|---|---|
| **Invoice** | A bill the business issued to a customer. Has a status (Draft, Sent, Paid, etc.) and a balance owed. |
| **Revenue** | A row representing money the business earned. Linked to an invoice when one exists. Has a `PaymentStatus`. |
| **Expense** | A row representing money the business spent. Linked to a supplier. |
| **Payment** | A row representing a single money movement on an invoice. Positive = money in; a refund (below) is money out. |
| **Refund** | A `Payment` row with `IsRefund=true` and a negative amount, tied to the invoice (and usually the original payment) it offsets, and dated on the day it was issued. |
| **Subtotal** | The line items added up, each less its own discount, before any invoice-wide discount, fee, shipping and tax (§4). Not the pre-tax amount, which is `EffectiveSubtotalUSD` (§3). |
| **Tax amount** | The sales tax portion. |
| **Total** | Subtotal − discount + fees + shipping + tax, plus the security deposit on an invoice (§4). What the customer was charged or the business paid. |

---

## 2. Foundational rules

### Rule 1: Revenue display is **gross** (includes tax); profit is **pre-tax**.

- **Total Revenue** card, charts, customer billings: `EffectiveTotalUSD` (gross).
- **Net Profit**, **Profit Margin**, **Profit Over Time**: `EffectiveSubtotalUSD` (pre-tax) on the revenue side.
- **Expenses**: `EffectiveTotalUSD` (gross).
- **Tracked stock** is the exception, on the profit side only (§14).

```
Total Revenue  = Σ Revenue.EffectiveTotalUSD − Refunds(full)
Net Profit     = Σ Revenue.EffectiveSubtotalUSD − Σ OperatingExpenseUSD(expense) − Σ CostOfGoodsSoldUSD(revenue) − Refunds(pre-tax)
Profit Margin  = Net Profit / Σ Revenue.EffectiveSubtotalUSD
Tax Owed       = Σ Revenue.EffectiveTaxAmountUSD − Refunds(tax part) − Σ Expense.EffectiveTaxAmountUSD
```

The refund terms are defined in §8.

### Rule 2: Dashboard figures are **cash-basis** (paid-only).

Every revenue money sum on the dashboard, in Analytics and in `ReportChartDataService` filters with `RevenueAggregator.IsCollected` first (§7), tax figures included.

Exceptions, where unpaid revenue counts:
- Outstanding and Overdue Invoices, which exist to show what hasn't been paid.
- The Revenue page list, which lists every revenue.
- Counts of rows or customers rather than money: Customer Growth, Active vs Inactive Customers, Customer Payment Status, Transactions Processed, Transactions by Accountant, Tax Rate Distribution.
- Formal reports (§10).

### Rule 3: Aggregate in **USD**, display in the **company's currency**.

Aggregations sum `Effective*USD` values so multi-currency companies roll up correctly. Every number the user sees or exports (stat cards, chart bars, chart titles, axes, tooltips, exports) is in the display currency: the company's currency setting, or USD for a report or Insights run that can't price every date (Rule 3a).

### Rule 3a: Always convert at one exact date's rate.

Every conversion uses the exchange rate for one exact date, with USD as the base, fetched from the Argo server (`/api/exchange-rates*.php`, which returns USD -> all per date). That date is the row's own date, except:

- **Online payments, their refunds, and a kept deposit** are stored in USD at their invoice's rate. One on an invoice still waiting for its rate waits too, queued at the invoice's date, so it converts at the rate the invoice does and a paid invoice owes nothing in USD either. Like every row, they convert from USD to the display currency at their own date.
- **Forecast figures and Past Predictions** are "as of now" projections and convert at today's rate (§10).

A missing rate is never replaced by another date's.

- **Rows already in the display currency** show their original amount and need no rate, one at a time (`FormatWithOriginal`) and in `TrySumDisplayFromUSD` totals, where one still waiting for its USD value counts 0 like everywhere else (§3). `FormatTotalOrPending` totals and reports convert every row from USD.
- **Precision.** Stored `*USD` fields are full precision, never rounded to cents (`ExchangeRateService.TryConvertToUsdBase`, `ConvertToUSDAsync`). Display conversion rounds to the currency's decimal places (`ExchangeRateService.TryConvertExact`). Import and the pending self-heal both store the unrounded value, so a row converted at once and one converted later store the same USD.
- **A row without its exact-date rate** (offline, future-dated, or missed by import's pre-scan) is saved `IsPendingConversion` and converts automatically once that rate can be fetched (`PendingConversionService`). Import fetches the rates it needs first (`RateReadinessService.EnsureRatesAsync`) and, if it can't, stops with a prompt to retry or cancel. Opening a company fetches any rates its transaction dates are missing the same way (`CurrencyService.WarmCompanyRatesAsync`).
- **Totals.** Each row converts at its own date before summing, so a total matches its rows to the cent. A card or total built with `FormatTotalOrPending` (or `TryComputeDisplay`, its core) or `FormatSumDisplayFromUSD` shows `CurrencyService.PendingMarker` while any row it converts lacks its rate. A chart can't show Pending: a point whose rate is missing plots the USD amount (`CurrencyService.GetDisplayAmount`), or 0 for a return or loss, which has no USD amount (§10).
- **Documents.** A report or an Insights run picks one currency for the whole document with `DisplayCurrency.Resolve` over `DisplayCurrency.ReportDates`: the company currency when every date has an exact rate, otherwise USD throughout, so a printed report never mixes currencies. Insights forecasts and Past Predictions are the exception: they always show the company currency at today's rate (§10).

---

## 3. Effective USD properties

USD aggregations should use the `Effective*USD` properties on each money model. Do not add up native fields (`Total`, `Amount`, `TaxAmount`) of rows in different currencies.

| Property | Meaning |
|---|---|
| `EffectiveTotalUSD` | Gross-of-tax USD. |
| `EffectiveSubtotalUSD` (`Transaction`) | Pre-tax USD (= Total − Tax). |
| `EffectiveTaxAmountUSD` (`Transaction`) | Tax portion USD. |
| `EffectiveShippingCostUSD` (`Transaction`) | Shipping USD. Already inside Total. |
| `EffectiveAmountUSD` (`Payment`) | One payment or refund in USD. Negative for refunds. |

`IsPendingConversion = true`: the row has no USD value yet (Rule 3a). Every `Effective*USD` property returns 0 until it converts.

---

## 4. Invoice math (per-invoice)

```
0. LineSubtotal     = max(0, Quantity × UnitPrice − Discount), rounded to 2 dp            (LineItem.Subtotal)
1. Subtotal         = Σ over LineItem of LineSubtotal
2. invoiceDiscount  = clamp(DiscountIsPercent ? Subtotal × DiscountAmount/100 : DiscountAmount, 0, Subtotal)
3. invoiceCustomFee = max(0, CustomFeeIsPercent ? Subtotal × CustomFeeAmount/100 : CustomFeeAmount)
4. TaxableBase      = max(0, Subtotal − invoiceDiscount + invoiceCustomFee + ShippingAmount)
5. TaxAmount        = max(0, TaxIsFixed ? TaxRate : TaxableBase × TaxRate/100)
6. Total            = max(0, TaxableBase + TaxAmount + max(0, SecurityDeposit))
```

`InvoiceMath` implements steps 1 to 6, and every C# caller uses it. The editable invoice preview's in-page script (`InvoicePreviewControl`) repeats the same steps in JavaScript.

- The invoice-level discount and fee show as their own lines, not folded into `Subtotal`. A percentage discount or fee is taken from `Subtotal`.
- A line's own discount comes off only that line, so a discount bigger than its line makes the line free.
- Shipping is still payable when a discount wipes out the goods, because the discount caps at the subtotal.
- A line item's own `TaxRate` is not used in the invoice total; the invoice's rate is.
- **Security deposit** is added to the total but is not taxed and is not revenue: it's a refundable hold against damages, and the revenue created from the invoice leaves it out (`Total − SecurityDeposit`). A deposit kept at return is in §15, and how refunds treat it is in §8. Helper: `SecurityDeposits`.

---

## 5. Payment math (per-invoice running totals)

These fields on `Invoice` are kept in sync from its `Payment` rows. The payment form, portal sync, currency conversion and recurring invoices call `InvoiceTotalsService.Recalculate(invoice, allPayments)` after changing an invoice's payments. The spreadsheet importer instead takes `AmountPaid`, `Balance` and `Status` from the sheet.

| Field | Formula | Notes |
|---|---|---|
| `AmountPaid` | Σ `Amount` of non-refund payments (in invoice's currency) | Does not decrease on refund. |
| `AmountRefunded` | Σ `\|Amount\|` of refunds (in invoice's currency) | Always ≥ 0. |
| `Balance` / `BalanceUSD` | `max(0, Total − AmountPaid)`; in USD, `max(0, TotalUSD − Σ EffectiveAmountUSD of non-refund payments)` | What the customer still owes. Refunds don't raise it. |

Opening a company reruns this once per heal version (`CompanyManager.HealInvoiceTotalsIfNeeded`), only on invoices that have Payment rows, so an invoice imported without payments keeps the `AmountPaid` the import gave it.

The payment form doesn't offer Draft invoices, because a draft has no linked revenue until it is sent.

A refund never edits the payment it offsets. The original keeps the gross `Amount` that came in, including any processing fee the customer paid, and refunds come off totals only, never off the amount shown for a single payment.

---

## 6. Invoice status

| Status | Meaning | How it's set |
|---|---|---|
| `Draft` | Being prepared; never sent. | Saved without sending. |
| `Pending` | Ready but not sent yet. | Only from a spreadsheet import. |
| `Sent` | Sent to customer, awaiting payment. | After send action. |
| `Viewed` | Recipient opened it. | Only from a spreadsheet import. |
| `Partial` | Customer paid some, owes more. | `0 < AmountPaid < Total`. |
| `Paid` | `AmountPaid >= Total`. | First payment that closes the balance. |
| `Overdue` | Past `DueDate`, and not Paid, Refunded, Cancelled or paid in full. | Derived (`Invoice.IsOverdue`), not set by the app. |
| `Cancelled` | Invoice voided. | Only from a spreadsheet import. |
| `PartiallyRefunded` | Paid, then refunded less than `Total`, or refunded in full and paid again. | Refund status rule below. |
| `Refunded` | Paid, then refunded in full with no later payment. | Refund status rule below. |

`InvoiceTotalsService.RecalculateStatus` sets Paid, Partial, PartiallyRefunded and Refunded. It leaves any other status alone until a payment or refund arrives, and saves it in `Invoice.StatusBeforePayment`; when every payment is removed again (deleted, undone, or moved to another invoice), the invoice goes back to it, so it is outstanding again and can go overdue. An invoice that reached a payment status some other way (an import marked Paid) has nothing to go back to and keeps its own.

**Refund status rule** (`InvoiceTotalsService.RefundedStatus`). Refunds under `Total` give `PartiallyRefunded`. Refunds of at least `Total` give `Refunded`, unless net paid (`AmountPaid − AmountRefunded`) is still at least `Total` because the customer paid again, which gives `PartiallyRefunded`. A processing fee the customer paid isn't refunded, so a $100 invoice paid with a $3 fee and refunded $100 is `Refunded`.

Screens and report tables show `InvoiceTotalsService.DisplayStatus`: Overdue when `IsOverdue`, otherwise the refund status rule worked out afresh when there are refunds, otherwise the stored status. Never print the stored `Status` alone.

---

## 7. Revenue `PaymentStatus`: the cash-basis filter

`Revenue.PaymentStatus` is the `RevenuePaymentStatus` enum. When a file loads, a blank or unknown value becomes `Paid`.

| Enum value | Treated as collected? |
|---|---|
| `Paid` | ✅ Yes (default for new rows) |
| `Complete` | ✅ Yes (legacy alias from older imports) |
| `Partial` | ❌ No |
| `Pending` | ❌ No |
| `Unpaid` | ❌ No |
| `Overdue` | ❌ No |

Use `RevenueAggregator.IsCollected(revenue)` everywhere, never the enum comparison inline. The spreadsheet importer's `NormalizePaymentStatus` maps free-form text to the enum.

**Revenue created from an invoice** counts as collected once the invoice is paid in full, and stays collected after a refund, because the refund is subtracted on its own date (§8). `InvoiceTotalsService.SyncLinkedRevenueStatus` sets it whenever a payment is recorded or removed, by hand or through the payment portal, so both count the same.

---

## 8. Refunds

A refund comes off revenue, profit and tax owed on the day it was issued, not the day of the original payment.

### Effect on revenue (gross, display)

Subtract the refund from gross revenue, less any security deposit it gave back (below):

```
Revenue in period = Σ Revenue.EffectiveTotalUSD (paid-only, in date range)
                  − Σ |Payment.EffectiveAmountUSD| × Payment.RevenueShare for refunds in date range
```

Helper: `RefundAggregator.GetRefundedInDateRangeUSD(payments, start, end)`.

### Effect on profit and tax (pre-tax part)

A refund reverses the tax on what it refunds too, so profit loses only the pre-tax part and tax owed loses the rest:

```
Per refund:   pre-tax part = |Payment.EffectiveAmountUSD| × Payment.RevenueShare
                             × ((Invoice.Total − Invoice.SecurityDeposit − Invoice.TaxAmount) / (Invoice.Total − Invoice.SecurityDeposit))
              tax part     = |Payment.EffectiveAmountUSD| × Payment.RevenueShare − pre-tax part
Fallback:     if the invoice link is missing, or the invoice has no revenue beyond its deposit, the whole revenue part is pre-tax
```

It is not `Subtotal / Total`: `Subtotal` is the line sum before an invoice-level discount and without shipping, fees or a deposit (§4). Helpers: `RefundAggregator.PreTaxShare`, `PreTaxPortionUSD`, `TaxPortionUSD`, `GetRefundedPreTaxInDateRangeUSD`.

### Refunds that give back a deposit

A deposit was never revenue (§4), so the part of a refund that hands it back comes off nothing. Each refund stores that part as `Payment.DepositAmount`, and every refund sum above uses `Payment.RevenueShare`, the rest of the refund as a share of it.

- A refund made in Argo Books names its deposit part: the refund form lists the deposit as its own line, and the payment sync returns that line's amount.
- A refund made in the provider's dashboard doesn't, so it is taken from the deposit first, which is what a refund on a deposit invoice usually is.
- Either way, never more than is still held: the deposit less earlier refunds of it and any kept deposit. Refunding a deposit the business kept comes off revenue, because keeping it made it revenue.

Set when the refund is synced (`PaymentPortalService`), and once for older refunds by the open-time heal. Helper: `SecurityDeposits.RefundPortion`.

---

## 9. Expenses

Expense rows have no paid/unpaid status, so every row counts. Expense figures use `EffectiveTotalUSD` (Rule 1); profit uses `OperatingExpenseUSD` (§14).

---

## 10. Formal reports and other surfaces

`AccountingReportDataService` builds the formal reports (Income Statement, Balance Sheet, Cash Flow, General Ledger, and similar), and `ReportTableDataService` fills the Report Builder's transaction tables. Both intentionally diverge from the dashboard rules:

- The Income Statement, General Ledger and Report Builder revenue and expense tables use `EffectiveSubtotalUSD`, because tax is shown separately as Sales Tax Payable on the Balance Sheet. Cash figures and Sales by Product (§13) use `EffectiveTotalUSD`.
- They include all revenue in range, paid or not (accrual).
- Cash (the Cash Flow Statement and the Balance Sheet's Cash line) is gross: paid revenue with no invoice, plus every payment (refunds count as negatives), less every expense.
- The Balance Sheet lists **Inventory** as a current asset (`InventoryValuationService.TotalValueAsOf`): each item's stock on hand as of the end date, with each stock change counted on its sale's or purchase's date, or on its own date when it has neither, valued at the item's current `UnitCost` in USD because there is no cost history. The Income Statement shows a Cost of Goods Sold line and Gross Profit whenever a sale in range carries a cost (§14).
- The Balance Sheet lists **Security Deposits** as a liability: the deposit on every issued invoice as of the end date, less what refunds dated by then gave back and any deposit kept by then (`SecurityDeposits.StillHeld`).

### Insights tab (`InsightsService`)

The Insights tab (trends, anomalies, forecasts, recommendations) uses collected gross revenue (`IsCollected`, `EffectiveTotalUSD`) and gross expenses (`EffectiveTotalUSD`), and does not subtract refunds (§8). Forecast profit is forecast revenue minus forecast expenses, not the Rule 1 net profit.

**Trend comparisons** use `ComparisonPeriod.For` (§12). The rule reads the preset from `AnalysisDateRange.PresetName`, so the page builds its range with `AnalysisDateRange.FromPreset`; a range built with `AnalysisDateRange.Custom` compares against the same number of days before. A forecast range ("Next Month") is first mapped to this month, quarter or year so far.

**Display currency.** Insights analyses run in USD. Only the amounts shown are converted, into one currency per run chosen the way reports choose it (Rule 3a), leaving out dates after today, which Insights never converts at. Amounts in descriptions, averages included, convert each row at its own date, and the overdue total converts each balance at its invoice's issue date. Forecast cards, ranges and Past Predictions show stored USD figures in the company currency at today's rate (`InsightsPageViewModel.FormatForecastAmount`). The free-tier teaser numbers are illustrative and never converted.

**Forecast accuracy.** A forecast is saved under the future period it covers and checked on the forecast's own basis once that period ends (`ForecastAccuracyService.ValidatePastForecasts`, `RunBacktestAsync`).

### Returns and Losses

Returns (items returned by customers or to suppliers) and Losses (lost or damaged inventory) don't feed revenue, profit or expenses; the refund `Payment` is what comes off revenue and profit (§8). The Financial Impact charts and stat cards sum `Return.RefundAmount` and `LostDamaged.ValueLost`. The Returns page shows `Return.NetRefund` (the refund less any restocking fee).

Neither record stores a USD amount or a currency. The amount is in the currency of the sale or purchase it came from (`Return.OriginalTransactionId`, `LostDamaged.InventoryItemId`), or the company currency when it has none, and converts from that currency at the record's own date (`ReturnLossAmounts.CurrencyOf`, `DisplayCurrency.FromNative`). An amount already in the display currency is used as it is. One whose rate is missing counts as 0 on a chart and shows Pending on a stat card or page total.

### Bank matching

Bank Matching (`BankMatchingService`) only sets `BankMatched` on the rows it matches (revenue, expenses, invoices, payments) and changes no figure. It compares each row's native amount (`Total`, or a payment's `Amount`) with the bank line, not a USD amount.

---

## 11. Quick-reference: where each number comes from

For a new chart or stat card, find its row here and copy a neighbour that is already correct.

| Surface | Revenue field | Expense field | Paid-only filter? | Subtract refunds? |
|---|---|---|---|---|
| Total Revenue card, Revenue Over Time, Revenue Growth (Analytics), Top Customers (chart and dashboard widget) | `EffectiveTotalUSD` | — | Yes | Full amount |
| Total Expenses card, Expenses Over Time | — | `EffectiveTotalUSD` | n/a | n/a |
| Net Profit card, Profit Margin (Analytics), Profit Over Time | `EffectiveSubtotalUSD` | `OperatingExpenseUSD`, plus cost of goods sold (§14) | Yes | Pre-tax portion |
| Revenue vs Expenses | `EffectiveTotalUSD` | `EffectiveTotalUSD` | Yes (revenue side) | Full amount (revenue side) |
| Report Summary box total | Same as the Total Revenue card (Revenue type), the Total Expenses card (Expenses type) or the Net Profit card (any other type) | | | |
| Tax charts and Analytics tax cards | `EffectiveTaxAmountUSD` | `EffectiveTaxAmountUSD` | Yes (revenue side) | Tax part (not by category or product, nor in the Effective Tax Rate) |
| Avg Shipping Cost (Analytics) | `EffectiveShippingCostUSD` | `EffectiveShippingCostUSD` | Yes (revenue side) | n/a |
| Return / Loss Financial Impact | `Return.RefundAmount` (§10) | `LostDamaged.ValueLost` (§10) | n/a | n/a |
| Forecasts, backtests and accuracy checks (Insights) | `EffectiveTotalUSD` | `EffectiveTotalUSD` | Yes (revenue side) | Not applied |
| Geographic / Country charts | `EffectiveTotalUSD` | `EffectiveTotalUSD` | Yes (revenue side) | Not applied |
| Outstanding and Overdue Invoices cards | `BalanceUSD` | — | **No** (by design) | n/a |
| Revenue page list | `EffectiveTotalUSD` | — | **No** (shows all) | n/a |
| Income Statement / GL | `EffectiveSubtotalUSD` | `EffectiveSubtotalUSD` (Income Statement: less tracked stock lines, plus cost of goods sold, §14) | No (accrual) | Pre-tax portion |
| Sales by Product (Analytics tab) | `EffectiveTotalUSD` (allocated per line item) | — | Yes | Not applied (§13) |
| Sales by Product (Report) | `EffectiveTotalUSD` (allocated per line item) | — | No (accrual) | Not applied (§13) |

---

## 12. Implementation pointers

Aggregation helpers in `ArgoBooks.Core/Services/`:

- `RevenueAggregator.SumCollectedRevenueUSD(revenues, start, end)`: gross-of-tax revenue, paid-only, USD.
- `RevenueAggregator.SumCollectedRevenuePreTaxUSD(revenues, start, end)`: pre-tax revenue, paid-only, USD (profit math, not display).
- `ExpenseAggregator.SumExpensesUSD(expenses, start, end)`: gross expenses, USD.
- `RefundAggregator.GroupRefundsByDayUSD(payments, start, end)`: per-day refund map for time-series charts.
- `ProfitCalculator.CalculateNetProfitUSD(data, start, end)` and `CalculateNetProfitByDayUSD`: the Rule 1 net profit, as a total and per day. Every profit surface calls these rather than working profit out again.
- `CostOfGoodsAggregator`: cost of goods sold on sales (`SumCostOfGoodsSoldUSD`, paid-only or not) and expenses without the tracked stock they bought (`SumOperatingExpensesUSD`). `ProfitCalculator` uses both.
- `ComparisonPeriod.For(preset, start, end)`: the period every "vs previous period" figure compares against (dashboard, Analytics, Insights trends, and the report Summary box's growth rate). This month, quarter or year so far compares with the same days of the one before, stopping at its end when it is shorter (This Month on Sep 11 is Aug 1 to Aug 11; This Year on Feb 29 is Jan 1 to Feb 28). Last month, quarter or year compares with the whole calendar period before it. Everything else compares with the same number of days just before.

---

## 13. Sales by product

The Analytics "Products" tab and the Report Builder "Sales by Product" template both break **revenue and units** down per product through `ProductSalesService`; only the basis differs (below).

**The Products tab and the Sales by Product report don't compute per-product profit.** Cost of goods sold exists only for products that track inventory, and reads low while opening stock is still selling (§14), so a per-product margin would mislead.

### Revenue per product (gross, USD)

Each line gets a share of its transaction's gross USD total, weighted by the line's native pre-tax subtotal; tax is shared the same way:

```
lineItemsTotal = Σ over LineItem of li.Subtotal          (native currency, pre-tax)
revenueUSD(li) = lineItemsTotal != 0
               ? round(li.Subtotal / lineItemsTotal × txn.EffectiveTotalUSD, 2)
               : 0
```

Line items with no `ProductId` group under "Unknown". A transaction with no line items is left out.

`unitsSold` = Σ `li.Quantity` over the product's line items; `avgSalePrice` = revenue ÷ unitsSold (0 when unitsSold is 0), rounded to 2 decimals.

### Basis: cash for analytics, accrual for the report

- **Analytics Products tab** (`cashBasis: true`): paid-only (`RevenueAggregator.IsCollected`). Its total reads higher than the Total Revenue card by any refunds (not subtracted, below) and lower by revenue on transactions with no line items.
- **Report Builder template** (`cashBasis: false`): all revenue in range, paid or not, like the Income Statement.

Refunds are invoice-level `Payment` rows (§8) with no line breakdown, so per-product revenue doesn't subtract them. Returns by Product and Losses by Product cover returned and lost stock.

---

## 14. Cost of goods sold

Products with **Track Inventory** turned on count what their stock cost against the sale that used it, not as an expense the day it was bought. Other products are unaffected.

`InventoryStockService` is the only place stock moves for a purchase, a sale, an edit or delete of either, a Stripe or Argo Books API import, or a transfer, and the only place cost of goods sold is fixed on a sale line. `CostOfGoodsAggregator` is the only place the resulting figures are summed.

### Buying tracked stock

A purchase line whose product tracks inventory adds its quantity to stock and is marked `LineItem.IsStockPurchase`. Its pre-tax amount is stock, not an expense:

```
stockPurchaseUSD(expense)    = min( Σ over stock lines of li.Subtotal × (expense.EffectiveTotalUSD / expense.Total),
                                    expense.EffectiveTotalUSD )
operatingExpenseUSD(expense) = expense.EffectiveTotalUSD − stockPurchaseUSD(expense)
```

Tax, shipping and fees on the purchase stay expenses. The stock record's `UnitCost` becomes the line's pre-tax price per unit in USD, at the purchase's own rate (left alone while the rate is pending). When the purchase is in the company currency, the product's `CostPrice` becomes the line's unit price.

### Selling tracked stock

A sale line whose product tracks inventory takes its quantity out of stock and fixes its cost on the line, in USD:

```
openingUsed(li)   = min(item.OpeningUnits, li.Quantity)
li.CostOfGoodsUSD = (li.Quantity − openingUsed(li)) × item.UnitCost
```

The cost is saved, not recalculated: editing a sale keeps the unit cost its lines were saved at for each product and location. A product or location the edit adds, or one whose saved lines all came from opening units, takes the current `UnitCost`. A sale pending currency conversion counts no cost, as it counts no revenue.

### Profit

Net profit is Rule 1's formula. The dashboard counts paid sales for both revenue and cost of goods sold (Rule 2). The Income Statement counts every sale in range, shows **Cost of Goods Sold** and **Gross Profit** under revenue whenever that cost is not zero, and leaves tracked stock lines out of its expense categories. The Expenses card, the Expenses page and cash flow still count every purchase in full.

### Stock on hand when this began (opening units)

Stock on hand when cost of goods sold began was already expensed, so it must not come off profit again when it sells. The first time a company opens with cost of goods sold, `CompanyManager.StartCostOfGoodsIfNeeded` records each stock record's units on hand as `InventoryItem.OpeningUnits`. Sales use opening units first, at no cost (`LineItem.OpeningUnitsUsed`), and editing or deleting a sale gives them back. A transfer moves opening units in proportion to the stock it moves. Editing a purchase or sale saved before cost of goods sold began keeps its old treatment: it moves stock, stays a full expense or carries no cost, and uses no opening units.

### Locations

A line takes stock from, or adds it to, the stock record at `LineItem.LocationId`. When the line names none, or the product has no stock record there, the product's first stock record is used; when the product has none at all, a new one is made at the line's location, or the company's first location. Editing or deleting a transaction gives back what its own stock adjustments show it moved, to the records it moved it from, so a transaction saved before its product tracked stock, or brought in by the spreadsheet import, has nothing to take back.

### Not matched

- **Returns** record the return only (§10). They don't restock or take back the sale's cost of goods sold.
- **Receipt scans and revenue created from invoices** move no stock.

---

## 15. Rentals

`RentalBookings` holds the rental math. A rental has one or more lines, each with an item, a quantity, a rate type and a per-unit deposit. A record saved by the old Rent Out action has no lines and keeps one on the record itself, where the deposit is the total; `RentalRecord.EffectiveLineItems()` turns it into a line.

### Charges

```
days            = max(1, returnDate.Date − startDate.Date)
line (Daily)    = rate × days × quantity
line (Weekly)   = rate × ceil(days / 7) × quantity
line (Monthly)  = rate × ceil(days / 30) × quantity
TotalCost       = Σ lines + ExtraCharges
```

Estimates run to the due date. An invoice made from a rental has one line per item, charged to the return date, or to the due date while the rental is still out, plus a line for any extra charges.

### Deposits and extra charges

The rental's `SecurityDeposit` is Σ per-unit deposit × quantity. At return, any part can be refunded (`DepositRefunded`) and the rest is kept. A kept deposit becomes revenue on the return date: on an invoiced rental, as its own revenue row (`Revenue.IsKeptDeposit`) linked to the invoice at the invoice's rate (Rule 3a); on a rental with no invoice, as part of the revenue recorded when it is marked paid (below). When the invoice was paid online, the deposit refund goes back through the provider, and the balance sheet stops holding the deposit once that refund syncs. Extra charges, such as a late fee or damage, are billed on top of the rental and don't come out of the deposit.

### Paid without an invoice

Marking a rental paid when it has no invoice records a revenue row for `TotalCost` plus any kept deposit, in the company currency and converted at its own date (Rule 3a). It is dated on the return when marked paid there, otherwise on the day it was marked. `RentalRecord.RevenueId` links it, so marking it unpaid or deleting the rental removes it. A rental with an invoice counts through the invoice instead, and a paid rental can't be invoiced.

### Stock and reservations

A rental starting after today is **Reserved** and leaves stock alone. Checking it out makes it Active and takes its units out of stock, and returning it puts them back. Before a rental is saved or checked out, each item needs enough units free on every day it covers:

```
owned  = InStock + units out on active and overdue rentals
booked = most units other rentals take on any one day of this rental's dates
free   = owned − booked
```

A reservation takes its start to due dates. A rental that is out takes its dates and, once late, every day up to today. A rental that takes stock now also can't take more than is in stock, plus what it already has out when it is being edited.
