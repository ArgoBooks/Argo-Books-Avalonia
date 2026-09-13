# Payroll rate updates

CRA issues new payroll deduction tables **twice a year**. Argo Books keeps none of those figures
in code: every rate, threshold and constant is data, which is why an update is a file rather
than a release.

Each edition lives in two places.

| | Where | Who it serves |
|---|---|---|
| **Upload** | `resources/downloads/payroll/` on the website | Everyone already running the app. They fetch it the first time a pay run needs an edition they do not hold. |
| **Commit** | `ArgoBooks.Core/Resources/Payroll/` in this repo | A fresh or offline install, which calculates from the embedded copy until it can reach the server. |

Uploading to the server alone leaves a new install unable to run payroll until it syncs. Committing to the desktop repo alone
means nobody gets it until they update the app.

**Published and effective are different dates, and the gap is the window we have to add the new
files.** CRA puts an edition out roughly a month before it applies.

| Edition | Published | Takes effect | Do the work |
|---|---|---|---|
| `YYYY-01` | around November or December | January 1 | December |
| `YYYY-07` | around May or June | July 1 | June |

Aim to have it uploaded a week early.

**An edition can be amended after it is published, so check that it still says what it said.**
The guide carries its own revision stamp at the top, reading `Rev. YY (YY/MM)`, and the month is
when that text was last touched. The January 2026 edition says `Rev. 26 (26/05)`, five months
into the period it governs, because PEI gained a bracket in May. If the stamp is later than the
day you built the file, something moved after you read it.

## What happens if the date passes

The app refuses to calculate a pay run when it holds no edition covering that pay date. It never
falls back to the previous edition, so no incorrect deduction is produced.

## The reminder chases you until it is done

`cron/payroll_rate_reminder.php` in the website repository sends reminder emails to the admin.

- **10th to 20th of December and June:** start the work. The numbers are out by then.
- **Last three days before the changeover:** only if the file still is not on the server.

Both check `resources/downloads/payroll/{edition}.json` and go silent once it is there.

---

## What else needs a look each year

The rate tables are the big one and the rest of this document is about them. They are not the
only thing that moves, and nothing else has a reminder attached, so check these when you do the
January edition.

### The XML specification

The file the year end screen exports is built to a specification that is year-stamped and
revised within the year:

