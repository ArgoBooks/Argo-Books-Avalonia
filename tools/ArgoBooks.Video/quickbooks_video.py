"""Make YouTube video 14, "How to Export Your QuickBooks Data and Switch to Free Software": the real QuickBooks import in the
real app, narrated by an Azure voice.

Usage:
  python quickbooks_video.py voice          fetches and measures the voice, and prints when each part starts
  python quickbooks_video.py frame 12 95    saves single frames at those seconds to work/qb/check/, for checking
  python quickbooks_video.py                renders the video

It uses the `quickbooks` take in work/qb/takes, recorded from the exports make_qb_exports.py writes, and the `receipt` and
`bank` takes of the other videos. The finished video and its captions go to work/videos/.
"""
import json
import subprocess
import sys
from pathlib import Path

import numpy as np
from openpyxl import load_workbook
from PIL import Image, ImageDraw

import catches_short as short
import make_video as mv
import roundup as r

HERE = Path(__file__).resolve().parent
WORK = HERE / "work" / "qb"
OUT_FILE = HERE / "work" / "videos" / "video_14_quickbooks.mp4"
TAKES = {"quickbooks": WORK / "takes", "receipt": HERE / "work" / "takes", "bank": HERE / "work" / "roundup" / "takes"}
RATE, FPS = r.RATE, r.FPS
SIZE = (1920, 1080)
GAP = 0.6
CLIP_SIZE = (2400, 1350)
ONE_TAKE_SPEED = 1.5
GLIDE = 0.9
# Seconds from a line's start to the next one's, so each line lands on its step in the continuous Reports clip.
LINE_LEADS = {"detail": 6.2, "dates": 6.8, "export": 5.2}
WHOLE = (0, 0, 2400, 1350)
FADE_FRAMES = 6
MUSIC_UNDER_VOICE_DB = -18.0
MUSIC_BETWEEN_DB = -13.0
GREEN, RED, AMBER = (22, 163, 74), (220, 38, 38), (217, 119, 6)
LINE = (203, 213, 225)

PARTS = {
    "empty": "This is a brand new company in Argo Books. It has nothing in it.",
    "full": "A minute later, it has customers, suppliers, sales, expenses and unpaid invoices, all brought over from QuickBooks.",
    "match": "And the totals match the QuickBooks report to the cent.",
    "promise": "In this video, I'll show you how to get your data out of QuickBooks, how to bring it into free software, how to prove nothing went missing, and what doesn't come across.",
    "desktop": "Why leave at all? If you're on QuickBooks Desktop 2023, Intuit ended its services on May 31st, 2026. That means no live support, no payroll, no payments, no online bank feeds and no security updates.",
    "online": "And if you're on QuickBooks Online, Intuit's pricing page lists Simple Start at 38 US dollars a month, and Plus at 140. At list price, Plus comes to 1,680 dollars a year.",
    "fit": "Argo Books is free accounting software that runs on your own computer, on Windows, Mac or Linux, with no account to create. It's a good fit if you run a small business and do your own books. It's not a good fit if you need payroll outside Canada, or several people working in the same books at once. If that's you, stay where you are.",
    "create": "Step one: create your company. Give it a name, pick your country, and enter today's bank balance, which is where your Balance Sheet starts.",
    "open": "Step two: open the file menu, choose Import, then QuickBooks. Argo Books tells you exactly what to export, whether you're on QuickBooks Online or Desktop, and this is where you'll drop the files.",
    "lists": "So head over to QuickBooks. This is QuickBooks Online. For your lists, open Settings, then Export data.",
    "reports_off": "Turn the reports off, because you won't need them here. Then switch to Lists, and export to Excel. That gives you your customers and your vendors.",
    "products": "For your products, open Products and services, and click the export icon.",
    "detail": "Then open Reports, and choose Profit and Loss Detail.",
    "dates": "Set the dates you want to bring across.",
    "export": "Then export it to Excel.",
    "invoices": "Do the same with the Open Invoices report. QuickBooks Desktop has these same reports under different menus, and Argo Books lists those steps for you.",
    "excel": "Choose Excel whenever QuickBooks offers it. These reports have headings and subtotals mixed in with the data, like these, and Argo Books reads them most reliably from an Excel file.",
    "archive": "While you're in QuickBooks, also save your Profit and Loss and your Balance Sheet as PDFs, for every year you have. That's your permanent archive, and it stays yours whatever happens to the subscription.",
    "drop": "Step three: back in Argo Books, drop all your files in here together. The order doesn't matter.",
    "review": "Argo Books reads each file and works out what every column means. You can check what it found, then accept.",
    "tidy": "There's no mapping spreadsheet to build, and the files don't need tidying first.",
    "count": "Here's the Profit and Loss report. 126 transactions in, and none skipped.",
    "save": "When it's done, save. Your books are now a file on your own computer.",
    "reports": "Step four, and please don't skip this one: prove it worked. Open Reports, and run the Income Statement for the same period.",
    "proof": "Now compare these totals with the same report in QuickBooks. Revenue matches, and expenses match, right down to the cent.",
    "double": "If a number doesn't match, undo the import, check the report covers the same dates, and run it again. That's why you do this before you cancel anything.",
    "owed": "The four unpaid invoices came across too, with 1,445 dollars still owed. The dashboard counts money received, so its revenue is lower by exactly those invoices.",
    "honest": "Now the honest part. Your chart of accounts and your journal entries don't come across, because Argo Books works out its reports from your transactions. Employee pay history doesn't come across either, so a switch in the middle of the year means entering that by hand. And keep QuickBooks for a month or so, so you can look things up while you settle in.",
    "free": "Once you're in, the free plan has no time limit. You get 25 invoices a month with card payments, 10 receipt scans, 25 bank statement imports and 100 spreadsheet imports.",
    "premium": "If you outgrow that, Premium is 15 Canadian dollars a month, or 150 a year, for unlimited invoices and a lot more scanning.",
    "ask": "Here's what I'd do. Don't cancel anything yet. Download Argo Books from the first link in the description, make a test company, and run one export through it. It doesn't touch your QuickBooks at all, and it costs nothing to find out.",
}
CHAPTERS = [("empty", "See the result"), ("desktop", "Why people are leaving QuickBooks"), ("fit", "Who this is for"), ("create", "Step 1: Create your company"),
            ("open", "Step 2: See what to export"), ("drop", "Step 3: Drop your files in"), ("reports", "Step 4: Prove it worked"),
            ("honest", "What doesn't come across"), ("free", "What it costs"), ("ask", "What to do next")]
