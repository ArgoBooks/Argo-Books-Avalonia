"""Make the vertical Short "You charge $40 an hour. What do you really earn?": a quiz for house cleaners. Three answers are
offered, the viewer gets a moment to pick, and the right one, $18.75, is shown as the $40 hour worn down by the unpaid drive,
gas and supplies, and taxes, then as a year of two houses a day. The rate that takes home $50,000 is then shown. It ends on
the free hourly rate calculator on argorobots.com. Narrated by the Azure voice, with big captions timed to each word.

Usage: python cleaner_short.py

Each house is 3 billed hours and 1 hour of driving, with $20 of gas and supplies, and a quarter of the rest set aside for
tax. A year is 2 houses a day, 5 days a week, 48 weeks. Its take is recorded with
`node record_generator.js cleaner work/cleaner_short/takes`. The finished video goes to work/videos/.
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
WORK = HERE / "work" / "cleaner_short"
TAKES = WORK / "takes"
OUT_FILE = HERE / "work" / "videos" / "short_cleaner_quiz.mp4"
LINES = [
    ("House cleaners, quick quiz. You charge 40 dollars an hour. What do you really earn?", 400),
    ("Each house is 3 hours of cleaning, plus an hour of driving.", 400),
    ("Is it A, 30 dollars, B, 18 dollars 75, or C, 26 dollars 25? Lock in your guess.", 1300),
    ("It's B. Just 18 dollars 75.", 450),
    ("You bill 120 dollars, but you're out for 4 hours. That's 30 an hour. Gas and supplies drop it to 25. Taxes take a quarter: 18 dollars 75.", 400),
    ("Clean two houses a day, all year, and you keep just 36 thousand.", 450),
    ("The fix? To take home 50 thousand, charge 53 an hour of cleaning, about 160 a house.", 450),
    ("There's a free hourly rate calculator on the Argo Books website,", 200),
    ("so you can find your real rate.", 450),
    ("It's linked below.", 0),
]
SCRIPT = " ".join(text for text, _ in LINES)
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 115.0
VX, VY, VW, VH = short.VIEW
LABEL = (124, 135, 158)
PALE_RED = (254, 226, 226)
PALE_GREEN = (220, 252, 231)
PALE_BLUE = (219, 234, 254)
SLATE = (100, 116, 139)
ORANGE = (241, 100, 30)
STAR = (250, 204, 21)
PAPER = (241, 245, 249)
WHITE = (255, 255, 255, 255)
GROW = 0.6
ANSWERS = [("A", "$30.00/hr"), ("B", "$18.75/hr"), ("C", "$26.25/hr")]
RIGHT = "B"


def load_take(name):
    log = json.loads((TAKES / f"{name}.json").read_text())
    log["path"] = TAKES / f"{name}.mkv"
    return log


def sparkle(draw, x, y, size):
    s, w = size, size * 0.28
    draw.polygon([(x, y - s), (x + w, y - w), (x + s, y), (x + w, y + w), (x, y + s), (x - w, y + w), (x - s, y), (x - w, y - w)],
                 fill=STAR + (255,))


def house(draw, cx, base_y):
    """A small house with a blue roof, a door and two windows, and sparkles for a fresh clean."""
    draw.rectangle((cx - 110, base_y - 150, cx + 110, base_y), fill=PALE_BLUE + (255,))
    draw.polygon([(cx - 140, base_y - 145), (cx, base_y - 255), (cx + 140, base_y - 145)], fill=mv.BLUE + (255,))
    draw.rounded_rectangle((cx - 28, base_y - 90, cx + 28, base_y), radius=6, fill=ORANGE + (255,))
    for x in (cx - 95, cx + 45):
        draw.rectangle((x, base_y - 125, x + 50, base_y - 75), fill=WHITE)
        draw.line((x + 25, base_y - 125, x + 25, base_y - 75), fill=PALE_BLUE + (255,), width=4)
        draw.line((x, base_y - 100, x + 50, base_y - 100), fill=PALE_BLUE + (255,), width=4)
    sparkle(draw, cx + 125, base_y - 225, 24)
    sparkle(draw, cx + 160, base_y - 175, 14)
    sparkle(draw, cx - 150, base_y - 40, 18)


def quiz_card(base, info_at, option_ats, lock_at, reveal_at):
    """The question, the hours each house takes, the three answers, a bar that runs down while the viewer picks, and the
    right answer lit green."""
    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 925
        qs.panel(draw, top, bottom)
        draw.text((540, top + 80), "YOU CHARGE $40 AN HOUR.", font=mv.font("Bold", 50), fill=mv.INK + (255,), anchor="ms")
        draw.text((540, top + 145), "WHAT DO YOU REALLY EARN?", font=mv.font("Bold", 48), fill=mv.INK + (255,), anchor="ms")
        house(draw, 290, top + 445)

        def made(d):
            d.text((500, top + 275), "3 hours", font=mv.font("Bold", 72), fill=ORANGE + (255,), anchor="ls")
            d.text((500, top + 340), "+ 1 hour of driving,", font=mv.font("SemiBold", 40), fill=LABEL + (255,), anchor="ls")
            d.text((500, top + 390), "each house", font=mv.font("SemiBold", 40), fill=LABEL + (255,), anchor="ls")
        qs.appear(out, t, info_at, made)

        for n, ((letter, amount), at) in enumerate(zip(ANSWERS, option_ats)):
            y = top + 480 + n * 110
            right = letter == RIGHT
            if t < at + 0.3:
                draw.rounded_rectangle((150, y, 930, y + 90), radius=22, fill=PAPER + (255,))
                draw.ellipse((170, y + 13, 234, y + 77), fill=(203, 213, 225, 255))
                draw.text((202, y + 45), letter, font=mv.font("Bold", 40), fill=WHITE, anchor="mm")
                draw.text((890, y + 45), "?", font=mv.font("Bold", 56), fill=(203, 213, 225, 255), anchor="rm")

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
            draw.text((540, top + 840), "B: just $18.75 an hour", font=mv.font("Bold", 46), fill=r.RED + (255,), anchor="ms")
        return out
    return frame


def drop_card(base, drive_at, costs_at, tax_at):
    """The $40 hour as four bars, each shorter than the last once the drive, the gas and supplies, and taxes are counted, with
    what each one lost left showing in pale red."""
    rows = [("You charge", 40.00, mv.BLUE, 0.0), ("Counting the drive", 30.00, SLATE, drive_at),
            ("After gas and supplies", 25.00, ORANGE, costs_at), ("After taxes", 18.75, r.RED, tax_at)]
    scale = 760 / 40

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 925
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "WHAT EACH HOUR REALLY PAYS", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        for n, (name, amount, fill, at) in enumerate(rows):
            if t < at:
                continue
            y = top + 175 + n * 170
            k = r.ease((t - at) / GROW)
            width = (40 - (40 - amount) * k) * scale
            draw.text((160, y), name, font=mv.font("Bold", 44), fill=mv.INK + (255,), anchor="ls")
            draw.rounded_rectangle((160, y + 25, 160 + 40 * scale, y + 105), radius=18, fill=PALE_RED + (255,))
            draw.rounded_rectangle((160, y + 25, 160 + width, y + 105), radius=18, fill=fill + (255,))
            draw.text((160 + width - 24, y + 65), f"${amount:.2f}", font=mv.font("Bold", 54), fill=WHITE, anchor="rm")
        return out
    return frame


def year_card(base, start, kept_at):
    """A year of two houses a day: the houses, the hours out, what the $40 bills, and the much smaller take-home."""
    rows = [("Houses cleaned", "480"), ("Hours out", "1,920"), ("Billed at $40", "$57,600")]

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 925
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "2 HOUSES A DAY, ALL YEAR", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        for n, (name, value) in enumerate(rows):
            if t < start + 0.35 * n:
                continue
            y = top + 190 + n * 105
            draw.text((150, y), name, font=mv.font("SemiBold", 50), fill=mv.INK + (255,), anchor="lm")
            draw.text((930, y), value, font=mv.font("Bold", 56), fill=SLATE + (255,), anchor="rm")
        draw.line((150, top + 470, 930, top + 470), fill=(203, 213, 225, 255), width=4)

        def kept(d):
            d.rounded_rectangle((130, top + 520, 950, top + 800), radius=26, fill=PALE_RED + (255,))
            d.text((540, top + 590), "You keep", font=mv.font("SemiBold", 48), fill=mv.INK + (255,), anchor="ms")
            d.text((540, top + 740), "$36,000", font=mv.font("Bold", 130), fill=r.RED + (255,), anchor="ms")
        qs.appear(out, t, kept_at, kept)
        return out
    return frame


def fix_card(base, rate_at, house_at):
    """Today's $40 against the rate that takes home $50,000, per cleaning hour, per house, a year and per hour out."""
    rows = [("Per cleaning hour", "$40", "$53", rate_at), ("Per house", "$120", "$159", house_at)]
    results = [("A year, kept", "$36,000", "$50,040"), ("Each hour out", "$18.75", "$26.06")]

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 925
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "TO TAKE HOME $50,000", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        draw.text((630, top + 160), "Now", font=mv.font("Bold", 44), fill=mv.INK + (255,), anchor="ms")
        if t >= rate_at:
            draw.text((870, top + 160), "Fix", font=mv.font("Bold", 44), fill=mv.BLUE + (255,), anchor="ms")
        for n, (name, now, fix, at) in enumerate(rows):
            y = top + 250 + n * 85
            draw.text((150, y), name, font=mv.font("SemiBold", 44), fill=mv.INK + (255,), anchor="lm")
            draw.text((690, y), now, font=mv.font("Bold", 46), fill=SLATE + (255,), anchor="rm")
            if t >= at:
                draw.text((930, y), fix, font=mv.font("Bold", 46), fill=mv.BLUE + (255,), anchor="rm")
        draw.line((150, top + 400, 930, top + 400), fill=(203, 213, 225, 255), width=4)
        for n, (name, now, fix) in enumerate(results):
            y = top + 470 + n * 95
            draw.text((150, y), name, font=mv.font("Bold", 44), fill=mv.INK + (255,), anchor="lm")
            draw.text((690, y), now, font=mv.font("Bold", 46), fill=r.RED + (255,), anchor="rm")

            def better(d, y=y, fix=fix):
                d.rounded_rectangle((725, y - 42, 960, y + 42), radius=18, fill=PALE_GREEN + (255,))
                d.text((935, y), fix, font=mv.font("Bold", 46), fill=qv.GREEN + (255,), anchor="rm")
            qs.appear(out, t, house_at + 0.5 + 0.2 * n, better)

        def drive(d):
            d.text((540, top + 780), "Price the drive in", font=mv.font("Bold", 64), fill=qv.GREEN + (255,), anchor="ms")
        qs.appear(out, t, house_at + 1.0, drive)
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
    quiz = quiz_card(base, word(1, "3"), [word(2, "A"), word(2, "B"), word(2, "C")], lock_word["end"], line(3))
    drop = drop_card(base, word(4, "30"), word(4, "25"), word(4, "18"))
    year = year_card(base, line(5), word(5, "36"))
    fix = fix_card(base, word(6, "53"), word(6, "160"))

    def still(at, card, title):
        return {"at": at, "still": lambda age: card(at + age), "title": title}

    return [
        still(0.0, quiz, "House cleaner quiz"),
        still(line(3) - 0.1, quiz, "The answer"),
        still(line(4) - 0.1, drop, "Where the $40 goes"),
        still(line(5) - 0.1, year, "A full year"),
        still(line(6) - 0.1, fix, "The fix"),
        {"at": line(7) - 0.1, "take": "cleaner", "from": ("click_income", -0.4), "title": "Free hourly rate calculator",
         "keys": [("click_income", -0.4, 1.3, 1056, 800), ("typed", 0.0, 1.3, 1056, 1000)]},
        {"at": word(7, "website"), "take": "cleaner", "from": ("priced", 0.05), "title": "Find your real rate",
         "keys": [("priced", 0.05, 1.3, 2144, 760), ("look_rate", 0.6, 1.45, 2144, 700)]},
        {"at": line(9) - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    inv.prepare(TAKES, "cleaner")
    voice_path = WORK / "voice.wav"
    r.write_mono(voice_path, mv.voice_line(inv.spoken(LINES)))
    words = short.phrase_timed_words(short.scripted_words(voice_path, SCRIPT, WORK), voice_path)
    total = max(w["end"] for w in words) + HOLD_END
    chunks = short.balanced_chunks(words, longest_pause=0.6)
    pc.finish(shot_list(words, total, short.background()), chunks, voice_path, WORK, OUT_FILE, MUSIC_FROM,
              load_take=load_take, end_card=inv.end_card_saying("Free calculator linked below"))


if __name__ == "__main__":
    main()
