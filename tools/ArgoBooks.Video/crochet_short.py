"""Make the vertical Short "You sell a crochet bear for $25. What do you earn an hour?": a quiz for crochet makers. Three
answers are offered, the viewer gets a moment to pick, and the right one, $3.23 an hour, is shown as what Etsy's fees and
the yarn leave of the $25, split over five hours and set beside the US minimum wage. A one-hour mini version is then shown
to pay far better. It ends on the free craft pricing calculator on argorobots.com. Narrated by the Azure voice, with big
captions timed to each word.

Usage: python crochet_short.py

The fees are Etsy's US ones on a $25 sale: $0.20 to list, 6.5% of the sale and 3% plus $0.25 to process the payment. Its
take is recorded with `node record_generator.js crochet work/crochet_short/takes`. The finished video goes to work/videos/.
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
WORK = HERE / "work" / "crochet_short"
TAKES = WORK / "takes"
OUT_FILE = HERE / "work" / "videos" / "short_crochet_quiz.mp4"
LINES = [
    ("Crochet makers, quick quiz. You sell a crochet bear for 25 dollars. What do you earn an hour?", 400),
    ("It takes 5 hours, and 6 dollars of yarn, stuffing and eyes.", 400),
    ("Is it A, 3 dollars 23, B, 7 dollars 60, or C, 12 dollars 50? Lock in your guess.", 1300),
    ("It's A. Just 3 dollars 23 an hour.", 450),
    ("Etsy's fees take 2 dollars 83. The yarn takes 6. That leaves 16 dollars 17 for 5 hours of work.", 400),
    ("That's less than half the US minimum wage.", 450),
    ("The fix? Sell a mini version too. One hour and 2 dollars of yarn, sold at 18, pays 13 dollars 84 an hour.", 450),
    ("There's a free craft pricing calculator on the Argo Books website,", 200),
    ("so you can price your time in.", 450),
    ("It's linked below.", 0),
]
SCRIPT = " ".join(text for text, _ in LINES)
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 80.0
VX, VY, VW, VH = short.VIEW
LABEL = (124, 135, 158)
PALE_RED = (254, 226, 226)
PALE_GREEN = (220, 252, 231)
SLATE = (100, 116, 139)
ORANGE = (241, 100, 30)
FUR = (214, 168, 120)
FUR_DARK = (176, 126, 82)
MUZZLE = (245, 226, 200)
WHITE = (255, 255, 255, 255)
GROW = 0.6
ANSWERS = [("A", "$3.23/hr"), ("B", "$7.60/hr"), ("C", "$12.50/hr")]
RIGHT = "A"


def load_take(name):
    log = json.loads((TAKES / f"{name}.json").read_text())
    log["path"] = TAKES / f"{name}.mkv"
    return log


def bear(draw, cx, base_y):
    """A crocheted teddy bear sitting up, with stitch rows across its body."""
    ink = mv.INK + (255,)
    draw.ellipse((cx - 85, base_y - 140, cx + 85, base_y), fill=FUR + (255,))
    for y in range(-120, -10, 22):
        draw.arc((cx - 80, base_y + y - 20, cx + 80, base_y + y + 20), 20, 160, fill=FUR_DARK + (255,), width=3)
    for side in (-1, 1):
        draw.ellipse((cx + side * 75 - 28, base_y - 40, cx + side * 75 + 28, base_y + 4), fill=FUR_DARK + (255,))
        draw.ellipse((cx + side * 62 - 28, base_y - 270, cx + side * 62 + 28, base_y - 214), fill=FUR_DARK + (255,))
    draw.ellipse((cx - 80, base_y - 260, cx + 80, base_y - 110), fill=FUR + (255,))
    draw.ellipse((cx - 36, base_y - 180, cx + 36, base_y - 128), fill=MUZZLE + (255,))
    draw.ellipse((cx - 10, base_y - 172, cx + 10, base_y - 158), fill=ink)
    for side in (-1, 1):
        draw.ellipse((cx + side * 34 - 9, base_y - 212, cx + side * 34 + 9, base_y - 194), fill=ink)


def quiz_card(base, info_at, option_ats, lock_at, reveal_at):
    """The question, the hours and yarn the bear takes, the three answers, a bar that runs down while the viewer picks, and
    the right answer lit green."""
    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 925
        qs.panel(draw, top, bottom)
        draw.text((540, top + 80), "YOU SELL A BEAR FOR $25.", font=mv.font("Bold", 50), fill=mv.INK + (255,), anchor="ms")
        draw.text((540, top + 145), "WHAT DO YOU EARN AN HOUR?", font=mv.font("Bold", 48), fill=mv.INK + (255,), anchor="ms")
        bear(draw, 270, top + 455)

        def made(d):
            d.text((440, top + 275), "5 hours", font=mv.font("Bold", 72), fill=ORANGE + (255,), anchor="ls")
            d.text((440, top + 340), "+ $6.00 of yarn,", font=mv.font("SemiBold", 40), fill=LABEL + (255,), anchor="ls")
            d.text((440, top + 390), "stuffing and eyes", font=mv.font("SemiBold", 40), fill=LABEL + (255,), anchor="ls")
        qs.appear(out, t, info_at, made)

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
            draw.text((540, top + 840), "A: just $3.23 an hour", font=mv.font("Bold", 46), fill=r.RED + (255,), anchor="ms")
        return out
    return frame


def split_card(base, fees_at, yarn_at, left_at, hours_at):
    """The $25 as one column that Etsy's fees and the yarn eat from the bottom, then what is left cut into five hours."""
    parts = [("Etsy fees", 2.83, ORANGE, fees_at), ("Yarn, stuffing, eyes", 6.00, SLATE, yarn_at)]
    scale = 620 / 25
    x0, x1 = 150, 370

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 925
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "WHERE YOUR $25 GOES", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        floor = top + 790
        draw.rounded_rectangle((x0, floor - 25 * scale, x1, floor), radius=14, fill=(241, 245, 249, 255))
        draw.text(((x0 + x1) / 2, floor - 25 * scale - 18), "$25.00", font=mv.font("Bold", 44), fill=mv.INK + (255,), anchor="ms")
        y = floor
        for name, amount, fill, at in parts:
            grow = r.ease((t - at) / GROW)
            if grow > 0:
                draw.rectangle((x0, y - amount * scale * grow, x1, y), fill=fill + (255,))
                if y < floor:
                    draw.line((x0, y, x1, y), fill=WHITE, width=4)
            y -= amount * scale
        rest_top = floor - 25 * scale
        if t >= left_at:
            k = r.ease((t - left_at) / GROW)
            draw.rectangle((x0, y - (y - rest_top) * k, x1, y), fill=qv.GREEN + (255,))
            draw.line((x0, y, x1, y), fill=WHITE, width=4)
        hour = (y - rest_top) / 5
        for n in range(5):
            if t >= hours_at + 0.12 * n:
                hy = y - hour * (n + 1)
                if n < 4:
                    draw.line((x0, hy, x1, hy), fill=WHITE, width=4)
                draw.text(((x0 + x1) / 2, hy + hour / 2), f"hour {n + 1}", font=mv.font("SemiBold", 30), fill=WHITE, anchor="mm")

        rows = [("Etsy fees", "-$2.83", ORANGE, fees_at, r.RED), ("Yarn, stuffing, eyes", "-$6.00", SLATE, yarn_at, r.RED),
                ("Left for 5 hours", "$16.17", qv.GREEN, left_at, qv.GREEN)]
        for n, (name, amount, fill, at, ink) in enumerate(rows):
            row_y = floor - 30 - n * 135
            if t >= at:
                draw.rounded_rectangle((420, row_y - 20, 460, row_y + 20), radius=8, fill=fill + (255,))
                draw.text((485, row_y - 20), name, font=mv.font("SemiBold", 42), fill=mv.INK + (255,), anchor="lm")
                draw.text((485, row_y + 32), amount, font=mv.font("Bold", 50), fill=ink + (255,), anchor="lm")

        def per_hour(d):
            d.rounded_rectangle((410, top + 130, 970, top + 300), radius=24, fill=PALE_RED + (255,))
            d.text((440, top + 185), "Each hour pays", font=mv.font("SemiBold", 42), fill=mv.INK + (255,), anchor="lm")
            d.text((940, top + 255), "$3.23", font=mv.font("Bold", 84), fill=r.RED + (255,), anchor="rm")
        qs.appear(out, t, hours_at + 0.5, per_hour)
        return out
    return frame