STEPS = {"create": "Step 1 of 4: Create your company", "open": "Step 2 of 4: See what to export", "drop": "Step 3 of 4: Drop your files in", "reports": "Step 4 of 4: Prove it worked"}


class Voice:
    def __init__(self):
        WORK.mkdir(parents=True, exist_ok=True)
        pieces, self.spans, t = [], {}, 0.3
        pieces.append(np.zeros(int(0.3 * RATE), dtype=np.float32))
        for key, text in PARTS.items():
            samples = mv.voice_line(text)
            self.spans[key] = (t, t + len(samples) / RATE)
            pieces.append(samples)
            gap = max(GAP, LINE_LEADS.get(key, 0) - len(samples) / RATE)
            pieces.append(np.zeros(int(gap * RATE), dtype=np.float32))
            t += len(samples) / RATE + gap
        self.samples = np.concatenate(pieces)
        self.length = t
        self.path = WORK / "voice.wav"
        r.write_mono(self.path, self.samples)
        self.words = short.timed_words(self.path, WORK)

    def start(self, key):
        return self.spans[key][0]

    def at(self, key, word, nth=1):
        """When a word of a part is said."""
        first, last = self.spans[key]
        found = [w["at"] for w in self.words if first - 0.3 <= w["at"] <= last + 0.3
                 and w["text"].strip(".,?:;").lower() == word.lower()]
        return found[nth - 1]


def load_take(name):
    log = json.loads((TAKES[name] / f"{name}.json").read_text())
    log["path"] = TAKES[name] / f"{name}.mkv"
    return log


# ---------------------------------------------------------------------------------------------------------------- cards

def backdrop():
    w, h = SIZE
    ramp = np.linspace(0, 1, h)[:, None, None]
    pixels = (np.array([239, 246, 255.0]) * (1 - ramp) + np.array([255, 255, 255.0]) * ramp) * np.ones((1, w, 1))
    return Image.fromarray(pixels.astype(np.uint8)).convert("RGB")


def tick(draw, x, y, colour=GREEN, size=30):
    draw.line([(x, y + size * 0.55), (x + size * 0.36, y + size * 0.9), (x + size, y + size * 0.1)], fill=colour, width=8, joint="curve")


def cross(draw, x, y, colour=RED, size=28):
    draw.line([(x, y), (x + size, y + size)], fill=colour, width=8)
    draw.line([(x + size, y), (x, y + size)], fill=colour, width=8)


def wrapped(draw, text, font, width):
    lines, line = [], ""
    for word in text.split():
        trial = f"{line} {word}".strip()
        if draw.textlength(trial, font=font) > width and line:
            lines.append(line)
            line = word
        else:
            line = trial
    return lines + [line]


def card(title, rows, footnote=None, kicker=None):
    """A title over rows of (mark, text), where the mark is "yes", "no", a number, or None."""
    frame = backdrop()
    draw = ImageDraw.Draw(frame)
    x, y = 170, 150
    if kicker:
        draw.text((x, y), kicker.upper(), font=mv.font("Bold", 32), fill=mv.BLUE, anchor="ls")
        y += 30
    draw.text((x, y + 70), title, font=mv.font("Bold", 74), fill=mv.INK, anchor="ls")
    y += 150
    font = mv.font("Regular", 46)
    for mark, text in rows:
        lines = wrapped(draw, text, font, 1420)
        if mark == "yes":
            tick(draw, x, y + 4)
        elif mark == "no":
            cross(draw, x, y + 4)
        elif mark is not None:
            draw.ellipse((x - 6, y - 4, x + 46, y + 48), fill=mv.BLUE)
            draw.text((x + 20, y + 22), str(mark), font=mv.font("Bold", 30), fill=(255, 255, 255), anchor="mm")
        for n, line in enumerate(lines):
            draw.text((x + 80, y + n * 60), line, font=font, fill=mv.INK, anchor="lt")
        y += 60 * len(lines) + 36
    if footnote:
        draw.text((x, 1000), footnote, font=mv.font("Regular", 28), fill=mv.GREY, anchor="ls")
    return frame


