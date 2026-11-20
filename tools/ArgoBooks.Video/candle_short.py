"""Make the vertical Short "Guess what this candle costs": a quiz for candle makers. Three answers are offered for what one
candle from a batch of 12 really costs, the viewer gets a moment to pick, and the right one is built up from materials, time
and selling costs. It ends on the free candle pricing calculator on argorobots.com. Narrated by the Azure voice, with big
captions timed to each word.

Usage: python candle_short.py

The figures are the calculator page's own worked example. Its take is recorded with
`node record_generator.js candle work/candle_short/takes`. The finished video goes to work/videos/.
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
WORK = HERE / "work" / "candle_short"
TAKES = WORK / "takes"
OUT_FILE = HERE / "work" / "videos" / "short_candle_quiz.mp4"
LINES = [
    ("Candle makers, quick quiz. What does one candle really cost to make?", 400),
    ("A batch of 12 takes 66 dollars 20 of materials.", 400),
    ("Is it A, 5 dollars 52, B, 7 dollars 32, or C, 12 dollars 32? Lock in your guess.", 1300),
    ("It's C. 12 dollars 32.", 450),
    ("5 dollars 52 is just the materials. Each candle also takes 15 minutes of your time. At 20 dollars an hour, that's 5 dollars.", 400),
    ("Then Etsy fees and a padded mailer add about 1 dollar 80.", 450),
    ("So if you just double your materials and charge 11 dollars, every candle pays you less than it costs.", 450),
    ("There's a free candle pricing calculator on the Argo Books website,", 200),
    ("so you can find the real cost of your own candles.", 450),
    ("It's linked below.", 0),
]
SCRIPT = " ".join(text for text, _ in LINES)
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 65.0
VX, VY, VW, VH = short.VIEW
LABEL = (124, 135, 158)
PALE_RED = (254, 226, 226)
PALE_GREEN = (220, 252, 231)
SLATE = (100, 116, 139)
ORANGE = (241, 100, 30)
WAX = (254, 243, 199)
GLASS = (226, 232, 240)
WHITE = (255, 255, 255, 255)
GROW = 0.6
ANSWERS = [("A", "$5.52"), ("B", "$7.32"), ("C", "$12.32")]


def load_take(name):
    log = json.loads((TAKES / f"{name}.json").read_text())
    log["path"] = TAKES / f"{name}.mkv"
    return log


def candle(draw, cx, base_y, lit=True):
    """A jar candle seen from the side: glass, wax, a label and a lit wick."""
    draw.rounded_rectangle((cx - 85, base_y - 200, cx + 85, base_y), radius=22, fill=GLASS + (255,))
    draw.rounded_rectangle((cx - 72, base_y - 150, cx + 72, base_y - 12), radius=16, fill=WAX + (255,))
    draw.rounded_rectangle((cx - 60, base_y - 110, cx + 60, base_y - 50), radius=8, fill=WHITE)
    draw.line((cx, base_y - 150, cx, base_y - 175), fill=mv.INK + (255,), width=5)
    if lit:
        draw.ellipse((cx - 14, base_y - 222, cx + 14, base_y - 172), fill=(251, 191, 36, 255))
        draw.ellipse((cx - 7, base_y - 200, cx + 7, base_y - 178), fill=(254, 240, 138, 255))


def quiz_card(base, batch_at, option_ats, lock_at, reveal_at):
    """The question, the batch it comes from, the three answers, a bar that runs down while the viewer picks, and the
    right answer lit green."""
    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 900
        qs.panel(draw, top, bottom)
        draw.text((540, top + 80), "WHAT DOES ONE", font=mv.font("Bold", 52), fill=mv.INK + (255,), anchor="ms")
        draw.text((540, top + 145), "CANDLE COST TO MAKE?", font=mv.font("Bold", 52), fill=mv.INK + (255,), anchor="ms")
        candle(draw, 260, top + 410)

        def batch(d):
            d.text((420, top + 270), "Batch of 12", font=mv.font("SemiBold", 44), fill=LABEL + (255,), anchor="ls")
            d.text((420, top + 345), "$66.20", font=mv.font("Bold", 72), fill=ORANGE + (255,), anchor="ls")
            d.text((420, top + 395), "of materials", font=mv.font("SemiBold", 40), fill=LABEL + (255,), anchor="ls")
        qs.appear(out, t, batch_at, batch)

        for n, ((letter, amount), at) in enumerate(zip(ANSWERS, option_ats)):
            y = top + 480 + n * 110
            right = letter == "C"
            if t < at + 0.3:
                draw.rounded_rectangle((150, y, 930, y + 90), radius=22, fill=(241, 245, 249, 255))
                draw.ellipse((170, y + 13, 234, y + 77), fill=(203, 213, 225, 255))
                draw.text((202, y + 45), letter, font=mv.font("Bold", 40), fill=WHITE, anchor="mm")
                draw.text((890, y + 45), "?", font=mv.font("Bold", 56), fill=(203, 213, 225, 255), anchor="rm")

            def option(d, y=y, letter=letter, amount=amount, right=right):
                fill, ink = (241, 245, 249), mv.INK
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
            draw.text((540, top + 835), "C: the real cost", font=mv.font("Bold", 46), fill=qv.GREEN + (255,), anchor="ms")
        return out
    return frame


def stack_card(base, materials_at, time_at, fees_at):
    """One candle's cost as a column that grows: materials, then the maker's time, then the selling costs."""
    parts = [("Materials", 5.52, SLATE, materials_at), ("Your time (15 min)", 5.00, mv.BLUE, time_at),
             ("Fees and mailer", 1.80, ORANGE, fees_at)]
    scale = 520 / 12.32
    x0, x1 = 150, 390

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 900
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "ONE CANDLE REALLY COSTS", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        floor = top + 700
        y = floor
        for n, (name, amount, fill, at) in enumerate(parts):
            grow = r.ease((t - at) / GROW)
            if grow <= 0:
                break
            h = amount * scale * grow
            draw.rectangle((x0, y - h, x1, y), fill=fill + (255,))
            if n:
                draw.line((x0, y, x1, y), fill=WHITE, width=5)
            if grow >= 1:
                mid = y - h / 2
                draw.text((440, mid - 22), name, font=mv.font("SemiBold", 42), fill=mv.INK + (255,), anchor="lm")
                draw.text((440, mid + 30), f"${amount:.2f}", font=mv.font("Bold", 52), fill=fill + (255,), anchor="lm")
            y -= h
        if t >= fees_at + GROW:
            draw.line((150, floor + 40, 930, floor + 40), fill=(203, 213, 225, 255), width=4)
            draw.text((150, floor + 110), "Real cost", font=mv.font("Bold", 54), fill=mv.INK + (255,), anchor="ls")
            draw.text((930, floor + 110), "$12.32", font=mv.font("Bold", 70), fill=qv.GREEN + (255,), anchor="rs")
        return out
    return frame


def double_card(base, price_at, loss_at):
    """The usual rule of doubling the materials, set against the real cost, and the gap on each candle and each batch."""
    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 900
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "\"JUST DOUBLE YOUR MATERIALS\"", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        draw.text((150, top + 190), "$5.52 x 2", font=mv.font("SemiBold", 52), fill=mv.INK + (255,), anchor="lm")
        draw.text((930, top + 190), "$11.04", font=mv.font("Bold", 64), fill=mv.INK + (255,), anchor="rm")

        def real(d):
            d.text((150, top + 300), "Real cost", font=mv.font("SemiBold", 52), fill=mv.INK + (255,), anchor="lm")
            d.text((930, top + 300), "$12.32", font=mv.font("Bold", 64), fill=qv.GREEN + (255,), anchor="rm")
        qs.appear(out, t, price_at, real)

        def loss(d):
            d.rounded_rectangle((110, top + 400, 970, top + 520), radius=24, fill=PALE_RED + (255,))
            d.text((150, top + 460), "Every candle", font=mv.font("Bold", 48), fill=mv.INK + (255,), anchor="lm")
            d.text((930, top + 460), "-$1.28", font=mv.font("Bold", 68), fill=r.RED + (255,), anchor="rm")
            d.rounded_rectangle((110, top + 550, 970, top + 670), radius=24, fill=PALE_RED + (255,))
            d.text((150, top + 610), "Every batch of 12", font=mv.font("Bold", 48), fill=mv.INK + (255,), anchor="lm")
            d.text((930, top + 610), "-$15.36", font=mv.font("Bold", 68), fill=r.RED + (255,), anchor="rm")
            d.text((540, top + 760), "out of your own pay", font=mv.font("SemiBold", 44), fill=LABEL + (255,), anchor="ms")
        qs.appear(out, t, loss_at, loss)
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
    quiz = quiz_card(base, word(1, "66"), [word(2, "A"), word(2, "B"), word(2, "C")], lock_word["end"], line(3))
    stack = stack_card(base, line(4), word(4, "20"), word(5, "Etsy"))
    double = double_card(base, word(6, "charge"), word(6, "less"))

    def still(at, card, title):
        return {"at": at, "still": lambda age: card(at + age), "title": title}

    return [
        still(0.0, quiz, "Candle maker quiz"),
        still(line(3) - 0.1, quiz, "The answer"),
        still(line(4) - 0.1, stack, "Where $12.32 comes from"),
        still(line(6) - 0.1, double, "Just double it?"),
        {"at": line(7) - 0.1, "take": "candle", "from": ("click_mat", -0.4), "title": "Free candle calculator",
         "keys": [("click_mat", -0.4, 1.3, 1056, 560), ("mats_done", 0.0, 1.3, 1056, 1000)]},
        {"at": word(7, "website"), "take": "candle", "from": ("priced", 0.05), "title": "Your real cost per candle",
         "keys": [("priced", 0.05, 1.3, 2144, 560), ("look_price", 1.6, 1.45, 2144, 460)]},
        {"at": line(9) - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    inv.prepare(TAKES, "candle")
    voice_path = WORK / "voice.wav"
    r.write_mono(voice_path, mv.voice_line(inv.spoken(LINES)))
    words = short.phrase_timed_words(short.scripted_words(voice_path, SCRIPT, WORK), voice_path)
    total = max(w["end"] for w in words) + HOLD_END
    chunks = short.balanced_chunks(words, longest_pause=0.6)
    pc.finish(shot_list(words, total, short.background()), chunks, voice_path, WORK, OUT_FILE, MUSIC_FROM,
              load_take=load_take, end_card=inv.end_card_saying("Free calculator linked below"))


if __name__ == "__main__":
    main()
