"""Make the vertical Short "Giving 20% off?": why a 20 percent discount takes 67 percent more sales to make the same profit.
One $100 sale is drawn as a bar of cost and profit, the discount comes off the profit end, and the sales needed for the
same $500 are counted out at both prices. It ends on the free break-even calculator on argorobots.com. Narrated by the
Azure voice, with big captions timed to each word.

Usage: python discount_short.py

The calculator take is recorded with `node record_generator.js breakeven work/discount_short/takes`, and the figures on the
cards are the ones it shows for that example. The finished video goes to work/videos/.
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
WORK = HERE / "work" / "discount_short"
TAKES = WORK / "takes"
OUT_FILE = HERE / "work" / "videos" / "short_discount.mp4"
LINES = [
    ("Giving 20 percent off? You'll need to sell 67 percent more, just to make the same profit.", 450),
    ("Say you sell something for 100 dollars, and it costs you 50. You make 50 dollars on every sale.", 400),
    ("Take 20 percent off, and the price drops to 80. But your cost is still 50.", 400),
    ("So your profit drops from 50 to 30. The whole discount came out of your profit.", 450),
    ("Ten sales used to make 500 dollars. Now it takes 17 to make the same 500.", 400),
    ("That's 67 percent more sales for the same money. If the sale doesn't bring in that many, you'd have done better without it.", 450),
    ("There's a free break-even calculator on the Argo Books website,", 200),
    ("so you can try your own prices before you run a sale.", 450),
    ("It's linked below.", 0),
]
SCRIPT = " ".join(text for text, _ in LINES)
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 105.0
VX, VY, VW, VH = short.VIEW
LABEL = (124, 135, 158)
PALE_RED = (254, 226, 226)
PALE_GREEN = (220, 252, 231)
MOVE = 0.9
BAR_LEFT, BAR_RIGHT = 130, 950
WHITE = (255, 255, 255, 255)


def load_take(name):
    log = json.loads((TAKES / f"{name}.json").read_text())
    log["path"] = TAKES / f"{name}.mkv"
    return log


def hook_card(base, crossed_at, real_at):
    """The sum most sellers assume, crossed out as the voice gives the real one."""
    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 90, VY + 890
        qs.panel(draw, top, bottom)
        draw.text((540, top + 130), "20% off", font=mv.font("Bold", 100), fill=r.RED + (255,), anchor="ms")
        draw.text((540, top + 260), "$100  →  $80", font=mv.font("Bold", 78), fill=LABEL + (255,), anchor="ms")
        draw.line((180, top + 320, 900, top + 320), fill=(203, 213, 225, 255), width=4)
        draw.text((540, top + 470), "= 20% less profit", font=mv.font("Bold", 84), fill=mv.INK + (255,), anchor="ms")

        def cross(d):
            d.line((130, top + 430, 950, top + 400), fill=r.RED + (255,), width=16)
            d.rounded_rectangle((360, top + 540, 720, top + 640), radius=24, fill=r.RED + (255,))
            d.text((540, top + 590), "WRONG", font=mv.font("Bold", 64), fill=WHITE, anchor="mm")
        qs.appear(out, t, crossed_at, cross)

        def real(d):
            d.rounded_rectangle((110, top + 670, 970, top + 780), radius=24, fill=PALE_RED + (255,))
            d.text((540, top + 725), "Real cost: +67% more sales", font=mv.font("Bold", 56), fill=r.RED + (255,), anchor="mm")
        qs.appear(out, t, real_at, real)
        return out
    return frame


def sale_card(base, cost_at, profit_at, off_at, drop_at):
    """One sale as a bar of cost and profit. The discount comes off the profit end while the cost stays put, and then the
    profit's drop is spelled out."""
    scale = (BAR_RIGHT - BAR_LEFT) / 100

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 100, VY + 840
        qs.panel(draw, top, bottom)
        cut = 20 * r.ease((t - off_at) / MOVE)
        price = 100 - cut
        profit = price - 50
        draw.text((540, top + 90), f"ONE ${price:,.0f} SALE", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        y0, y1 = top + 180, top + 320
        cost_end = BAR_LEFT + 50 * scale
        price_end = BAR_LEFT + price * scale

        def cost(d):
            d.rounded_rectangle((BAR_LEFT, y0, cost_end, y1), radius=18, fill=mv.BLUE + (255,))
            d.text(((BAR_LEFT + cost_end) / 2, (y0 + y1) / 2), "Cost $50", font=mv.font("Bold", 46), fill=WHITE, anchor="mm")
        qs.appear(out, t, cost_at, cost)

        def gain(d):
            d.rectangle((cost_end - 18, y0, cost_end, y1), fill=mv.BLUE + (255,))
            if cut > 0:
                d.rounded_rectangle((cost_end, y0, BAR_RIGHT, y1), radius=18, fill=PALE_RED + (255,), outline=r.RED + (255,), width=4)
                d.text(((price_end + BAR_RIGHT) / 2, (y0 + y1) / 2), f"-${cut:,.0f}", font=mv.font("Bold", 40), fill=r.RED + (255,), anchor="mm")
            d.rounded_rectangle((cost_end, y0, price_end, y1), radius=18, fill=qv.GREEN + (255,))
            d.rectangle((cost_end, y0, cost_end + 18, y1), fill=qv.GREEN + (255,))
            d.text(((cost_end + price_end) / 2, (y0 + y1) / 2), f"${profit:,.0f}", font=mv.font("Bold", 46), fill=WHITE, anchor="mm")
            d.text(((cost_end + price_end) / 2, y0 - 22), "PROFIT", font=mv.font("Bold", 30), fill=qv.GREEN + (255,), anchor="ms")
            if cut > 0:
                d.text(((price_end + BAR_RIGHT) / 2, y0 - 22), "DISCOUNT", font=mv.font("Bold", 30), fill=r.RED + (255,), anchor="ms")
        qs.appear(out, t, profit_at, gain)

        def same(d):
            d.text(((BAR_LEFT + cost_end) / 2, y1 + 50), "still $50", font=mv.font("SemiBold", 38), fill=mv.BLUE + (255,), anchor="ms")
        qs.appear(out, t, off_at + MOVE, same)

        def drop(d):
            d.rounded_rectangle((110, top + 450, 970, top + 680), radius=26, fill=PALE_RED + (255,))
            d.text((150, top + 510), "PROFIT PER SALE", font=mv.font("Bold", 34), fill=r.RED + (255,), anchor="lm")
            d.text((150, top + 600), "$50 → $30", font=mv.font("Bold", 72), fill=mv.INK + (255,), anchor="lm")
            d.text((930, top + 600), "-40%", font=mv.font("Bold", 92), fill=r.RED + (255,), anchor="rm")
        qs.appear(out, t, drop_at, drop)
        return out
    return frame


def sales_card(base, full_at, sale_at, more_at):
    """The sales it takes to make $500, counted out as blocks at each price."""
    size, gap = 42, 8
    left = (1080 - 17 * size - 16 * gap) // 2
    count_time = 1.6

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 100, VY + 840
        qs.panel(draw, top, bottom)
        draw.text((540, top + 90), "SALES TO MAKE $500 PROFIT", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")

        def blocks(d, y, shown, fill):
            for n in range(shown):
                x = left + n * (size + gap)
                d.rounded_rectangle((x, y, x + size, y + 80), radius=8, fill=fill + (255,))

        def full(d):
            d.text((left, top + 180), "Full price, $50 each", font=mv.font("SemiBold", 40), fill=mv.INK + (255,), anchor="ls")
            d.text((1080 - left, top + 180), "10", font=mv.font("Bold", 56), fill=qv.GREEN + (255,), anchor="rs")
            blocks(d, top + 210, 10, qv.GREEN)
        qs.appear(out, t, full_at, full)

        if t >= sale_at:
            shown = min(17, 1 + int(17 * (t - sale_at) / count_time))
            draw.text((left, top + 380), "20% off, $30 each", font=mv.font("SemiBold", 40), fill=mv.INK + (255,), anchor="ls")
            draw.text((1080 - left, top + 380), f"{shown}", font=mv.font("Bold", 56), fill=r.RED + (255,), anchor="rs")
            blocks(draw, top + 410, shown, r.RED)

        def more(d):
            d.rounded_rectangle((110, top + 560, 970, top + 700), radius=26, fill=PALE_RED + (255,))
            d.text((540, top + 630), "+67% more sales", font=mv.font("Bold", 76), fill=r.RED + (255,), anchor="mm")
        qs.appear(out, t, more_at, more)
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

    hook = hook_card(base, word(0, "You'll"), word(0, "67"))
    sale = sale_card(base, word(1, "for"), word(1, "make"), word(2, "off"), word(3, "drops"))
    sales = sales_card(base, line(4), word(4, "Now"), line(5))

    def still(at, card, title):
        return {"at": at, "still": lambda age: card(at + age), "title": title}

    return [
        still(0.0, hook, "Giving 20% off?"),
        still(line(1) - 0.1, sale, "One sale"),
        still(line(2) - 0.1, sale, "20% off"),
        still(line(3) - 0.1, sale, "Where it comes from"),
        still(line(4) - 0.1, sales, "Same profit"),
        {"at": line(6) - 0.1, "take": "breakeven", "from": ("click_fixed", -0.5), "title": "Free break-even calculator",
         "keys": [("click_fixed", -0.5, 1.3, 1056, 880), ("typed", 0.0, 1.3, 1056, 960)]},
        {"at": word(6, "website"), "take": "breakeven", "from": ("look_full", -0.6), "title": "Try your prices first",
         "keys": [("look_full", -0.6, 1.3, 2144, 860), ("look_sale", 1.0, 1.45, 2144, 820)]},
        {"at": line(8) - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    inv.prepare(TAKES, "breakeven")
    voice_path = WORK / "voice.wav"
    r.write_mono(voice_path, mv.voice_line(inv.spoken(LINES)))
    words = short.phrase_timed_words(short.scripted_words(voice_path, SCRIPT, WORK), voice_path)
    total = max(w["end"] for w in words) + HOLD_END
    chunks = short.balanced_chunks(words, longest_pause=0.6)
    pc.finish(shot_list(words, total, short.background()), chunks, voice_path, WORK, OUT_FILE, MUSIC_FROM,
              load_take=load_take, end_card=inv.end_card_saying("Free calculator linked below"))


if __name__ == "__main__":
    main()