def table_card(title, columns, rows, footnote=None, emphasis=None):
    """Rows of cells under column headings, with one row or column picked out in blue."""
    frame = backdrop()
    draw = ImageDraw.Draw(frame)
    draw.text((170, 210), title, font=mv.font("Bold", 74), fill=mv.INK, anchor="ls")
    x0, x1, y = 170, 1750, 300
    widths = [x1 - x0 - 2 * 430, 430, 430]
    xs = [x0, x0 + widths[0], x0 + widths[0] + widths[1]]
    head = mv.font("Bold", 38)
    for x, text in zip(xs[1:], columns[1:]):
        draw.text((x + 400, y + 46), text, font=head, fill=mv.BLUE if text == emphasis else mv.GREY, anchor="rm")
    draw.text((xs[0], y + 46), columns[0], font=head, fill=mv.GREY, anchor="lm")
    y += 92
    for row in rows:
        draw.line((x0, y, x1, y), fill=LINE, width=3)
        draw.text((xs[0], y + 62), row[0], font=mv.font("Regular", 48), fill=mv.INK, anchor="lm")
        for x, text, name in zip(xs[1:], row[1:], columns[1:]):
            draw.text((x + 400, y + 62), text, font=mv.font("Bold", 50), fill=mv.BLUE if name == emphasis else mv.INK, anchor="rm")
        y += 124
    draw.line((x0, y, x1, y), fill=LINE, width=3)
    if footnote:
        draw.text((170, 1000), footnote, font=mv.font("Regular", 28), fill=mv.GREY, anchor="ls")
    return frame


def report_sheet():
    """The top of the Profit and Loss Detail export, as a spreadsheet shows it, with its heading and subtotal rows picked out."""
    sheet = load_workbook(WORK / "exports" / "Profit and Loss Detail.xlsx", data_only=True).active
    rows = [[("" if c is None else c) for c in row] for row in sheet.iter_rows(values_only=True)]
    total = next(i for i, row in enumerate(rows) if str(row[0]).startswith("Total for Hardware"))
    shown = rows[1:3] + rows[4:11] + rows[total - 3:total + 3]
    frame = backdrop()
    draw = ImageDraw.Draw(frame)
    first_column = round(max(draw.textlength(str(row[0]), font=mv.font("Bold", 25)) for row in shown)) + 30
    columns = [(0, first_column), (1, 220), (2, 220), (3, 90), (4, 340), (7, 150), (8, 150)]
    draw.text((960, 90), "A QuickBooks report is not a plain table", font=mv.font("Bold", 58), fill=mv.INK, anchor="mm")
    x0, y0, row_h = 20, 150, 56
    width = sum(c[1] for c in columns)
    x0 = (1920 - width) // 2
    regular, bold = mv.font("Regular", 25), mv.font("Bold", 25)
    for n, row in enumerate(shown):
        y = y0 + n * row_h
        first = str(row[0])
        structural = first != "" and n > 1 and not first.startswith("Transaction")
        is_total = first.startswith("Total") or first == "Net Income"
        draw.rectangle((x0, y, x0 + width, y + row_h), fill=(254, 243, 199) if is_total else (219, 234, 254) if structural else (255, 255, 255))
        x = x0
        for index, column_width in columns:
            value = row[index] if index < len(row) else ""
            text = f"{value:,.2f}" if isinstance(value, (int, float)) and index >= 7 else str(value)
            draw.text((x + 10, y + row_h / 2), text[:34], font=bold if structural or n < 3 else regular, fill=mv.INK, anchor="lm")
            x += column_width
        draw.line((x0, y + row_h, x0 + width, y + row_h), fill=LINE, width=2)
    x = x0
    for _, column_width in columns:
        draw.line((x, y0, x, y0 + len(shown) * row_h), fill=LINE, width=2)
        x += column_width
    draw.line((x, y0, x, y0 + len(shown) * row_h), fill=LINE, width=2)
    key_y = y0 + len(shown) * row_h + 44
    draw.rectangle((470, key_y - 16, 502, key_y + 16), fill=(219, 234, 254), outline=LINE)
    draw.text((516, key_y), "Headings", font=mv.font("Regular", 30), fill=mv.INK, anchor="lm")
    draw.rectangle((690, key_y - 16, 722, key_y + 16), fill=(254, 243, 199), outline=LINE)
    draw.text((736, key_y), "Subtotals", font=mv.font("Regular", 30), fill=mv.INK, anchor="lm")
    draw.text((1500, key_y), "Example report for a made-up company", font=mv.font("Regular", 28), fill=mv.GREY, anchor="rm")
    return frame


