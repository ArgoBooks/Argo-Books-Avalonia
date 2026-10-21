# Asset rate updates

For Canadian companies, the asset form fills in the Capital Cost Allowance (CCA) class and rate CRA gives each type of asset, and depreciates the asset the way CRA does. Like the payroll tables, none of those figures are in code. They are one data file, so a change is a file upload rather than a release.

The file lives in two places.

| | Where | Who it serves |
|---|---|---|
| **Upload** | `resources/downloads/assets/cca.json` on the website | Everyone already running the app. Each run checks it once, in the background, the first time an asset or expense form opens for a Canadian company. |
| **Commit** | `ArgoBooks.Core/Resources/Assets/cca.json` in this repo | A fresh or offline install, which uses the built-in copy until it can reach the server. |

The app only replaces its copy with a download whose `revision` is higher, so **raise `revision` by one every time the file changes**, and commit the same file you upload. A download that does not parse or fails `CcaRateValidator` is ignored, and nothing tells the customer, so check it before uploading.

## When to look

Unlike payroll, CCA has no fixed changeover dates. Rates change when a federal budget or fall economic statement says so, from whatever date it announces, and most classes go years without moving. What moves most is the first-year rule.

`cron/cca_rate_reminder.php` in the website repository emails from the 10th to the 20th of December and June, the same windows as the payroll reminder, so both are done in one sitting. It goes quiet once the server's file has a `reviewedOn` date in that month. **Updating `reviewedOn` and `revision` is the whole job when nothing changed**, which will be most times.

## What to check

1. **The latest budget and fall economic statement.** Search their tax measures for "capital cost allowance", "accelerated investment incentive" and "immediate expensing". Anything announced but not yet law goes in only once it is enacted, or once CRA says to claim it.
2. **Each class's rate and description**, against CRA's classes page.
3. **The first-year rules** (below).
4. **Each asset type's default class** (`typeDefaults`), if a new class suits a type better.

### Sources

- [Classes of depreciable property](https://www.canada.ca/en/revenue-agency/services/tax/businesses/topics/sole-proprietorships-partnerships/report-business-income-expenses/claiming-capital-cost-allowance/classes-depreciable-property.html), CRA's list of classes and rates
- [T4002, Self-employed Business, Professional, Commission, Farming, and Fishing Income](https://www.canada.ca/en/revenue-agency/services/forms-publications/publications/t4002.html), chapter 4, which explains the first-year rules in plain language
- Schedule II of the Income Tax Regulations, the final word on what each class holds
- The budget implementation acts, for the dates a first-year rule starts and ends. Budget 2025's changes are in [Bill C-15](https://www.parl.ca/documentviewer/en/45-1/bill/C-15/royal-assent).

canada.ca blocks plain HTTP requests. Read it with Claude driving Chrome, as `PayrollRateUpdates.md` describes, or with Chrome itself in headless mode, which loads the page as a real browser does and saves it for reading:

```powershell
& "C:\Program Files\Google\Chrome\Application\chrome.exe" --headless=new --disable-gpu --dump-dom "<page url>" > page.html
```

CRA's own pages lag the law. In October 2026 the Accelerated Investment Incentive page still described the rules as of July 2025, and the classes page called Budget 2025's measures "proposed" months after Bill C-15 became law. Finance Canada's yearly *Report on Federal Tax Expenditures* (part 4) describes each measure with its dates and says whether it is legislated, and LEGISinfo gives a bill's royal assent date.

## The file

```json
{
  "revision": 1,
  "reviewedOn": "2026-10-20",
  "classes": [
    { "number": "10", "description": "Vans, trucks and other motor vehicles...", "rates": [ { "percent": 30 } ] }
  ],
  "firstYearRules": [
    { "name": "Half-year rule", "factor": 0.5, "to": "2018-11-20" },
    { "name": "Small tools and utensils are not subject to the half-year rule", "factor": 1.0, "classes": [ "12" ] }
  ],
  "typeDefaults": { "Vehicle": "10" },
  "sources": [ "..." ]
}
```

- **A rate** applies to assets bought from `from` to `to`, both optional and both inclusive. When a budget changes a class's rate from a date, end the old rate the day before and add the new one, rather than editing the old one, so assets bought earlier keep theirs.
- **A first-year rule** gives the share of the rate claimed in the year an asset is bought: 0.5 under the half-year rule, 1.0 when it is suspended, 1.5 under the Accelerated Investment Incentive. A rule with `classes` applies only to those and wins over a general one. Two general rules may not cover the same day.
- **`typeDefaults`** maps each `AssetType` name, except Land, to the class it starts in.
- **A class that is no type's default** names the type it is offered under (`assetType`) and what the type list calls it (`label`), such as class 10.1 under Vehicle as "Passenger vehicle over CRA's limit". Without them a class is in the file but not offered.

Only the classes a small business commonly uses are listed. Adding one is a new entry in `classes`, with no code change.

## How the app uses it

- **The type list** in the asset form and the expense form, for a company whose country is Canada, offers each type with its class and rate, such as "Vehicle (Class 10, 30%)", plus the other classes a type can be in, such as "Passenger vehicle over CRA's limit (Class 10.1, 30%)". So one list picks both.
- **Picking a type in the asset form** sets Declining balance and the class's rate for the purchase date. Everything stays editable, and editing an asset never overwrites its saved rate. The expense form asks for no number of years when the type has a class.
- **An asset with a class and Declining balance** is depreciated by CRA's method (`AssetDepreciation.CcaMonths`): each calendar year takes the rate of what is left, the first year only its first-year share, and the year it is sold or disposed of none. The first-year share is stored on the asset when it is saved, so a later change to the rules only reaches an asset when it is edited.
- **Prior depreciation** carried over from earlier books means the asset is past its first year, so no first-year share applies.

## What it does not cover

- **The tax year is the calendar year.** Sole proprietors and partnerships must use it. A corporation with another year end gets each year's amount on calendar years, which an accountant adjusts when filing.
- **Class 10.1's cost limit.** CRA caps what a passenger vehicle in class 10.1 can be depreciated on, and changes the cap most years. The app depreciates the full cost.
- **Pools.** CRA depreciates a class as one pool, so a sale reduces the pool and can leave recapture or a terminal loss. The app depreciates each asset on its own and counts a sale's gain or loss on the asset.
- **The manufacturing, clean energy and zero-emission vehicle classes** (43, 43.1, 43.2, 53, 54 to 56), whose first-year rules depend on more than the purchase date. Those classes are not in the file. Immediate expensing for computers (class 50, bought from April 16, 2024 and in use before 2027) is, as a first-year factor of 1.8182, which is 100% over the class's 55%.
- **"Available for use" dates.** CRA's first-year rules turn on when an asset is first used; the file is matched on the purchase date, which for a small business is almost always the same year.

The form says to use the rate the accountant gives, and the app files no taxes, so these show in the reports as figures to confirm, not as a filed claim.
