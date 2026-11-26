"""Make the vertical Short "You drive 20,000 miles a year delivering. If you never log them, what does that cost you?": a
quiz for delivery drivers. Three answers are offered, the viewer gets a moment to pick, and the right one, $3,712 a year,
is shown as the 2026 IRS rates on each half of the year, the deduction they add up to and the tax it saves. A claim with no
log is then stamped denied, and a simple mileage log is shown as the fix. It ends on the free mileage calculator on
argorobots.com. Narrated by the Azure voice, with big captions timed to each word.

Usage: python driver_short.py

The IRS rate for 2026 is 72.5 cents a mile up to 30 June and 76 cents from 1 July, and the example drives 10,000 miles in
each half with a 25 percent marginal tax rate. Its take is recorded with
`node record_generator.js driver work/driver_short/takes`. The finished video goes to work/videos/.
"""
import json
from pathlib import Path

from PIL import Image, ImageDraw

import catches_short as short
import invoice_short as inv
import make_video as mv
import pc_short as pc
import qb_desktop_short as qs
import quickbooks_video as qv
import roundup as r

HERE = Path(__file__).resolve().parent
WORK = HERE / "work" / "driver_short"
TAKES = WORK / "takes"
OUT_FILE = HERE / "work" / "videos" / "short_driver_quiz.mp4"
LINES = [
    ("Delivery drivers, quick quiz. You drive 20 thousand miles a year, and never log them. What does that cost you?", 400),
    ("Your tax rate is 25 percent.", 400),
    ("Is it A, 1,500 dollars, B, 3,700, or C, 14,850? Lock in your guess.", 1300),
    ("It's B. About 3,700 dollars, every year.", 450),
    ("The IRS rate is 72 and a half cents a mile, and 76 since July. That's 14,850 dollars off your income, and 3,712 less tax.", 400),
    ("But without a log, the IRS can deny the whole claim.", 450),
    ("The fix? Log each trip: the date, the miles and what it was for.", 450),
    ("There's a free mileage calculator on the Argo Books website,", 200),
    ("so you know what your miles are worth.", 450),
    ("It's linked below.", 0),
]
SCRIPT = " ".join(text for text, _ in LINES)
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 20.0
VX, VY, VW, VH = short.VIEW
LABEL = (124, 135, 158)
PALE_RED = (254, 226, 226)
PALE_GREEN = (220, 252, 231)
PALE_BLUE = (219, 234, 254)
SLATE = (100, 116, 139)
ORANGE = (241, 100, 30)
PAPER = (241, 245, 249)
LINE = (203, 213, 225)
WHITE = (255, 255, 255, 255)
ANSWERS = [("A", "$1,500"), ("B", "$3,700"), ("C", "$14,850")]
RIGHT = "B"
LOG = [("Nov 3", "84"), ("Nov 4", "112"), ("Nov 5", "67"), ("Nov 6", "95")]


def load_take(name):
    log = json.loads((TAKES / f"{name}.json").read_text())
    log["path"] = TAKES / f"{name}.mkv"
    return log


def car(draw, cx, base_y):
    """A small blue car seen from the side with a delivery bag on its roof and speed lines behind it."""
    ink = mv.INK + (255,)
    draw.rounded_rectangle((cx - 40, base_y - 215, cx + 40, base_y - 150), radius=10, fill=ORANGE + (255,))
    draw.polygon([(cx - 95, base_y - 90), (cx - 60, base_y - 152), (cx + 60, base_y - 152), (cx + 105, base_y - 90)], fill=mv.BLUE + (255,))
    draw.polygon([(cx - 75, base_y - 95), (cx - 50, base_y - 138), (cx - 5, base_y - 138), (cx - 5, base_y - 95)], fill=PALE_BLUE + (255,))
    draw.polygon([(cx + 7, base_y - 95), (cx + 7, base_y - 138), (cx + 52, base_y - 138), (cx + 82, base_y - 95)], fill=PALE_BLUE + (255,))
    draw.rounded_rectangle((cx - 150, base_y - 100, cx + 150, base_y - 30), radius=22, fill=mv.BLUE + (255,))
    draw.rectangle((cx + 128, base_y - 82, cx + 146, base_y - 66), fill=(250, 204, 21, 255))
    for x in (cx - 88, cx + 88):
        draw.ellipse((x - 34, base_y - 68, x + 34, base_y), fill=ink)
        draw.ellipse((x - 14, base_y - 48, x + 14, base_y - 20), fill=LINE + (255,))
    for n, y in enumerate((base_y - 120, base_y - 80, base_y - 40)):
        draw.line((cx - 215 + 15 * n, y, cx - 172, y), fill=LINE + (255,), width=8)