def end_card():
    frame = backdrop().convert("RGBA")
    draw = ImageDraw.Draw(frame)
    mark = Image.open(mv.ASSETS / "logo.png").convert("RGBA")
    mark = mark.resize((780, round(780 * mark.height / mark.width)), Image.LANCZOS)
    frame.alpha_composite(mark, ((1920 - 780) // 2, 300))
    draw.text((960, 560), "Run one export through it. It costs nothing to find out.", font=mv.font("SemiBold", 48), fill=mv.INK, anchor="mm")
    draw.text((960, 650), "argorobots.com", font=mv.font("Bold", 70), fill=mv.BLUE, anchor="mm")
    draw.text((960, 750), "Free for Windows, Mac and Linux. Link in the description.", font=mv.font("Regular", 36), fill=mv.GREY, anchor="mm")
    return frame.convert("RGB")


def statement_still(reveal_revenue, reveal_expenses):
    """The app's Income Statement through a slow camera, with its two totals outlined as the voice reaches them."""
    source = Image.open(WORK / "takes" / "statement.png").convert("RGB")
    boxes = [((700, 1130, 2440, 1184), reveal_revenue), ((700, 1452, 2440, 1506), reveal_expenses)]

    def frame_at(t):
        image = source.copy()
        draw = ImageDraw.Draw(image)
        for box, at in boxes:
            if t >= at:
                draw.rounded_rectangle(box, radius=10, outline=mv.BLUE, width=7)
        p = r.ease(t / 10)
        width = 1960 - 140 * p
        height = width * 9 / 16
        centre = (1560, 985 + 40 * p)
        crop = (centre[0] - width / 2, centre[1] - height / 2, centre[0] + width / 2, centre[1] + height / 2)
        return image.resize(SIZE, Image.LANCZOS, box=crop)
    return frame_at


# ---------------------------------------------------------------------------------------------------------------- edit

def glide(log, mark, offset, click, wait, seconds):
    """A cursor that rests where the take left it at a mark, then eases to where the take clicks at `click`."""
    start = log["frames"][round((log["marks"][mark] + offset) * FPS)]
    end = log["frames"][round(click * FPS)]

    def cursor(age):
        p = r.ease((age - wait) / seconds)
        return [start[0] + (end[0] - start[0]) * p, start[1] + (end[1] - start[1]) * p, 0, None]
    return cursor


def shot_list(v, totals):
    S = v.start

    def take(at, name, mark, offset, keys, until=None, step=None):
        shot = {"at": at, "take": name, "from": (mark, offset), "keys": keys, "step": step}
        if until:
            shot["until"] = until
        return shot

    def clip(at, name, first, last, start_box, end_box):
        return {"at": at, "clip": name, "from": first, "until": last, "boxes": (start_box, end_box), "step": "open"}

    def full(mark, offset=0.0):
        return [(mark, offset, 1.0, 1600, 900)]

    income, expenses = totals
    log = load_take("quickbooks")
    report_click = max(e["t"] for e in log["events"] if e["kind"] == "click" and e["t"] < log["marks"]["page_reports"])
    income_at, totals_at = v.at("reports", "Income") - 0.1, v.at("proof", "totals")
    fit = card("Is this right for you?", [
        ("yes", "You run a small business and do your own books"),
        ("yes", "You want your books on your own computer"),
        ("no", "You need payroll outside Canada"),
        ("no", "Several people work in the same books at once")])
    desktop = card("QuickBooks Desktop 2023", [
        ("no", "Live technical support"), ("no", "Desktop Payroll"), ("no", "Desktop Payments"),
        ("no", "Online bank feeds"), ("no", "Security updates")],
        footnote="Services ended May 31, 2026. Source: Intuit's QuickBooks Desktop service discontinuation policy.", kicker="Since May 31, 2026")
    online = table_card("QuickBooks Online list prices", ["Plan", "Per month", "Per year"], [
        ("Simple Start", "$38", "$456"), ("Essentials", "$85", "$1,020"), ("Plus", "$140", "$1,680"), ("Advanced", "$340", "$4,080")],
        footnote="US dollars, from quickbooks.intuit.com/pricing on Nov 1, 2026. Intuit also discounts the first 3 months.")
    promise = card("In this video", [(1, "Get your data out of QuickBooks"), (2, "Bring it into free software"), (3, "Prove nothing went missing"), (4, "See what doesn't come across")])
    archive = card("Before you cancel anything", [
        ("yes", "Profit and Loss, as a PDF, for every year"), ("yes", "Balance Sheet, as a PDF, for every year"),
        ("yes", "Keep the folder in two places")], kicker="Your permanent archive")
    proof = table_card("Does it match?", ["", "QuickBooks", "Argo Books"], [
        ("Total revenue", income, "$33,262.00"), ("Total expenses", expenses, "$6,985.06")],
        footnote="A made-up company of 126 transactions. Compare your own report before you rely on the import.", emphasis="Argo Books")
    double = card("A number doesn't match?", [(1, "Undo the import"), (2, "Check the report covers the same dates"), (3, "Run it again")])
    honest = card("What doesn't come across", [
        ("no", "Your chart of accounts"), ("no", "Your journal entries"), ("no", "Employee pay history"), ("yes", "Keep QuickBooks for a month while you settle in")])
    free = card("The free plan, with no time limit", [
        ("yes", "25 invoices a month, with card payments"), ("yes", "10 receipt scans a month"), ("yes", "25 bank statement imports a month"), ("yes", "100 spreadsheet imports a month")])
    price = table_card("What it costs", ["", "Per month", "Per year"], [
        ("Argo Books Free", "$0", "$0"), ("Argo Books Premium", "$15 CAD", "$150 CAD"), ("QuickBooks Online Plus", "$140 USD", "$1,680 USD")],
        footnote="QuickBooks list price from quickbooks.intuit.com/pricing on November 1, 2026. Before tax.")

    shots = [
        take(0.0, "quickbooks", "empty", 0.0, [("empty", 0.0, 1.19, 1858, 760), ("empty", 4.0, 1.3, 1950, 700)], until=("empty", 2.2)),
        take(S("full") - 0.1, "quickbooks", "page_dashboard", 0.3, [("page_dashboard", 0.3, 1.19, 1858, 760), ("page_dashboard", 4.0, 1.26, 1920, 720)], until=("page_revenue", -1.0)),
        take(v.at("full", "suppliers") - 0.15, "quickbooks", "page_suppliers", 0.2, [("page_suppliers", 0.2, 1.19, 1858, 760)], until=("page_reports", -1.0)),
        take(v.at("full", "sales") - 0.15, "quickbooks", "page_revenue", 0.2, [("page_revenue", 0.2, 1.19, 1858, 760)], until=("page_expenses", -1.0)),
        take(v.at("full", "unpaid") - 0.15, "quickbooks", "page_invoices", 0.2, [("page_invoices", 0.2, 1.19, 1858, 760)], until=("page_customers", -1.0)),
        {"at": S("match") - 0.1, "still": statement_still(0.3, 0.9)},
        {"at": S("promise") - 0.1, "still": promise},
        {"at": S("desktop") - 0.1, "still": desktop},
        {"at": S("online") - 0.1, "still": online},
        {"at": S("fit") - 0.1, "still": fit},
        take(S("create") - 0.1, "quickbooks", "click_create", -0.2, [("wizard", 0.0, 1.0, 1600, 900), ("wizard", 1.0, 1.75, 1600, 880)], until=("click_finish", 1.6), step="create"),
        take(S("open") - 0.1, "quickbooks", "click_file", -0.6, [("click_file", -0.6, 1.0, 1600, 900), ("choices", 0.0, 1.3, 1600, 920), ("online_steps", 0.0, 1.5, 1600, 900), ("online_steps", 6.5, 1.62, 1600, 880)], until=("online_steps", 1.9), step="open"),
        clip(S("lists") - 0.1, "lists", 1.2, 8.0, WHOLE, WHOLE),
        {**clip(S("reports_off") - 0.1, "lists", 14.5, 29.5, WHOLE, WHOLE), "speed": 1.7},
        clip(S("products") - 0.1, "products", 0.0, 6.0, WHOLE, (1000, 0, 1400, 788)),
        {**clip(S("detail") - 0.1, "pnl_one_tight", 2.0, 29.0, WHOLE, WHOLE), "speed": ONE_TAKE_SPEED},
        clip(S("invoices") - 0.1, "invoices", 6.6, 10.0, (300, 100, 1800, 1012), (560, 240, 1300, 731)),
        take(v.at("invoices", "QuickBooks") - 0.1, "quickbooks", "desktop_steps", 0.0, [("desktop_steps", 0.0, 1.5, 1600, 900), ("desktop_steps", 7.0, 1.7, 1600, 840)], until=("drag", -0.1), step="open"),
        {"at": S("excel") - 0.1, "still": report_sheet()},
        {"at": S("archive") - 0.1, "still": archive, "step": "open"},
        take(S("drop") - 0.1, "quickbooks", "drag", -0.2, [("drag", 0.0, 1.45, 1600, 960), ("dropped", 0.5, 1.6, 1600, 1060)], until=("click_continue", 0.2), step="drop"),
        take(S("review") - 0.1, "quickbooks", "review_1", -3.2, [("review_1", -3.2, 1.0, 1600, 900), ("review_1", -0.2, 1.65, 1600, 900)], until=("result_1", 1.3), step="drop"),
        take(S("tidy") - 0.1, "quickbooks", "review_2", 0.0, [("review_2", 0.0, 1.6, 1600, 900)], until=("result_2", 1.6), step="drop"),
        take(S("count") - 0.1, "quickbooks", "result_4", -0.6, [("result_4", -0.6, 1.2, 1600, 900), ("result_4", 1.6, 1.75, 1600, 860)], until=("result_4", 2.6), step="drop"),
        take(S("save") - 0.1, "quickbooks", "imported", -0.4, [("imported", -0.4, 1.0, 1600, 900), ("click_save", 0.0, 1.3, 1200, 500)], until=("click_save", 1.0), step="drop"),
        {**take(S("reports") - 0.1, "quickbooks", "click_save", 1.0, full("click_save", 1.0), until=("click_save", 1.04), step="reports"),
         "cursor": glide(log, "click_save", 1.0, report_click, v.at("reports", "Open") - S("reports") + 0.1 - GLIDE, GLIDE)},
        take(v.at("reports", "Open") + 0.1, "quickbooks", "page_reports", report_click + 0.3 - log["marks"]["page_reports"],
             [("page_reports", report_click + 0.3 - log["marks"]["page_reports"], 1.0, 1600, 900)], until=("page_reports", 3.6), step="reports"),
        {"at": income_at, "still": statement_still(totals_at - income_at, totals_at - income_at + 0.5)},
        {"at": v.at("proof", "Revenue") - 0.1, "still": proof, "step": "reports"},
        {"at": S("double") - 0.1, "still": double, "step": "reports"},
        take(S("owed") - 0.1, "quickbooks", "page_invoices", 0.3, [("page_invoices", 0.3, 1.19, 1858, 760), ("page_invoices", 3.0, 1.45, 1950, 780)], until=("page_customers", -1.0), step="reports"),
        take(v.at("owed", "dashboard") - 0.1, "quickbooks", "page_dashboard", 0.3, [("page_dashboard", 0.3, 1.6, 1530, 470)], until=("page_revenue", -1.0), step="reports"),
        {"at": S("honest") - 0.1, "still": honest},
        {"at": S("free") - 0.1, "still": free},
        take(v.at("free", "receipt") - 0.1, "receipt", "scan_done", 0.5, full("scan_done", 0.5), until=("suggested", 2.0)),
        take(v.at("free", "bank") - 0.1, "bank", "rows", 0.4, [("rows", 0.4, 1.0, 1600, 900), ("rows", 3.0, 1.35, 1600, 860)]),
        {"at": S("premium") - 0.1, "still": price},
        {"at": S("ask") - 0.1, "still": end_card()},
    ]
    shots.sort(key=lambda s: s["at"])
    outro = float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(r.OUTRO)],
                                 capture_output=True, text=True, check=True).stdout)
    shots.append({"at": v.length + 1.0, "outro": True})
    shots.append({"at": v.length + 1.0 + outro, "stop": True})
    return shots


