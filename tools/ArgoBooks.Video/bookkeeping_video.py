"""Make YouTube video 17, "How to Do Your Own Bookkeeping": a small business's bookkeeping routine step by step, from the
first receipt to the reports at the end of the month, shown in Argo Books and narrated by an Azure voice.

Usage:
  python bookkeeping_video.py voice          fetches and measures the voice, and prints when each part starts
  python bookkeeping_video.py frame 12 95    saves single frames at those seconds to work/books/check/, for checking
  python bookkeeping_video.py                renders the video

The takes are recorded from the released app (see README.md): `receipt` into work/books/takes, and `payment`, `matching`
and `reports` into the same folder from a copy of the company the `quickbooks` take saved. `welcome`, `tour`, `bill` and
`salestax` are video 16's. The finished video and its captions go to work/videos/.
"""
import json
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw

import mac_video as mac
import make_video as mv
import qb_desktop_video as qd
import quickbooks_video as qv
import roundup as r

HERE = Path(__file__).resolve().parent
WORK = HERE / "work" / "books"
OUT_FILE = HERE / "work" / "videos" / "video_17_bookkeeping.mp4"
TAKES = {name: WORK / "takes" for name in ("receipt", "payment", "matching", "reports")}
TAKES.update({name: HERE / "work" / "qbvideo" / "takes" for name in ("welcome", "tour", "bill", "salestax")})

PARTS = {
    "hook": "Most small business owners don't need an accountant every week. What they need is a routine. So this is that routine: how to do your own bookkeeping, from the first receipt to the reports at the end of the month, step by step. You don't need any accounting background.",
    "hook2": "I'll show each step in Argo Books, which is free, and which I make. But the steps are the same in any accounting software, or even a spreadsheet.",
    "what": "First, what bookkeeping actually is. It's a record of every dollar that comes into your business, and every dollar that goes out, with the proof for each one.",
    "what2": "Keep that up to date, and three questions answer themselves. Am I making money? Who owes me? And what do I owe? And at tax time, the numbers are already there.",
    "sep": "Step one: keep the business money separate. Open a bank account just for the business, and run every sale and every expense through it.",
    "sep2": "Even where the law doesn't require it, one account means one statement to check, and no guessing later which purchases were for the business.",
    "out": "Step two: record the money going out. Every expense needs four things: the date, the amount, who you paid, and what it was for.",
    "out2": "That last one is its category, like fuel, software or supplies. Categories are how your spending gets sorted at tax time.",
    "out3": "Keep the receipt for every expense, and the easiest habit is to take a photo the moment you get it. Here, I drop the photo in, and the scan fills in the store, the date, the total and each item. I check it, and save it, with the photo attached.",
    "keep": "How long should you keep receipts? In the US, the IRS generally says three years after you file, and longer in some cases. In Canada, the CRA says six years from the end of the tax year they relate to.",
    "in": "Step three: record the money coming in. When you finish a job, send an invoice. It records what you're owed and when it's due, and until it's paid, it stays on your list of money owed to you.",
    "in2": "When the customer pays, record the payment against the invoice. Priya's invoice here was overdue. She's paid now, so I record it, and the invoice is marked paid.",
    "bills": "Step four: keep track of the bills you owe. Some things you pay later, like a supplier's invoice that's due in 30 days. Record those as bills, with a due date, so they don't sneak up on you.",
    "bills2": "Here, I untick Paid on an expense, and it becomes a bill with a due date. The accounts payable aging report then lists what you owe each supplier, and how long it's been waiting.",
    "bank": "Step five: check your books against your bank, once a month. This is the step most people skip, and it's the one that catches mistakes. Download your statement from your bank's website, and compare it with your books. Accountants call this reconciling.",
    "bank2": "Argo Books does the comparing for you. Bring in the statement, and it matches each line to what you've already recorded.",
    "bank3": "This month, everything matched except two lines. One is a transfer to my own savings account. Moving your own money isn't income or an expense, so I ignore it. The other is a fuel stop I never recorded. That's exactly what this check is for: without it, that expense would be missing from my books.",
    "bank4": "When it isn't sure, it suggests a match, and I confirm it with one click. And the calendar shows which months are done. Green is fully matched. October just needs that fuel receipt.",
    "reports": "Step six: read your reports. Once a month, look at two of them. The income statement, also called profit and loss, is your money in minus your money out. It tells you whether you're actually making money.",
    "reports2": "And the accounts receivable aging report is who owes you, and for how long. When something gets old, send a reminder.",
    "tax": "Step seven: put tax money aside. If you charge sales tax, that money was never yours. You're holding it until the return is due. So track what you've collected, what you owe, and when it's due.",
    "tax2": "For income tax, a lot of owners move a share of every payment into a separate savings account, like that transfer earlier, so the bill is never a surprise. Your accountant can tell you what share fits you.",
    "routine": "So here's the whole routine. Every week: record your receipts, and send your invoices. Every month: check your bank statement, record what's been paid, pay your bills, and read your reports. And once a year, hand your accountant those reports.",
    "ask": "Argo Books is free, and it's the first link in the description. There's no account to create, and your books stay on your own computer, on Windows, Mac or Linux.",
}
SECTIONS = {"what", "sep", "out", "in", "bills", "bank", "reports", "tax", "routine"}
CHAPTERS = [("hook", "Do your own bookkeeping"), ("what", "What bookkeeping is"), ("sep", "1. Keep the money separate"),
            ("out", "2. Record money going out"), ("in", "3. Record money coming in"), ("bills", "4. Track the bills you owe"),
            ("bank", "5. Check against your bank"), ("reports", "6. Read your reports"), ("tax", "7. Put tax money aside"),
            ("routine", "The routine")]


