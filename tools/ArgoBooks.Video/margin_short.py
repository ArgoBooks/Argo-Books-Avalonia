"""Make the vertical Short "Don't mark it up 50%": why a 50 percent markup is only a 33 percent margin. One sale is drawn as
a bar of cost and profit, the profit is measured against the cost and then against the price, the bar grows to the price
that really keeps half, and the two are compared over a year of sales. It ends on the free markup and margin calculator on
argorobots.com. Narrated by the Azure voice, with big captions timed to each word.

Usage: python margin_short.py

The calculator take is recorded with `node record_generator.js margin work/margin_short/takes`, and the figures on the cards
are the ones it shows for that example. The finished video goes to work/videos/.
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
WORK = HERE / "work" / "margin_short"
TAKES = WORK / "takes"
OUT_FILE = HERE / "work" / "videos" / "short_markup_margin.mp4"
LINES = [
    ("Want a 50 percent profit margin? Don't mark things up 50 percent.", 450),
    ("Say something costs you 40 dollars. Add 50 percent, and you sell it for 60.", 400),
    ("That's 20 dollars of profit. Markup measures it against your cost, 40, so it's 50 percent.", 400),
    ("But margin measures it against the price, 60. And 20 out of 60 is only 33 percent.", 450),
    ("To really keep half of every sale, you have to double the cost, and charge 80 dollars.", 450),
    ("Across 100,000 dollars of sales, a 50 percent markup keeps about 33,000. A 50 percent margin keeps 50,000.", 450),
    ("There's a free markup and margin calculator on the Argo Books website,", 200),
    ("so you can check your own prices.", 450),
    ("It's linked below.", 0),
]
SCRIPT = " ".join(text for text, _ in LINES)
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 75.0
VX, VY, VW, VH = short.VIEW
LABEL = (124, 135, 158)
GROW = 0.9
BAR_LEFT, BAR_RIGHT = 130, 950


def load_take(name):
    log = json.loads((TAKES / f"{name}.json").read_text())
    log["path"] = TAKES / f"{name}.mkv"
    return log


def hook_card(base, crossed_at):
    """The sum most sellers do, crossed out as the voice says not to do it."""
    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 120, VY + 820
        qs.panel(draw, top, bottom)
        draw.text((540, top + 130), "$40 cost", font=mv.font("Bold", 92), fill=mv.INK + (255,), anchor="ms")
        draw.text((540, top + 260), "+ 50% markup", font=mv.font("Bold", 78), fill=LABEL + (255,), anchor="ms")
        draw.line((180, top + 320, 900, top + 320), fill=(203, 213, 225, 255), width=4)
        draw.text((540, top + 470), "= 50% profit", font=mv.font("Bold", 100), fill=mv.INK + (255,), anchor="ms")

        def cross(d):
            d.line((150, top + 430, 930, top + 400), fill=r.RED + (255,), width=16)
            d.rounded_rectangle((360, top + 540, 720, top + 640), radius=24, fill=r.RED + (255,))
            d.text((540, top + 590), "WRONG", font=mv.font("Bold", 64), fill=(255, 255, 255, 255), anchor="mm")
        qs.appear(out, t, crossed_at, cross)
        return out
    return frame


def sale_card(base, cost_at, bar_at, markup_at, margin_at, double_at):
    """One sale as a bar of cost and profit. The profit is measured against the cost, then against the whole price, and then
    the profit grows until it is half the price."""
    scale = (BAR_RIGHT - BAR_LEFT) / 80

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 80, VY + 860
        qs.panel(draw, top, bottom)
        grown = r.ease((t - double_at) / GROW)
        profit = 20 + 20 * grown
        price = 40 + profit
        done = t >= double_at + GROW
        draw.text((540, top + 90), f"ONE ${price:,.0f} SALE", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        y0, y1 = top + 180, top + 320
        cost_end = BAR_LEFT + 40 * scale
        price_end = BAR_LEFT + price * scale

        def cost(d):
            d.rounded_rectangle((BAR_LEFT, y0, cost_end, y1), radius=18, fill=mv.BLUE + (255,))
            d.text(((BAR_LEFT + cost_end) / 2, (y0 + y1) / 2), "Cost $40", font=mv.font("Bold", 46), fill=(255, 255, 255, 255), anchor="mm")
        qs.appear(out, t, cost_at, cost)

        def bar(d):
            d.rectangle((cost_end - 18, y0, cost_end, y1), fill=mv.BLUE + (255,))
            d.rounded_rectangle((cost_end, y0, price_end, y1), radius=18, fill=qv.GREEN + (255,))
            d.rectangle((cost_end, y0, cost_end + 18, y1), fill=qv.GREEN + (255,))
            d.text(((cost_end + price_end) / 2, (y0 + y1) / 2), f"${profit:,.0f}", font=mv.font("Bold", 46), fill=(255, 255, 255, 255), anchor="mm")
            d.text(((cost_end + price_end) / 2, y0 - 22), "PROFIT", font=mv.font("Bold", 30), fill=qv.GREEN + (255,), anchor="ms")
        qs.appear(out, t, bar_at, bar)

        def bracket(d, x0, x1, y, colour):
            d.line((x0 + 4, y, x1 - 4, y), fill=colour + (255,), width=6)
            d.line((x0 + 4, y - 18, x0 + 4, y), fill=colour + (255,), width=6)
            d.line((x1 - 4, y - 18, x1 - 4, y), fill=colour + (255,), width=6)

        def row(d, y, name, sum_text, answer, colour, fill):
            d.rounded_rectangle((110, y, 970, y + 170), radius=26, fill=fill + (255,))
            d.text((150, y + 52), name, font=mv.font("Bold", 34), fill=colour + (255,), anchor="lm")
            d.text((150, y + 118), sum_text, font=mv.font("SemiBold", 44), fill=mv.INK + (255,), anchor="lm")
            d.text((930, y + 92), answer, font=mv.font("Bold", 92), fill=colour + (255,), anchor="rm")

        def markup(d):
            bracket(d, BAR_LEFT, cost_end, y1 + 34, mv.BLUE)
            row(d, top + 400, "MARKUP: profit ÷ cost", f"${profit:,.0f} ÷ $40", f"{profit / 40:.0%}", mv.BLUE, (219, 234, 254))
        qs.appear(out, t, markup_at, markup)

        def margin(d):
            bracket(d, BAR_LEFT, price_end, y1 + 64, r.RED if not done else qv.GREEN)
            colour, fill = (qv.GREEN, (220, 252, 231)) if done else (r.RED, (254, 226, 226))
            row(d, top + 600, "MARGIN: profit ÷ price", f"${profit:,.0f} ÷ ${price:,.0f}", f"{profit / price:.0%}", colour, fill)
        qs.appear(out, t, margin_at, margin)
        return out
    return frame


def compare_card(base, low_at, high_at):
    """What each kind of 50 percent really keeps out of a year of sales."""
    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 120, VY + 820
        qs.panel(draw, top, bottom)
        draw.text((540, top + 90), "PROFIT ON $100,000 OF SALES", font=mv.font("Bold", 38), fill=LABEL + (255,), anchor="ms")

        def side(d, x0, x1, name, keep, fill, accent):
            d.rounded_rectangle((x0, top + 160, x1, top + 620), radius=28, fill=fill + (255,), outline=accent + (255,), width=4)
            middle = (x0 + x1) / 2
            d.text((middle, top + 260), "50%", font=mv.font("Bold", 72), fill=mv.INK + (255,), anchor="ms")
            d.text((middle, top + 320), name, font=mv.font("Regular", 40), fill=mv.GREY + (255,), anchor="ms")
            d.text((middle, top + 450), "You keep", font=mv.font("SemiBold", 38), fill=accent + (255,), anchor="ms")
            d.text((middle, top + 540), keep, font=mv.font("Bold", 76), fill=accent + (255,), anchor="ms")
        qs.appear(out, t, low_at, lambda d: side(d, 110, 525, "markup", "$33,333", (254, 226, 226), r.RED))
        qs.appear(out, t, high_at, lambda d: side(d, 555, 970, "margin", "$50,000", (220, 252, 231), qv.GREEN))
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

    hook = hook_card(base, word(0, "Don't"))
    sale = sale_card(base, word(1, "costs"), word(1, "Add"), word(2, "Markup"), line(3) - 0.1, word(4, "double"))
    compare = compare_card(base, line(5) + 0.4, word(5, "margin"))

    def still(at, card, title):
        return {"at": at, "still": lambda age: card(at + age), "title": title}

    return [
        still(0.0, hook, "Don't mark it up 50%"),
        still(line(1) - 0.1, sale, "One sale"),
        still(line(2) - 0.1, sale, "Markup"),
        still(line(3) - 0.1, sale, "Margin"),
        still(line(4) - 0.1, sale, "Keep half"),
        still(line(5) - 0.1, compare, "The difference"),
        {"at": line(6) - 0.1, "take": "margin", "from": ("click_cost", -0.6), "title": "Free margin calculator",
         "keys": [("click_cost", -0.6, 1.3, 1056, 900), ("click_fifty", 0.0, 1.3, 1056, 980)]},
        {"at": word(6, "website"), "take": "margin", "from": ("look_warn", -1.0), "title": "Check your prices",
         "keys": [("look_warn", -1.0, 1.3, 2144, 1000), ("look_warn", 1.8, 1.5, 2144, 1260)]},
        {"at": line(8) - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    inv.prepare(TAKES, "margin")
    voice_path = WORK / "voice.wav"
    r.write_mono(voice_path, mv.voice_line(inv.spoken(LINES)))
    words = short.phrase_timed_words(short.scripted_words(voice_path, SCRIPT, WORK), voice_path)
    total = max(w["end"] for w in words) + HOLD_END
    chunks = short.balanced_chunks(words, longest_pause=0.6)
    pc.finish(shot_list(words, total, short.background()), chunks, voice_path, WORK, OUT_FILE, MUSIC_FROM,
              load_take=load_take, end_card=inv.end_card_saying("Free calculator linked below"))


if __name__ == "__main__":
    main()