- [T619, Electronic Transmittal](https://www.canada.ca/en/revenue-agency/services/e-services/filing-information-returns-electronically-t4-t5-other-types-returns-overview/t619-2026.html)
- [T4, Statement of Remuneration Paid](https://www.canada.ca/en/revenue-agency/services/e-services/filing-information-returns-electronically-t4-t5-other-types-returns-overview/t619-2026/t4-2026.html)

The year is in the URL, so `t4-2026.html` becomes `t4-2027.html`. Both open with a **What's new**
section listing what changed, which makes this a five minute read rather than a re-check of
every field.

Worth doing, because the changes are not cosmetic. Both documents were at 2026V4, four revisions
inside one year, and that year the T619 language code became Required while the T4 gained a
validation that the account number on the slip and on the summary must match. Either would turn
a working export into a rejected one, and the rejection lands at the filing deadline against a
file whose every figure is correct.

`T4XmlWriter` names the version it was written against at the top. Update that when you check,
so the next person can tell whether anyone has looked since.

### The XML schemas

Reading the What's new section tells you what changed. It does not tell you whether the export
still validates, and that is the part that decides whether a submission is accepted.

CRA publishes the schemas as a downloadable package, and a copy lives in
`ArgoBooks.Tests/Schemas/Cra`. `T4XmlSchemaTests` builds a return, exports it and validates the
result against them, so a schema change that breaks the writer fails the build rather than the
February deadline. The Quebec branch is covered separately, because QPP replaces CPP there and
that changes which elements appear and in what order.

Replace the package when you do the January edition, following the README in that folder. The
schema version and the specification version named in `T4XmlWriter` are asserted against each
other, so updating one without the other fails a test.

### The fixtures captured from CRA's calculator

`PayrollAgainstCraCalculatorTests` holds twelve cases whose expected figures were read off CRA's
Payroll Deductions Online Calculator, and one off Revenu Québec's WebRAS. They are the only tests
here that check the engine against the authority rather than against our own reading of T4127.

They are dated, because the tables change on 1 January. Recapture all twelve with pay dates in
the new year when you do the January edition. A row whose figures have not been captured fails
with a message naming what to enter, so an empty one is visible rather than looking like coverage.

Assume the engine is wrong before assuming the calculator is, and never edit an expected value to
make a test pass: that turns a check against CRA into a check against nothing.

### The ROE payroll extract

The Record of Employment export follows
[Appendix D of the ROE Web user requirements](https://www.canada.ca/en/employment-social-development/programs/ei/ei-list/ei-roe/user-requirements/appendix-d.html),
XML file layout Version 2.0, which has been mandatory since September 2016 and so moves far less
than the CRA specifications above. Read it when you do the January edition anyway, and update the
version named in `RoeXmlWriter`.

Service Canada distributes the validating schema, `PayrollExtractXmlV2.xsd`, through ROE Web
rather than publishing it, so there is no copy in the repo and `RoeXmlTests` asserts the layout by
hand instead. If you download it, drop it into `ArgoBooks.Tests/Schemas/ServiceCanada` and
`ValidatesAgainstTheServiceCanadaSchema` starts checking properly with no other change. That is
worth doing once.

The other thing worth doing once: export a real ROE as a draft (`Issue="D"`), upload it through
ROE Web and read it back in their screen. A draft is not submitted, so it costs nothing and it is
the only way to find out what their business validations think of the file.

### The RL-1 guide

Revenu Québec reissues
[the guide](https://www.revenuquebec.ca/en/online-services/forms-and-publications/current-details/rl-1.g-v/)
every year; the box mapping in this app came from RL-1.G-V (2025-10). The boxes themselves are
stable, the rules around them less so, and Revenu Québec has already retired its per-box web
pages once, so cite the guide rather than a page.

### The filing thresholds

Both agencies allow paper only below a slip count and require electronic filing above it. CRA's
moved from 50 to 5 not long ago, so this is not a constant. Neither threshold is enforced in
code any more: CRA's appears only in documentation, and Revenu Québec's stopped mattering once
the RL-1 output became a worksheet (below).

### The RL-1 authorization number, if that ever changes

Argo Books does not produce a filable RL-1 and says so on the page. Revenu Québec accepts a
paper slip printed by software only when it carries an authorization number, two letters and
seven digits like FS9999999, plus a two-dimensional barcode on copy 1. The number is issued to
the software developer **per taxation year** after Revenu Québec certifies the product, so
getting one is not a single piece of work: it is an annual renewal that lands on this same
twice-a-year schedule.

Until that happens the RL-1 PDFs stay worksheets, and `Rl1Service.FilingNotice` is the one place
that wording lives. If certification is ever obtained, that constant, the two PDF headers and
`Rl1PdfRenderer`'s class comment are what change.

### The remitter type

CRA assigns it from the average monthly withholding amount two years back, and it decides the
deadline shown on the Pay runs page, the pay run review and the dashboard's Next Remittance Due
card, which all read it through `PayrollService`. It is a company setting, not a calculation,
because a new employer has no history to read it from. The four schedules are in `RemitterType`
and their due dates in `PayrollService.PeriodsStartingIn`; both come straight from CRA's table of
remitter types and have not moved in years, but the table is worth a glance when the thresholds
change.

### What does not need touching

There are no hardcoded years anywhere in the payroll code. Every figure that changes lives in a
rate file, and the only year-stamped things in code are the comments naming the spec version.

Box 45's dental codes are CRA's own numbers held in an enum and written straight to the XML, so
they must never be renumbered, but they have not moved since 2023.

---

## What to collect

Everything below comes from CRA, except Quebec which comes from Revenu Québec.

**Federal**
- Income tax brackets: thresholds, rates, and the constant `K` for each
- Basic personal amount `BPAF`, including its phase-out range
- Canada Employment Amount
- The lowest rate, used to convert credits

**Contributions**
- CPP: employee and employer rates, basic exemption, YMPE ceiling, annual maximum
- CPP2: rates, YAMPE ceiling, annual maximum
- EI: employee rate, employer multiplier, maximum insurable earnings, annual maximum, and
  the separate Quebec rate

**Each province and territory**
- Brackets, rates and constants
- Basic personal amount, which for Manitoba and Yukon is a formula rather than a number
- Ontario only: surtax thresholds and rates, the health premium bands, the tax reduction
- British Columbia only: the tax reduction

### Sources

- [T4127 Payroll Deductions Formulas](https://www.canada.ca/en/revenue-agency/services/forms-publications/payroll/t4127-payroll-deductions-formulas.html), the authoritative one
- [CPP contribution rates, maximums and exemptions](https://www.canada.ca/en/revenue-agency/services/tax/businesses/topics/payroll/payroll-deductions-contributions/canada-pension-plan-cpp/cpp-contribution-rates-maximums-exemptions.html)
- [CPP2 rates and maximums](https://www.canada.ca/en/revenue-agency/services/tax/businesses/topics/payroll/calculating-deductions/making-deductions/second-additional-cpp-contribution-rates-maximums.html)
- [EI premium rates and maximums](https://www.canada.ca/en/revenue-agency/services/tax/businesses/topics/payroll/payroll-deductions-contributions/employment-insurance-ei/ei-premium-rates-maximums.html)
- [TP-1015.F-V, Formulas to Calculate Source Deductions](https://www.revenuquebec.ca/en/online-services/forms-and-publications/current-details/tp-1015-f-v/), for Quebec. It is a separate gathering exercise, covered in its own section below

### Reading the tables with Claude driving Chrome

**canada.ca cannot be fetched programmatically.** Plain HTTP gets 403 or times out, whichever
tool is asking. The Chrome extension is the way in, because the request comes from a real
browser session.

**If the extension reports "not connected", Chrome is simply not running. Start it rather than
stopping to ask:**

```powershell
Start-Process "C:\Program Files\Google\Chrome\Application\chrome.exe"
```

Give it five seconds or so, then connect.

**Do not try to read the page as text.** T4127 is about 100,000 characters and the text tool
stops at 50,000, so the whole of Chapter 8 falls off the end. Run JavaScript against the tables
instead.

Step 1, find which table is which. Return the list as the last expression: the tool hands back
the value of the last expression and throws away anything logged to the console, so a
`console.log` here comes back empty.

```js
[...document.querySelectorAll('table')]
  .map((t, i) => i + ': ' + (t.caption ? t.caption.innerText.trim() : '(no caption)'))
  .join('\n')
```

Step 2, dump one table, **a dozen rows at a time**. A whole table overflows the output limit and
gets truncated mid-number, which is the one failure here that looks like data rather than an
error.

```js
const t = document.querySelectorAll('table')[3];   // the index from step 1
[...t.rows].slice(0, 12)
  .map(r => [...r.cells].map(c => c.innerText.replace(/\s+/g, ' ').trim()).join('|'))
  .join('\n')
```

For the formula sections rather than the tables, read `document.body.innerText` around a
distinctive string. Those trip an output filter that reads `=` as query-string data and blocks
the whole response, so rewrite the symbols before returning: `.split('=').join(' EQ ')`, and
`.split('?').join(' QQ ')` if the passage has question marks.

**Build the new edition as a diff from the previous one, not by retyping it.** Copy the file,
apply only the jurisdictions the "What's new" section names, then verify every cell of the
result against Table 8.1 with a script. In the 2026-01 round that verification caught a real
error: the wrong PEI bracket had been dropped, leaving a 21% top rate that does not exist in
the first half of the year. Retyping thirteen jurisdictions by hand would not have caught it.

The `_verification` note at the top of each rate file records exactly what was compared.

Note that the July edition only reproduces the sections that CHANGED. Formulas that did not
change, and Ontario's health premium bands, are in the January edition instead.

### The current edition tells you what the next one holds

Each edition carries an **"Upcoming Changes"** section for the one after it, so the shape of the
next update is knowable months ahead rather than being a surprise in the working window. Worth
reading on the way past, because it is where a change big enough to need code rather than data
would show up first.

As of the July 2026 edition, for `2027-01`:

- **CPP base rate falls from 4.95% to 4.75%** on January 1 2027, announced April 28 2026. This
  one is not just a number: `baseRateEmployee` splits CPP into the part relieved as a credit and
  the part relieved as a deduction, so it moves income tax as well as the contribution.
- **British Columbia pauses indexation** at 2026 levels for the 2027 to 2030 tax years, resuming
  in 2031. So BC's brackets and personal amounts should come across unchanged, and a diff showing
  them moving means something is wrong.

### Reading Table 8.1 into the rate file

`A` is the LOWER bound of each bracket. Our `upTo` is the TOP of each bracket, which is the
next `A`, with `null` for the last one. So a row reading

    A   0       61,200   154,259
    V   0.0800  0.1000   0.1200
    KP  0       1,224    4,309

becomes `upTo 61200 / rate .08 / k 0`, then `upTo 154259 / rate .10 / k 1224`, then
`upTo 185111 / rate .12 / k 4309`. Getting this off by one shifts every bracket and does not
look wrong in the output, so check the first row against the previous edition.


---

## Quebec is a separate update, from a separate source

Quebec administers its own income tax, pension plan and parental insurance plan, so none of
its figures appear in T4127 and none of the PDOC fixtures say anything about it. It is a
second gathering exercise on its own schedule.

**Sources**

- [TP-1015.F-V, Formulas to Calculate Source Deductions](https://www.revenuquebec.ca/en/online-services/forms-and-publications/current-details/tp-1015-f-v/),
  which carries the brackets, the constants, the personal amounts and the formula itself.
  Appendix 1 has a worked example that is the Quebec equivalent of T4127's, and is pinned as
  a test.
- [QPP maximums and contribution rate](https://www.revenuquebec.ca/en/businesses/source-deductions-and-employer-contributions/calculating-source-deductions-and-contributions/qpp-contributions/maximum-pensionable-earnings-and-contribution-rate/)
- [QPIP maximums and premium rates](https://www.revenuquebec.ca/en/businesses/source-deductions-and-employer-contributions/calculating-source-deductions-and-contributions/qpip-premiums/maximum-insurable-earnings-and-premium-rate/)
- The Quebec EI maximum, which is lower than the rest of Canada because QPIP covers parental
  benefits. The RATE is in T4127; the maximum is announced with the EI rate each autumn.
- The federal abatement, 16.5%, which is in T4127's Table 8.2 against QC.

**The oracle is not WebRAS.** This is the opposite of the rest of Canada. Revenu Quebec states
in the guide and again inside WebRAS itself: "In the event of a discrepancy between the
calculations using the formulas and those using WebRAS, the calculations using the formulas
prevail." So TP-1015.F is authoritative and WebRAS is a sanity check. Do not "fix" the
calculator to match WebRAS.

**A search summary got QPIP wrong.** It reported 0.455% and 0.636% for 2026. The published
table says 0.430% and 0.602%, and 103,000 x 0.00430 = 442.90 confirms it. Read the tables.

---

## The RL-1

A Quebec employer files an **RL-1 slip and RL-1 Summary with Revenu Quebec**, in addition to
the T4 and T4 Summary with CRA. That is why the T4 Summary's CPP totals deliberately exclude
Quebec's QPP: those figures belong on the RL-1, and CRA has no field for them.

Built in `Rl1Service`, `Rl1PdfRenderer` and `Models/Payroll/Rl1Slip.cs`, and reachable from the
Quebec section of the year end modal, which only appears for an employer who actually has a
Quebec employee.

### What was settled about building it

**The slip and summary are PDFs, not XML.** Revenu Quebec requires online XML filing only
from employers sending MORE than 5 RL slips of the same type. Anyone filing fewer than 6 may
use the Transmitting RL Slips online service or send paper. This app's employer has two or
three staff, so they are under the threshold and will key the figures in, exactly as they
would with CRA's Web Forms rather than T4 XML.

**The RL-1 XML specification is not public anyway.** Unlike CRA, which publishes the T4 layout
as a web page, Revenu Quebec routes RL-slip specifications through its Division de
l'acquisition des donnees electroniques for registered product developers. Building the XML
would mean registering first.

Above five slips the app says so and stops, rather than handing over paper that Revenu Quebec
will send back. See `Rl1Service.Validate`.

### The boxes

From the [Guide to Filing the RL-1 Slip](https://www.revenuquebec.ca/en/online-services/forms-and-publications/current-details/rl-1.g-v/):

| Box | Meaning | Where it comes from |
|---|---|---|
| A | Employment income | gross pay for the year |
| B.A | QPP contribution | PayRunLine.CppEmployee for a QC employee |
| B.B | Additional QPP contribution | PayRunLine.Cpp2Employee |
| C | Employment Insurance premium | PayRunLine.EiEmployee |
| D | RPP contribution | not collected, always nil |
| E | Quebec income tax withheld | PayRunLine.ProvincialTax |
| F | Union dues | not collected, always nil |
| G | Pensionable salary or wages under the QPP | gross capped at the YAMPE, or nil if QPP exempt |
| H | QPIP premium | PayRunLine.QpipEmployee |
| I | Eligible salary or wages under the QPIP | gross capped at the QPIP maximum, or nil when no premium was withheld |

H and I were confirmed against sections 5.10 and 5.11 of the
[Guide to Filing the RL-1 Slip](https://www.revenuquebec.ca/en/online-services/forms-and-publications/current-details/rl-1.g-v/)
rather than inferred, because a wrong box letter puts a real number in the wrong place on a
government slip and nothing downstream would catch it.

Revenu Quebec used to publish a page per box on its website and has since retired them: the
whole `how-to-complete-the-rl-1-slip-box-by-box-instructions` branch now answers 410. The guide
is the surviving source, so cite that rather than a web page for anything box-specific.

Three things about those boxes are easy to get wrong:

- **Box I takes `0` rather than being left blank** when there is no eligible salary. Revenu
  Quebec states this explicitly, which is why the renderer always prints it.
- **Nil in box I means no premium was withheld, NOT that the employee is EI exempt.** Those
  were conflated once, and Revenu Quebec is explicit that they are different: "employment that
  is not insurable under the Employment Insurance Act is not necessarily excluded employment
  under the Act respecting parental insurance", and premiums are due on a shareholder's salary
  "regardless of the number of shares held by that person". That is exactly the owner-manager
  this app marks EI exempt, so they pay QPIP and box I carries their earnings. A premium in
  box H against a nil box I is the one pair that cannot be right.
- **Box G is capped at the YAMPE, not the YMPE.** The intuition is the other way round, since
  earnings above the first ceiling belong to QPP2 and are reported in box B.B. RL-1.G-V §5.9
  gives box G two maximums and selects the additional one whenever box B.B carries an amount,
  which is every case where the cap can bind. Capping at the YMPE understates the box for
  precisely the employees who have QPP2 against it.
- **Box E is the Quebec tax alone.** The federal tax withheld in the same pay run goes to CRA
  and appears in box 22 of the T4. Printing the combined figure would credit the employee with
  Quebec tax that Quebec never received. Pinned by a test.
- **Box I is capped at the QPIP maximum insurable earnings** ($103,000 for 2026, $98,000 for
  2025), which comes from the rate file rather than from the slip.

### What the RL-1 Summary does NOT cover

The summary's remittance total is QPP, QPIP and Quebec income tax. It excludes the
**contribution to the health services fund**, the **contribution related to labour standards**,
and the **workforce skills development contribution**, none of which this app calculates. The
PDF says so on its face, because an employer comparing the total against what they actually
remitted would otherwise conclude they had overpaid.

Employment insurance is federal and is deliberately absent: it is on the T4 Summary.

---

## Prorated figures, the easiest thing here to get wrong

When a province changes its rates part way through the year, CRA does not print the annual
figure in the July edition. It prints a **prorated** one that, combined with what was already
withheld over the first six months, lands the year on the right total.

Three jurisdictions are prorated in the July 2026 edition:

| | January edition | Annual figure | July edition figure |
|---|---|---|---|
| BC, lowest rate | 5.06% | 5.60% | **6.14%** |
| BC, basic reduction | $575 | $690 | **$805** |
| Newfoundland and Labrador, basic personal amount | $11,188 | $13,094 | **$15,000** |
| PEI, top bracket rate | no such bracket | 20% over $200,000 | **21%** |

The January column is what was actually withheld from January to June, and it is the figure
the January edition carries. Read the middle column as the answer to "what did the province
announce", which is the one number that appears in NEITHER edition. Both editions are correct
and neither matches the press release, which is the whole trap.

Two things follow. Putting the annual figure into a July edition is wrong in a way that looks
right, because the number matches what the province itself announced. And carrying a July
figure forward into the next January edition is wrong the other way, because the proration has
done its job and expired.

Whenever the "What's new" section of T4127 names a province, assume its figures are prorated
until you have checked otherwise.

---

## How to check the numbers before trusting them

Never take these on faith. Three checks catch almost everything, and all are quick.

**1. Derived maximums must reproduce from their own rates.** These cannot survive a
fabricated number:

```
CPP  max = (YMPE - basic exemption) x rate
CPP2 max = (YAMPE - YMPE)           x rate
EI   max = maximum insurable earnings x rate
```

For 2026: `(74600 - 3500) x 0.0595 = 4230.45`, `(85000 - 74600) x 0.04 = 416.00`,
`68900 x 0.0163 = 1123.07`. All three match the published maximums exactly.

**2. Bracket constants must be continuous.** At each boundary, tax computed from the bracket
below and the bracket above must agree:

```
rate_below x boundary - K_below  ==  rate_above x boundary - K_above
```

They will differ by under a dollar, because `K` is published rounded to whole dollars.
Anything larger means a wrong rate or a wrong constant.

**3. Two independent retrievals must agree.** Ask two different tools, then diff the results
field by field. In the 2026 round this caught nothing wrong but did reveal that each tool had
silently omitted a different set of provinces, which is the more likely failure than a bad
number.

---

## Building the file

1. Copy the previous edition from `ArgoBooks.Core/Resources/Payroll/` and rename it to the
   new edition, for example `2027-01.json`.
2. Update `editionId`, `effectiveFrom` and `effectiveTo`. The January edition runs to June 30,
   the July edition to December 31.
3. Replace the figures.
4. Update the `_source` and `_verification` notes at the top so the next person can see where
   the numbers came from and what was checked.
5. Run the test suite. `PayrollCalculatorTests` checks bracket continuity and the annual
   maximums against whatever table is loaded, so a transcription error usually fails there.

---

## Verifying against CRA's calculator

The tests prove the file is internally consistent. They do not prove it agrees with CRA.

Run a handful of cases through
[PDOC](https://www.canada.ca/en/revenue-agency/services/e-services/digital-services-businesses/payroll-deductions-online-calculator.html)
and compare against the app. Cover each pay frequency, one income below the CPP ceiling, one
between the ceilings, and one employee already at their annual maximum. Any disagreement is
the file, not the engine, unless the engine changed too.

---

## Shipping it

Upload it to the server and commit it to the desktop repo, as set out at the top. Nothing in the csproj needs touching: it globs
`Resources\Payroll\*.json`, so a new file is embedded by being in the folder.

### How the download works

`PayrollRateUpdateService` fetches `{ApiConfig.BaseUrl}/resources/downloads/payroll/{edition}.json`,
the same shape `LanguageService` uses for translations. It runs when a pay run meets a date no
loaded edition covers, which is the moment the user actually needs one, and the edition id is
derived from the pay date rather than looked up: `2027-01` for January to June, `2027-07` after.

**It refuses far more than it accepts, and every refusal leaves the cache alone.** A download
is only written if it parses, its `editionId` matches the filename that was asked for, and it
passes `PayrollRateValidator`: every derived maximum reproducing from its own rate, and every
bracket ascending, ending open and meeting its neighbour at the boundary. A 404, an offline
machine, a captive portal answering 200 with HTML, and a truncated file all take the same path,
which is to change nothing.

That matters because this is the one download in the app where being wrong is silent and
expensive. Everything else degrades visibly: a missing translation shows English, a missing
exchange rate shows Pending. A wrong rate table produces a wrong deduction on a real person's
pay that nothing downstream questions.

**A rejected file is invisible to the customer.** They see "no tables loaded" and nothing says
why. So validate before uploading, not after. The rules are in
`resources/downloads/payroll/README.md` on the website, and `PayrollRateValidatorTests` shows
what each failure looks like.

What this does NOT do is check the numbers against CRA. It cannot: a table can pass every check
and still carry last year's figures. The verification pass above is still the only thing that
catches that.

---

## If a deadline is missed

The app refuses to calculate rather than guessing, so nothing incorrect is produced. Users on
the affected dates will see that payroll cannot be run and will contact support.

Recovering is just the normal process above. Any pay run already approved under the previous
edition keeps its stored figures and is not retroactively changed, which is intended: it
matches the stub the employee was given.

---

## Amending a filed T4

CRA is explicit that **an amended return must not include original slips, and an original
return must not contain amended slips**. They go up as separate submissions.

The app has no record of what was filed last time, so it cannot work out which slips changed.
That is why choosing Amendment or Cancellation on the year end screen turns on a checkbox per
employee: only the employer knows. Filing all of them would restate every employee as amended.

Two details from the [2026V4 T4 specification](https://www.canada.ca/en/revenue-agency/services/e-services/filing-information-returns-electronically-t4-t5-other-types-returns-overview/t619-2026/t4-2026.html)
that are easy to get wrong:

| | Slip `rpt_tcd` | Summary `rpt_tcd` |
|---|---|---|
| Original | O | O |
| Amendment | A | A |
| Cancellation | C | **A** |

The summary has no C. CRA lists only O and A for it, so a cancellation carries C on its slips
and A on the summary. Writing C there would be a value the specification does not define.

`<fileramendmentnote>` takes up to 1309 characters and is **for report type A only**. Since an
optional element carrying no value rejects the whole submission, it is written only when the
filing is an amendment AND the note is non-empty.

CRA also states the summary totals are "those reported from the T4 Slips filed with this T4
Summary", so an amendment for one employee must not total the others. The totals follow the
selected slips by themselves because `T4Return` computes them from its own list.

---

## The Record of Employment

Due within **five calendar days of the end of the pay period** in which the interruption of
earnings occurs, not five days from the last day worked. Service Canada calculates an EI claim
from it, so a wrong figure does not bounce: it quietly shortens how long someone is paid.

Argo Books produces a **worksheet for ROE Web**, not an ROE. Service Canada issues ROEs through
ROE Web or ROE SAT; a printed sheet is not a filing. The worksheet exists so nobody has to
re-add 27 pay periods by hand on a five day deadline.

### The two windows are different lengths

This is the trap. From the
[ROE guide](https://www.canada.ca/en/employment-social-development/programs/ei/ei-list/reports/roe-guide.html):

| Pay period type | Blocks 15A and 15C | Block 15B |
|---|---|---|
| Weekly | 53 | 27 |
| Biweekly | 27 | 14 |
| Semi-monthly | 25 | 13 |
| Monthly | 13 | 7 |

Neither column is a clean function of the frequency, so both are transcribed from Service
Canada's charts rather than derived. A test asserts the hours window is always longer than the
earnings window, because collapsing them into one constant is the obvious "tidy-up" that would
break this.

### Other things worth knowing

- **Block 15C is most recent pay period FIRST**, which is the opposite of storage order. Nil
  periods are printed as `0.00` rather than skipped: a skipped row shifts every later period
  into the wrong slot.
- **Block 15A needs insurable hours, and a salaried pay run records none.** Service Canada's
  answer for an employer who does not track hours is the contract hours, which is what
  `Employee.StandardHoursPerWeek` is for. When it is blank the worksheet says the hours are
  unknown instead of printing zero, because zero hours costs the employee their claim.
- **A salaried nil period earns no hours.** Crediting contract hours to a period with no
  earnings would invent hours nobody worked.
- **Block 12 can never be earlier than block 11.** An end date after the last pay period, which
  is what a final unpaid stretch looks like, would otherwise produce exactly that.
- **Block 16, the reason for issuing, is deliberately absent.** The app knows someone stopped
  being paid; it does not know whether they quit, were dismissed or went on leave, and those
  are different legal statements with different consequences for the claim. It is chosen in
  ROE Web.
- A voided run and its reversal share a pay period, so they are grouped by `PeriodEnd` before
  the window is applied. Counting them as two periods would push a real period off the end.
