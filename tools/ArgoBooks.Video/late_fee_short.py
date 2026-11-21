"""Make the vertical Short "Why clients pay you last": a client's pile of 12 bills with money for 8. The bills that cost more
to leave late rise to the top and the viewer's invoice, which has no late fee, sinks below the line. One line of terms on
the invoice gives it a cost of $147.95 over 60 days, and it moves back up. It ends on the free late fee calculator on
argorobots.com. Narrated by the Azure voice, with big captions timed to each word.

Usage: python late_fee_short.py

The figures are the calculator's for a $5,000 invoice 60 days late at 1.5% a month. Its take is recorded with
`node record_generator.js latefee work/late_fee_short/takes`. The finished video goes to work/videos/.
"""
import json
from pathlib import Path

from PIL import ImageDraw

import catches_short as short
import invoice_short as inv
import make_video as mv
import pc_short as pc
import qb_desktop_short as qs
import quickbooks_video as qv
import roundup as r

HERE = Path(__file__).resolve().parent
WORK = HERE / "work" / "late_fee_short"
TAKES = WORK / "takes"
OUT_FILE = HERE / "work" / "videos" / "short_late_fee.mp4"
LINES = [
    ("Your client has 12 bills to pay this week, and money for 8. Which ones get paid first?", 450),
    ("The ones that cost them more to pay late.", 450),
    ("Say your invoice is 5,000 dollars, with no late fee. Paying it 60 days late costs them nothing.", 400),
    ("Now add one line to your invoice: 1.5 percent a month on late payments. The same 60 days costs them 147 dollars 95, and it grows by 2 dollars 47 every day.", 450),
    ("Most businesses never even charge it. Having the line is what moves you up the pile.", 450),
    ("There's a free late fee calculator on the Argo Books website,", 200),
    ("so you can see what a late invoice should cost.", 450),
    ("It's linked below.", 0),
]
SCRIPT = " ".join(text for text, _ in LINES)
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 130.0
VX, VY, VW, VH = short.VIEW
LABEL = (124, 135, 158)
PALE_RED = (254, 226, 226)
PALE_GREEN = (220, 252, 231)
BORDER = (226, 232, 240, 255)
WHITE = (255, 255, 255, 255)
MOVE = 0.9
PAID = 8
# Each bill as (name, amount, has a late fee). The last one is the viewer's invoice.
BILLS = [("Rent", "$3,200", True), ("Payroll", "$8,400", True), ("Credit card", "$1,150", True), ("Electricity", "$420", True),
         ("Supplier", "$2,600", True), ("Equipment loan", "$900", True), ("Insurance", "$310", True), ("Software", "$180", False),
         ("Cleaner", "$240", False), ("Ad agency", "$1,500", False), ("Courier", "$95", False), ("Your invoice", "$5,000", False)]
YOU = len(BILLS) - 1
# Where each bill sits in the pile: as it arrives, sorted by what lateness costs, and after the viewer adds a late fee.
START = [0, 2, 4, 11, 6, 1, 8, 3, 9, 5, 10, 7]
SORTED = list(range(12))
FIXED = [0, 1, 2, 3, 5, 6, 7, 8, 9, 10, 11, 4]


def load_take(name):
    log = json.loads((TAKES / f"{name}.json").read_text())
    log["path"] = TAKES / f"{name}.mkv"
    return log


def slot_xy(slot, top):
    col, row = slot % 2, slot // 2
    return 140 + col * 410, top + 150 + row * 100 + (40 if slot >= PAID else 0)


def pile_card(base, steps):
    """The client's bills in two columns, the first eight above the line that the week's money reaches. `steps` is a list of
    (time, order, tagged) changes: at each time the bills slide to their new places and the viewer's invoice may gain a tag."""
    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 40, VY + 920
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "YOUR CLIENT'S BILLS THIS WEEK", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        draw.text((140, top + 125), "Paid this week", font=mv.font("Bold", 36), fill=qv.GREEN + (255,), anchor="ls")
        cut = top + 150 + 4 * 100
        draw.line((120, cut + 8, 960, cut + 8), fill=r.RED + (255,), width=5)
        draw.text((140, cut + 60), "Waits until next month", font=mv.font("Bold", 36), fill=r.RED + (255,), anchor="ls")
        before, after, tagged, at = steps[0][1], steps[0][1], steps[0][2], -1
        for when, order, tag in steps:
            if t >= when:
                before, after, tagged, at = after, order, tag, when
        k = r.ease((t - at) / MOVE) if at >= 0 else 1
        drawn = sorted(range(len(BILLS)), key=lambda b: b == YOU)
        for b in drawn:
            (x0, y0), (x1, y1) = slot_xy(before[b], top + 40), slot_xy(after[b], top + 40)
            x, y = x0 + (x1 - x0) * k, y0 + (y1 - y0) * k
            name, amount, fee = BILLS[b]
            mine = b == YOU
            box = (x, y, x + 390, y + 84)
            if mine:
                draw.rounded_rectangle(box, radius=16, fill=mv.BLUE + (255,))
            else:
                draw.rounded_rectangle(box, radius=16, fill=(248, 250, 252, 255), outline=BORDER, width=3)
            ink = WHITE if mine else mv.INK + (255,)
            draw.text((x + 22, y + 30), name, font=mv.font("Bold", 32), fill=ink, anchor="lm")
            draw.text((x + 370, y + 30), amount, font=mv.font("Bold", 32), fill=ink, anchor="rm")
            if fee or (mine and tagged):
                draw.rounded_rectangle((x + 22, y + 52, x + 150, y + 76), radius=10, fill=r.RED + (255,))
                draw.text((x + 86, y + 64), "LATE FEE", font=mv.font("Bold", 18), fill=WHITE, anchor="mm")
            elif not mine:
                draw.text((x + 22, y + 64), "no late fee", font=mv.font("SemiBold", 22), fill=LABEL + (255,), anchor="lm")
            else:
                draw.text((x + 22, y + 64), "no late fee", font=mv.font("SemiBold", 22), fill=(219, 234, 254, 255), anchor="lm")
        return out
    return frame