def quiz_card(base, info_at, option_ats, lock_at, reveal_at):
    """The question, the miles and tax rate, the three answers, a bar that runs down while the viewer picks, and the right
    answer lit green."""
    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 925
        qs.panel(draw, top, bottom)
        draw.text((540, top + 80), "20,000 MILES. NO LOG.", font=mv.font("Bold", 50), fill=mv.INK + (255,), anchor="ms")
        draw.text((540, top + 145), "WHAT DOES IT COST YOU?", font=mv.font("Bold", 48), fill=mv.INK + (255,), anchor="ms")
        car(draw, 330, top + 440)

        def made(d):
            d.text((520, top + 275), "25% tax", font=mv.font("Bold", 72), fill=ORANGE + (255,), anchor="ls")
            d.text((520, top + 340), "on each extra", font=mv.font("SemiBold", 40), fill=LABEL + (255,), anchor="ls")
            d.text((520, top + 390), "dollar you make", font=mv.font("SemiBold", 40), fill=LABEL + (255,), anchor="ls")
        qs.appear(out, t, info_at, made)

        for n, ((letter, amount), at) in enumerate(zip(ANSWERS, option_ats)):
            y = top + 480 + n * 110
            right = letter == RIGHT
            if t < at + 0.3:
                draw.rounded_rectangle((150, y, 930, y + 90), radius=22, fill=PAPER + (255,))
                draw.ellipse((170, y + 13, 234, y + 77), fill=LINE + (255,))
                draw.text((202, y + 45), letter, font=mv.font("Bold", 40), fill=WHITE, anchor="mm")
                draw.text((890, y + 45), "?", font=mv.font("Bold", 56), fill=LINE + (255,), anchor="rm")

            def option(d, y=y, letter=letter, amount=amount, right=right):
                fill, ink = PAPER, mv.INK
                if t >= reveal_at:
                    fill, ink = (PALE_GREEN, qv.GREEN) if right else (PALE_RED, r.RED)
                d.rounded_rectangle((150, y, 930, y + 90), radius=22, fill=fill + (255,))
                d.ellipse((170, y + 13, 234, y + 77), fill=(ink if t >= reveal_at else mv.BLUE) + (255,))
                d.text((202, y + 45), letter, font=mv.font("Bold", 40), fill=WHITE, anchor="mm")
                d.text((890, y + 45), amount, font=mv.font("Bold", 56), fill=ink + (255,), anchor="rm")
            qs.appear(out, t, at, option)

        if lock_at <= t < reveal_at:
            left = 1 - (t - lock_at) / max(0.1, reveal_at - lock_at)
            draw.rounded_rectangle((150, top + 815, 930, top + 835), radius=10, fill=(226, 232, 240, 255))
            draw.rounded_rectangle((150, top + 815, 150 + max(20, 780 * left), top + 835), radius=10, fill=mv.BLUE + (255,))
        elif t >= reveal_at:
            draw.text((540, top + 840), "B: about $3,700 a year", font=mv.font("Bold", 46), fill=r.RED + (255,), anchor="ms")
        return out
    return frame


