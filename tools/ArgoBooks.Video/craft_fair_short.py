"""Make the vertical Short "$392 at a craft fair. Good day?": the day's takings printed as a till receipt, with each cost of
the day printing below it until what is left works out at $7.76 an hour. A better day of 24 sales is set beside it, and the
number of sales the day needs is shown. It ends on the free craft fair calculator on argorobots.com. Narrated by the Azure
voice, with big captions timed to each word.

Usage: python craft_fair_short.py

The figures are the calculator page's own worked example. Its take is recorded with
`node record_generator.js craftfair work/craft_fair_short/takes`. The finished video goes to work/videos/.
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
WORK = HERE / "work" / "craft_fair_short"
TAKES = WORK / "takes"
OUT_FILE = HERE / "work" / "videos" / "short_craft_fair.mp4"
LINES = [
    ("You come home from a craft fair with 392 dollars in the cash tin. Good day? Let's check the receipt.", 450),
    ("The booth was 120 dollars. Gas and parking, 45. Table hire and lunch, 20.", 400),
    ("And your 14 sales used 137 dollars 20 of materials.", 450),
    ("That leaves 69 dollars 80, for a nine hour day. That's 7 dollars 76 an hour.", 450),
    ("Make 24 sales instead of 14, and the same day pays you 27 dollars 98 an hour.", 450),
    ("So before you book a booth, know your number. Here, it takes 11 sales just to get your money back.", 450),
    ("There's a free craft fair calculator on the Argo Books website,", 200),
    ("so you can check a market before you pay for it.", 450),
    ("It's linked below.", 0),
]
SCRIPT = " ".join(text for text, _ in LINES)
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 110.0
VX, VY, VW, VH = short.VIEW
LABEL = (124, 135, 158)
PALE_RED = (254, 226, 226)
PALE_GREEN = (220, 252, 231)
SLATE = (100, 116, 139)
PAPER = (255, 255, 255, 255)
DOTS = (203, 213, 225, 255)
WHITE = (255, 255, 255, 255)
GROW = 0.6
ROW = 78


def load_take(name):
    log = json.loads((TAKES / f"{name}.json").read_text())
    log["path"] = TAKES / f"{name}.mkv"
    return log


def receipt_card(base, good_at, row_ats, left_at, hourly_at):
    """A till receipt for the day: the cash tin at the top, each cost printing below it, then what is left and what it paid
    an hour. The paper grows as each line prints."""
    rows = [("Booth", "-120.00"), ("Gas and parking", "-45.00"), ("Table and lunch", "-20.00"), ("Materials (14 sales)", "-137.20")]
    left, right = 150, 930
    mono = mv.font("Bold", 46)

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top = VY + 50
        printed = sum(1 for at in row_ats if t >= at)
        end = top + 330 + printed * ROW + (190 if t >= left_at else 0) + (120 if t >= hourly_at else 0)
        edge = (214, 222, 232, 255)
        draw.rectangle((106, top - 4, 974, end), fill=edge)
        for x in range(110, 970, 40):
            draw.polygon([(x - 4, end), (x + 20, end + 25), (x + 44, end)], fill=edge)
        draw.rectangle((110, top, 970, end), fill=PAPER)
        for x in range(110, 970, 40):
            draw.polygon([(x, end), (x + 20, end + 20), (x + 40, end)], fill=PAPER)
        draw.text((540, top + 75), "CRAFT FAIR DAY", font=mv.font("Bold", 50), fill=mv.INK + (255,), anchor="ms")
        draw.text((540, top + 120), "* * * * * * * * * * * *", font=mv.font("Bold", 30), fill=DOTS, anchor="ms")
        draw.text((left, top + 200), "Cash tin", font=mv.font("Bold", 58), fill=mv.INK + (255,), anchor="ls")
        draw.text((right, top + 200), "$392.00", font=mv.font("Bold", 66), fill=qv.GREEN + (255,), anchor="rs")
        if good_at <= t < row_ats[0]:
            draw.text((540, top + 285), "Good day?", font=mv.font("Bold", 52), fill=LABEL + (255,), anchor="ms")
        for n, ((name, amount), at) in enumerate(zip(rows, row_ats)):
            y = top + 300 + n * ROW

            def row(d, y=y, name=name, amount=amount):
                d.text((left, y + 50), name, font=mv.font("SemiBold", 44), fill=mv.INK + (255,), anchor="ls")
                d.text((right, y + 50), amount, font=mono, fill=r.RED + (255,), anchor="rs")
            qs.appear(out, t, at, row)
        y = top + 300 + printed * ROW

        def kept(d):
            d.line((left, y + 25, right, y + 25), fill=DOTS, width=4)
            d.text((left, y + 105), "Left for you", font=mv.font("Bold", 52), fill=mv.INK + (255,), anchor="ls")
            d.text((right, y + 105), "$69.80", font=mv.font("Bold", 62), fill=mv.INK + (255,), anchor="rs")
            d.text((left, y + 165), "for 9 hours of work", font=mv.font("SemiBold", 40), fill=LABEL + (255,), anchor="ls")
        qs.appear(out, t, left_at, kept)

        def hourly(d):
            d.rounded_rectangle((left - 20, y + 200, right + 20, y + 300), radius=22, fill=PALE_RED + (255,))
            d.text((left + 10, y + 250), "You made", font=mv.font("Bold", 48), fill=mv.INK + (255,), anchor="lm")
            d.text((right - 10, y + 250), "$7.76/hr", font=mv.font("Bold", 64), fill=r.RED + (255,), anchor="rm")
        qs.appear(out, t, hourly_at, hourly)
        return out
    return frame


def days_card(base, better_at):
    """The same day with 14 sales and with 24, as two columns of pay an hour."""
    scale = 420 / 28

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 900
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "SAME FAIR, SAME 9 HOURS", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        floor = top + 620
        for n, (sales, rate, fill, at) in enumerate([(14, 7.76, r.RED, -1), (24, 27.98, qv.GREEN, better_at)]):
            x = 230 + n * 360
            grow = 1 if at < 0 else r.ease((t - at) / GROW)
            if grow > 0:
                draw.rounded_rectangle((x, floor - rate * scale * grow, x + 260, floor), radius=16, fill=fill + (255,))
            if at < 0 or t >= at + GROW:
                draw.text((x + 130, floor - rate * scale - 24), f"${rate:.2f}/hr", font=mv.font("Bold", 54), fill=fill + (255,), anchor="ms")
            if at < 0 or t >= at:
                draw.text((x + 130, floor + 70), f"{sales} sales", font=mv.font("Bold", 50), fill=mv.INK + (255,), anchor="ms")
        return out
    return frame


def needed_card(base, number_at):
    """The 24 sales of the good day as dots, with the first 11, the ones that only win back the day's costs, picked out."""
    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 900
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "SALES THIS FAIR NEEDS", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        lit = int(min(11, max(0, (t - number_at) / 0.08 + 1))) if t >= number_at else 0
        for n in range(24):
            x, y = 200 + (n % 6) * 136, top + 160 + (n // 6) * 120
            fill = r.RED if n < lit else (226, 232, 240)
            draw.ellipse((x - 44, y - 44, x + 44, y + 44), fill=fill + (255,))
            if n < lit:
                draw.text((x, y), str(n + 1), font=mv.font("Bold", 38), fill=WHITE, anchor="mm")

        def label(d):
            d.rounded_rectangle((110, top + 680, 970, top + 790), radius=24, fill=PALE_RED + (255,))
            d.text((150, top + 735), "Just to get your $185 back", font=mv.font("Bold", 42), fill=mv.INK + (255,), anchor="lm")
            d.text((930, top + 735), "11", font=mv.font("Bold", 70), fill=r.RED + (255,), anchor="rm")
        qs.appear(out, t, number_at + 0.9, label)
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

    receipt = receipt_card(base, word(0, "Good"), [word(1, "120"), word(1, "45"), word(1, "20"), word(2, "137")],
                           word(3, "69"), word(3, "7"))
    days = days_card(base, word(4, "27"))
    needed = needed_card(base, word(5, "11"))

    def still(at, card, title):
        return {"at": at, "still": lambda age: card(at + age), "title": title}

    return [
        still(0.0, receipt, "$392 at a craft fair"),
        still(line(1) - 0.1, receipt, "What the day cost"),
        still(line(3) - 0.1, receipt, "What's left"),
        still(line(4) - 0.1, days, "A better day"),
        still(line(5) - 0.1, needed, "Know your number"),
        {"at": line(6) - 0.1, "take": "craftfair", "from": ("click_booth", -0.4), "title": "Free craft fair calculator",
         "keys": [("click_booth", -0.4, 1.3, 1056, 600), ("costs_done", 0.0, 1.3, 1056, 700)]},
        {"at": word(6, "website"), "take": "craftfair", "from": ("sold", 0.05), "title": "Check a market first",
         "keys": [("sold", 0.05, 1.3, 2144, 700), ("look_hourly", 1.0, 1.45, 2144, 1150)]},
        {"at": line(8) - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    inv.prepare(TAKES, "craftfair")
    voice_path = WORK / "voice.wav"
    r.write_mono(voice_path, mv.voice_line(inv.spoken(LINES)))
    words = short.phrase_timed_words(short.scripted_words(voice_path, SCRIPT, WORK), voice_path)
    total = max(w["end"] for w in words) + HOLD_END
    chunks = short.balanced_chunks(words, longest_pause=0.6)
    pc.finish(shot_list(words, total, short.background()), chunks, voice_path, WORK, OUT_FILE, MUSIC_FROM,
              load_take=load_take, end_card=inv.end_card_saying("Free calculator linked below"))


if __name__ == "__main__":
    main()