def cost_card(base, nothing_at, line_at, count_at, daily_at):
    """What 60 days of lateness costs the client: nothing without a late fee, then a counter running up to $147.95 with one."""
    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 900
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "PAYING YOUR $5,000 INVOICE 60 DAYS LATE", font=mv.font("Bold", 36), fill=LABEL + (255,), anchor="ms")

        def nothing(d):
            d.rounded_rectangle((110, top + 120, 970, top + 290), radius=24, fill=(241, 245, 249, 255))
            d.text((150, top + 180), "No late fee", font=mv.font("Bold", 48), fill=mv.INK + (255,), anchor="lm")
            d.text((150, top + 240), "costs them", font=mv.font("SemiBold", 40), fill=LABEL + (255,), anchor="lm")
            d.text((930, top + 210), "$0", font=mv.font("Bold", 90), fill=LABEL + (255,), anchor="rm")
        qs.appear(out, t, nothing_at, nothing)

        def fee(d):
            d.rounded_rectangle((110, top + 330, 970, top + 620), radius=24, fill=PALE_RED + (255,))
            d.text((150, top + 395), "\"1.5% a month on late payments\"", font=mv.font("Bold", 42), fill=mv.INK + (255,), anchor="lm")
            run = min(1, max(0, (t - count_at) / 1.6)) if t >= count_at else 0
            d.text((150, top + 470), f"Day {round(60 * run)}", font=mv.font("SemiBold", 44), fill=LABEL + (255,), anchor="lm")
            d.text((930, top + 545), f"${147.95 * run:,.2f}", font=mv.font("Bold", 100), fill=r.RED + (255,), anchor="rm")
        qs.appear(out, t, line_at, fee)

        def daily(d):
            d.text((540, top + 720), "and it grows", font=mv.font("SemiBold", 46), fill=LABEL + (255,), anchor="ms")
            d.text((540, top + 800), "+$2.47 every day", font=mv.font("Bold", 64), fill=r.RED + (255,), anchor="ms")
        qs.appear(out, t, daily_at, daily)
        return out
    return frame


def shot_list(words, total, base):
    starts, n = [], 0
    for text, _ in LINES:
        starts.append(n)
        n += len(text.split())

    def line(k):
        return words[starts[k]]["at"]

    def word(k, text, nth=1):
        last = starts[k + 1] if k + 1 < len(starts) else len(words)
        found = [w["at"] for w in words[starts[k]:last] if w["text"].strip(".,?:").replace(",", "").lower() == text.lower()]
        return found[nth - 1]

    pile = pile_card(base, [(-1, START, False), (line(1), SORTED, False), (word(4, "Having"), FIXED, True)])
    cost = cost_card(base, word(2, "no"), word(3, "line"), word(3, "147"), word(3, "grows"))

    def still(at, card, title):
        return {"at": at, "still": lambda age: card(at + age), "title": title}

    return [
        still(0.0, pile, "Why clients pay you last"),
        still(line(1) - 0.1, pile, "Late fees get paid first"),
        still(line(2) - 0.1, cost, "No late fee"),
        still(line(3) - 0.1, cost, "One line on your invoice"),
        still(line(4) - 0.1, pile, "Up the pile"),
        {"at": line(5) - 0.1, "take": "latefee", "from": ("click_amount", -0.4), "title": "Free late fee calculator",
         "keys": [("click_amount", -0.4, 1.3, 1056, 500), ("typed", 0.0, 1.3, 1056, 500)]},
        {"at": word(5, "website"), "take": "latefee", "from": ("typed", 0.05), "title": "What lateness should cost",
         "keys": [("typed", 0.05, 1.3, 2144, 560), ("look_daily", 1.0, 1.45, 2144, 620)]},
        {"at": line(7) - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    inv.prepare(TAKES, "latefee")
    voice_path = WORK / "voice.wav"
    r.write_mono(voice_path, mv.voice_line(inv.spoken(LINES)))
    words = short.phrase_timed_words(short.scripted_words(voice_path, SCRIPT, WORK), voice_path)
    total = max(w["end"] for w in words) + HOLD_END
    chunks = short.balanced_chunks(words, longest_pause=0.6)
    pc.finish(shot_list(words, total, short.background()), chunks, voice_path, WORK, OUT_FILE, MUSIC_FROM,
              load_take=load_take, end_card=inv.end_card_saying("Free calculator linked below"))


if __name__ == "__main__":
    main()