def carried_files():
    """What the cursor carries while the exports are dragged in: a small stack of Excel files."""
    w, h, pad = 300, 320, 24
    card_image = Image.new("RGBA", (w + 2 * pad, h + 2 * pad), (0, 0, 0, 0))
    draw = ImageDraw.Draw(card_image)
    for n in (2, 1, 0):
        x, y = pad + n * 12, pad + n * 12
        draw.rounded_rectangle((x, y, x + w - 24, y + h - 24), radius=18, fill=(255, 255, 255, 255), outline=(148, 163, 184, 255), width=3)
    draw.rounded_rectangle((pad + 40, pad + 40, pad + 140, pad + 80), radius=8, fill=(22, 163, 74, 255))
    draw.text((pad + 90, pad + 60), "XLSX", font=mv.font("Bold", 24), fill=(255, 255, 255, 255), anchor="mm")
    draw.text((pad + 40, pad + 130), "5 reports", font=mv.font("Bold", 42), fill=mv.INK + (255,), anchor="lm")
    draw.text((pad + 40, pad + 190), "from QuickBooks", font=mv.font("Regular", 28), fill=mv.GREY + (255,), anchor="lm")
    return card_image


def clip_frames(name, start, count, speed=1.0):
    """Frames of a screen recording of QuickBooks from `start` on, with the last one held once it runs out."""
    width, height = CLIP_SIZE
    process = subprocess.Popen([mv.FFMPEG, "-v", "error", "-ss", f"{start:.3f}", "-i", str(WORK / "clips" / f"{name}.mp4"), "-vf", f"setpts=PTS/{speed},fps={FPS}",
                                "-frames:v", str(count), "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], stdout=subprocess.PIPE)
    last = None
    for _ in range(count):
        data = process.stdout.read(width * height * 3)
        if len(data) == width * height * 3:
            last = data
        yield last
    process.stdout.close()
    process.wait()


def clip_image(pixels, shot, p):
    """A frame of a recording through a window that drifts from one framing to the other, so a held frame never looks stuck."""
    a, b = shot["boxes"]
    e = r.ease(p)
    x, y, w, h = (u + (v - u) * e for u, v in zip(a, b))
    h = w * 9 / 16
    x, y = min(x, CLIP_SIZE[0] - w), min(y, CLIP_SIZE[1] - h)
    return Image.frombuffer("RGB", CLIP_SIZE, pixels, "raw", "RGB", 0, 1).resize(SIZE, Image.LANCZOS, box=(x, y, x + w, y + h))


def badge(frame, text):
    draw = ImageDraw.Draw(frame)
    font = mv.font("SemiBold", 30)
    width = draw.textlength(text, font=font)
    x, y = 36, 1006
    draw.rounded_rectangle((x, y, x + width + 52, y + 58), radius=29, fill=(17, 24, 39))
    draw.text((x + 26, y + 29), text, font=font, fill=(255, 255, 255), anchor="lm")


def still_frame(shot, age):
    still = shot["still"]
    return still(age) if callable(still) else still


def render(shots, path):
    encoder = subprocess.Popen([mv.FFMPEG, "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-video_size", f"{SIZE[0]}x{SIZE[1]}",
                                "-framerate", str(FPS), "-i", "-", "-c:v", "libx264", "-preset", "slow", "-crf", "17",
                                "-pix_fmt", "yuv420p", str(path)], stdin=subprocess.PIPE)
    last, done, spoken = None, 0, []
    for shot, following in zip(shots, shots[1:]):
        first, stop = round(shot["at"] * FPS), round(following["at"] * FPS)
        count = stop - first
        step = STEPS.get(shot.get("step"))
        if "outro" in shot:
            process = subprocess.Popen([mv.FFMPEG, "-v", "error", "-i", str(r.OUTRO), "-vf", f"scale={SIZE[0]}:{SIZE[1]}", "-frames:v", str(count),
                                        "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], stdout=subprocess.PIPE)
            size = SIZE[0] * SIZE[1] * 3
            for _ in range(count):
                data = process.stdout.read(size)
                if len(data) == size:
                    previous = data
                encoder.stdin.write(previous)
            process.stdout.close()
            process.wait()
            continue
        if "clip" in shot:
            speed = shot.get("speed", 1.0)
            played = min(count, round((shot["until"] - shot["from"]) / speed * FPS))
            source = clip_frames(shot["clip"], shot["from"], played, speed)
        if "take" in shot:
            take = load_take(shot["take"])
            painter = mv.Painter("wide", take, [], [])
            painter.carry = carried_files()
            start = round((take["marks"][shot["from"][0]] + shot["from"][1]) * FPS)
            end = shot.get("until")
            played = count if not end else min(count, max(1, round((take["marks"][end[0]] + end[1]) * FPS) - start))
            part = {"wide": shot["keys"], "frames": count}
            source = mv.take_frames(take, start, played)
            spoken.append((shot["at"], start / FPS, played / FPS, take["events"]))
        for age in range(count):
            if "take" in shot:
                if age < played:
                    pixels = next(source)
                index = start + min(age, played - 1)
                if "cursor" in shot:
                    take["frames"][index] = shot["cursor"](age / FPS)
                frame = painter.app_frame(pixels, part, 0, index, (start + age) / FPS).convert("RGB")
            elif "clip" in shot:
                if age < played:
                    pixels = next(source)
                frame = clip_image(pixels, shot, age / max(1, count - 1))
            else:
                frame = still_frame(shot, age / FPS)
            if last is not None and age < FADE_FRAMES:
                frame = Image.blend(last, frame, (age + 1) / (FADE_FRAMES + 1))
            clean = frame
            if step:
                frame = frame.copy()
                badge(frame, step)
            encoder.stdin.write(frame.tobytes())
            done += 1
        last = clean
        print(f"  {shot['at']:6.1f} s done", flush=True)
    encoder.stdin.close()
    encoder.wait()
    return spoken


def mix(v, shots, spoken, total):
    n = int(total * RATE) + RATE
    voice = np.pad(v.samples, (0, max(0, n - len(v.samples))))[:n]

    def rms(x):
        return np.sqrt(np.mean(x * x) + 1e-12)

    held = mv.window_sum(np.abs(voice) > 10 ** (-40 / 20), 0.3) > 0.5
    track = mv.decode(mv.MUSIC).mean(axis=1)
    while len(track) < n:
        track = np.concatenate([track, track])
    bed = track[:n]
    level = rms(voice[held]) / rms(bed) * 10 ** (MUSIC_BETWEEN_DB / 20)
    duck = mv.smooth(np.where(held, 10 ** ((MUSIC_UNDER_VOICE_DB - MUSIC_BETWEEN_DB) / 20), 1.0), 0.2)
    outro_at = int((v.length + 1.0) * RATE)
    fade = np.clip((outro_at + 1.5 * RATE - np.arange(n)) / (2.5 * RATE), 0, 1)
    bed = bed * level * duck * fade

    effects = np.zeros(n)
    for at, start, played, events in spoken:
        for event in events:
            kind = {"click": "click", "drop": "drop"}.get(event["kind"])
            if kind and start <= event["t"] < start + played:
                s = mv.sound(kind) * 10 ** (mv.SOUND_GAIN_DB[kind] / 20)
                i = int((at + event["t"] - start) * RATE)
                effects[i:i + len(s)] += s[: max(0, n - i)]
    for key, word in (("match", "cent"), ("proof", "cent")):
        s = mv.sound("chime") * 10 ** (mv.SOUND_GAIN_DB["chime"] / 20)
        i = int(v.at(key, word) * RATE)
        effects[i:i + len(s)] += s[: max(0, n - i)]

    tail = np.zeros(n)
    outro = mv.decode(r.OUTRO, channels=1)[:, 0]
    tail[outro_at:outro_at + len(outro)] = outro[: max(0, n - outro_at)]

    def db(x):
        return 20 * np.log10(rms(x))
    print(f"levels (RMS): voice {db(voice[held]):.1f} dB, music under it {db(bed[held]):.1f} dB, between lines {db(bed[~held & (fade > 0.99)]):.1f} dB")
    mono = voice + effects + bed + tail
    return np.stack([mono, mono], axis=1)


def write_captions(path, v):
    """The script itself, a short line at a time, timed by how far through its part each line falls."""
    def stamp(seconds):
        ms = int(round(seconds * 1000))
        return f"{ms // 3600000:02}:{ms // 60000 % 60:02}:{ms // 1000 % 60:02},{ms % 1000:03}"

    blocks = []
    for key, text in PARTS.items():
        first, last = v.spans[key]
        lines, line = [], []
        for word in text.split():
            line.append(word)
            if len(line) >= 10 or word.endswith((".", "?", ":")):
                lines.append(" ".join(line))
                line = []
        if line:
            lines.append(" ".join(line))
        done = 0
        for caption in lines:
            start = first + (last - first) * done / len(text)
            done += len(caption) + 1
            blocks.append((start, first + (last - first) * min(1, done / len(text)), caption))
    path.write_text("\n".join(f"{n}\n{stamp(a)} --> {stamp(b)}\n{c}\n" for n, (a, b, c) in enumerate(blocks, 1)), encoding="utf-8")


def clock(seconds):
    return f"{int(seconds) // 60}:{int(seconds) % 60:02}"


def main():
    v = Voice()
    if sys.argv[1:2] == ["voice"]:
        for key, (first, last) in v.spans.items():
            print(f"{first:6.1f} {last - first:5.1f} s  {key}")
        print(f"voice {v.length:.1f} s")
        print("heard:", " ".join(w["text"] for w in v.words))
        return
    sheet = load_workbook(WORK / "exports" / "Profit and Loss Detail.xlsx", data_only=True).active
    rows = [row for row in sheet.iter_rows(values_only=True) if str(row[0]).startswith("Total for")]
    amounts = {row[0]: row[7] for row in rows}
    totals = (f"${amounts['Total for Income']:,.2f}", f"${amounts['Total for Expenses']:,.2f}")
    shots = shot_list(v, totals)
    if sys.argv[1:2] == ["frame"]:
        folder = WORK / "check"
        folder.mkdir(exist_ok=True)
        for second in map(float, sys.argv[2:]):
            shot = next(s for s, f in zip(shots, shots[1:]) if s["at"] <= second < f["at"])
            age = second - shot["at"]
            if "take" in shot:
                take = load_take(shot["take"])
                start = round((take["marks"][shot["from"][0]] + shot["from"][1]) * FPS)
                end = shot.get("until")
                played = 10 ** 6 if not end else round((take["marks"][end[0]] + end[1]) * FPS) - start
                index = start + min(round(age * FPS), played - 1)
                painter = mv.Painter("wide", take, [], [])
                painter.carry = carried_files()
                pixels = next(mv.take_frames(take, index, 1))
                if "cursor" in shot:
                    take["frames"][index] = shot["cursor"](age)
                frame = painter.app_frame(pixels, {"wide": shot["keys"], "frames": 1}, 0, index, (start + round(age * FPS)) / FPS).convert("RGB")
            elif "clip" in shot:
                speed = shot.get("speed", 1.0)
                played = round((shot["until"] - shot["from"]) / speed * FPS)
                index = min(round(age * FPS), played - 1)
                following = next(f for f in shots if f["at"] > shot["at"])
                pixels = next(clip_frames(shot["clip"], shot["from"] + index * speed / FPS, 1))
                frame = clip_image(pixels, shot, age / (following["at"] - shot["at"]))
            elif "still" in shot:
                frame = still_frame(shot, age)
            else:
                continue
            if STEPS.get(shot.get("step")):
                frame = frame.copy()
                badge(frame, STEPS[shot["step"]])
            frame.save(folder / f"f{second:06.1f}.png")
        return
    picture = WORK / "picture.mp4"
    spoken = render(shots, picture)
    total = shots[-1]["at"]
    mixed = WORK / "mix.wav"
    mv.write_wav(mixed, mix(v, shots, spoken, total))
    OUT_FILE.parent.mkdir(parents=True, exist_ok=True)
    mv.master(mixed, picture, OUT_FILE)
    write_captions(OUT_FILE.with_suffix(".srt"), v)
    meter = subprocess.run([mv.FFMPEG, "-hide_banner", "-nostats", "-i", str(OUT_FILE), "-af", "ebur128=peak=true", "-f", "null", "-"],
                           capture_output=True, text=True).stderr
    summary = meter[meter.rindex("Summary:"):]
    print(f"{OUT_FILE.name}: {total:.1f} s, {summary.split('I:')[1].split('LUFS')[0].strip()} LUFS, "
          f"true peak {summary.split('Peak:')[1].split('dBFS')[0].strip()} dBFS")
    for key, name in CHAPTERS:
        print(f"{clock(0 if key == 'empty' else v.start(key))} - {name}")


if __name__ == "__main__":
    main()
