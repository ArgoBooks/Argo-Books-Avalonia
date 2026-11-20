"""Make the vertical Short "How to read a profit and loss statement": the example company's year so far drawn as a waterfall,
revenue, then each expense coming off it, then the net profit left, then what one forgotten expense does to it, and the
receipt scan in Argo Books. Narrated by the Azure voice, with big captions timed to each word.

Usage: python pnl_short.py

It uses the `receipt` take of bookkeeping_video.py in work/books/takes, so record it first. The figures on the card are the
ones the Income Statement in that video's `reports` take shows, since the statement itself is too small to read in a Short. The finished video goes to work/videos/.
"""
from pathlib import Path

from PIL import ImageDraw

import bookkeeping_video as bv
import catches_short as short
import invoice_short as inv
import make_video as mv
import pc_short as pc
import qb_desktop_short as qs
import quickbooks_video as qv
import roundup as r

HERE = Path(__file__).resolve().parent
WORK = HERE / "work" / "pnl_short"
OUT_FILE = HERE / "work" / "videos" / "short_pnl.mp4"
LINES = [
    ("Here's how to read a profit and loss statement, in under a minute.", 450),
    ("It answers one question: did the business make money?", 400),
    ("At the top is revenue: everything customers paid you. Here, about 33,000 dollars.", 400),
    ("Then the expenses: what it cost to run the business. Parts, office supplies, utilities, the vehicle and software. About 7,000 dollars.", 400),
    ("Revenue minus expenses is the bottom line: your net profit. About 26,000 dollars.", 450),
    ("And that's roughly what your income tax is worked out on.", 400),
    ("So every expense you forget to record makes your profit look bigger, and your tax bill too.", 450),
    ("In Argo Books, you drop in a photo of a receipt,", 200),
    ("and it reads the expense for you, ready for your profit and loss.", 450),
    ("The full guide is linked below.", 0),
]
SCRIPT = " ".join(text for text, _ in LINES)
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 95.0
VX, VY, VW, VH = short.VIEW
# The year so far of the example company, as its Income Statement in the `reports` take shows it, in CAD.
REVENUE = 33262.00
EXPENSES = [("Parts", 3647.38), ("Office supplies", 1234.37), ("Utilities", 899.90), ("Vehicle", 778.41), ("Software", 425.00)]
SPENT = sum(amount for _, amount in EXPENSES)
PROFIT = REVENUE - SPENT
GROW = 0.6


def money(value, cents=False):
    return f"${value:,.2f}" if cents else f"${value:,.0f}"


