"""Make the vertical Short "Made $60,000 freelancing?": why a US freelancer who sets aside their 12% tax bracket comes up
short. What they set aside is drawn against what they owe, self-employment tax first and then income tax, and the gap is
given. It ends on the free self-employed tax calculator on argorobots.com. Narrated by the Azure voice, with big captions
timed to each word.

Usage: python freelance_tax_short.py

The figures are the calculator's federal 2026 estimate for $60,000 of profit. Its take is recorded with
`node record_generator.js freelancetax work/freelance_tax_short/takes`. The finished video goes to work/videos/.
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
WORK = HERE / "work" / "freelance_tax_short"
TAKES = WORK / "takes"
OUT_FILE = HERE / "work" / "videos" / "short_freelance_tax.mp4"
LINES = [
    ("Made 60,000 dollars freelancing? You could owe the IRS almost 13,000.", 450),
    ("Most people look up their tax bracket, see 12 percent, and set aside 7,200 dollars.", 400),
    ("But when you work for yourself, you also pay self-employment tax. That's Social Security and Medicare, and on 60,000 it's 8,478 dollars.", 450),
    ("Income tax adds another 4,511. So the real bill is 12,989 dollars, about 22 percent.", 450),
    ("Set aside 12 percent, and you're almost 5,800 dollars short. And that's before state tax.", 450),
    ("The IRS also wants it in four payments through the year, not all in April.", 450),
    ("There's a free self-employed tax calculator on the Argo Books website,", 200),
    ("so you know what to set aside before the bill comes.", 450),
    ("It's linked below.", 0),
]
SCRIPT = " ".join(text for text, _ in LINES)
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 160.0
VX, VY, VW, VH = short.VIEW
LABEL = (124, 135, 158)
PALE_RED = (254, 226, 226)
PALE_GREEN = (220, 252, 231)
SLATE = (100, 116, 139)
ORANGE = (241, 100, 30)
WHITE = (255, 255, 255, 255)
GROW = 0.7
SET_ASIDE, SE_TAX, INCOME_TAX = 7200, 8477.73, 4511.34


def load_take(name):
    log = json.loads((TAKES / f"{name}.json").read_text())
    log["path"] = TAKES / f"{name}.mkv"
    return log


def hook_card(base, crossed_at, real_at):
    """The set-aside a freelancer works out from their tax bracket, crossed out, then what they could really owe."""
    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 90, VY + 890
        qs.panel(draw, top, bottom)
        draw.text((540, top + 140), "$60,000 profit", font=mv.font("Bold", 100), fill=ORANGE + (255,), anchor="ms")
        draw.text((540, top + 260), "x 12% tax bracket", font=mv.font("Bold", 70), fill=LABEL + (255,), anchor="ms")
        draw.line((180, top + 320, 900, top + 320), fill=(203, 213, 225, 255), width=4)
        draw.text((540, top + 470), "= $7,200 tax", font=mv.font("Bold", 92), fill=mv.INK + (255,), anchor="ms")

        def cross(d):
            d.line((170, top + 430, 910, top + 400), fill=r.RED + (255,), width=16)
            d.rounded_rectangle((360, top + 540, 720, top + 640), radius=24, fill=r.RED + (255,))
            d.text((540, top + 590), "WRONG", font=mv.font("Bold", 64), fill=WHITE, anchor="mm")
        qs.appear(out, t, crossed_at, cross)

        def real(d):
            d.rounded_rectangle((110, top + 670, 970, top + 780), radius=24, fill=PALE_RED + (255,))
            d.text((540, top + 725), "You could owe $12,989", font=mv.font("Bold", 58), fill=r.RED + (255,), anchor="mm")
        qs.appear(out, t, real_at, real)
        return out
    return frame


def bill_card(base, set_at, se_at, income_at, total_at):
    """What was set aside as one bar, against what is owed: self-employment tax, then income tax stacked on top of it."""
    left, span = 150, 780
    scale = span / 13000

    def bar(draw, y, x0, amount, grow, fill):
        if grow > 0:
            draw.rounded_rectangle((x0, y, x0 + max(24, amount * scale * grow), y + 90), radius=14, fill=fill + (255,))

    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 900
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "TAX ON $60,000 OF PROFIT", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        y1, y2 = top + 210, top + 460
        draw.text((left, y1 - 30), "You set aside (12%)", font=mv.font("SemiBold", 44), fill=mv.INK + (255,), anchor="ls")
        bar(draw, y1, left, SET_ASIDE, r.ease((t - set_at) / GROW), SLATE)
        if t >= set_at + GROW:
            draw.text((left + SET_ASIDE * scale + 20, y1 + 45), "$7,200", font=mv.font("Bold", 50), fill=SLATE + (255,), anchor="lm")
        if t >= se_at:
            draw.text((left, y2 - 30), "You owe", font=mv.font("SemiBold", 44), fill=mv.INK + (255,), anchor="ls")
        bar(draw, y2, left, SE_TAX, r.ease((t - se_at) / GROW), ORANGE)
        grow = r.ease((t - income_at) / GROW)
        if grow > 0:
            x0 = left + SE_TAX * scale
            draw.rectangle((x0 - 14, y2, x0 + 6, y2 + 90), fill=ORANGE + (255,))
            draw.rounded_rectangle((x0, y2, x0 + INCOME_TAX * scale * grow, y2 + 90), radius=14, fill=r.RED + (255,))
            draw.line((x0, y2, x0, y2 + 90), fill=WHITE, width=6)
        rows = [("Self-employment tax", SE_TAX, ORANGE, se_at), ("Income tax", INCOME_TAX, r.RED, income_at)]
        for n, (name, amount, fill, at) in enumerate(rows):
            y = top + 640 + n * 70

            def row(d, y=y, name=name, amount=amount, fill=fill):
                d.rounded_rectangle((150, y - 22, 194, y + 22), radius=8, fill=fill + (255,))
                d.text((220, y), name, font=mv.font("SemiBold", 46), fill=mv.INK + (255,), anchor="lm")
                d.text((930, y), f"${amount:,.0f}", font=mv.font("Bold", 50), fill=fill + (255,), anchor="rm")
            qs.appear(out, t, at + 0.3, row)

        def total(d):
            y = top + 790
            d.line((150, y - 40, 930, y - 40), fill=(203, 213, 225, 255), width=4)
            d.text((150, y), "Real bill", font=mv.font("Bold", 50), fill=mv.INK + (255,), anchor="lm")
            d.text((930, y), "$12,989", font=mv.font("Bold", 60), fill=r.RED + (255,), anchor="rm")
        qs.appear(out, t, total_at, total)
        return out
    return frame


def short_card(base, gap_at, state_at, quarter_at):
    """How far short the 12% leaves the freelancer, and the four payments the bill is really due in."""
    def frame(t):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 60, VY + 900
        qs.panel(draw, top, bottom)
        draw.text((540, top + 70), "WHEN THE BILL COMES", font=mv.font("Bold", 40), fill=LABEL + (255,), anchor="ms")
        for n, (name, amount, fill) in enumerate([("You owe", "$12,989", r.RED), ("You set aside", "-$7,200", SLATE)]):
            y = top + 170 + n * 80
            draw.text((150, y), name, font=mv.font("SemiBold", 48), fill=mv.INK + (255,), anchor="lm")
            draw.text((930, y), amount, font=mv.font("Bold", 54), fill=fill + (255,), anchor="rm")

        def gap(d):
            d.rounded_rectangle((110, top + 320, 970, top + 430), radius=24, fill=PALE_RED + (255,))
            d.text((150, top + 375), "You're short", font=mv.font("Bold", 48), fill=mv.INK + (255,), anchor="lm")
            d.text((930, top + 375), "$5,789", font=mv.font("Bold", 68), fill=r.RED + (255,), anchor="rm")
        qs.appear(out, t, gap_at, gap)

        def state(d):
            d.text((540, top + 490), "...and that's before state tax", font=mv.font("SemiBold", 42), fill=LABEL + (255,), anchor="ms")
        qs.appear(out, t, state_at, state)

        width, space = 180, 20
        x0 = (1080 - 4 * width - 3 * space) // 2
        for n, month in enumerate(["Apr", "Jun", "Sep", "Jan"]):
            def payment(d, n=n, month=month):
                x = x0 + n * (width + space)
                d.rounded_rectangle((x, top + 560, x + width, top + 720), radius=18, fill=PALE_GREEN + (255,))
                d.text((x + width / 2, top + 615), month, font=mv.font("SemiBold", 40), fill=mv.INK + (255,), anchor="mm")
                d.text((x + width / 2, top + 675), "$3,247", font=mv.font("Bold", 44), fill=qv.GREEN + (255,), anchor="mm")
            qs.appear(out, t, quarter_at + 0.25 * n, payment)

        def due(d):
            d.text((540, top + 790), "Paid in 4 parts, not all in April", font=mv.font("Bold", 46), fill=mv.INK + (255,), anchor="ms")
        qs.appear(out, t, quarter_at + 1.0, due)
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

    hook = hook_card(base, word(0, "You"), word(0, "almost"))
    bill = bill_card(base, word(1, "12"), word(2, "self-employment"), word(3, "Income"), word(3, "12989"))
    gap = short_card(base, word(4, "5800"), word(4, "state"), line(5))

    def still(at, card, title):
        return {"at": at, "still": lambda age: card(at + age), "title": title}

    return [
        still(0.0, hook, "Made $60,000 freelancing?"),
        still(line(1) - 0.1, bill, "What most people set aside"),
        still(line(2) - 0.1, bill, "Self-employment tax"),
        still(line(3) - 0.1, bill, "The real bill"),
        still(line(4) - 0.1, gap, "How far short"),
        still(line(5) - 0.1, gap, "Four payments"),
        {"at": line(6) - 0.1, "take": "freelancetax", "from": ("click_income", -0.4), "title": "Free tax calculator",
         "keys": [("click_income", -0.4, 1.3, 1042, 640), ("income_done", 0.0, 1.3, 1042, 640)]},
        {"at": word(6, "website"), "take": "freelancetax", "from": ("income_done", 0.05), "title": "Know what to set aside",
         "keys": [("income_done", 0.05, 1.3, 2130, 760), ("look_total", 1.0, 1.45, 2130, 600)]},
        {"at": line(8) - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    inv.prepare(TAKES, "freelancetax")
    voice_path = WORK / "voice.wav"
    r.write_mono(voice_path, mv.voice_line(inv.spoken(LINES)))
    words = short.phrase_timed_words(short.scripted_words(voice_path, SCRIPT, WORK), voice_path)
    total = max(w["end"] for w in words) + HOLD_END
    chunks = short.balanced_chunks(words, longest_pause=0.6)
    pc.finish(shot_list(words, total, short.background()), chunks, voice_path, WORK, OUT_FILE, MUSIC_FROM,
              load_take=load_take, end_card=inv.end_card_saying("Free calculator linked below"))


if __name__ == "__main__":
    main()
