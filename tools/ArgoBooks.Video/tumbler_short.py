"""Make the vertical Short "You sell a custom tumbler for $30. How much do you make?": a quiz for tumbler makers. Three
answers are offered, the viewer gets a moment to pick, and the right one, a loss of $5.90, is shown as the costs of one
tumbler stacking up past the $30 it sold for. Making the same design in a run of 10 is then shown to bring the cost down.
It ends on the free tumbler pricing calculator on argorobots.com. Narrated by the Azure voice, with big captions timed to
each word.

Usage: python tumbler_short.py

The figures are the calculator page's own worked example. Its take is recorded with
`node record_generator.js tumbler work/tumbler_short/takes`. The finished video goes to work/videos/.
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
WORK = HERE / "work" / "tumbler_short"
TAKES = WORK / "takes"
OUT_FILE = HERE / "work" / "videos" / "short_tumbler_quiz.mp4"
LINES = [
    ("Tumbler makers, quick quiz. You sell a tumbler for 30 dollars. How much do you make?", 400),
    ("The blank, ink, tape and box cost 11 dollars.", 400),
    ("Is it A, 10 dollars 60, B, minus 5 dollars 90, or C, 19 dollars? Lock in your guess.", 1300),
    ("It's B. You lose 5 dollars 90.", 450),
    ("Materials, 11 dollars. Fees and postage, 8 dollars 40.", 400),
    ("Your 45 minutes at 22 an hour, 16 dollars 50. That's 35 dollars 90 on a 30 dollar sale.", 450),
    ("The fix? Design once and make 10. Each one drops to 24 dollars 90, so 35 dollars keeps 10 dollars 10.", 450),
    ("There's a free tumbler pricing calculator on the Argo Books website,", 200),
    ("so you can price your own tumblers.", 450),
    ("It's linked below.", 0),
]
SCRIPT = " ".join(text for text, _ in LINES)
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 100.0
VX, VY, VW, VH = short.VIEW
LABEL = (124, 135, 158)
PALE_RED = (254, 226, 226)
PALE_GREEN = (220, 252, 231)
SLATE = (100, 116, 139)
ORANGE = (241, 100, 30)
CUP = (153, 246, 228)
CUP_SIDE = (45, 212, 191)
LID = (148, 163, 184)
STRAW = (244, 114, 182)
WHITE = (255, 255, 255, 255)
GROW = 0.6
ANSWERS = [("A", "$10.60"), ("B", "-$5.90"), ("C", "$19.00")]
RIGHT = "B"


def load_take(name):
    log = json.loads((TAKES / f"{name}.json").read_text())
    log["path"] = TAKES / f"{name}.mkv"
    return log


def tumbler(draw, cx, base_y):
    """A custom tumbler with a lid, a straw and a heart printed on its side."""
    draw.line((cx + 25, base_y - 220, cx + 50, base_y - 262), fill=STRAW + (255,), width=16)
    draw.polygon([(cx - 85, base_y - 205), (cx + 85, base_y - 205), (cx + 65, base_y), (cx - 65, base_y)], fill=CUP + (255,))
    draw.polygon([(cx + 45, base_y - 205), (cx + 85, base_y - 205), (cx + 65, base_y), (cx + 30, base_y)], fill=CUP_SIDE + (255,))
    draw.rounded_rectangle((cx - 95, base_y - 235, cx + 95, base_y - 200), radius=12, fill=LID + (255,))
    hx, hy = cx - 15, base_y - 115
    draw.ellipse((hx - 34, hy - 30, hx + 2, hy + 6), fill=STRAW + (255,))
    draw.ellipse((hx - 2, hy - 30, hx + 34, hy + 6), fill=STRAW + (255,))
    draw.polygon([(hx - 33, hy - 6), (hx + 33, hy - 6), (hx, hy + 34)], fill=STRAW + (255,))


def quiz_card(base, batch_at, option_ats, lock_at, reveal_at):
    """The question, what the materials cost, the three answers, a bar that runs down while the viewer picks, and the
    right answer lit green."""
    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 925
        qs.panel(draw, top, bottom)
        draw.text((540, top + 80), "YOU SELL A TUMBLER FOR $30.", font=mv.font("Bold", 48), fill=mv.INK + (255,), anchor="ms")
        draw.text((540, top + 145), "HOW MUCH DO YOU MAKE?", font=mv.font("Bold", 52), fill=mv.INK + (255,), anchor="ms")
        tumbler(draw, 270, top + 455)

        def batch(d):
            d.text((440, top + 270), "One tumbler", font=mv.font("SemiBold", 44), fill=LABEL + (255,), anchor="ls")
            d.text((440, top + 345), "$11.00", font=mv.font("Bold", 72), fill=ORANGE + (255,), anchor="ls")
            d.text((440, top + 395), "of materials", font=mv.font("SemiBold", 40), fill=LABEL + (255,), anchor="ls")
        qs.appear(out, t, batch_at, batch)

        for n, ((letter, amount), at) in enumerate(zip(ANSWERS, option_ats)):
            y = top + 480 + n * 110
            right = letter == RIGHT
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
            draw.text((540, top + 835), "B: you lose $5.90 a tumbler", font=mv.font("Bold", 46), fill=r.RED + (255,), anchor="ms")
        return out
    return frame


def split_card(base, materials_at, fees_at, time_at, lose_at):
    """The $30 sale as an empty column that the costs fill from the bottom, until your time pushes them past the top and
    the part that overflows turns red."""
    parts = [("Materials", 11.00, SLATE, materials_at), ("Etsy fees, postage", 8.40, ORANGE, fees_at),
             ("Your time (45 min)", 16.50, mv.BLUE, time_at)]
    scale = 640 / 36
    x0, x1 = 150, 370

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 900
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "WHERE YOUR $30 GOES", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        floor = top + 790
        sale = floor - 30 * scale
        draw.rounded_rectangle((x0, sale, x1, floor), radius=14, fill=(241, 245, 249, 255))
        y = floor
        for name, amount, fill, at in parts:
            grow = r.ease((t - at) / GROW)
            if grow > 0:
                draw.rectangle((x0, y - amount * scale * grow, x1, y), fill=fill + (255,))
                if y < floor:
                    draw.line((x0, y, x1, y), fill=WHITE, width=4)
            y -= amount * scale
        over = r.ease((t - lose_at) / GROW)
        if over > 0:
            draw.rectangle((x0, sale - (sale - y) * over, x1, sale), fill=r.RED + (255,))
        draw.line((x0 - 20, sale, x1 + 20, sale), fill=mv.INK + (255,), width=6)
        draw.text((x1 + 40, sale), "$30 sale", font=mv.font("Bold", 40), fill=mv.INK + (255,), anchor="lm")

        rows = parts + [("You lose", 5.90, r.RED, lose_at)]
        for n, (name, amount, fill, at) in enumerate(rows):
            row_y = floor - 30 - n * 135
            if t >= at:
                lost = n == 3
                draw.rounded_rectangle((440, row_y - 20, 480, row_y + 20), radius=8, fill=fill + (255,))
                draw.text((505, row_y - 20), name, font=mv.font("Bold" if lost else "SemiBold", 42), fill=mv.INK + (255,), anchor="lm")
                draw.text((505, row_y + 32), f"-${amount:.2f}", font=mv.font("Bold", 60 if lost else 50), fill=r.RED + (255,), anchor="lm")
        return out
    return frame


def batch_card(base, run_at, cost_at, keep_at):
    """The cost of a one-off tumbler against one from a run of 10 that share a design, and what $35 then keeps."""
    rows = [("Materials", "$11.00", "$11.00"), ("Your time", "$16.50", "$5.50"), ("Fees, postage", "$8.40", "$8.40")]

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 900
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "DESIGN ONCE, MAKE 10", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        draw.text((630, top + 160), "One-off", font=mv.font("Bold", 42), fill=mv.INK + (255,), anchor="ms")
        if t >= run_at:
            draw.text((850, top + 160), "Run of 10", font=mv.font("Bold", 42), fill=mv.BLUE + (255,), anchor="ms")
        for n, (name, single, run) in enumerate(rows):
            y = top + 240 + n * 75
            draw.text((150, y), name, font=mv.font("SemiBold", 44), fill=mv.INK + (255,), anchor="lm")
            draw.text((700, y), single, font=mv.font("Bold", 44), fill=SLATE + (255,), anchor="rm")
            if t >= run_at + 0.2 * n:
                draw.text((920, y), run, font=mv.font("Bold", 44), fill=(mv.BLUE if single != run else SLATE) + (255,), anchor="rm")
        draw.line((150, top + 440, 930, top + 440), fill=(203, 213, 225, 255), width=4)
        draw.text((150, top + 500), "Costs", font=mv.font("Bold", 48), fill=mv.INK + (255,), anchor="lm")
        draw.text((700, top + 500), "$35.90", font=mv.font("Bold", 52), fill=r.RED + (255,), anchor="rm")

        def cost(d):
            d.text((920, top + 500), "$24.90", font=mv.font("Bold", 52), fill=qv.GREEN + (255,), anchor="rm")
        qs.appear(out, t, cost_at, cost)

        def keep(d):
            d.rounded_rectangle((110, top + 600, 970, top + 760), radius=24, fill=PALE_GREEN + (255,))
            d.text((150, top + 655), "Sell at $35", font=mv.font("Bold", 46), fill=mv.INK + (255,), anchor="lm")
            d.text((150, top + 715), "and you keep", font=mv.font("SemiBold", 40), fill=LABEL + (255,), anchor="lm")
            d.text((930, top + 680), "$10.10", font=mv.font("Bold", 90), fill=qv.GREEN + (255,), anchor="rm")
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
    quiz = quiz_card(base, word(1, "11"), [word(2, "A"), word(2, "B"), word(2, "C")], lock_word["end"], line(3))
    split = split_card(base, word(4, "Materials"), word(4, "Fees"), word(5, "Your"), word(5, "35"))
    batch = batch_card(base, word(6, "10"), word(6, "90"), word(6, "keeps"))

    def still(at, card, title):
        return {"at": at, "still": lambda age: card(at + age), "title": title}

    return [
        still(0.0, quiz, "Tumbler maker quiz"),
        still(line(3) - 0.1, quiz, "The answer"),
        still(line(4) - 0.1, split, "Where the $30 goes"),
        still(line(6) - 0.1, batch, "The fix"),
        {"at": line(7) - 0.1, "take": "tumbler", "from": ("click_mat", -0.4), "title": "Free tumbler calculator",
         "keys": [("click_mat", -0.4, 1.3, 1056, 560), ("mats_done", 0.0, 1.3, 1056, 900)]},
        {"at": word(7, "website"), "take": "tumbler", "from": ("priced", 0.05), "title": "Your real cost per tumbler",
         "keys": [("priced", 0.05, 1.3, 2144, 560), ("look_price", 1.6, 1.45, 2144, 460)]},
        {"at": line(9) - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    inv.prepare(TAKES, "tumbler")
    voice_path = WORK / "voice.wav"
    r.write_mono(voice_path, mv.voice_line(inv.spoken(LINES)))
    words = short.phrase_timed_words(short.scripted_words(voice_path, SCRIPT, WORK), voice_path)
    total = max(w["end"] for w in words) + HOLD_END
    chunks = short.balanced_chunks(words, longest_pause=0.6)
    pc.finish(shot_list(words, total, short.background()), chunks, voice_path, WORK, OUT_FILE, MUSIC_FROM,
              load_take=load_take, end_card=inv.end_card_saying("Free calculator linked below"))


if __name__ == "__main__":
    main()