def worth_card(base, first_at, second_at, total_at, tax_at):
    """Each half of 2026 at its own IRS rate, the deduction they add up to, and the tax that deduction saves."""
    halves = [("Jan to Jun", "10,000 miles at 72.5¢", "$7,250", first_at), ("Jul to Dec", "10,000 miles at 76¢", "$7,600", second_at)]

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 925
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "WHAT YOUR MILES ARE WORTH", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        for n, (name, detail, amount, at) in enumerate(halves):
            y = top + 170 + n * 125

            def half(d, y=y, name=name, detail=detail, amount=amount):
                d.text((150, y), name, font=mv.font("Bold", 46), fill=mv.INK + (255,), anchor="ls")
                d.text((150, y + 48), detail, font=mv.font("SemiBold", 36), fill=LABEL + (255,), anchor="ls")
                d.text((930, y + 20), amount, font=mv.font("Bold", 54), fill=SLATE + (255,), anchor="rs")
            qs.appear(out, t, at, half)
        draw.line((150, top + 420, 930, top + 420), fill=LINE + (255,), width=4)

        def total(d):
            d.text((150, top + 495), "Deduction", font=mv.font("Bold", 50), fill=mv.INK + (255,), anchor="ls")
            d.text((930, top + 495), "$14,850", font=mv.font("Bold", 62), fill=mv.BLUE + (255,), anchor="rs")
        qs.appear(out, t, total_at, total)

        def saved(d):
            d.rounded_rectangle((130, top + 560, 950, top + 800), radius=26, fill=PALE_GREEN + (255,))
            d.text((540, top + 635), "Tax it saves you, at 25%", font=mv.font("SemiBold", 46), fill=mv.INK + (255,), anchor="ms")
            d.text((540, top + 760), "$3,712.50", font=mv.font("Bold", 110), fill=qv.GREEN + (255,), anchor="ms")
        qs.appear(out, t, tax_at, saved)
        return out
    return frame


