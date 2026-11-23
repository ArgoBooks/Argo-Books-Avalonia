"""Make the vertical Short "You sell a bar of soap for $6. How much do you keep?": a quiz for soap makers. Three answers are
offered, the viewer gets a moment to pick, and the right one, 27 cents, is shown as what is left of the $6 once materials,
time and selling costs are paid. A double batch is then shown to bring the cost down. It ends on the free soap pricing
calculator on argorobots.com. Narrated by the Azure voice, with big captions timed to each word.

Usage: python soap_short.py

The figures are the calculator page's own worked example. Its take is recorded with
`node record_generator.js soap work/soap_short/takes`. The finished video goes to work/videos/.
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
WORK = HERE / "work" / "soap_short"
TAKES = WORK / "takes"
OUT_FILE = HERE / "work" / "videos" / "short_soap_quiz.mp4"
LINES = [
    ("Soap makers, quick quiz. You sell a bar for 6 dollars. How much do you keep?", 400),
    ("A batch of 10 bars takes 24 dollars 60 of materials.", 400),
    ("Is it A, 3 dollars 54, B, 1 dollar 27, or C, 27 cents? Lock in your guess.", 1300),
    ("It's C. Just 27 cents.", 450),
    ("Materials take 2 dollars 46. Your 8 minutes, at 20 dollars an hour, take 2 dollars 67.", 400),
    ("Stall and card fees take 60 cents. That leaves 27.", 450),
    ("The fix? Pour a double batch in the same time. Each bar drops to 4 dollars 39, so 9 dollars a bar keeps 4 dollars 61.", 450),
    ("There's a free soap pricing calculator on the Argo Books website,", 200),
    ("so you can price your own bars.", 450),
    ("It's linked below.", 0),
]
SCRIPT = " ".join(text for text, _ in LINES)
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 50.0
VX, VY, VW, VH = short.VIEW
LABEL = (124, 135, 158)
PALE_RED = (254, 226, 226)
PALE_GREEN = (220, 252, 231)
SLATE = (100, 116, 139)
ORANGE = (241, 100, 30)
SOAP = (233, 213, 255)
SOAP_SIDE = (196, 167, 231)
WHITE = (255, 255, 255, 255)
GROW = 0.6
ANSWERS = [("A", "$3.54"), ("B", "$1.27"), ("C", "$0.27")]


def load_take(name):
    log = json.loads((TAKES / f"{name}.json").read_text())
    log["path"] = TAKES / f"{name}.mkv"
    return log


def soap_bar(draw, cx, base_y):
    """A bar of handmade soap seen at a slight angle, with a stamped top and a few bubbles."""
    draw.rounded_rectangle((cx - 120, base_y - 120, cx + 120, base_y - 20), radius=26, fill=SOAP_SIDE + (255,))
    draw.rounded_rectangle((cx - 120, base_y - 160, cx + 120, base_y - 55), radius=26, fill=SOAP + (255,))
    draw.rounded_rectangle((cx - 80, base_y - 135, cx + 80, base_y - 80), radius=14, outline=SOAP_SIDE + (255,), width=5)
    draw.text((cx, base_y - 107), "SOAP", font=mv.font("Bold", 34), fill=SOAP_SIDE + (255,), anchor="mm")
    for x, y, size in [(-90, -200, 18), (-55, -230, 12), (70, -215, 22), (100, -250, 12), (20, -245, 9)]:
        draw.ellipse((cx + x - size, base_y + y - size, cx + x + size, base_y + y + size), outline=SOAP_SIDE + (255,), width=4)


def quiz_card(base, batch_at, option_ats, lock_at, reveal_at):
    """The question, the batch it comes from, the three answers, a bar that runs down while the viewer picks, and the
    right answer lit green."""
    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 900
        qs.panel(draw, top, bottom)
        draw.text((540, top + 80), "YOU SELL A BAR FOR $6.", font=mv.font("Bold", 52), fill=mv.INK + (255,), anchor="ms")
        draw.text((540, top + 145), "HOW MUCH DO YOU KEEP?", font=mv.font("Bold", 52), fill=mv.INK + (255,), anchor="ms")
        soap_bar(draw, 270, top + 420)

        def batch(d):
            d.text((440, top + 270), "Batch of 10", font=mv.font("SemiBold", 44), fill=LABEL + (255,), anchor="ls")
            d.text((440, top + 345), "$24.60", font=mv.font("Bold", 72), fill=ORANGE + (255,), anchor="ls")
            d.text((440, top + 395), "of materials", font=mv.font("SemiBold", 40), fill=LABEL + (255,), anchor="ls")
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
            draw.text((540, top + 835), "C: just 27 cents a bar", font=mv.font("Bold", 46), fill=qv.GREEN + (255,), anchor="ms")
        return out
    return frame


def split_card(base, materials_at, time_at, fees_at, kept_at):
    """The $6 as one column, eaten from the bottom by materials, time and selling costs until a green sliver is left."""
    parts = [("Materials", 2.46, SLATE, materials_at), ("Your time (8 min)", 2.67, mv.BLUE, time_at),
             ("Stall and card fees", 0.60, ORANGE, fees_at), ("You keep", 0.27, qv.GREEN, kept_at)]
    scale = 560 / 6
    x0, x1 = 150, 370

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 900
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "WHERE YOUR $6 GOES", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        floor = top + 720
        draw.rounded_rectangle((x0, floor - 6 * scale, x1, floor), radius=14, fill=(241, 245, 249, 255))
        draw.text(((x0 + x1) / 2, floor - 6 * scale - 20), "$6.00", font=mv.font("Bold", 48), fill=mv.INK + (255,), anchor="ms")
        y = floor
        for n, (name, amount, fill, at) in enumerate(parts):
            grow = r.ease((t - at) / GROW)
            if grow > 0:
                h = amount * scale * grow
                draw.rectangle((x0, y - h, x1, y), fill=fill + (255,))
                if n:
                    draw.line((x0, y, x1, y), fill=WHITE, width=4)
            y -= amount * scale
            row_y = floor - 40 - n * 140
            if t >= at:
                kept = n == 3
                draw.rounded_rectangle((420, row_y - 20, 460, row_y + 20), radius=8, fill=fill + (255,))
                draw.text((485, row_y - 20), name, font=mv.font("Bold" if kept else "SemiBold", 42), fill=mv.INK + (255,), anchor="lm")
                amount_text = f"${amount:.2f}" if kept else f"-${amount:.2f}"
                draw.text((485, row_y + 32), amount_text, font=mv.font("Bold", 64 if kept else 50),
                          fill=(qv.GREEN if kept else r.RED) + (255,), anchor="lm")
        return out
    return frame


def batch_card(base, double_at, cost_at, keep_at):
    """The cost of one bar from a batch of 10 against a double batch made in the same time, and what $9 a bar then keeps."""
    rows = [("Materials", "$2.46", "$2.46"), ("Your time", "$2.67", "$1.33"), ("Fees", "$0.60", "$0.60")]

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 900
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "SAME 80 MINUTES", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        draw.text((640, top + 160), "10 bars", font=mv.font("Bold", 44), fill=mv.INK + (255,), anchor="ms")
        if t >= double_at:
            draw.text((850, top + 160), "20 bars", font=mv.font("Bold", 44), fill=mv.BLUE + (255,), anchor="ms")
        for n, (name, single, double) in enumerate(rows):
            y = top + 240 + n * 75
            draw.text((150, y), name, font=mv.font("SemiBold", 44), fill=mv.INK + (255,), anchor="lm")
            draw.text((700, y), single, font=mv.font("Bold", 44), fill=SLATE + (255,), anchor="rm")
            if t >= double_at + 0.2 * n:
                draw.text((910, y), double, font=mv.font("Bold", 44), fill=(mv.BLUE if single != double else SLATE) + (255,), anchor="rm")
        draw.line((150, top + 440, 930, top + 440), fill=(203, 213, 225, 255), width=4)
        draw.text((150, top + 500), "A bar costs", font=mv.font("Bold", 48), fill=mv.INK + (255,), anchor="lm")
        draw.text((700, top + 500), "$5.73", font=mv.font("Bold", 52), fill=r.RED + (255,), anchor="rm")

        def cost(d):
            d.text((910, top + 500), "$4.39", font=mv.font("Bold", 52), fill=qv.GREEN + (255,), anchor="rm")
        qs.appear(out, t, cost_at, cost)

        def keep(d):
            d.rounded_rectangle((110, top + 600, 970, top + 760), radius=24, fill=PALE_GREEN + (255,))
            d.text((150, top + 655), "Sell at $9 a bar", font=mv.font("Bold", 46), fill=mv.INK + (255,), anchor="lm")
            d.text((150, top + 715), "and you keep", font=mv.font("SemiBold", 40), fill=LABEL + (255,), anchor="lm")
            d.text((930, top + 680), "$4.61", font=mv.font("Bold", 90), fill=qv.GREEN + (255,), anchor="rm")
        qs.appear(out, t, keep_at, keep)
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
    quiz = quiz_card(base, word(1, "24"), [word(2, "A"), word(2, "B"), word(2, "C")], lock_word["end"], line(3))
    split = split_card(base, word(4, "Materials"), word(4, "8"), word(5, "Stall"), word(5, "leaves"))
    batch = batch_card(base, word(6, "double"), word(6, "39"), word(6, "9"))

    def still(at, card, title):
        return {"at": at, "still": lambda age: card(at + age), "title": title}

    return [
        still(0.0, quiz, "Soap maker quiz"),
        still(line(3) - 0.1, quiz, "The answer"),
        still(line(4) - 0.1, split, "Where the $6 goes"),
        still(line(6) - 0.1, batch, "The fix"),
        {"at": line(7) - 0.1, "take": "soap", "from": ("click_mat", -0.4), "title": "Free soap calculator",
         "keys": [("click_mat", -0.4, 1.3, 1056, 560), ("mats_done", 0.0, 1.3, 1056, 1000)]},
        {"at": word(7, "website"), "take": "soap", "from": ("priced", 0.05), "title": "Your real cost per bar",
         "keys": [("priced", 0.05, 1.3, 2144, 560), ("look_price", 1.6, 1.45, 2144, 460)]},
        {"at": line(9) - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    inv.prepare(TAKES, "soap")
    voice_path = WORK / "voice.wav"
    r.write_mono(voice_path, mv.voice_line(inv.spoken(LINES)))
    words = short.phrase_timed_words(short.scripted_words(voice_path, SCRIPT, WORK), voice_path)
    total = max(w["end"] for w in words) + HOLD_END
    chunks = short.balanced_chunks(words, longest_pause=0.6)
    pc.finish(shot_list(words, total, short.background()), chunks, voice_path, WORK, OUT_FILE, MUSIC_FROM,
              load_take=load_take, end_card=inv.end_card_saying("Free calculator linked below"))


if __name__ == "__main__":
    main()
