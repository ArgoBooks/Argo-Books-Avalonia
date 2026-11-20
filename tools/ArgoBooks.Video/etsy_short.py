"""Make the vertical Short "Selling on Etsy for $30?": how little of a $30 Etsy sale the seller keeps. The sale is drawn as one
bar that materials, shipping, Etsy's fees and an Offsite Ad each eat into, while the amount kept counts down. It ends on the
free Etsy fee calculator on argorobots.com. Narrated by the Azure voice, with big captions timed to each word.

Usage: python etsy_short.py

The calculator take is recorded with `node record_generator.js etsy work/etsy_short/takes`, and the figures on the cards are
the ones it shows for a US shop. The finished video goes to work/videos/.
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
WORK = HERE / "work" / "etsy_short"
TAKES = WORK / "takes"
OUT_FILE = HERE / "work" / "videos" / "short_etsy_fees.mp4"
LINES = [
    ("Selling something on Etsy for 30 dollars? You might only keep 8 of it.", 450),
    ("Say it costs you 8 dollars in materials, and you offer free shipping, which costs you 6.", 400),
    ("Etsy charges 20 cents to list it, 6.5 percent of the sale, and 3 percent plus 25 cents to take the payment. That's 3 dollars 30.", 400),
    ("So you keep 12 dollars 70.", 450),
    ("But if the buyer came from one of Etsy's Offsite Ads, Etsy takes another 15 percent. That's 4 dollars 50 more.", 400),
    ("Now you keep 8 dollars 20 out of 30, and that's before you pay yourself for your time.", 450),
    ("There's a free Etsy fee calculator on the Argo Books website,", 200),
    ("so you can see what each of your items really makes.", 450),
    ("It's linked below.", 0),
]
SCRIPT = " ".join(text for text, _ in LINES)
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 140.0
VX, VY, VW, VH = short.VIEW
LABEL = (124, 135, 158)
PALE_RED = (254, 226, 226)
SLATE = (100, 116, 139)
LIGHT_SLATE = (148, 163, 184)
ORANGE = (241, 100, 30)
MOVE = 0.7
BAR_LEFT, BAR_RIGHT = 130, 950
WHITE = (255, 255, 255, 255)
# What eats into the $30, in the order the voice names it.
BITES = [("Materials", 8.00, SLATE), ("Shipping", 6.00, LIGHT_SLATE), ("Etsy fees", 3.30, ORANGE), ("Offsite Ads", 4.50, r.RED)]


def load_take(name):
    log = json.loads((TAKES / f"{name}.json").read_text())
    log["path"] = TAKES / f"{name}.mkv"
    return log


def hook_card(base, crossed_at, real_at):
    """What a seller hopes a $30 sale is worth, crossed out, then what it can really leave them."""
    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 90, VY + 890
        qs.panel(draw, top, bottom)
        draw.text((540, top + 140), "$30 sale", font=mv.font("Bold", 110), fill=ORANGE + (255,), anchor="ms")
        draw.text((540, top + 260), "on Etsy", font=mv.font("Bold", 78), fill=LABEL + (255,), anchor="ms")
        draw.line((180, top + 320, 900, top + 320), fill=(203, 213, 225, 255), width=4)
        draw.text((540, top + 470), "= $30 for you", font=mv.font("Bold", 92), fill=mv.INK + (255,), anchor="ms")

        def cross(d):
            d.line((130, top + 430, 950, top + 400), fill=r.RED + (255,), width=16)
            d.rounded_rectangle((360, top + 540, 720, top + 640), radius=24, fill=r.RED + (255,))
            d.text((540, top + 590), "WRONG", font=mv.font("Bold", 64), fill=WHITE, anchor="mm")
        qs.appear(out, t, crossed_at, cross)

        def real(d):
            d.rounded_rectangle((110, top + 670, 970, top + 780), radius=24, fill=PALE_RED + (255,))
            d.text((540, top + 725), "You might keep $8.20", font=mv.font("Bold", 60), fill=r.RED + (255,), anchor="mm")
        qs.appear(out, t, real_at, real)
        return out
    return frame


def keep_card(base, bar_at, bite_times):
    """The $30 as one bar. Each cost eats in from the left in turn, and the amount kept counts down above it."""
    scale = (BAR_RIGHT - BAR_LEFT) / 30

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 900
        qs.panel(draw, top, bottom)
        if t < bar_at:
            return out
        # Etsy's three fees are named one at a time, so that bite grows in three steps.
        eaten = [sum(step * r.ease((t - when) / MOVE) for when, step in (at if isinstance(at, list) else [(at, amount)]))
                 for (_, amount, _), at in zip(BITES, bite_times)]
        kept = 30 - sum(eaten)
        draw.text((540, top + 80), "YOU KEEP", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        colour = r.RED if t >= bite_times[3] else qv.GREEN
        first = [at[0][0] if isinstance(at, list) else at for at in bite_times]
        draw.text((540, top + 230), f"${kept:,.2f}", font=mv.font("Bold", 140), fill=colour + (255,), anchor="ms")
        draw.text((540, top + 285), "of a $30 sale", font=mv.font("SemiBold", 36), fill=LABEL + (255,), anchor="ms")
        y0, y1 = top + 320, top + 440
        draw.rounded_rectangle((BAR_LEFT, y0, BAR_RIGHT, y1), radius=18, fill=qv.GREEN + (255,))
        x = BAR_LEFT
        for (_, _, fill), part in zip(BITES, eaten):
            if part <= 0:
                continue
            x1 = x + part * scale
            draw.rectangle((x, y0, x1, y1), fill=fill + (255,))
            x = x1
        draw.rounded_rectangle((BAR_LEFT, y0, BAR_RIGHT, y1), radius=18, outline=(255, 255, 255, 255), width=0)
        for n, ((name, _, fill), at, amount) in enumerate(zip(BITES, first, eaten)):
            y = top + 520 + n * 82

            def row(d, y=y, name=name, amount=amount, fill=fill):
                d.rounded_rectangle((150, y - 22, 194, y + 22), radius=8, fill=fill + (255,))
                d.text((220, y), name, font=mv.font("SemiBold", 46), fill=mv.INK + (255,), anchor="lm")
                d.text((930, y), f"-${amount:,.2f}", font=mv.font("Bold", 50), fill=r.RED + (255,), anchor="rm")
            qs.appear(out, t, at, row)
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

    hook = hook_card(base, word(0, "You"), word(0, "keep"))
    fees = [(word(2, "list"), 0.20), (word(2, "6.5"), 1.95), (word(2, "payment"), 1.15)]
    keep = keep_card(base, line(1) - 0.1, [word(1, "materials"), word(1, "6"), fees, word(4, "another")])

    def still(at, card, title):
        return {"at": at, "still": lambda age: card(at + age), "title": title}

    return [
        still(0.0, hook, "Selling on Etsy?"),
        still(line(1) - 0.1, keep, "Your costs"),
        still(line(2) - 0.1, keep, "Etsy's fees"),
        still(line(4) - 0.1, keep, "Offsite Ads"),
        still(line(5) - 0.1, keep, "What's left"),
        {"at": line(6) - 0.1, "take": "etsy", "from": ("click_price", -0.5), "title": "Free Etsy fee calculator",
         "keys": [("click_price", -0.5, 1.3, 1056, 750), ("typed", 0.0, 1.3, 1056, 1150)]},
        {"at": word(6, "website"), "take": "etsy", "from": ("click_more", -0.6), "title": "What each item makes",
         "keys": [("click_more", -0.6, 1.3, 2144, 620), ("ads_on", 1.2, 1.5, 2144, 520)]},
        {"at": line(8) - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    inv.prepare(TAKES, "etsy")
    voice_path = WORK / "voice.wav"
    r.write_mono(voice_path, mv.voice_line(inv.spoken(LINES)))
    words = short.phrase_timed_words(short.scripted_words(voice_path, SCRIPT, WORK), voice_path)
    total = max(w["end"] for w in words) + HOLD_END
    chunks = short.balanced_chunks(words, longest_pause=0.6)
    pc.finish(shot_list(words, total, short.background()), chunks, voice_path, WORK, OUT_FILE, MUSIC_FROM,
              load_take=load_take, end_card=inv.end_card_saying("Free calculator linked below"))


if __name__ == "__main__":
    main()