def wage_card(base, wage_at):
    """What each hour of the bear pays beside the US federal minimum wage."""
    bars = [("Your crochet bear", 3.23, r.RED), ("US minimum wage", 7.25, SLATE)]
    scale = 760 / 7.25

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 925
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "AN HOUR OF YOUR WORK", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        for n, (name, amount, fill) in enumerate(bars):
            y = top + 180 + n * 230
            at = 0 if n == 0 else wage_at
            if t < at:
                continue
            k = r.ease((t - at) / GROW)
            draw.text((160, y), name, font=mv.font("Bold", 46), fill=mv.INK + (255,), anchor="ls")
            draw.rounded_rectangle((160, y + 30, 160 + max(30, amount * scale * k), y + 130), radius=18, fill=fill + (255,))
            draw.text((160 + amount * scale * k + 20 if amount < 5 else 160 + amount * scale * k - 20, y + 80),
                      f"${amount:.2f}", font=mv.font("Bold", 60), fill=(fill if amount < 5 else (255, 255, 255)) + (255,),
                      anchor="lm" if amount < 5 else "rm")

        def half(d):
            d.rounded_rectangle((110, top + 650, 970, top + 790), radius=24, fill=PALE_RED + (255,))
            d.text((540, top + 735), "Less than half", font=mv.font("Bold", 64), fill=r.RED + (255,), anchor="ms")
        qs.appear(out, t, wage_at + 0.4, half)
        return out
    return frame


