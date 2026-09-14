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
| **Payment** | A row representing a single money movement, linked to either an invoice or a revenue. Positive = money in. Negative + `IsRefund=true` = money out. |
| **Refund** | A `Payment` row with `IsRefund=true` and a negative amount, tied to the invoice (and usually the original payment) it offsets. |
| **Subtotal** | Pre-tax amount. What stayed in the business if we set tax aside. |
| **Tax amount** | The sales tax portion. Money collected on behalf of the government, not the business. |
| **Total** | Subtotal + tax + fees + shipping − discount. The grand total the customer was charged or the business paid. |

---

## 2. The two foundational rules

These are the two rules that resolve almost every disagreement between surfaces of the app.

### Rule 1: Revenue display is **gross** (includes tax); profit is **pre-tax**.

Tax we collect from customers is money we owe the government, not money we keep. So:

- **"Total Revenue" stat card / charts / customer billings** → use `EffectiveTotalUSD` (gross, includes tax). This is what the user typed on the invoice and matches the Revenue page.
- **"Net Profit" / "Profit Margin" / "Profit Over Time"** → use `EffectiveSubtotalUSD` (pre-tax) on the revenue side. This excludes the tax we owe out.
- **Expenses** always use `EffectiveTotalUSD` (gross). Tax we paid suppliers is real cash out the door, not a separate liability we can ignore.
- **Tracked stock** is the exception, on the profit side only. What a purchase spent on products that track inventory is stock, not an expense, and comes off profit as cost of goods sold when it sells (§14). The Expenses card and page still show every dollar spent.

```
Total Revenue (display)  = Σ Revenue.EffectiveTotalUSD
Net Profit               = Σ Revenue.EffectiveSubtotalUSD − Σ OperatingExpenseUSD(expense) − Σ CostOfGoodsSoldUSD(revenue) − Refunds(pre-tax)
Profit Margin            = Net Profit / Σ Revenue.EffectiveSubtotalUSD
Tax Owed to Government   = Σ Revenue.EffectiveTaxAmountUSD     (collected) − tax paid on expenses (if user is tracking it)
```

### Rule 2: Dashboard figures are **cash-basis** (paid-only).

The dashboard and analytics charts answer "how is the business actually doing", not "how much have we invoiced." Unpaid invoices are promises, not money.

- Every revenue aggregation on the dashboard or in `ReportChartDataService` must filter by `RevenueAggregator.IsCollected` first. That keeps only rows whose `PaymentStatus` is `Paid` or `Complete`.
- Partial / Pending / Unpaid / Overdue revenues are **excluded** from dashboard sums.
- Outstanding-balance widgets (Outstanding Invoices, Overdue Invoices) are the exception: their whole purpose is to show what hasn't been paid.

Exceptions to Rule 2, surfaces where unpaid revenue legitimately counts:
- The Revenue page list view (it lists all revenues regardless of status).
- "Customer Growth" and "Active vs Inactive Customers", these count customer engagement, not money.
- Formal accounting reports (Income Statement, Balance Sheet), see §10.

### Rule 3: Aggregate in **USD**, display in the **user's currency**.

Every aggregation in `ArgoBooks.Core` is USD-normalized so multi-currency companies roll up correctly. But every number the user sees (stat cards, chart bars, chart titles, chart axes, tooltips, exports) must be in the **display currency** they chose in settings.

Conversion happens at the **presentation boundary**, not at the data layer:

- **Stat cards** call `CurrencyService.FormatFromUSD(usdAmount, date)` as the final step. Aggregations stay USD; only the display string converts.
- **Cartesian charts (LiveCharts):** `ChartLoaderService.CreateDateTimeSeries` and `CreateProfitDateTimeSeries` auto-convert USD → display currency before handing values to LiveCharts (controlled by the `convertFromUSD` parameter, default `true`). Count-based callers pass `false`.
- **Pie charts:** `ChartLoaderService.CreatePieSeriesWithLegend` follows the same pattern; tooltips and legend `Value` are in display currency.
- **Chart titles** sum the USD values and call `FormatFromUSD` on the sum.
- **Y-axis labels and tooltips** receive display-currency values, so the default `{currencySymbol}{value:N2}` labeler is correct as-is.
- **Chart exports** (`ChartExportData.Values`) are stored in display currency so spreadsheet / PDF exports match what the chart shows.

Symptoms of breaking this rule: stat card shows `$91`, chart bar shows `$66.46`, chart title shows `$81`. All three are derived from the same underlying revenue row, they only disagree when display conversion is applied unevenly.

The helper for any new chart surface: `ChartLoaderService.ConvertUSDValuesToDisplay(double[], DateTime[])`, which converts each value at its own bucket date (Calculations.md §3a).

### Rule 3a: Always convert at the transaction's EXACT-date rate.

Every monetary conversion uses the exchange rate for the transaction's own date, with USD as the
base, fetched from the Argo server (`/api/exchange-rates*.php`, which returns USD -> all per date).
The app never substitutes a different date's rate or shows a raw cross-currency amount.

- The single chokepoint is `ExchangeRateService.TryConvertExact`; it succeeds only on an exact-date
  cache hit (or same-currency). There is no "nearest cached rate" fallback for money.
