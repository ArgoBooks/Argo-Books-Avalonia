"""Make the vertical Short "Don't charge $30 an hour": why $60,000 a year divided by a full-time year's hours is the wrong
rate. The 2,080 hours of a working year shrink as the weeks off and the unpaid admin come out, the money the billable hours
have to cover is added up, and the two rates are compared. It ends on the free hourly rate calculator on argorobots.com.
Narrated by the Azure voice, with big captions timed to each word.

Usage: python hourly_short.py

The calculator take is recorded with `node record_generator.js hourly work/hourly_short/takes`, and the figures on the cards
are the ones it shows for that example. The finished video goes to work/videos/.
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
WORK = HERE / "work" / "hourly_short"
TAKES = WORK / "takes"
OUT_FILE = HERE / "work" / "videos" / "short_hourly_rate.mp4"
LINES = [
    ("Want 60,000 dollars a year as a freelancer? Don't charge 30 dollars an hour.", 450),
    ("A full-time year is 2,080 hours.", 300),
    ("Take off six weeks for holidays and sick days, and it's 1,840.", 400),
    ("But quotes, emails, invoices and finding clients are work nobody pays for. Bill 60 percent of your time, and you're down to 1,104 hours.", 450),
    ("Those hours have to pay your business costs, say 12,000 dollars, and your tax.", 400),
    ("So to take home 60,000, you need to bill 92,000. That's 83 dollars an hour.", 450),
    ("Charge 32 dollars an hour instead, and you'd take home about 18,000.", 450),
    ("There's a free hourly rate calculator on the Argo Books website,", 200),
    ("so you can put in your own numbers.", 450),
    ("It's linked below.", 0),
]
SCRIPT = " ".join(text for text, _ in LINES)
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 125.0
VX, VY, VW, VH = short.VIEW
AMBER = (245, 158, 11)
PALE = (226, 232, 240)
COUNT = 0.8


def load_take(name):
    log = json.loads((TAKES / f"{name}.json").read_text())
    log["path"] = TAKES / f"{name}.mkv"
    return log


def hook_card(base, crossed_at):
    """The quick sum everyone does, crossed out as the voice says not to charge it."""
    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 120, VY + 820
        qs.panel(draw, top, bottom)
        draw.text((540, top + 130), "$60,000 a year", font=mv.font("Bold", 92), fill=mv.INK + (255,), anchor="ms")
        draw.text((540, top + 260), "÷ 2,080 hours", font=mv.font("Bold", 78), fill=(124, 135, 158, 255), anchor="ms")
        draw.line((180, top + 320, 900, top + 320), fill=(203, 213, 225, 255), width=4)
        draw.text((540, top + 470), "≈ $30 an hour", font=mv.font("Bold", 100), fill=mv.INK + (255,), anchor="ms")

        def cross(d):
            d.line((150, top + 430, 930, top + 400), fill=r.RED + (255,), width=16)
            d.rounded_rectangle((360, top + 540, 720, top + 640), radius=24, fill=r.RED + (255,))
            d.text((540, top + 590), "WRONG", font=mv.font("Bold", 64), fill=(255, 255, 255, 255), anchor="mm")
        qs.appear(out, t, crossed_at, cross)
        return out
    return frame


def hours_card(base, off_at, bill_at):
    """A working year as 52 weeks, each a block of its hours: the weeks off go pale, then the unpaid share of every
    remaining week turns amber, with the hours left counting down beside them."""
    cols, rows = 13, 4
    size, gap = 58, 10
    left = (1080 - cols * size - (cols - 1) * gap) // 2

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 100, VY + 820
        qs.panel(draw, top, bottom)
        off = r.ease((t - off_at) / COUNT)
        unpaid = r.ease((t - bill_at) / COUNT)
        hours = 2080 - 240 * off - 736 * unpaid
        label = "HOURS YOU CAN BILL" if t >= bill_at else "AFTER 6 WEEKS OFF" if t >= off_at else "HOURS IN A FULL-TIME YEAR"
        draw.text((540, top + 90), label, font=mv.font("Bold", 38), fill=(124, 135, 158, 255), anchor="ms")
        colour = qv.GREEN if t >= bill_at + COUNT else mv.INK
        draw.text((540, top + 250), f"{hours:,.0f}", font=mv.font("Bold", 150), fill=colour + (255,), anchor="ms")
        y0 = top + 320
        for n in range(52):
            x = left + (n % cols) * (size + gap)
            y = y0 + (n // cols) * (size + gap)
            if n >= 46:
                shade = tuple(round(a + (b - a) * off) for a, b in zip(mv.BLUE, PALE))
                draw.rounded_rectangle((x, y, x + size, y + size), radius=8, fill=shade + (255,))
                continue
            draw.rounded_rectangle((x, y, x + size, y + size), radius=8, fill=mv.BLUE + (255,))
            if unpaid > 0:
                draw.rounded_rectangle((x, y, x + size, y + round(size * 0.4 * unpaid)), radius=8, fill=AMBER + (255,))
        legend = y0 + rows * (size + gap) + 50
        items = [(mv.BLUE, "Billable")]
        if t >= off_at:
            items.append((PALE, "Weeks off"))
        if t >= bill_at:
            items.append((AMBER, "Unpaid admin"))
        x = 130
        for fill, text in items:
            draw.rounded_rectangle((x, legend - 30, x + 36, legend + 6), radius=6, fill=fill + (255,))
            draw.text((x + 50, legend - 12), text, font=mv.font("SemiBold", 32), fill=mv.INK + (255,), anchor="lm")
            x += 70 + draw.textlength(text, font=mv.font("SemiBold", 32)) + 30
        return out
    return frame


def money_card(base, times):
    """What the billable hours have to bring in, added up row by row, then divided into the rate."""
    rows = [("Take home", "$60,000", mv.INK), ("+ Business costs", "$12,000", r.RED), ("+ Tax", "$20,000", r.RED),
            ("= You must bill", "$92,000", mv.INK), ("÷ Hours you can bill", "1,104", mv.INK)]

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 900
        qs.panel(draw, top, bottom)
        draw.text((130, top + 90), "TO TAKE HOME $60,000", font=mv.font("Bold", 38), fill=(124, 135, 158, 255), anchor="ls")
        for n, ((label, amount, colour), at) in enumerate(zip(rows, times)):
            y = top + 190 + n * 100

            def row(d, y=y, label=label, amount=amount, colour=colour, n=n):
                bold = n == 3
                d.text((130, y), label, font=mv.font("Bold" if bold else "Regular", 46), fill=mv.INK + (255,), anchor="lm")
                d.text((950, y), amount, font=mv.font("Bold", 54), fill=colour + (255,), anchor="rm")
                if n == 2:
                    d.line((130, y + 48, 950, y + 48), fill=(203, 213, 225, 255), width=3)
            qs.appear(out, t, at, row)

        def rate(d):
            d.rounded_rectangle((110, top + 680, 970, top + 810), radius=24, fill=(220, 252, 231, 255))
            d.text((150, top + 745), "Charge at least", font=mv.font("Bold", 44), fill=mv.INK + (255,), anchor="lm")
            d.text((930, top + 745), "$83/hr", font=mv.font("Bold", 72), fill=qv.GREEN + (255,), anchor="rm")
        qs.appear(out, t, times[5], rate)
        return out
    return frame


def compare_card(base, low_at, high_at):
    """The two rates side by side, and what each would really leave in the freelancer's pocket."""
    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 120, VY + 820
        qs.panel(draw, top, bottom)
        draw.text((540, top + 90), "WHAT YOU REALLY TAKE HOME", font=mv.font("Bold", 38), fill=(124, 135, 158, 255), anchor="ms")

        def side(d, x0, x1, rate, keep, fill, accent):
            d.rounded_rectangle((x0, top + 160, x1, top + 620), radius=28, fill=fill + (255,), outline=accent + (255,), width=4)
            middle = (x0 + x1) / 2
            d.text((middle, top + 260), rate, font=mv.font("Bold", 64), fill=mv.INK + (255,), anchor="ms")
            d.text((middle, top + 320), "an hour", font=mv.font("Regular", 36), fill=mv.GREY + (255,), anchor="ms")
            d.text((middle, top + 450), "You keep", font=mv.font("SemiBold", 38), fill=accent + (255,), anchor="ms")
            d.text((middle, top + 540), keep, font=mv.font("Bold", 76), fill=accent + (255,), anchor="ms")
        qs.appear(out, t, low_at, lambda d: side(d, 110, 525, "$32.61", "$18,000", (254, 226, 226), r.RED))
        qs.appear(out, t, high_at, lambda d: side(d, 555, 970, "$83.33", "$60,000", (220, 252, 231), qv.GREEN))
        return out
    return frame


