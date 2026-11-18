"""Make the vertical Short "Selling a cake for $50?": what a $50 cake really pays the baker an hour. The cake is drawn from
above and cut into what it costs, the slice that is left is shared out over the three hours it took, and the price that would
pay $25 an hour is given. It ends on the free cake pricing calculator on argorobots.com. Narrated by the Azure voice, with
big captions timed to each word.

Usage: python cake_short.py

The figures are the calculator page's own worked example. Its take is recorded with
`node record_generator.js cake work/cake_short/takes`. The finished video goes to work/videos/.
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
WORK = HERE / "work" / "cake_short"
TAKES = WORK / "takes"
OUT_FILE = HERE / "work" / "videos" / "short_cake_pricing.mp4"
LINES = [
    ("Selling a cake for 50 dollars? You might be paying yourself 6 dollars an hour.", 450),
    ("Say the ingredients, frosting, decorations and box come to 25 dollars 40.", 400),
    ("Add 6 dollars for the oven, insurance and card fees. That leaves 18 dollars 60.", 450),
    ("But that cake took three hours to bake and decorate. So you made 6 dollars 20 an hour.", 450),
    ("To pay yourself 25 dollars an hour, the same cake has to sell for 106 dollars 40. And that's before any profit.", 450),
    ("It's why so many bakers feel busy and broke at the same time.", 450),
    ("There's a free cake pricing calculator on the Argo Books website,", 200),
    ("so you can price your own cakes properly.", 450),
    ("It's linked below.", 0),
]
SCRIPT = " ".join(text for text, _ in LINES)
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 35.0
VX, VY, VW, VH = short.VIEW
LABEL = (124, 135, 158)
PALE_RED = (254, 226, 226)
PALE_GREEN = (220, 252, 231)
SLATE = (100, 116, 139)
ORANGE = (241, 100, 30)
SPONGE = (253, 230, 138)
WHITE = (255, 255, 255, 255)
GROW = 0.7


def load_take(name):
    log = json.loads((TAKES / f"{name}.json").read_text())
    log["path"] = TAKES / f"{name}.mkv"
    return log


def hook_card(base, crossed_at, real_at):
    """What a baker hopes a $50 cake pays, crossed out, then what it can really pay an hour."""
    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 90, VY + 890
        qs.panel(draw, top, bottom)
        draw.text((540, top + 140), "$50 cake", font=mv.font("Bold", 110), fill=ORANGE + (255,), anchor="ms")
        draw.text((540, top + 260), "3 hours of work", font=mv.font("Bold", 70), fill=LABEL + (255,), anchor="ms")
        draw.line((180, top + 320, 900, top + 320), fill=(203, 213, 225, 255), width=4)
        draw.text((540, top + 470), "= good money", font=mv.font("Bold", 92), fill=mv.INK + (255,), anchor="ms")

        def cross(d):
            d.line((150, top + 430, 930, top + 400), fill=r.RED + (255,), width=16)
            d.rounded_rectangle((360, top + 540, 720, top + 640), radius=24, fill=r.RED + (255,))
            d.text((540, top + 590), "WRONG", font=mv.font("Bold", 64), fill=WHITE, anchor="mm")
        qs.appear(out, t, crossed_at, cross)

        def real(d):
            d.rounded_rectangle((110, top + 670, 970, top + 780), radius=24, fill=PALE_RED + (255,))
            d.text((540, top + 725), "You might make $6.20/hr", font=mv.font("Bold", 58), fill=r.RED + (255,), anchor="mm")
        qs.appear(out, t, real_at, real)
        return out
    return frame


def cake_card(base, materials_at, kitchen_at, left_at):
    """The $50 cake seen from above, cut into the share that goes on materials, on the kitchen, and what is left."""
    cx, radius = 540, 230
    slices = [("Materials", 25.40, SLATE, materials_at), ("Kitchen and fees", 6.00, ORANGE, kitchen_at)]

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 900
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "ONE $50 CAKE", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        cy = top + 340
        box = (cx - radius, cy - radius, cx + radius, cy + radius)
        draw.ellipse((box[0] - 14, box[1] - 14, box[2] + 14, box[3] + 14), fill=(241, 245, 249, 255))
        draw.ellipse(box, fill=SPONGE + (255,))
        start = -90.0
        for name, amount, fill, at in slices:
            share = amount / 50 * r.ease((t - at) / GROW)
            if share > 0:
                draw.pieslice(box, start, start + 360 * share, fill=fill + (255,), outline=WHITE, width=6)
            start += 360 * share
        if t >= left_at:
            draw.pieslice(box, start, 270, fill=qv.GREEN + (255,), outline=WHITE, width=6)
            middle = math.radians((start + 270) / 2)
            draw.text((cx + 0.58 * radius * math.cos(middle), cy + 0.58 * radius * math.sin(middle)), "$18.60", font=mv.font("Bold", 56), fill=WHITE, anchor="mm")
        rows = slices + [("Left for you", 18.60, qv.GREEN, left_at)]
        for n, (name, amount, fill, at) in enumerate(rows):
            y = top + 640 + n * 70

            def row(d, y=y, name=name, amount=amount, fill=fill, n=n):
                d.rounded_rectangle((150, y - 22, 194, y + 22), radius=8, fill=fill + (255,))
                d.text((220, y), name, font=mv.font("Bold" if n == 2 else "SemiBold", 46), fill=mv.INK + (255,), anchor="lm")
                colour = qv.GREEN if n == 2 else r.RED
                d.text((930, y), f"{'' if n == 2 else '-'}${amount:,.2f}", font=mv.font("Bold", 50), fill=colour + (255,), anchor="rm")
            qs.appear(out, t, at, row)
        return out
    return frame


def pay_card(base, hours_at, rate_at, goal_at, price_at):
    """The $18.60 shared over the three hours as columns of pay, against the $25 an hour the baker meant to earn, and the
    price that would pay it."""
    scale = 360 / 25
    width, gap = 200, 60
    left = (1080 - 3 * width - 2 * gap) // 2

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 900
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "YOUR PAY FOR EACH HOUR", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        floor = top + 470
        goal = r.ease((t - goal_at) / GROW)
        for n in range(3):
            x = left + n * (width + gap)
            if goal > 0:
                y = floor - 25 * scale * goal
                draw.rounded_rectangle((x, y, x + width, floor), radius=16, outline=qv.GREEN + (255,), width=5)
            fill = r.ease((t - hours_at - 0.25 * n) / GROW)
            if fill > 0:
                draw.rounded_rectangle((x, floor - 6.20 * scale * fill, x + width, floor), radius=12, fill=r.RED + (255,))
            draw.text((x + width / 2, floor + 50), f"Hour {n + 1}", font=mv.font("SemiBold", 38), fill=mv.INK + (255,), anchor="ms")
            if t >= hours_at + 0.25 * n + GROW:
                draw.text((x + width / 2, floor - 6.20 * scale - 22), "$6.20", font=mv.font("Bold", 46), fill=r.RED + (255,), anchor="ms")
            if t >= goal_at + GROW:
                draw.text((x + width / 2, floor - 25 * scale + 60), "$25", font=mv.font("Bold", 50), fill=qv.GREEN + (255,), anchor="ms")

        def rate(d):
            d.rounded_rectangle((110, top + 560, 970, top + 660), radius=24, fill=PALE_RED + (255,))
            d.text((150, top + 610), "You made", font=mv.font("Bold", 44), fill=mv.INK + (255,), anchor="lm")
            d.text((930, top + 610), "$6.20/hr", font=mv.font("Bold", 64), fill=r.RED + (255,), anchor="rm")
        qs.appear(out, t, rate_at, rate)

        def price(d):
            d.rounded_rectangle((110, top + 690, 970, top + 790), radius=24, fill=PALE_GREEN + (255,))
            d.text((150, top + 740), "To earn $25/hr, charge", font=mv.font("Bold", 42), fill=mv.INK + (255,), anchor="lm")
            d.text((930, top + 740), "$106.40", font=mv.font("Bold", 64), fill=qv.GREEN + (255,), anchor="rm")
        qs.appear(out, t, price_at, price)
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

    hook = hook_card(base, word(0, "You"), word(0, "6"))
    cake = cake_card(base, word(1, "ingredients"), word(2, "Add"), word(2, "leaves"))
    pay = pay_card(base, line(3) - 0.1, word(3, "So"), word(4, "25"), word(4, "106"))

    def still(at, card, title):
        return {"at": at, "still": lambda age: card(at + age), "title": title}

    return [
        still(0.0, hook, "Selling a cake for $50?"),
        still(line(1) - 0.1, cake, "What it costs"),
        still(line(2) - 0.1, cake, "What's left"),
        still(line(3) - 0.1, pay, "Three hours"),
        still(line(4) - 0.1, pay, "What it should cost"),
        still(line(5) - 0.1, pay, "Busy and broke"),
        {"at": line(6) - 0.1, "take": "cake", "from": ("click_mat", -0.4), "title": "Free cake pricing calculator",
         "keys": [("click_mat", -0.4, 1.3, 1056, 560), ("mats_done", 0.0, 1.3, 1056, 900)]},
        {"at": word(6, "website"), "take": "cake", "from": ("priced", 0.05), "title": "Price your cakes properly",
         "keys": [("priced", 0.05, 1.3, 2144, 640), ("look_price", 1.6, 1.45, 2144, 560)]},
        {"at": line(8) - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    inv.prepare(TAKES, "cake")
    voice_path = WORK / "voice.wav"
    r.write_mono(voice_path, mv.voice_line(inv.spoken(LINES)))
    words = short.phrase_timed_words(short.scripted_words(voice_path, SCRIPT, WORK), voice_path)
    total = max(w["end"] for w in words) + HOLD_END
    chunks = short.balanced_chunks(words, longest_pause=0.6)
    pc.finish(shot_list(words, total, short.background()), chunks, voice_path, WORK, OUT_FILE, MUSIC_FROM,
              load_take=load_take, end_card=inv.end_card_saying("Free calculator linked below"))


if __name__ == "__main__":
    main()