- **Storage vs display precision.** `TryConvertExact` rounds to the target currency's decimal places
  (two for most, none for yen) because it produces a number the user sees, and a total of converted
  rows has to equal the rows as shown. The stored USD base is different: every `*USD` field is
  written at FULL precision (never rounded to cents) via `ExchangeRateService.TryConvertToUsdBase` (cache-only) and
  `ConvertToUSDAsync` (the async manual-entry path). USD is the aggregation currency; rounding the
  base to cents made a same-currency round-trip (native -> USD base -> native) drift by a cent, so a
  $10 CAD expense read $9.99 on a chart that re-derives its value from the USD base while the stat
  card (which short-circuits to the original amount, §3) still read $10.00. Only the presentation
  boundary rounds; the base does not. This is why import and the pending self-heal must both use the
  unrounded path, so an immediately-converted row and a later-healed row store identical USD.
- **Import** runs `RateReadinessService.EnsureRatesAsync` first and PAUSES with a connect-and-retry
  prompt (offline vs server-unreachable) until the needed rates are cached. Any row still
  unpriceable (future-dated, or a date the pre-scan missed) imports as `IsPendingConversion` and is
  enqueued, so it converts automatically later instead of using a wrong-date rate. The gate is a
  best-effort optimization; the pending self-heal is the guarantee.
- **Manual add/edit** saves a row as `IsPendingConversion` when its exact-date rate is unavailable;
  it converts automatically later (`PendingConversionService`) once that exact rate is fetchable.
- **Online payments** take their invoice's rate, as its refunds do. A payment or refund on an
  invoice still waiting for its rate waits too, queued at the invoice's date, so it converts at the
  rate the invoice does and a paid invoice owes nothing in USD either.
- **Future-dated rows** have no rate (the future is unpriced) and stay pending until their date
  arrives.
- **Display** shows `CurrencyService.PendingMarker` instead of a number when a row's exact-date
  rate is unavailable, never a wrong-date figure.