def load_take(name):
    log = json.loads((TAKES[name] / f"{name}.json").read_text())
    log["path"] = TAKES[name] / f"{name}.mkv"
    return log


def reveal(title, rows, times, footnote=None, kicker=None):
    """A card whose rows come in one at a time, each on its word."""
    frames = [qv.card(title, rows[:n], footnote, kicker) for n in range(len(rows) + 1)]
    return lambda t: frames[sum(1 for at in times if t >= at)]


def end_card():
    frame = qv.backdrop().convert("RGBA")
    draw = ImageDraw.Draw(frame)
    mark = Image.open(mv.ASSETS / "logo.png").convert("RGBA")
    mark = mark.resize((780, round(780 * mark.height / mark.width)), Image.LANCZOS)
    frame.alpha_composite(mark, ((1920 - 780) // 2, 300))
    draw.text((960, 560), "Free accounting for Windows, Mac and Linux", font=mv.font("SemiBold", 48), fill=mv.INK, anchor="mm")
    draw.text((960, 650), "argorobots.com", font=mv.font("Bold", 70), fill=mv.BLUE, anchor="mm")
    draw.text((960, 750), "No account to create. Your books stay on your own computer.", font=mv.font("Regular", 36), fill=mv.GREY, anchor="mm")
    return frame.convert("RGB")


def shot_list(v):
    S, E, W = v.start, v.end, v.at

    def take(at, name, mark, offset, keys, until=None):
        shot = {"at": at, "take": name, "from": (mark, offset), "keys": keys}
        if until:
            shot["until"] = until
        return shot

    def still(at, image):
        return {"at": at, "still": image}

    def step(key, number, name, kind):
        return still(S(key) - 0.3, mac.section_card(number, name, kind))

    record = reveal("Bookkeeping is a record of", [(None, "Every dollar that comes in"), (None, "Every dollar that goes out"), (None, "The proof for each one")],
                    [W("what", "comes"), W("what", "out"), W("what", "proof")], kicker="What bookkeeping is")
    questions = reveal("Three questions it answers", [(1, "Am I making money?"), (2, "Who owes me?"), (3, "What do I owe?")],
                       [W("what2", "Am"), W("what2", "Who"), W("what2", "What")], kicker="What bookkeeping is")
    separate = reveal("One bank account for the business", [
        ("yes", "Every sale and every expense goes through it"), ("yes", "One statement to check each month"),
        ("no", "No guessing later which purchases were for the business")],
        [S("sep2") - 0.1, W("sep2", "statement"), W("sep2", "guessing")], kicker="Step 1")
    four = reveal("Every expense needs", [(1, "The date"), (2, "The amount"), (3, "Who you paid"), (4, "What it was for: its category")],
                  [W("out", "date"), W("out", "amount"), W("out", "who"), W("out", "what")], kicker="Step 2")
    keeping = reveal("How long to keep receipts", [
        (None, "US: generally 3 years after you file, and longer in some cases (IRS)"),
        (None, "Canada: 6 years from the end of the tax year they relate to (CRA)")],
        [W("keep", "US"), W("keep", "Canada")],
        footnote="Sources: IRS, How long should I keep records?, and CRA, Keeping records. November 2026.", kicker="Step 2")
    aside = reveal("Income tax", [("yes", "Move a share of every payment into a separate savings account"),
                                  ("yes", "Ask your accountant what share fits you")],
                   [W("tax2", "move"), W("tax2", "accountant")], kicker="Step 7")
    routine = reveal("The routine", [
        (None, "Every week: record your receipts, and send your invoices"),
        (None, "Every month: check your bank statement, record what's been paid, pay your bills, and read your reports"),
        (None, "Once a year: hand your accountant those reports")],
        [W("routine", "week"), W("routine", "month"), W("routine", "year")], kicker="Do your own bookkeeping")

    shots = [
        take(0.0, "tour", "dashboard", 0.5, [("dashboard", 0.5, 1.0, 1600, 900), ("dashboard", 6.0, 1.1, 1600, 900)], until=("dashboard", 7.0)),
        take(W("hook", "What") - 0.2, "matching", "calendar", -0.3, [("calendar", -0.3, 1.25, 1830, 1150), ("calendar", 4.5, 1.26, 1830, 1150)], until=("click_missing", -0.4)),
        take(S("hook2") - 0.1, "welcome", "start", 0.0, [("start", 0.0, 1.0, 1600, 900), ("hover_create", 0.0, 1.25, 1450, 950)]),

        still(S("what") - 0.3, record),
        still(S("what2") - 0.1, questions),

        step("sep", 1, "Keep the money separate", "One bank account for the business"),
        still(S("sep2") - 0.2, separate),

        step("out", 2, "Record money going out", "Every expense, with its receipt"),
        still(W("out", "Every") - 0.2, four),
        take(S("out2") - 0.1, "reports", "statement", 0.5, [("statement", 0.5, 1.75, 1570, 1350), ("statement", 6.0, 1.85, 1570, 1380)], until=("statement", 5.3)),
        take(S("out3") - 0.1, "receipt", "drop_zone", -0.6, [("drop_zone", -0.6, 1.3, 1600, 960), ("dropped", 0.0, 1.35, 1600, 960)], until=("scanning", 0.6)),
        take(W("out3", "fills") - 0.3, "receipt", "scan_done", 0.0, [("scan_done", 0.0, 1.15, 1600, 930), ("look_total", 0.0, 1.45, 2200, 600), ("look_items", 0.4, 1.3, 2000, 1250)], until=("click_products", -0.4)),
        take(W("out3", "save") - 0.6, "receipt", "click_create", -0.8, [("click_create", -0.8, 1.12, 2050, 1200), ("saved", 0.2, 1.0, 1600, 900)], until=("saved", 2.5)),
        still(S("keep") - 0.1, keeping),

        step("in", 3, "Record money coming in", "Invoices, and the payments against them"),
        take(W("in", "When") - 0.2, "payment", "invoices", 0.0, [("invoices", 0.0, 1.2, 1850, 1000), ("invoices", 4.0, 1.28, 1850, 1000)], until=("row", -0.2)),
        take(S("in2") - 0.1, "payment", "row", -0.4, [("row", -0.4, 1.55, 1900, 1234), ("click_pay", 0.0, 1.55, 2000, 1234), ("form", 0.0, 1.4, 1600, 940)], until=("click_save", 0.3)),
        take(W("in2", "marked") - 0.4, "payment", "paid", 0.0, [("paid", 0.0, 1.55, 2000, 1234), ("paid", 4.0, 1.6, 2000, 1234)]),

        step("bills", 4, "Track the bills you owe", "What you pay later, with its due date"),
        take(W("bills", "Some") - 0.2, "bill", "expenses", 0.3, [("expenses", 0.3, 1.1, 1700, 950), ("row", 0.0, 1.3, 1700, 1000)], until=("click_edit", -0.2)),
        take(S("bills2") - 0.1, "bill", "scroll", 0.4, [("scroll", 0.4, 1.4, 1600, 1000), ("click_paid", 0.0, 1.7, 1550, 1080)], until=("click_save", 0.3)),
        take(W("bills2", "accounts") - 0.2, "bill", "aging", -0.5, [("aging", -0.5, 1.75, 1568, 1275)]),

        step("bank", 5, "Check against your bank", "Once a month"),
        take(W("bank", "Download") - 0.2, "matching", "page", 0.5, [("page", 0.5, 1.05, 1700, 950), ("click_import", 0.0, 1.15, 2400, 850), ("prompt", 0.3, 1.8, 1600, 930)], until=("click_choose", 0.1)),
        take(S("bank2") - 0.1, "matching", "click_choose", -0.6, [("click_choose", -0.6, 1.8, 1600, 930), ("imported", 0.3, 2.0, 1600, 920), ("matched", 0.0, 1.1, 1700, 1000)], until=("look_matched", 1.0)),
        take(W("bank3", "two") - 0.3, "matching", "look_lines", -0.2, [("look_lines", -0.2, 1.6, 2100, 700), ("transfer", 0.0, 1.45, 1800, 1050)], until=("fuel", -0.6)),
        take(W("bank3", "fuel") - 0.4, "matching", "fuel", -0.4, [("fuel", -0.4, 1.45, 1800, 1300)], until=("scroll", -0.1)),
        take(S("bank4") - 0.1, "matching", "suggested", -0.6, [("suggested", -0.6, 1.4, 1800, 1250), ("click_accept", 1.0, 1.4, 1800, 1250)], until=("click_calendar", -0.2)),
        take(W("bank4", "calendar") - 0.3, "matching", "calendar", -0.4, [("calendar", -0.4, 1.25, 1830, 1150), ("calendar", 4.5, 1.26, 1830, 1150)], until=("click_missing", -0.4)),

        step("reports", 6, "Read your reports", "Once a month"),
        take(W("reports", "income") - 0.3, "reports", "statement", -0.3, [("statement", -0.3, 1.5, 1570, 1100), ("statement", 5.0, 1.62, 1570, 1150)], until=("statement", 5.3)),
        take(S("reports2") - 0.1, "reports", "receivable", -0.3, [("receivable", -0.3, 1.5, 1570, 1050), ("receivable", 5.0, 1.62, 1570, 1050)]),

        step("tax", 7, "Put tax money aside", "Sales tax, and income tax"),
        take(W("tax", "charge") - 0.2, "salestax", "click_sales_tax", -0.6, [("click_sales_tax", -0.6, 1.0, 1600, 900), ("owed", 0.0, 1.2, 1900, 900), ("due", 1.0, 1.35, 2100, 1100)]),
        take(S("tax2") - 0.1, "matching", "click_ignore", 1.0, [("click_ignore", 1.0, 1.6, 1700, 1050), ("click_ignore", 3.5, 1.7, 1700, 1040)], until=("click_ignore", 2.5)),
        still(W("tax2", "accountant") - 0.4, aside),

        still(S("routine") - 0.3, routine),
        still(S("ask") - 0.1, end_card()),
    ]
    shots.sort(key=lambda s: s["at"])
    outro = float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(r.OUTRO)],
                                 capture_output=True, text=True, check=True).stdout)
    shots.append({"at": v.length + 1.0, "outro": True})
    shots.append({"at": v.length + 1.0 + outro, "stop": True})
    return shots