def denied_card(base, stamp_at):
    """A vehicle expense claim with no log behind it, stamped DENIED in red."""
    stamp = Image.new("RGBA", (520, 190), (0, 0, 0, 0))
    sd = ImageDraw.Draw(stamp)
    sd.rounded_rectangle((10, 10, 510, 180), radius=24, outline=r.RED + (255,), width=12)
    sd.text((260, 98), "DENIED", font=mv.font("Bold", 112), fill=r.RED + (255,), anchor="mm")
    stamp = stamp.rotate(8, expand=True, resample=Image.BICUBIC)

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 925
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "NO LOG?", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        draw.rounded_rectangle((190, top + 130, 890, top + 720), radius=20, fill=WHITE, outline=LINE + (255,), width=4)
        draw.text((240, top + 220), "Vehicle expenses", font=mv.font("Bold", 50), fill=mv.INK + (255,), anchor="ls")
        rows = [("Business miles", "20,000"), ("Deduction", "$14,850"), ("Mileage log", "none")]
        for n, (name, value) in enumerate(rows):
            y = top + 300 + n * 85
            draw.text((240, y), name, font=mv.font("SemiBold", 42), fill=LABEL + (255,), anchor="ls")
            draw.text((840, y), value, font=mv.font("Bold", 46), fill=(r.RED if value == "none" else mv.INK) + (255,), anchor="rs")
            draw.line((240, y + 25, 840, y + 25), fill=PAPER + (255,), width=3)
        k = r.ease((t - stamp_at) / 0.2)
        if k > 0:
            layer = stamp.copy()
            layer.putalpha(layer.getchannel("A").point(lambda a: int(a * k)))
            size = 1.25 - 0.25 * k
            layer = layer.resize((round(layer.width * size), round(layer.height * size)), Image.BICUBIC)
            out.alpha_composite(layer, (540 - layer.width // 2, top + 600 - layer.height // 2))

        def note(d):
            d.text((540, top + 805), "The IRS wants a record of every trip", font=mv.font("SemiBold", 40), fill=mv.INK + (255,), anchor="ms")
        qs.appear(out, t, stamp_at + 0.5, note)
        return out
    return frame


def log_card(base, date_at, miles_at, why_at):
    """A mileage log filling in column by column: the date, the miles and what each trip was for."""
    cols = [("Date", 170, "la", date_at), ("Miles", 560, "ra", miles_at), ("What for", 620, "la", why_at)]

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 925
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "THE FIX: A MILEAGE LOG", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        draw.rounded_rectangle((140, top + 120, 940, top + 640), radius=20, fill=PAPER + (255,))
        for n in range(len(LOG) + 1):
            y = top + 220 + n * 100
            draw.line((160, y, 920, y), fill=LINE + (255,), width=3)
        for c, (head, x, anchor, at) in enumerate(cols):
            lit = t >= at
            draw.text((x, top + 150), head, font=mv.font("Bold", 44), fill=(mv.BLUE if lit else LABEL) + (255,), anchor=anchor)
            for n, (date, miles) in enumerate(LOG):
                value = (date, miles, "Deliveries")[c]

                def cell(d, x=x, anchor=anchor, n=n, value=value):
                    d.text((x, top + 250 + n * 100), value, font=mv.font("Bold", 46), fill=mv.INK + (255,), anchor=anchor)
                qs.appear(out, t, at + 0.12 * n, cell)

        def go(d):
            d.text((540, top + 770), "Log it as you go", font=mv.font("Bold", 64), fill=qv.GREEN + (255,), anchor="ms")
        qs.appear(out, t, why_at + 0.6, go)
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

    lock_word = [w for w in words[starts[2]:starts[3]] if w["text"].strip(".,?:").lower() == "guess"][0]
    quiz = quiz_card(base, word(1, "25"), [word(2, "A"), word(2, "B"), word(2, "C")], lock_word["end"], line(3))
    worth = worth_card(base, word(4, "72"), word(4, "76"), word(4, "14850"), word(4, "3712"))
    denied = denied_card(base, word(5, "deny"))
    log = log_card(base, word(6, "date"), word(6, "miles"), word(6, "what"))

    def still(at, card, title):
        return {"at": at, "still": lambda age: card(at + age), "title": title}

    return [
        still(0.0, quiz, "Delivery driver quiz"),
        still(line(3) - 0.1, quiz, "The answer"),
        still(line(4) - 0.1, worth, "What your miles are worth"),
        still(line(5) - 0.1, denied, "No log, no deduction"),
        still(line(6) - 0.1, log, "The fix"),
        {"at": line(7) - 0.1, "take": "driver", "from": ("click_first", -0.4), "title": "Free mileage calculator",
         "keys": [("click_first", -0.4, 1.3, 1056, 900), ("second_done", 0.0, 1.3, 1056, 950)]},
        {"at": word(7, "website"), "take": "driver", "from": ("rate_done", 0.05), "title": "What your miles are worth",
         "keys": [("rate_done", 0.05, 1.3, 2144, 780), ("look_saved", 0.6, 1.4, 2144, 820)]},
        {"at": line(9) - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    inv.prepare(TAKES, "driver")
    voice_path = WORK / "voice.wav"
    r.write_mono(voice_path, mv.voice_line(inv.spoken(LINES)))
    words = short.phrase_timed_words(short.scripted_words(voice_path, SCRIPT, WORK), voice_path)
    total = max(w["end"] for w in words) + HOLD_END
    chunks = short.balanced_chunks(words, longest_pause=0.6)
    pc.finish(shot_list(words, total, short.background()), chunks, voice_path, WORK, OUT_FILE, MUSIC_FROM,
              load_take=load_take, end_card=inv.end_card_saying("Free calculator linked below"))


if __name__ == "__main__":
    main()