Phase 2 extends this to aggregates: each transaction is converted at its own date before summing,
so totals match the sum of the displayed rows to the cent (supersedes the "convert the USD sum at
one date" step in Rule 3 for non-USD display currencies). A card or total on screen shows Pending
while any row it converts lacks its rate (`CurrencyService.FormatTotalOrPending`,
`FormatSumDisplayFromUSD`). A report or an Insights run picks one currency for the whole document
with `DisplayCurrency.Resolve`: the company currency when every date it converts at has an exact
rate, otherwise USD throughout, so a printed report never mixes currencies.

---

## 3. Effective USD properties

Every aggregation across multi-currency data uses the `Effective*USD` properties on `Transaction`, `Invoice`, and `Payment`. Do not sum native fields (`Total`, `Amount`, `TaxAmount`) directly, they're in the original currency and silently mix dollars and euros.

| Property | Meaning |
|---|---|
| `EffectiveTotalUSD` | Gross-of-tax USD. Use for revenue display & expense sums. |
| `EffectiveSubtotalUSD` | Pre-tax USD. Use for profit revenue side. (= Total − Tax) |
| `EffectiveTaxAmountUSD` | Tax portion USD. Use for sales-tax-owed reporting. |
| `EffectiveShippingCostUSD` | Shipping USD. Already inside Total. |
| `EffectiveAmountUSD` (`Payment`) | Single payment/refund USD. Negative for refunds. |

`IsPendingConversion = true` means the row was saved offline and never got a USD conversion. All `Effective*USD` properties return `0` in that state to prevent cross-currency contamination, those rows are invisible to aggregations until the user re-enters them with a rate.

---

## 4. Invoice math (per-invoice)

How an invoice's grand total is built from its parts. The Invoice model stores the final numbers; this is how they're derived.

```
0. LineSubtotal       = max(0, Quantity × UnitPrice − Discount), rounded to 2 dp   (LineItem.Subtotal)
1. Subtotal           = Σ over LineItem of LineSubtotal                            (the stored/displayed Subtotal)
2. invoiceDiscount    = min(Subtotal, DiscountIsPercent ? Subtotal × DiscountAmount/100 : DiscountAmount)
3. invoiceCustomFee   = CustomFeeIsPercent ? Subtotal × CustomFeeAmount/100 : CustomFeeAmount
4. TaxableBase        = Subtotal − invoiceDiscount + invoiceCustomFee + ShippingAmount
5. TaxAmount          = TaxableBase × TaxRate
6. Total              = TaxableBase + TaxAmount + SecurityDeposit
                      = Subtotal − invoiceDiscount + invoiceCustomFee + ShippingAmount + TaxAmount + SecurityDeposit
```

`InvoiceMath` implements steps 1 to 6 and is the only copy of them. The invoice form, the live preview, the saved invoice and the rendered HTML all call it, so they cannot drift apart.

Notes:
- **`Subtotal` is the line-items sum.** It is stored on the invoice and shown as the "Subtotal" line, and it is the base a *percentage* discount or fee is taken from. The invoice-level discount and custom fee are shown as their own separate lines, not folded into `Subtotal`.
- **Discount on a line item** reduces only that line's subtotal, and never below zero: a discount bigger than the line it sits on makes that line free, it does not come off the rest of the invoice.
- **No figure on an invoice can be negative.** The invoice-level discount is worth at most the `Subtotal` it comes off (a 150 discount on a 100 subtotal is a 100 discount), and the taxable base floors at zero, so an invoice can come to nothing but can never owe the customer money. Shipping is still payable when a discount wipes out the goods, because the discount caps at the subtotal rather than at the whole invoice.
- **Invoice-level discount**, **custom fee**, and **shipping** adjust the *taxable base* before tax (industry standard: tax is charged on the net amount after discount, and a taxable fee and shipping are taxed). The discount lowers it; the fee and shipping raise it.
- **Security deposit** is added to the total but is *not* taxed and is *not* revenue: it's a refundable hold against damages. The revenue created from the invoice leaves it out (`Total − SecurityDeposit`). If a rental comes back and the business keeps the deposit, the return adds it as its own revenue row (`Revenue.IsKeptDeposit`), dated on the return, linked to the invoice, and priced at the invoice's rate as the invoice's refunds are. A rental with no invoice has no deposit in the books, so there is nothing to move. How refunds treat the deposit is in §8. Helper: `SecurityDeposits`.
- **Tax** is applied to `TaxableBase` (Subtotal − discount + fee + shipping), not to the raw `Subtotal`. Argo Books treats tax as a single flat rate; we do not support different tax rates per line item at the invoice-roll-up level (line items each carry a `TaxRate` but the invoice header rate is what's stored as the final tax).

---

## 5. Payment math (per-invoice running totals)

These four fields on `Invoice` are kept in sync from the `Payment` rows attached to that invoice. `InvoiceTotalsService` owns this calculation, anything that mutates an invoice's payment list must call `InvoiceTotalsService.Recalculate(invoice, allPayments)` after the change.

A payment can instead be linked to a revenue (`Payment.RevenueId`) rather than an invoice, used for direct cash sales that have no invoice. Revenue-linked payments do not feed invoice totals (there is no invoice) and are not summed into revenue or profit (income is counted from the Revenue rows themselves, per §2). They are informational records of the money movement.

| Field | Formula | Notes |
|---|---|---|
| `AmountPaid` | Σ over positive (non-refund) Payments of `Amount` (in invoice's currency) | Gross payments received. Does not decrease on refund. |
| `AmountRefunded` | Σ over refund Payments of `|Amount|` (in invoice's currency) | Always ≥ 0. |
| `Balance` | `max(0, Total − AmountPaid)` | What the customer still owes. Refunds do not raise the balance, the customer paid; they don't owe again because we returned money. |
| `BalanceUSD` | `max(0, TotalUSD − sumOfPaymentsUSD)` | USD-normalized balance for cross-currency aggregation. |
| `NetPaid` *(computed)* | `AmountPaid − AmountRefunded` | Cash kept from this invoice. Used in revenue/profit aggregation. |

`InvoiceTotalsService.RecalculateStatus` separately recomputes the stored `Status` from `AmountPaid` / `AmountRefunded`, flipping between Paid / Partial / PartiallyRefunded / Refunded as appropriate. Lifecycle states (Draft / Pending / Sent / Viewed / Cancelled / Overdue) are owned by the surfaces that drive them. This service leaves one only when a payment or refund arrives, and remembers it in `Invoice.StatusBeforePayment`. When every payment is removed again (deleted, undone, or moved to another invoice), the invoice goes back to that status, so it is outstanding again and can go overdue. An invoice that reached a payment status some other way (an import marked Paid) has no earlier status to return to and keeps its own.

Payments are recorded on invoices that have been sent. The payment form does not offer drafts, because an invoice's revenue is only created when it is sent (§7); a draft's existing payment stays linked when edited.

A one-time recalc pass runs on `CompanyData` load (`CompanyManager.OpenCompanyAsync`) to heal any historic drift, scoped to invoices that actually have Payment rows, invoices imported from spreadsheets without payments keep the stored `AmountPaid` value the import gave them.

### `Payment.Amount` is the gross money movement and never changes after creation

A `Payment` row records a single money movement: a positive `Amount` when money came in, a negative `Amount` (with `IsRefund=true`) when money went out. Once written, neither field is mutated, a refund is a **separate row**, not a downward edit of the original payment.

This matters for any surface that shows individual payments:

- The **Payments page list** displays `Payment.Amount` (the original gross, including any processing fee the customer absorbed). It does **not** show a net "remaining" figure. Refund context is conveyed by the **Status** column flipping to "Refunded" / "Partially Refunded". Refund rows themselves are hidden from the list so each transaction appears once.
- Net-of-refund figures are still available where they're needed: `Invoice.NetPaid`, `RefundAggregator.GetRefundedForPayment(...)`, and the dashboard stats subtract refunds from gross at aggregation time. But the per-row Amount column always shows the gross.

If you find code that subtracts refunds from a `Payment.Amount` before displaying it, that's a bug, netting belongs in aggregations, not in row data.

---

## 6. Invoice status: what each one means and when it applies

| Status | Meaning | How it's set |
|---|---|---|
| `Draft` | Being prepared; never sent. | User saves without sending. |
| `Pending` | Ready but not sent yet. | User explicitly chose to hold off. |
| `Sent` | Sent to customer, awaiting payment. | After send action. |
| `Viewed` | Recipient opened it. | Portal tracking signal. |
| `Partial` | Customer paid some, owes more. | `0 < AmountPaid < Total`. |
| `Paid` | `AmountPaid >= Total`. | First positive payment that closes the balance. |
| `Overdue` | Today is past `DueDate` and something is still owed: not Paid, Refunded, Cancelled, or paid in full and then partly refunded. | Derived (`IsOverdue` getter), not a stored transition. |
| `Cancelled` | Invoice voided. | Explicit user action. |
| `PartiallyRefunded` | Was paid, then some (but not all) was refunded, or was fully refunded and then paid again. | See refund rule below. |
| `Refunded` | Was paid, then fully refunded with no subsequent payment. | See refund rule below. |

**Refund status rule.** `AmountRefunded vs Total` alone isn't enough, because two scenarios both produce `AmountPaid > Total`:

1. *Single payment + processing fee, fully refunded.* AmountPaid = \$103 (\$100 invoice + \$3 fee customer absorbed), AmountRefunded = \$100 (refunds don't include the fee). Status should be `Refunded`.
2. *Pay → refund → pay again.* AmountPaid = \$200, AmountRefunded = \$100. Status should be `PartiallyRefunded` so the refund history stays visible alongside the new payment.

The discriminator is **net paid** (`AmountPaid − AmountRefunded`). If it is less than one full invoice value (`Total`), the gap is fee residue, status is `Refunded`. If it is at least one full invoice value, the customer paid the invoice over again on top of the refund, status is `PartiallyRefunded`.

Owned by `InvoiceTotalsService.RecalculateStatus`.

Heuristics:
- `Overdue` is a *display state*, not a write. Always compute it with `Invoice.IsOverdue` (past `DueDate`, not Paid / Refunded / Cancelled, and not paid in full) rather than storing it. A partly paid invoice that was then partly refunded still owes its balance, so it can be overdue.
- Refund statuses (`PartiallyRefunded` / `Refunded`) supersede `Paid` as soon as a refund row exists for the invoice. The display layer self-heals with the same net-paid rule (`InvoiceTotalsService.RefundedStatus`) rather than relying on the stored status, if the stored status disagrees, the computed one wins.
- `InvoiceTotalsService.DisplayStatus` applies both of the above and is what report tables print, the same status the invoices list shows. Never print the stored `Status` alone.

---

## 7. Revenue `PaymentStatus`: the cash-basis filter

`Revenue.PaymentStatus` is the `RevenuePaymentStatus` enum (`ArgoBooks.Core.Enums.RevenuePaymentStatus`). A permissive `JsonConverter` handles legacy `.argo` files: typos, alternate spellings ("settled", "received"), and empty strings all deserialize to `Paid`.

| Enum value | Treated as collected? |
|---|---|
| `Paid` | ✅ Yes (default for new rows) |
| `Complete` | ✅ Yes (legacy alias from older imports) |
| `Partial` | ❌ No |
| `Pending` | ❌ No |
| `Unpaid` | ❌ No |
| `Overdue` | ❌ No |

Use `RevenueAggregator.IsCollected(revenue)` everywhere, never inline the enum comparison. If a new status should count as collected, add it to that helper in one place. The spreadsheet importer's `NormalizePaymentStatus` maps free-form text to the enum and is the only place that should parse strings.

**Revenue created from an invoice** counts as collected once the invoice is paid in full, and stays collected after a refund, because the refund is subtracted on its own date (§8). `InvoiceTotalsService.SyncLinkedRevenueStatus` sets it whenever a payment is recorded or removed, by hand or through the payment portal, so both count the same.

`Payment.Source` is similarly the `PaymentSource` enum (`Manual` / `Online`), also with a permissive JSON converter for legacy data.

---

## 8. Refunds

A refund is a `Payment` row with `IsRefund = true` and a negative `Amount`. It is dated on the day the refund was issued, not the day of the original payment, this matters for cash-basis charts.

### Effect on revenue (gross, display)

Subtract the refund from gross revenue, less any security deposit it gave back (see *Refunds that give back a deposit* below):

```
Revenue in period = Σ Revenue.EffectiveTotalUSD (paid-only, in date range)
                  − Σ |Payment.EffectiveAmountUSD| × Payment.RevenueShare for refunds in date range
```

Helper: `RefundAggregator.GetRefundedInDateRangeUSD(...)`.

### Effect on profit (pre-tax)

Subtract the **pre-tax portion** of the refund. Because a refund both reverses revenue *and* reverses the tax we owed on that revenue, the net profit impact is only the pre-tax portion:

```
Per refund:   profit reduction = |Payment.EffectiveAmountUSD| × Payment.RevenueShare
                                 × ((Invoice.Total − Invoice.SecurityDeposit − Invoice.TaxAmount) / (Invoice.Total − Invoice.SecurityDeposit))
Fallback:     if invoice link missing → the refund's revenue part in full
```

The share is total less deposit and tax, over total less deposit, which is what the invoice's revenue counted before tax. It is not `Subtotal / Total`: `Subtotal` is the line sum before an invoice-level discount and without shipping, fees or a deposit (§4), so it disagrees whenever an invoice has any of those. Helper: `RefundAggregator.PreTaxShare`.

### Refunds that give back a deposit

A deposit was never revenue (§4), so the part of a refund that hands it back comes off nothing. Each refund stores that part as `Payment.DepositAmount`, and every refund sum above uses `Payment.RevenueShare`, the rest of the refund as a share of it.

- A refund made in Argo Books names its deposit part: the refund form lists the deposit as its own line, and the payment sync returns that line's amount.
- A refund made in the provider's dashboard doesn't, so it is taken from the deposit first, which is what a refund on a deposit invoice usually is.
- Either way, never more than is still held: the deposit less earlier refunds of it and any kept deposit. Refunding a deposit the business kept comes off revenue, because keeping it made it revenue.

Set when the refund is synced (`PaymentPortalService`), and once for older refunds by the open-time heal. Helper: `SecurityDeposits.RefundPortion`.

Helper: `RefundAggregator.GetRefundedPreTaxInDateRangeUSD(payments, invoicesById, start, end)`. `ProfitCalculator` already calls this; new profit surfaces should reuse `ProfitCalculator.CalculateNetProfitUSD` or `CalculateNetProfitByDayUSD` rather than re-deriving the formula.

Worked example:
- Invoice: \$86.91 subtotal + \$32.09 tax = \$119 total.
- Customer pays \$119; profit picks up \$86.91; we owe gov \$32.09.
- Full refund of \$119 issued.
- Profit reduces by \$86.91 (not \$119), the \$32.09 tax was never ours.
- Tax owed simultaneously drops by \$32.09 (we owe nothing on a refunded transaction). Net profit impact on this transaction: \$0. Correct.

### Effect on invoice status

See §6. The status flips to `PartiallyRefunded` or `Refunded` by the refund status rule there (net paid vs `Total`).

---

## 9. Expenses

Expenses are simpler than revenue because there's no "paid vs unpaid" distinction, an expense row means money already went out.

- All expense aggregations use `EffectiveTotalUSD`. The full amount paid to the supplier (including any tax paid) is the expense.
- The "Expenses" stat card, "Expenses Over Time" chart, "Revenue vs Expenses" chart, all use the same Total figure.
- Profit is the exception: it does not subtract a purchase's tracked stock lines. `CostOfGoodsAggregator.OperatingExpenseUSD` is the expense less those lines' pre-tax amount at the expense's own rate; tax, shipping and fees stay expenses. See §14.
- Sales tax paid on expenses *can* in principle be deducted from sales tax collected, but Argo Books does **not** net these automatically. The user reports total tax collected; tax deductions are a manual / advisor conversation, in keeping with the "simple bookkeeping, not accounting" philosophy.

---

## 10. Where these rules don't apply: formal accounting reports

`AccountingReportDataService` and `ReportTableDataService` power the Income Statement, Balance Sheet, General Ledger, and similar formal reports. They intentionally diverge from the dashboard rules:

- They use `EffectiveSubtotalUSD` for revenue **and** expense, because formal reports break tax out as a separate liability line.
- They include all invoiced revenue, not just collected, formal reports typically follow accrual basis. (If the user toggles cash-basis on a report, the report layer applies the paid-only filter itself.)
- `GetCashFlowData` is the exception inside this service: it correctly uses paid-only revenue plus all Payment cash events plus all Expense rows (which are themselves cash-out events, there is no `PaymentStatus` on expenses).
- The Balance Sheet lists **Inventory** as a current asset. Its value is computed by `InventoryValuationService.TotalValueAsOf`, which reconstructs each item's stock-on-hand as of the report end date by rolling back, from the current `InStock`, the signed quantity delta of every `StockAdjustment` whose effective date is after that date. An adjustment's effective date is its linked transaction's `Date` when `ReferenceNumber` resolves to a Revenue/Expense, otherwise its own `Timestamp`, read as the local day it was made on (timestamps are stored in UTC, and the report date is a local day). Quantity is reconstructed historically; **cost is not** (the app keeps no per-date cost history), so value uses each item's current `UnitCost`, treated as USD-equivalent. Profit on the Income Statement and the dashboard matches tracked stock to the sale that used it, as cost of goods sold (§14): buying tracked stock is not an expense, and the Income Statement shows a Cost of Goods Sold line and Gross Profit whenever a sale in range carries a cost. Stock bought before cost of goods sold began was expensed when bought and stays that way.
- The Balance Sheet lists **Security Deposits** as a liability: the deposit on every issued invoice as of the report end date, less what refunds dated by then gave back and any deposit kept by then (`SecurityDeposits.StillHeld`). The invoice total carrying a deposit sits in cash or receivables from the day it is issued, and a deposit is not earned (§4), so without this line it would land in Retained Earnings.

If you find yourself touching these services, do not "fix" them to match the dashboard, they are correct for their context.

### Insights tab (`InsightsService`)

The Insights tab (trends, anomalies, forecasts, recommendations) follows the **dashboard** rules: `IsCollected` filter + `EffectiveTotalUSD` for revenue figures shown in descriptions, `EffectiveTotalUSD` for expenses. Forecast inputs use the same cash-basis signal so projections match the Revenue stat card. Forecast profit is computed as `forecasted gross revenue − forecasted gross expenses`, an approximation of net profit; the exact net profit formula (§2 Rule 1) is not currently applied to forecasts because doing so would require two parallel revenue series (gross for display, pre-tax for profit).

**Trend comparisons.** Trends compare the chosen period with the one `ComparisonPeriod.For` gives (§12), the same rule as the dashboard, so Last Month is measured against the whole calendar month before it and Last 3 Months against the same number of days just before. The rule reads the preset from `AnalysisDateRange.PresetName`, so the page builds its range with `AnalysisDateRange.FromPreset`; a range made with `AnalysisDateRange.Custom` compares against the same number of days before. A forecast range ("Next Month") is first mapped to this month, quarter or year so far.

**Display currency.** The analytics above run entirely in USD (percentages, z-scores, and forecasting are currency-invariant). Only the amounts shown to the user are converted to the company display currency, display-only: `InsightsService` resolves one currency per run (`ResolveDisplayCode`), the company currency when an exact-date USD→currency rate exists for every conversion date, otherwise "USD" for the whole run (the same all-or-nothing rule reports use, §3a). Description amounts convert each contributing transaction at its own date (`SumDisplay`, §3a Phase 2); statistical means, which have no single date, convert at today's rate. The forecast cards/ranges are "as of now" projections, so `InsightsPageViewModel` converts them at today's rate via `CurrencyService.FormatFromUSD` (warmed with `TryWarmTodayRateAsync`). The free-tier teaser numbers are illustrative and shown with the symbol only, never converted.

**Forecast accuracy.** A live forecast is saved under the period it forecasts: the future range picked on the page ("Next Month" on Sep 11 is Oct 1 to Oct 31). Checking it once that period ends (`ForecastAccuracyService.ValidatePastForecasts`) and the backtests (`RunBacktestAsync`) measure revenue and expenses on the same basis as the forecast itself, gross collected revenue and gross expenses, so every accuracy score compares like with like. Past Predictions shows the stored USD figures the way the forecast cards do, in the display currency at today's rate.

### Returns and Losses

Returns (customer-returned items) and Losses (lost / damaged inventory) have their own charts but do **not** participate in the revenue / profit / expense pipeline directly. They surface in dedicated "Return Financial Impact" / "Loss Financial Impact" charts and stat cards that sum `Return.RefundAmount` and `LostDamaged.ValueLost` respectively. The recorded *refund* (a Payment row) is what flows through the revenue / profit subtraction defined in §8.

Neither record stores a USD amount or a currency of its own. The amount is in the currency of the sale or purchase it came from (`Return.OriginalTransactionId`, `LostDamaged.InventoryItemId`), or the company currency when it has none, and each one converts from that currency to the display currency at the record's own date (`ReturnLossAmounts.CurrencyOf`, `CurrencyService.GetDisplayAmountFromNative`, §3a). An amount already in the display currency is used as it is. One whose rate is missing counts as 0 on a chart and shows Pending on a stat card.

### Bank matching

Bank Matching (`BankMatchingService`) is a non-financial reference layer: it imports bank statement lines and links them to existing Revenue and Expense rows to help the user verify their books. It only sets a "matched" flag (`BankMatched`) on records and never feeds revenue / expense / profit / tax aggregation, so none of the rules above are affected. It matches against Revenue and Expenses only, invoices and payments are excluded because invoiced sales are already represented by their Revenue row (§5). Matching compares the gross transacted amount (the actual cash movement), not a USD-normalized aggregate.

---

## 11. Quick-reference: where each number comes from

| Surface | Revenue field | Expense field | Paid-only filter? | Subtract refunds? |
|---|---|---|---|---|
| Total Revenue stat card | `EffectiveTotalUSD` | — | Yes | Full amount |
| Total Expenses stat card | — | `EffectiveTotalUSD` | n/a | n/a |
| Net Profit stat card | `EffectiveSubtotalUSD` | `OperatingExpenseUSD`, plus cost of goods sold (§14) | Yes | Pre-tax portion |
| Profit Margin (Analytics) | `EffectiveSubtotalUSD` | `OperatingExpenseUSD`, plus cost of goods sold (§14) | Yes | Pre-tax portion |
| Revenue Over Time | `EffectiveTotalUSD` | — | Yes | Full amount |
| Expenses Over Time | — | `EffectiveTotalUSD` | n/a | n/a |
| Profit Over Time | `EffectiveSubtotalUSD` | `OperatingExpenseUSD`, plus cost of goods sold (§14) | Yes | Pre-tax portion |
| Revenue vs Expenses | `EffectiveTotalUSD` | `EffectiveTotalUSD` | Yes (revenue side) | Full amount (revenue side) |
| Report Summary box | `EffectiveTotalUSD` (Revenue); Net Profit card rule for any other type | `EffectiveTotalUSD` | Yes (revenue side) | Full amount (revenue side) |
| Top Customers by Revenue | `EffectiveTotalUSD` | — | Yes | Full amount |
| Customer Lifetime Value | `EffectiveTotalUSD` | — | Yes | Full amount |
| Revenue Growth (Analytics) | `EffectiveTotalUSD` | — | Yes | Full amount |
| Avg Shipping Cost (Analytics) | `EffectiveShippingCostUSD` | `EffectiveShippingCostUSD` | Yes (revenue side) | n/a |
| Return / Loss Financial Impact | `Return.RefundAmount`, in its sale's currency (§10) | `LostDamaged.ValueLost`, in its sale's or purchase's currency (§10) | n/a | n/a |
| Forecasts, backtests and accuracy checks (Insights) | `EffectiveTotalUSD` | `EffectiveTotalUSD` | Yes (revenue side) | Not applied |
| Geographic / Country charts | `EffectiveTotalUSD` | `EffectiveTotalUSD` | Yes (revenue side) | (not applied, TODO) |
| Outstanding Invoices stat | `EffectiveBalanceUSD` | — | **No** (by design) | n/a |
| Overdue Invoices stat | `EffectiveBalanceUSD` | — | **No** (by design) | n/a |
| Revenue page list | `EffectiveTotalUSD` | — | **No** (shows all) | n/a |
| Income Statement / GL | `EffectiveSubtotalUSD` | `EffectiveSubtotalUSD` (Income Statement: less tracked stock lines, plus cost of goods sold, §14) | No (accrual) | Pre-tax portion |
| Sales by Product (Analytics tab) | `EffectiveTotalUSD` (allocated per line item) | — | Yes | Not applied (see §13) |
| Sales by Product (Report) | `EffectiveTotalUSD` (allocated per line item) | — | No (accrual) | Not applied (see §13) |

---

## 12. Implementation pointers

When writing or reviewing aggregation code:

**Filters and aggregators (`ArgoBooks.Core/Services/`):**
- `RevenueAggregator.IsCollected(revenue)`: paid-only filter; switch on the `RevenuePaymentStatus` enum, never on strings.
- `RevenueAggregator.SumCollectedRevenueUSD(revenues, start, end)`: gross-of-tax revenue, paid-only, USD.
- `RevenueAggregator.SumCollectedRevenuePreTaxUSD(revenues, start, end)`: pre-tax revenue, paid-only, USD (use for profit math, not display).
- `ExpenseAggregator.SumExpensesUSD(expenses, start, end)`: gross expenses, USD.
- `RefundAggregator.GetRefundedInDateRangeUSD(payments, start, end)`: gross refund sum.
- `RefundAggregator.GetRefundedPreTaxInDateRangeUSD(payments, invoicesById, start, end)`: pre-tax refund portion for profit math.
- `RefundAggregator.GroupRefundsByDayUSD(payments, start, end)`: per-day refund map for time-series charts.
- `ProfitCalculator.CalculateNetProfitUSD(data, start, end)`: the cross-cutting net profit formula. Use this; don't re-derive.
- `ProfitCalculator.CalculateNetProfitByDayUSD(data, start, end)`: per-day profit for charts.
- `CostOfGoodsAggregator`: cost of goods sold on sales (`SumCostOfGoodsSoldUSD`, paid-only or not) and expenses without the tracked stock they bought (`SumOperatingExpensesUSD`). `ProfitCalculator` uses both; never subtract `ExpenseAggregator` totals from revenue to get profit.
- `InventoryStockService`: the only place stock moves for a purchase, a sale, an edit or delete of either, an import or a transfer, and the only place cost of goods sold is fixed on a line.
- `ComparisonPeriod.For(preset, start, end)`: the period every "vs previous period" figure compares against (dashboard, Analytics, Insights trends, and the report Summary box's growth rate). Report presets This Week, This Month and This Quarter run to the end of today, as the dashboard's do. This month, quarter or year so far compares with the same days of the one before, stopping at its end when it is shorter (This Month on Sep 11 is Aug 1 to Aug 11; This Year on Feb 29 is Jan 1 to Feb 28). Last month, quarter or year compares with the whole calendar period before it. Everything else compares with the same number of days just before.
- `InvoiceTotalsService.Recalculate(invoice, allPayments)`: call after any mutation to an invoice's payment list.
- `InvoiceMath`: the §4 per-invoice formula (subtotal, discount, fee, taxable base, tax, total). Anything that needs an invoice figure calls it rather than re-deriving the arithmetic.

**Display helpers (`ArgoBooks/Services/`):**
- `CurrencyService.FormatFromUSD(amountUSD, date)`: USD → display currency string. Use as the last step before binding to a UI text.
- `CurrencyService.GetDisplayAmount(amountUSD, date)`: USD → display currency decimal (when you need the number, not the string).
- `ChartLoaderService.ConvertUSDValuesToDisplay(double[])`: bulk variant for chart values.

**Rules of thumb:**
- For profit, sum `EffectiveSubtotalUSD` on revenue. For everything else revenue-side, sum `EffectiveTotalUSD`.
- All aggregations stay in USD. Convert to display currency only at the presentation boundary (stat card binding, chart series construction).
- For any new chart or stat card, look at the table in §11 to decide which column you fall under, and copy the pattern from a neighbor that is already correct.
- If you can't find an answer here, the rule does not exist yet, propose one before writing the code.

---

## 13. Sales by product

The Analytics "Products" tab and the Report Builder "Sales by Product" template both break **revenue and units** down per product. `ProductSalesService` is the single source of truth; both surfaces call it so their math never diverges, only the basis differs (see below).

**Why revenue, not profit.** Per-product *profit* is deliberately **not** computed. Profit would need a trustworthy cost of goods sold per product, and Argo can't produce one: cost of goods sold exists only for products that track inventory, and reads low while stock from before it began is still selling (§14), and anything you assemble or make (a pizza built from flour and cheese) has no purchasable "cost of itself" at all. Rather than show a misleading margin, these surfaces report only what is always true: how much each product sold for and how many units moved.

### Revenue per product (gross, USD)

A transaction's `Effective*USD` properties live at the transaction level, not the line item. To attribute revenue to a product, allocate each line item's share of the transaction's **gross** USD total in proportion to its native pre-tax subtotal:

```
lineItemsTotal = Σ over LineItem of li.Subtotal          (native currency, pre-tax)
revenueUSD(li) = lineItemsTotal != 0
               ? round(li.Subtotal / lineItemsTotal × txn.EffectiveTotalUSD, 2)
               : 0
```

Revenue is **gross** (`EffectiveTotalUSD`, tax-inclusive), matching every other revenue display in the app per Rule 1 (Total Revenue card, Top Customers, etc.). The allocation weight is the line's pre-tax subtotal, which is the only per-line figure available; tax is distributed in the same proportion. Line items with no `ProductId` group under "Unknown". A transaction with **no line items at all** has no product to attribute to and is excluded from these surfaces entirely.

### Units and average price

```
unitsSold(product) = Σ over its line items of li.Quantity
avgSalePriceUSD(product) = unitsSold != 0 ? revenueUSD / unitsSold : 0
```

`avgSalePrice` is gross revenue per unit (it carries the same tax-inclusive basis as the revenue column).

### Basis: cash for analytics, accrual for the report

Consistent with Rule 2 and §10, the two surfaces deliberately differ and `ProductSalesService` takes a single `cashBasis` flag to switch:

- **Analytics Products tab** (`cashBasis: true`): filters revenues with `RevenueAggregator.IsCollected` first, like every other analytics tab. Its aggregate revenue is close to the Total Revenue stat card for the same range but does not reconcile exactly, and the two differences pull opposite ways: it does **not** subtract refunds (so it reads higher than the card by the refund amount, see below), and it excludes revenue on transactions with **no line items** (so it reads lower than the card by that amount).
- **Report Builder template** (`cashBasis: false`): counts all invoiced revenue in range, like the Income Statement.

### Refunds are not attributed per-product

Refunds are recorded as invoice-level `Payment` rows (§8) with no line-item breakdown, so there is no reliable way to subtract a refund from the specific product it concerned. Per-product revenue therefore does **not** subtract refunds, and will read slightly high for products that were later refunded. Customer-returned items and lost/damaged stock already have their own per-product surfaces (Returns by Product, Losses by Product).

---

## 14. Cost of goods sold

Products with **Track Inventory** turned on are bought and sold as one item with one stock count, so both the purchase and the sale forms offer them. For those products, profit matches what stock cost to the sale that used it, instead of counting stock as an expense the day it was bought. Products that don't track inventory are unaffected.

`InventoryStockService` is the only place stock moves for a purchase, a sale, an edit or delete of either, a Stripe or Argo Books API import, or a transfer. `CostOfGoodsAggregator` is the only place the resulting figures are summed.

### Buying tracked stock

A purchase line whose product tracks inventory adds its quantity to stock and is marked `LineItem.IsStockPurchase`. Its pre-tax amount is stock, not an expense:

```
stockPurchaseUSD(expense)    = min( Σ over stock lines of li.Subtotal × (expense.EffectiveTotalUSD / expense.Total),
                                    expense.EffectiveTotalUSD )
operatingExpenseUSD(expense) = expense.EffectiveTotalUSD − stockPurchaseUSD(expense)
```

Tax, shipping and fees on the purchase stay expenses. The stock record's `UnitCost` becomes the line's pre-tax price per unit in USD, at the purchase's own rate (left alone while the rate is pending). When the purchase is in the company currency, the product's `CostPrice` becomes the line's unit price, so nobody has to keep a cost price current by hand.

### Selling tracked stock

A sale line whose product tracks inventory takes its quantity out of stock and fixes its cost on the line, in USD, when the sale is saved:

```
openingUsed(li)   = min(item.OpeningUnits, li.Quantity)
li.CostOfGoodsUSD = (li.Quantity − openingUsed(li)) × item.UnitCost
```

The cost is saved, not recalculated, so a later change to a cost price never rewrites past profit. A sale pending currency conversion counts no cost, as it counts no revenue.

### Profit

```
Net Profit = Σ Revenue.EffectiveSubtotalUSD − Σ operatingExpenseUSD − Σ li.CostOfGoodsUSD − Refunds(pre-tax)
```

The dashboard counts paid sales for both revenue and cost of goods sold (Rule 2). The Income Statement counts every sale in range, shows **Cost of Goods Sold** and **Gross Profit** under revenue whenever that cost is not zero, and leaves tracked stock lines out of its expense categories. The Expenses card, the Expenses page and cash flow still count every purchase in full, because that money did leave the business.

### Stock on hand when this began (opening units)

Stock bought before cost of goods sold existed was expensed at the time, so counting it again when it sells would take it off profit twice. The first time a company opens in a version with cost of goods sold, `CompanyManager.StartCostOfGoodsIfNeeded` records each stock record's units on hand as `InventoryItem.OpeningUnits`. Sales use opening units first, at no cost (`LineItem.OpeningUnitsUsed`), and editing or deleting a sale gives them back. A transfer moves opening units in proportion to the stock it moves. Editing a purchase or sale saved before cost of goods sold began keeps its old treatment: it moves stock, stays a full expense or carries no cost, and uses no opening units.

### Locations

A line takes stock from, or adds it to, the stock record at `LineItem.LocationId`. The form only asks when the product is stocked at two or more locations; otherwise the product's first stock record is used, or a new one is made at the company's first location. The location is saved on the line, so editing or deleting the transaction later returns stock to the same place.

### Not matched

- **Returns** record the return only (§10). They don't restock or take back the sale's cost of goods sold.
- **Receipt scans and revenue created from invoices** move no stock, as before.
- **Per-product profit** is still not shown (§13).

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

Dates count, not times: out Monday and back Wednesday is two days. The add form and Rent Out estimate the same way, to the due date. An invoice made from a rental has one line per item, charged to the return date, or to the due date while the rental is still out, plus a line for any extra charges.

### Deposits and extra charges

The rental's `SecurityDeposit` is Σ per-unit deposit × quantity. At return, anything from nothing to the whole deposit can be refunded (`DepositRefunded`) and the rest is kept. A kept deposit on an invoice becomes revenue on the return date (§4). Extra charges, such as a late fee or damage, are billed on top of the rental and don't come out of the deposit.

### Paid without an invoice

Marking a rental paid when it has no invoice records a revenue row for `TotalCost` plus any kept deposit, in the company currency and converted at its own date (Rule 3a). It is dated on the return when marked paid there, otherwise on the day it was marked. `RentalRecord.RevenueId` links it, so marking it unpaid or deleting the rental removes it. A rental with an invoice counts through the invoice instead, and a paid rental can't be invoiced, so the money is never counted twice.

### Stock and reservations

A rental starting after today is **Reserved** and leaves stock alone. Checking it out makes it Active and takes its units out of stock, and returning it puts them back. Before a rental is saved or checked out, each item needs enough units free on every day it covers:

```
owned  = InStock + units out on active and overdue rentals
booked = most units other rentals take on any one day of this rental's dates
free   = owned − booked
```

A reservation takes its start to due dates. A rental that is out takes its dates and, once late, every day up to today. A rental that takes stock now also can't take more than is in stock, plus what it already has out when it is being edited.
