"""Make the vertical Short "You spend $400 on a cutting machine. How many $4 stickers until it's paid off?": a quiz for
sticker makers. Three answers are offered, the viewer gets a moment to pick, and the right one, 220 stickers, is shown as
what Etsy's fees, the postage and the sticker leave of each $4 sale, then as 220 envelopes to mail. Packs of five are then
shown to pay the machine off in 39 orders. It ends on the free break-even calculator on argorobots.com. Narrated by the
Azure voice, with big captions timed to each word.

Usage: python sticker_short.py

The fees are Etsy's US ones, each rounded to the cent as Etsy charges them: $0.20 to list, 6.5% of the sale and 3% plus
$0.25 to process the payment. Its take is recorded with `node record_generator.js sticker work/sticker_short/takes`. The
finished video goes to work/videos/.
"""
import json
import math
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
WORK = HERE / "work" / "sticker_short"
TAKES = WORK / "takes"
OUT_FILE = HERE / "work" / "videos" / "short_sticker_quiz.mp4"
LINES = [
    ("Sticker makers, quick quiz. You spend 400 dollars on a cutting machine and printer. How many 4 dollar stickers until it's paid off?", 400),
    ("Each one costs 35 cents to make, and shipping is free.", 400),
    ("Is it A, 100, B, 160, or C, 220? Lock in your guess.", 1300),
    ("It's C. 220 stickers.", 450),
    ("Etsy's fees take 83 cents. The envelope and stamp take a dollar. The sticker takes 35 cents. You keep 1 dollar 82.", 400),
    ("That's 220 envelopes to stuff before you see any profit.", 450),
    ("The fix? Sell packs of 5 for 15 dollars. Each order keeps 10 dollars 37, so just 39 orders pay it off.", 450),
    ("There's a free break-even calculator on the Argo Books website,", 200),
    ("so you know your number before you buy.", 450),
    ("It's linked below.", 0),
]
SCRIPT = " ".join(text for text, _ in LINES)
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 55.0
VX, VY, VW, VH = short.VIEW
LABEL = (124, 135, 158)
PALE_RED = (254, 226, 226)
PALE_GREEN = (220, 252, 231)
SLATE = (100, 116, 139)
ORANGE = (241, 100, 30)
PURPLE = (147, 51, 234)
STAR = (250, 204, 21)
PAPER = (241, 245, 249)
WHITE = (255, 255, 255, 255)
GROW = 0.6
ANSWERS = [("A", "100 stickers"), ("B", "160 stickers"), ("C", "220 stickers")]
RIGHT = "C"


def load_take(name):
    log = json.loads((TAKES / f"{name}.json").read_text())
    log["path"] = TAKES / f"{name}.mkv"
    return log


def star_points(cx, cy, outer, inner):
    return [(cx + (outer if n % 2 == 0 else inner) * math.cos(math.radians(-90 + 36 * n)),
             cy + (outer if n % 2 == 0 else inner) * math.sin(math.radians(-90 + 36 * n))) for n in range(10)]


def sticker(draw, cx, cy, k=0.78):
    """A die-cut star sticker with a smiling face, on its white border and a soft shadow, `k` times its full size."""
    ink = mv.INK + (255,)
    draw.polygon(star_points(cx + 5, cy + 7, 175 * k, 92 * k), fill=(203, 213, 225, 255))
    draw.polygon(star_points(cx, cy, 175 * k, 92 * k), fill=WHITE)
    draw.polygon(star_points(cx, cy + 4 * k, 145 * k, 72 * k), fill=STAR + (255,))
    for side in (-1, 1):
        draw.ellipse((cx + (side * 30 - 11) * k, cy - 14 * k, cx + (side * 30 + 11) * k, cy + 14 * k), fill=ink)
        draw.ellipse((cx + (side * 52 - 13) * k, cy + 22 * k, cx + (side * 52 + 13) * k, cy + 36 * k), fill=(251, 146, 60, 255))
    draw.arc((cx - 26 * k, cy + 4 * k, cx + 26 * k, cy + 44 * k), 20, 160, fill=ink, width=max(4, round(7 * k)))


def envelope(draw, x, y, w, h, fill):
    draw.rectangle((x, y, x + w, y + h), fill=fill + (255,))
    draw.line((x, y, x + w / 2, y + h * 0.6, x + w, y), fill=WHITE, width=2)