def main():
    v = mac.Voice(PARTS, SECTIONS, WORK)
    if sys.argv[1:2] == ["voice"]:
        for key, (first, last) in v.spans.items():
            print(f"{first:6.1f} {last - first:5.1f} s  {key}")
        print(f"voice {v.length:.1f} s")
        print("heard:", " ".join(w["text"] for w in v.words))
        return
    shots = shot_list(v)
    if sys.argv[1:2] == ["frame"]:
        folder = WORK / "check"
        folder.mkdir(exist_ok=True)
        pages, sprites = qd.Pages(), {}
        for second in map(float, sys.argv[2:]):
            frame = qd.frame_at(shots, second, pages, sprites, load_take)
            if frame is not None:
                frame.save(folder / f"f{second:06.1f}.png")
        return
    picture = WORK / "picture.mp4"
    spoken = qd.render(shots, picture, load_take)
    total = shots[-1]["at"]
    mixed = WORK / "mix.wav"
    mv.write_wav(mixed, mac.mix(v, spoken, total))
    OUT_FILE.parent.mkdir(parents=True, exist_ok=True)
    mv.master(mixed, picture, OUT_FILE)
    mac.write_captions(OUT_FILE.with_suffix(".srt"), v, PARTS)
    meter = subprocess.run([mv.FFMPEG, "-hide_banner", "-nostats", "-i", str(OUT_FILE), "-af", "ebur128=peak=true", "-f", "null", "-"],
                           capture_output=True, text=True).stderr
    summary = meter[meter.rindex("Summary:"):]
    print(f"{OUT_FILE.name}: {total:.1f} s, {summary.split('I:')[1].split('LUFS')[0].strip()} LUFS, "
          f"true peak {summary.split('Peak:')[1].split('dBFS')[0].strip()} dBFS")
    for key, name in CHAPTERS:
        print(f"{mac.clock(0 if key == 'hook' else v.start(key))} - {name}")


if __name__ == "__main__":
    main()