def waterfall_card(base, times):
    """Revenue as a bar, expenses hanging off its top, and the net profit left over, each growing in on its word. The
    expense names fade in below as the voice reads them, and a forgotten expense shows as a dashed bar on top of profit.
    `times` holds seconds from the start of the Short: revenue, expenses, each expense name, profit, and forgotten."""
    revenue_at, expenses_at, names_at, profit_at, forgot_at = times
    top, bottom = VY + 60, VY + 950
    base_y, tallest = VY + 720, 380
    scale = tallest / REVENUE
    columns = [(130, 370), (420, 660), (710, 950)]
    revenue_top = base_y - REVENUE * scale
    spent_bottom = revenue_top + SPENT * scale

    def grown(t, at):
        return r.ease((t - at) / GROW)

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        qs.panel(draw, top, bottom)
        draw.text((130, top + 90), "PROFIT AND LOSS", font=mv.font("Bold", 38), fill=(124, 135, 158, 255), anchor="ls")
        if t < forgot_at:
            draw.text((130, top + 145), "January 1 to November 6", font=mv.font("Regular", 34), fill=mv.GREY + (255,), anchor="ls")
        draw.line((110, base_y, 970, base_y), fill=(203, 213, 225, 255), width=3)
        # Grey outlines of the finished chart, so the opening already reads as a chart before its bars grow in.
        for (x0, x1), y0, y1 in zip(columns, (revenue_top, revenue_top, spent_bottom), (base_y, spent_bottom, base_y)):
            draw.rounded_rectangle((x0, y0, x1, y1), radius=14, outline=(226, 232, 240, 255), width=3)
        for (x0, x1), name in zip(columns, ("Revenue", "Expenses", "Net profit")):
            draw.text(((x0 + x1) / 2, base_y + 50), name, font=mv.font("Bold", 38), fill=mv.INK + (255,), anchor="mm")

        p = grown(t, revenue_at)
        if p > 0:
            x0, x1 = columns[0]
            draw.rounded_rectangle((x0, base_y - REVENUE * scale * p, x1, base_y), radius=14, fill=mv.BLUE + (255,))
            qs.appear(out, t, revenue_at + GROW, lambda d: d.text(((x0 + x1) / 2, revenue_top - 30), money(REVENUE),
                                                                  font=mv.font("Bold", 46), fill=mv.INK + (255,), anchor="ms"))
        p = grown(t, expenses_at)
        if p > 0:
            x0, x1 = columns[1]
            draw.line((columns[0][1], revenue_top, x0, revenue_top), fill=(203, 213, 225, 255), width=3)
            draw.rounded_rectangle((x0, revenue_top, x1, revenue_top + SPENT * scale * p), radius=14, fill=r.RED + (255,))
            qs.appear(out, t, expenses_at + GROW, lambda d: d.text(((x0 + x1) / 2, revenue_top - 30), "- " + money(SPENT),
                                                                   font=mv.font("Bold", 46), fill=r.RED + (255,), anchor="ms"))
        for n, ((name, amount), at) in enumerate(zip(EXPENSES, names_at)):
            x, y = 130 + (n % 2) * 440, base_y + 128 + (n // 2) * 43

            def row(d, x=x, y=y, name=name, amount=amount):
                d.text((x, y), name, font=mv.font("Regular", 34), fill=mv.INK + (255,), anchor="ls")
                d.text((x + 400, y), money(amount), font=mv.font("SemiBold", 34), fill=r.RED + (255,), anchor="rs")
            qs.appear(out, t, at, row)
        p = grown(t, profit_at)
        if p > 0:
            x0, x1 = columns[2]
            draw.line((columns[1][1], spent_bottom, x0, spent_bottom), fill=(203, 213, 225, 255), width=3)
            draw.rounded_rectangle((x0, base_y - PROFIT * scale * p, x1, base_y), radius=14, fill=qv.GREEN + (255,))
            if t < forgot_at:
                qs.appear(out, t, profit_at + GROW, lambda d: d.text(((x0 + x1) / 2, spent_bottom - 30), money(PROFIT),
                                                                     font=mv.font("Bold", 46), fill=qv.GREEN + (255,), anchor="ms"))

        def forgot(d):
            x0, x1 = columns[2]
            ghost = spent_bottom - EXPENSES[0][1] * scale
            for y in range(round(ghost), round(spent_bottom), 18):
                d.line((x0, y, x0, y + 9), fill=r.RED + (255,), width=4)
                d.line((x1, y, x1, y + 9), fill=r.RED + (255,), width=4)
            for x in range(x0, x1, 18):
                d.line((x, ghost, x + 9, ghost), fill=r.RED + (255,), width=4)
            d.text(((x0 + x1) / 2, ghost - 30), money(PROFIT + EXPENSES[0][1]), font=mv.font("Bold", 46), fill=r.RED + (255,), anchor="ms")
            d.line((126, base_y + 116, 534, base_y + 116), fill=r.RED + (255,), width=4)
            d.text((130, top + 145), "Forget the parts: profit looks " + money(EXPENSES[0][1]) + " bigger", font=mv.font("Bold", 34),
                   fill=r.RED + (255,), anchor="ls")
        qs.appear(out, t, forgot_at, forgot)
        draw.text((540, bottom + 60), "An example business. Amounts in CAD.", font=mv.font("Regular", 30), fill=mv.GREY + (255,), anchor="mm")
        return out
    return frame


def shot_list(words, total, base):
    starts, n = [], 0
    for text, _ in LINES:
        starts.append(n)
        n += len(text.split())

    def line(k):
        return words[starts[k]]["at"]

    def word(k, text):
        last = starts[k + 1] if k + 1 < len(starts) else len(words)
        return next(w["at"] for w in words[starts[k]:last] if w["text"].strip(".,?:").lower() == text.lower())

    card = waterfall_card(base, [word(2, "revenue"), word(3, "expenses"),
                                 [word(3, "Parts"), word(3, "office"), word(3, "utilities"), word(3, "vehicle"), word(3, "software")],
                                 word(4, "profit"), word(6, "forget")])

    def still(at, title):
        return {"at": at, "still": lambda age: card(at + age), "title": title}

    return [
        still(0.0, "Profit and loss"),
        still(line(1) - 0.1, "Did it make money?"),
        still(line(2) - 0.1, "Revenue"),
        still(line(3) - 0.1, "Expenses"),
        still(line(4) - 0.1, "Net profit"),
        still(line(5) - 0.1, "What your tax is on"),
        still(line(6) - 0.1, "Every expense counts"),
        {"at": line(7) - 0.1, "take": "receipt", "from": ("dropped", -1.1), "title": "Drop in a receipt",
         "keys": [("dropped", -1.1, 1.2, 1600, 930), ("dropped", 0.0, 1.2, 1600, 930), ("dropped", 1.0, 1.1, 1570, 930)]},
        {"at": line(8) - 0.1, "take": "receipt", "from": ("scan_done", -0.2), "title": "Read for you",
         "keys": [("scan_done", -0.2, 1.15, 1976, 900), ("scan_done", 2.0, 1.2, 1976, 880)]},
        {"at": line(9) - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    voice_path = WORK / "voice.wav"
    r.write_mono(voice_path, mv.voice_line(inv.spoken(LINES)))
    words = short.phrase_timed_words(short.scripted_words(voice_path, SCRIPT, WORK), voice_path)
    total = max(w["end"] for w in words) + HOLD_END
    chunks = short.balanced_chunks(words, longest_pause=0.6)
    pc.finish(shot_list(words, total, short.background()), chunks, voice_path, WORK, OUT_FILE, MUSIC_FROM,
              load_take=bv.load_take, end_card=inv.end_card_saying("Full guide linked below"))


if __name__ == "__main__":
    main()