def mini_card(base, mini_at, pay_at):
    """The bear against a one-hour mini version, and what an hour of each pays once fees and yarn are out."""
    rows = [("Sells for", "$25.00", "$18.00"), ("Etsy fees", "$2.83", "$2.16"), ("Yarn", "$6.00", "$2.00"), ("Your time", "5 hours", "1 hour")]

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 925
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "SELL A MINI TOO", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        draw.text((640, top + 160), "Bear", font=mv.font("Bold", 44), fill=mv.INK + (255,), anchor="ms")
        if t >= mini_at:
            draw.text((850, top + 160), "Mini", font=mv.font("Bold", 44), fill=mv.BLUE + (255,), anchor="ms")
        for n, (name, big, small) in enumerate(rows):
            y = top + 240 + n * 75
            draw.text((150, y), name, font=mv.font("SemiBold", 44), fill=mv.INK + (255,), anchor="lm")
            draw.text((720, y), big, font=mv.font("Bold", 44), fill=SLATE + (255,), anchor="rm")
            if t >= mini_at + 0.2 * n:
                draw.text((930, y), small, font=mv.font("Bold", 44), fill=mv.BLUE + (255,), anchor="rm")
        draw.line((150, top + 515, 930, top + 515), fill=(203, 213, 225, 255), width=4)
        draw.text((150, top + 580), "Per hour", font=mv.font("Bold", 48), fill=mv.INK + (255,), anchor="lm")
        draw.text((720, top + 580), "$3.23", font=mv.font("Bold", 52), fill=r.RED + (255,), anchor="rm")

        def pay(d):
            d.rounded_rectangle((740, top + 535, 960, top + 625), radius=20, fill=PALE_GREEN + (255,))
            d.text((930, top + 580), "$13.84", font=mv.font("Bold", 52), fill=qv.GREEN + (255,), anchor="rm")
            d.text((540, top + 730), "4 times the pay", font=mv.font("Bold", 60), fill=qv.GREEN + (255,), anchor="ms")
        qs.appear(out, t, pay_at, pay)
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
    quiz = quiz_card(base, word(1, "5"), [word(2, "A"), word(2, "B"), word(2, "C")], lock_word["end"], line(3))
    split = split_card(base, word(4, "fees"), word(4, "yarn"), word(4, "leaves"), word(4, "for"))
    wage = wage_card(base, word(5, "US"))
    mini = mini_card(base, word(6, "mini"), word(6, "pays"))

    def still(at, card, title):
        return {"at": at, "still": lambda age: card(at + age), "title": title}

    return [
        still(0.0, quiz, "Crochet maker quiz"),
        still(line(3) - 0.1, quiz, "The answer"),
        still(line(4) - 0.1, split, "Where the $25 goes"),
        still(word(5, "half") - 0.1, wage, "Under minimum wage"),
        still(line(6) - 0.1, mini, "The fix"),
        {"at": line(7) - 0.1, "take": "crochet", "from": ("click_material", -0.4), "title": "Free craft pricing calculator",
         "keys": [("click_material", -0.4, 1.3, 1056, 1150), ("typed", 0.0, 1.3, 1056, 1350)]},
        {"at": word(7, "website"), "take": "crochet", "from": ("priced", 0.05), "title": "What 5 hours really cost",
         "keys": [("priced", 0.05, 1.3, 2144, 1250), ("look_price", 1.6, 1.45, 2144, 1150)]},
        {"at": line(9) - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    inv.prepare(TAKES, "crochet")
    voice_path = WORK / "voice.wav"
    r.write_mono(voice_path, mv.voice_line(inv.spoken(LINES)))
    words = short.phrase_timed_words(short.scripted_words(voice_path, SCRIPT, WORK), voice_path)
    total = max(w["end"] for w in words) + HOLD_END
    chunks = short.balanced_chunks(words, longest_pause=0.6)
    pc.finish(shot_list(words, total, short.background()), chunks, voice_path, WORK, OUT_FILE, MUSIC_FROM,
              load_take=load_take, end_card=inv.end_card_saying("Free calculator linked below"))


if __name__ == "__main__":
    main()