def quiz_card(base, info_at, option_ats, lock_at, reveal_at):
    """The question, what each sticker costs to make, the three answers, a bar that runs down while the viewer picks, and
    the right answer lit green."""
    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 925
        qs.panel(draw, top, bottom)
        draw.text((540, top + 80), "$400 MACHINE. $4 STICKERS.", font=mv.font("Bold", 50), fill=mv.INK + (255,), anchor="ms")
        draw.text((540, top + 145), "HOW MANY TO PAY IT OFF?", font=mv.font("Bold", 48), fill=mv.INK + (255,), anchor="ms")
        sticker(draw, 300, top + 320)

        def made(d):
            d.text((500, top + 275), "$0.35", font=mv.font("Bold", 72), fill=ORANGE + (255,), anchor="ls")
            d.text((500, top + 340), "to make each one,", font=mv.font("SemiBold", 40), fill=LABEL + (255,), anchor="ls")
            d.text((500, top + 390), "free shipping", font=mv.font("SemiBold", 40), fill=LABEL + (255,), anchor="ls")
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
            draw.text((540, top + 840), "C: 220 stickers", font=mv.font("Bold", 46), fill=qv.GREEN + (255,), anchor="ms")
        return out
    return frame


def split_card(base, fees_at, mail_at, make_at, keep_at):
    """One $4 sale as a column that Etsy's fees, the postage and the sticker eat from the bottom, with what is left on top."""
    parts = [("Etsy fees", 0.83, ORANGE, fees_at), ("Envelope and stamp", 1.00, SLATE, mail_at), ("The sticker", 0.35, PURPLE, make_at)]
    scale = 620 / 4
    x0, x1 = 150, 370

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 925
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "WHERE EACH $4 GOES", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        floor = top + 790
        draw.rounded_rectangle((x0, floor - 4 * scale, x1, floor), radius=14, fill=PAPER + (255,))
        draw.text(((x0 + x1) / 2, floor - 4 * scale - 18), "$4.00", font=mv.font("Bold", 44), fill=mv.INK + (255,), anchor="ms")
        y = floor
        for name, amount, fill, at in parts:
            grow = r.ease((t - at) / GROW)
            if grow > 0:
                draw.rectangle((x0, y - amount * scale * grow, x1, y), fill=fill + (255,))
                if y < floor:
                    draw.line((x0, y, x1, y), fill=WHITE, width=4)
            y -= amount * scale
        rest_top = floor - 4 * scale
        if t >= keep_at:
            k = r.ease((t - keep_at) / GROW)
            draw.rectangle((x0, y - (y - rest_top) * k, x1, y), fill=qv.GREEN + (255,))
            draw.line((x0, y, x1, y), fill=WHITE, width=4)

        rows = [("Etsy fees", "-$0.83", ORANGE, fees_at, r.RED), ("Envelope and stamp", "-$1.00", SLATE, mail_at, r.RED),
                ("The sticker", "-$0.35", PURPLE, make_at, r.RED), ("You keep", "$1.82", qv.GREEN, keep_at, qv.GREEN)]
        for n, (name, amount, fill, at, ink) in enumerate(rows):
            row_y = floor - 30 - n * 130
            if t >= at:
                draw.rounded_rectangle((420, row_y - 20, 460, row_y + 20), radius=8, fill=fill + (255,))
                draw.text((485, row_y - 20), name, font=mv.font("SemiBold", 42), fill=mv.INK + (255,), anchor="lm")
                draw.text((485, row_y + 32), amount, font=mv.font("Bold", 50), fill=ink + (255,), anchor="lm")

        def half(d):
            d.rounded_rectangle((410, top + 120, 970, top + 290), radius=24, fill=PALE_RED + (255,))
            d.text((690, top + 195), "Less than half", font=mv.font("Bold", 56), fill=r.RED + (255,), anchor="ms")
            d.text((690, top + 255), "of each sale is yours", font=mv.font("SemiBold", 36), fill=mv.INK + (255,), anchor="ms")
        qs.appear(out, t, keep_at + 0.6, half)
        return out
    return frame