def shot_list(words, total, base):
    starts, n = [], 0
    for text, _ in LINES:
        starts.append(n)
        n += len(text.split())

    def line(k):
        return words[starts[k]]["at"]

    def word(k, text):
        last = starts[k + 1] if k + 1 < len(starts) else len(words)
        return next(w["at"] for w in words[starts[k]:last] if w["text"].strip(".,?:").replace(",", "").lower() == text.lower())

    hook = hook_card(base, word(0, "Don't"))
    hours = hours_card(base, word(2, "six"), word(3, "Bill"))
    money = money_card(base, [line(4) - 0.1, word(4, "12000"), word(4, "tax"), word(5, "bill"), word(5, "That's"), word(5, "83")])
    compare = compare_card(base, line(6) - 0.1, word(6, "18000") + 0.6)

    def still(at, card, title):
        return {"at": at, "still": lambda age: card(at + age), "title": title}

    return [
        still(0.0, hook, "Don't charge $30/hr"),
        still(line(1) - 0.1, hours, "A full-time year"),
        still(line(2) - 0.1, hours, "Minus time off"),
        still(line(3) - 0.1, hours, "Minus unpaid work"),
        still(line(4) - 0.1, money, "What those hours must pay"),
        still(line(5) - 0.1, money, "Your real rate"),
        still(line(6) - 0.1, compare, "The difference"),
        {"at": line(7) - 0.1, "take": "hourly", "from": ("click_income", -0.5), "title": "Free rate calculator",
         "keys": [("click_income", -0.5, 1.3, 1100, 900), ("typed", 0.0, 1.3, 1100, 1050)]},
        {"at": line(8) - 0.1, "take": "hourly", "from": ("look_rate", -0.8), "title": "Your own numbers",
         "keys": [("look_rate", -0.8, 1.3, 2144, 560), ("look_rate", 3.0, 1.35, 2144, 560)]},
        {"at": line(9) - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    inv.prepare(TAKES, "hourly")
    voice_path = WORK / "voice.wav"
    r.write_mono(voice_path, mv.voice_line(inv.spoken(LINES)))
    words = short.phrase_timed_words(short.scripted_words(voice_path, SCRIPT, WORK), voice_path)
    total = max(w["end"] for w in words) + HOLD_END
    chunks = short.balanced_chunks(words, longest_pause=0.6)
    pc.finish(shot_list(words, total, short.background()), chunks, voice_path, WORK, OUT_FILE, MUSIC_FROM,
              load_take=load_take, end_card=inv.end_card_saying("Free calculator linked below"))


if __name__ == "__main__":
    main()