def envelope_card(base, start, done):
    """220 envelopes filling a grid one after another while the count runs up, the orders it takes to pay off the $400."""
    cols, rows = 20, 11

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 925
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "ORDERS TO PAY OFF $400", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        k = r.ease((t - start) / max(0.1, done - start))
        shown = round(cols * rows * k)
        for n in range(cols * rows):
            x = 150 + (n % cols) * 39
            y = top + 140 + (n // cols) * 46
            envelope(draw, x, y, 32, 24, SLATE if n < shown else (226, 232, 240))
        draw.text((540, top + 740), f"{shown} envelopes", font=mv.font("Bold", 84), fill=(r.RED if shown else LABEL) + (255,), anchor="ms")
        draw.text((540, top + 810), "before any profit", font=mv.font("SemiBold", 44), fill=LABEL + (255,), anchor="ms")
        return out
    return frame


def pack_card(base, pack_at, keep_at, orders_at):
    """A single sticker against a pack of five sent in one envelope, and how many orders of each pay off the machine."""
    rows = [("Sells for", "$4.00", "$15.00"), ("Etsy fees", "$0.83", "$1.88"), ("Stickers", "$0.35", "$1.75"), ("Mailing", "$1.00", "$1.00")]

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 925
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "SELL PACKS OF 5", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        draw.text((640, top + 160), "Single", font=mv.font("Bold", 44), fill=mv.INK + (255,), anchor="ms")
        if t >= pack_at:
            draw.text((850, top + 160), "Pack of 5", font=mv.font("Bold", 44), fill=mv.BLUE + (255,), anchor="ms")
        for n, (name, one, pack) in enumerate(rows):
            y = top + 240 + n * 75
            draw.text((150, y), name, font=mv.font("SemiBold", 44), fill=mv.INK + (255,), anchor="lm")
            draw.text((720, y), one, font=mv.font("Bold", 44), fill=SLATE + (255,), anchor="rm")
            if t >= pack_at + 0.2 * n:
                draw.text((930, y), pack, font=mv.font("Bold", 44), fill=mv.BLUE + (255,), anchor="rm")
        draw.line((150, top + 515, 930, top + 515), fill=(203, 213, 225, 255), width=4)
        draw.text((150, top + 580), "You keep", font=mv.font("Bold", 48), fill=mv.INK + (255,), anchor="lm")
        draw.text((720, top + 580), "$1.82", font=mv.font("Bold", 52), fill=r.RED + (255,), anchor="rm")
        draw.text((150, top + 680), "Orders needed", font=mv.font("Bold", 48), fill=mv.INK + (255,), anchor="lm")
        draw.text((720, top + 680), "220", font=mv.font("Bold", 52), fill=r.RED + (255,), anchor="rm")

        def keep(d):
            d.text((930, top + 580), "$10.37", font=mv.font("Bold", 52), fill=qv.GREEN + (255,), anchor="rm")
        qs.appear(out, t, keep_at, keep)

        def orders(d):
            d.rounded_rectangle((760, top + 635, 960, top + 725), radius=20, fill=PALE_GREEN + (255,))
            d.text((930, top + 680), "39", font=mv.font("Bold", 52), fill=qv.GREEN + (255,), anchor="rm")
            d.text((540, top + 820), "One envelope, 5 stickers", font=mv.font("Bold", 56), fill=qv.GREEN + (255,), anchor="ms")
        qs.appear(out, t, orders_at, orders)
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
    quiz = quiz_card(base, word(1, "35"), [word(2, "A"), word(2, "B"), word(2, "C")], lock_word["end"], line(3))
    split = split_card(base, word(4, "fees"), word(4, "envelope"), word(4, "sticker"), word(4, "keep"))
    envelopes = envelope_card(base, line(5), word(5, "stuff"))
    packs = pack_card(base, word(6, "packs"), word(6, "keeps"), word(6, "39"))

    def still(at, card, title):
        return {"at": at, "still": lambda age: card(at + age), "title": title}

    return [
        still(0.0, quiz, "Sticker maker quiz"),
        still(line(3) - 0.1, quiz, "The answer"),
        still(line(4) - 0.1, split, "Where each $4 goes"),
        still(line(5) - 0.1, envelopes, "220 envelopes"),
        still(line(6) - 0.1, packs, "The fix"),
        {"at": line(7) - 0.1, "take": "sticker", "from": ("click_fixed", -0.4), "title": "Free break-even calculator",
         "keys": [("click_fixed", -0.4, 1.3, 1056, 800), ("typed", 0.0, 1.3, 1056, 950)]},
        {"at": word(7, "website"), "take": "sticker", "from": ("typed", 0.05), "title": "Know your number first",
         "keys": [("typed", 0.05, 1.3, 2144, 850), ("look_units", 0.6, 1.45, 2144, 760)]},
        {"at": line(9) - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    inv.prepare(TAKES, "sticker")
    voice_path = WORK / "voice.wav"
    r.write_mono(voice_path, mv.voice_line(inv.spoken(LINES)))
    words = short.phrase_timed_words(short.scripted_words(voice_path, SCRIPT, WORK), voice_path)
    total = max(w["end"] for w in words) + HOLD_END
    chunks = short.balanced_chunks(words, longest_pause=0.6)
    pc.finish(shot_list(words, total, short.background()), chunks, voice_path, WORK, OUT_FILE, MUSIC_FROM,
              load_take=load_take, end_card=inv.end_card_saying("Free calculator linked below"))


if __name__ == "__main__":
    main()
