"""Make "How Much to Set Aside for Taxes When You're Self-Employed": the 25 to 30 percent rule, the three things it covers,
a worked example drawn as it is added up, the free self-employed tax calculator on argorobots.com, and the habit that
makes it automatic. Its files go to work/taxes/, and the video to work/videos/.

Usage:
    python taxes_video.py voice              fetches and measures the voice
    python taxes_video.py frame 12 95        saves single frames to work/taxes/check/
    python taxes_video.py                    renders the video

The calculator take is recorded with `node record_generator.js taxcalc work/taxes/takes`. The app's takes are video 16's
`salestax` and video 17's `receipt` and `reports`.
"""
import json
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw

import bookkeeping_video as bv
import invoice_short as inv
import mac_video as mac
import make_video as mv
import qb_desktop_video as qd
import quickbooks_video as qv
import roundup as r

HERE = Path(__file__).resolve().parent
WORK = HERE / "work" / "taxes"
OUT_FILE = HERE / "work" / "videos" / "video_18_taxes.mp4"
TAKES = {"taxcalc": WORK / "takes", "receipt": HERE / "work" / "books" / "takes", "reports": HERE / "work" / "books" / "takes",
         "salestax": HERE / "work" / "qbvideo" / "takes"}

PARTS = {
    "hook": "A client pays you 3,000 dollars. How much of it can you actually spend? If you work for yourself, the honest answer is: not all of it. Part of that payment has a tax bill attached, and the bill doesn't arrive until months later.",
    "hook2": "So in this video: how much to set aside, why it's a share of your profit and not your income, the three things it has to cover, and real numbers for the US and Canada, from a free calculator you can use yourself.",
    "why": "First, why this catches people out. With a job, your employer takes the tax out of every paycheck before you ever see it. When you work for yourself, nobody does. Every payment lands whole.",
    "why2": "It feels like you earned all of it. But the bill comes months after the money does, and by then, the money's usually been spent.",
    "rule": "Here's the rule of thumb. Set aside 25 to 30 percent of your profit. Not of what clients pay you: of your profit, which is what's left after your business costs.",
    "rule2": "Say clients pay you 90,000 dollars in a year, and running the business costs 18,000. Your profit is 72,000, and that's the number your tax is worked out on.",
    "rule3": "It's a range, because tax isn't one flat rate. The first part of your profit is taxed lightly, each higher part more, and there's a second contribution on top. Smaller profits land near the bottom of the range, and larger ones near the top, or higher.",
    "buckets": "What you set aside has up to three things to cover.",
    "buckets2": "The first is income tax. That's the one everyone expects.",
    "buckets3": "The second surprises people. With a job, your employer paid half of your pension and social contributions. Working for yourself, you pay both halves. In the US, that's self-employment tax, at 15.3 percent on most of your net profit. In Canada, it's both shares of your CPP.",
    "buckets4": "The third is sales tax, and it was never yours at all. You collected it for the government, so keep it apart from your set-aside. What you owe is the sales tax you collected, minus the sales tax you paid on business purchases. Argo Books keeps that running total for you, period by period.",
    "example": "Let's put real numbers on it. A freelance web designer. Clients paid her 90,000 dollars this year, and she spent 18,000 running the business: software, a laptop, a co-working desk, and her accountant. Her profit is 72,000.",
    "example2": "At 28 percent, the middle of the range, she sets aside 20,160 dollars. That's about 1,680 a month.",
    "example3": "Now look at what recording her expenses did. If she'd never tracked those costs, and paid as if all 90,000 were profit, 28 percent would be 25,200. Keeping her receipts lowered her set-aside by over 5,000 dollars.",
    "calc": "28 percent is a planning figure. For a number that fits your own profit, there's a free self-employed tax calculator on argorobots.com, for the US and Canada, with nothing to sign up for.",
    "calc2": "Type in her income and her expenses. In the US, it estimates 16,064 dollars: about 10,000 of self-employment tax, and the rest federal income tax. That's 22 percent of her profit, before any state income tax, which is why most US freelancers aim for 25 to 30.",
    "calc3": "Switch to Canada, and Ontario, and it's 20,527 dollars, with her CPP and income tax. That's 28 and a half percent.",
    "method": "Knowing your number is half of it. The other half is not spending it.",
    "method2": "Open a separate savings account, and name it Tax. Then every time a payment lands, move your share into it that same day. Say a client pays you 3,000 dollars. At 28 percent, 840 goes into Tax, and the other 2,160 is yours to spend. If you charge sales tax, move that out too.",
    "method3": "When the bill comes, the money's already sitting there. You're not scrambling, you're just paying. And if you set aside a little too much, the rest is yours to keep.",
    "profit": "All of this depends on knowing your real profit, and that means recording every business cost. In Argo Books, you drop in a photo of a receipt, and it reads the supplier, the date and the amount for you.",
    "profit2": "Then the income statement shows your profit for the year so far, so you always know the number you're setting aside from.",
    "recap": "So: set aside 25 to 30 percent of your profit, not your income. Keep sales tax separate. Move your share the day each payment lands. And check your own number with the calculator, and with your accountant.",
    "ask": "Argo Books is free, and it's the first link in the description, along with the tax calculator. There's no account to create, and your books stay on your own computer, on Windows, Mac or Linux.",
}
SECTIONS = {"why", "rule", "buckets", "example", "calc", "method", "profit", "recap"}
CHAPTERS = [("hook", "How much to set aside"), ("why", "Why it catches people out"), ("rule", "1. The rule of thumb"),
            ("buckets", "2. What it covers"), ("example", "3. A worked example"), ("calc", "4. Your own number"),
            ("method", "5. Make it automatic"), ("profit", "6. Know your real profit"), ("recap", "Recap")]
FADE = 0.3
SECTION_HOLD = 2.6
SOFT_GREEN = (220, 252, 231)
SOFT_RED = (254, 226, 226)
SOFT_BLUE = (219, 234, 254)


def load_take(name):
    log = json.loads((TAKES[name] / f"{name}.json").read_text())
    log["path"] = TAKES[name] / f"{name}.mkv"
    return log


def appear(frame, t, at, draw_it):
    """Draws something that fades in at second `at` of the video."""
    p = r.ease((t - at) / FADE)
    if p <= 0:
        return
    layer = Image.new("RGBA", frame.size, (0, 0, 0, 0))
    draw_it(ImageDraw.Draw(layer))
    if p < 1:
        layer.putalpha(layer.getchannel("A").point(lambda a: int(a * p)))
    frame.alpha_composite(layer)


def ledger(title, rows, kicker=None, footnote=None):
    """A sum worked down the card: rows of (second, label, amount, kind) fading in on their seconds, where kind is "plain",
    "minus" (red), "total" (on a green band) or "note" (grey, smaller)."""
    def frame_at(t):
        frame = qv.backdrop().convert("RGBA")
        draw = ImageDraw.Draw(frame)
        x, y = 170, 150
        if kicker:
            draw.text((x, y), kicker.upper(), font=mv.font("Bold", 32), fill=mv.BLUE, anchor="ls")
            y += 30
        draw.text((x, y + 70), title, font=mv.font("Bold", 74), fill=mv.INK, anchor="ls")
        y += 170
        for at, label, amount, kind in rows:
            height = 84 if kind == "note" else 116

            def row(d, y=y, label=label, amount=amount, kind=kind):
                middle = y + height / 2
                if kind == "total":
                    d.rounded_rectangle((140, y + 6, 1780, y + height - 6), radius=20, fill=SOFT_GREEN + (255,))
                    d.text((x, middle), label, font=mv.font("Bold", 52), fill=mv.INK + (255,), anchor="lm")
                    d.text((1750, middle), amount, font=mv.font("Bold", 64), fill=qv.GREEN + (255,), anchor="rm")
                elif kind == "note":
                    d.text((x, middle), label, font=mv.font("Regular", 40), fill=mv.GREY + (255,), anchor="lm")
                    d.text((1750, middle), amount, font=mv.font("SemiBold", 44), fill=mv.GREY + (255,), anchor="rm")
                else:
                    colour = r.RED if kind == "minus" else mv.INK
                    d.text((x, middle), label, font=mv.font("Regular", 52), fill=mv.INK + (255,), anchor="lm")
                    d.text((1750, middle), amount, font=mv.font("Bold", 60), fill=colour + (255,), anchor="rm")
                    d.line((x, y + height, 1750, y + height), fill=qv.LINE + (255,), width=2)
            appear(frame, t, at, row)
            y += height + 14
        if footnote:
            draw.text((x, 1000), footnote, font=mv.font("Regular", 28), fill=mv.GREY, anchor="ls")
        return frame.convert("RGB")
    return frame_at


def buckets_card(times, footnote=None):
    """The three things a set-aside covers, side by side, the third kept apart because it was never the owner's money."""
    boxes = [("1", "Income tax", "On your profit, in bands", SOFT_BLUE, mv.BLUE),
             ("2", "Self-employment tax or CPP", "Both halves, yours and the employer's", SOFT_BLUE, mv.BLUE),
             ("3", "Sales tax", "Never yours. Keep it apart.", SOFT_RED, r.RED)]

    def frame_at(t):
        frame = qv.backdrop().convert("RGBA")
        draw = ImageDraw.Draw(frame)
        draw.text((170, 150), "WHAT YOUR SET-ASIDE COVERS", font=mv.font("Bold", 32), fill=mv.BLUE, anchor="ls")
        draw.text((170, 250), "Up to three things", font=mv.font("Bold", 74), fill=mv.INK, anchor="ls")
        for n, ((number, name, note, fill, accent), at) in enumerate(zip(boxes, times)):
            x0 = 170 + n * 540
            x1 = x0 + 480

            def box(d, x0=x0, x1=x1, number=number, name=name, note=note, fill=fill, accent=accent):
                d.rounded_rectangle((x0, 360, x1, 860), radius=28, fill=fill + (255,), outline=accent + (255,), width=4)
                d.ellipse((x0 + 40, 400, x0 + 120, 480), fill=accent + (255,))
                d.text((x0 + 80, 440), number, font=mv.font("Bold", 46), fill=(255, 255, 255, 255), anchor="mm")
                y = 560
                for line in qv.wrapped(d, name, mv.font("Bold", 42), x1 - x0 - 80):
                    d.text((x0 + 40, y), line, font=mv.font("Bold", 42), fill=mv.INK + (255,), anchor="ls")
                    y += 64
                for line in qv.wrapped(d, note, mv.font("Regular", 38), x1 - x0 - 80):
                    d.text((x0 + 40, y + 20), line, font=mv.font("Regular", 38), fill=mv.GREY + (255,), anchor="ls")
                    y += 50
            appear(frame, t, at, box)
        if footnote:
            draw.text((170, 1000), footnote, font=mv.font("Regular", 28), fill=mv.GREY, anchor="ls")
        return frame.convert("RGB")
    return frame_at


def split_card(times):
    """A client's payment split the day it lands: the share for tax into its own account, and what is left to spend."""
    def frame_at(t):
        frame = qv.backdrop().convert("RGBA")
        draw = ImageDraw.Draw(frame)
        draw.text((170, 150), "EVERY PAYMENT, THE DAY IT LANDS", font=mv.font("Bold", 32), fill=mv.BLUE, anchor="ls")
        draw.text((170, 250), "Move your share first", font=mv.font("Bold", 74), fill=mv.INK, anchor="ls")

        def payment(d):
            d.rounded_rectangle((660, 330, 1260, 520), radius=28, fill=(255, 255, 255, 255), outline=qv.LINE + (255,), width=3)
            d.text((960, 395), "A client pays", font=mv.font("Regular", 42), fill=mv.GREY + (255,), anchor="mm")
            d.text((960, 465), "$3,000", font=mv.font("Bold", 76), fill=mv.INK + (255,), anchor="mm")

        def tax(d):
            d.line((860, 530, 560, 640), fill=qv.LINE + (255,), width=6)
            d.rounded_rectangle((260, 640, 860, 880), radius=28, fill=SOFT_BLUE + (255,), outline=mv.BLUE + (255,), width=4)
            d.text((560, 705), "Savings account: Tax", font=mv.font("SemiBold", 42), fill=mv.BLUE + (255,), anchor="mm")
            d.text((560, 790), "$840", font=mv.font("Bold", 84), fill=mv.INK + (255,), anchor="mm")
            d.text((560, 850), "28%", font=mv.font("Regular", 34), fill=mv.GREY + (255,), anchor="mm")

        def yours(d):
            d.line((1060, 530, 1360, 640), fill=qv.LINE + (255,), width=6)
            d.rounded_rectangle((1060, 640, 1660, 880), radius=28, fill=SOFT_GREEN + (255,), outline=qv.GREEN + (255,), width=4)
            d.text((1360, 705), "Yours to spend", font=mv.font("SemiBold", 42), fill=qv.GREEN + (255,), anchor="mm")
            d.text((1360, 790), "$2,160", font=mv.font("Bold", 84), fill=mv.INK + (255,), anchor="mm")
        for at, part in zip(times, (payment, tax, yours)):
            appear(frame, t, at, part)
        return frame.convert("RGB")
    return frame_at


def end_card():
    frame = qv.backdrop().convert("RGBA")
    draw = ImageDraw.Draw(frame)
    mark = Image.open(mv.ASSETS / "logo.png").convert("RGBA")
    mark = mark.resize((780, round(780 * mark.height / mark.width)), Image.LANCZOS)
    frame.alpha_composite(mark, ((1920 - 780) // 2, 300))
    draw.text((960, 560), "Free accounting for Windows, Mac and Linux", font=mv.font("SemiBold", 48), fill=mv.INK, anchor="mm")
    draw.text((960, 650), "argorobots.com", font=mv.font("Bold", 70), fill=mv.BLUE, anchor="mm")
    draw.text((960, 750), "The self-employed tax calculator is linked in the description too.", font=mv.font("Regular", 36), fill=mv.GREY, anchor="mm")
    return frame.convert("RGB")


def shot_list(v):
    S, E, W = v.start, v.end, v.at

    def take(at, name, mark, offset, keys, until=None):
        shot = {"at": at, "take": name, "from": (mark, offset), "keys": keys}
        if until:
            shot["until"] = until
        return shot

    def still(at, image):
        return {"at": at, "still": image}

    def step(key, number, name, kind):
        return {**still(S(key) - 0.3, mac.section_card(number, name, kind)), "section": True}

    hook = ledger("How much of it is yours?", [
        (S("hook") + 0.2, "A client pays you", "$3,000", "plain"),
        (W("hook", "bill"), "Part of it has a tax bill attached", "?", "minus")], kicker="Working for yourself")
    inside = bv.reveal("In this video", [(1, "How much to set aside"), (2, "Why it's a share of profit, not income"),
                                         (3, "The three things it covers"), (4, "Real numbers for the US and Canada")],
                       [W("hook2", "how"), W("hook2", "why"), W("hook2", "three"), W("hook2", "real")], kicker="Set aside for taxes")
    job = bv.reveal("Nobody takes the tax out", [
        ("yes", "With a job: tax comes out of every paycheck before you see it"),
        ("no", "Working for yourself: every payment lands whole"),
        ("no", "The bill comes months later, after the money's been spent")],
        [W("why", "job"), W("why", "nobody"), S("why2") + 0.4], kicker="Why it catches people out")
    rule = bv.reveal("Set aside 25 to 30% of your profit", [
        ("yes", "Profit: what's left after your business costs"), ("no", "Not what clients pay you")],
        [W("rule", "Set"), W("rule", "Not")], kicker="1. The rule of thumb")
    profit = ledger("Tax is worked out on profit", [
        (W("rule2", "clients"), "Clients pay you", "$90,000", "plain"),
        (W("rule2", "running"), "Business costs", "- $18,000", "minus"),
        (W("rule2", "profit"), "Profit", "$72,000", "total")], kicker="1. The rule of thumb")
    range_card = bv.reveal("Why it's a range", [
        (1, "The first part of your profit is taxed lightly"), (2, "Each higher part is taxed more"),
        (3, "A second contribution comes on top"), (None, "Smaller profits: near 25%. Larger ones: near 30%, or higher")],
        [W("rule3", "first"), W("rule3", "higher"), W("rule3", "second"), W("rule3", "Smaller")], kicker="1. The rule of thumb")
    three = buckets_card([W("buckets2", "first"), W("buckets3", "second"), W("buckets4", "third")])
    halves = bv.reveal("Self-employed, you pay both halves", [
        (None, "US: self-employment tax, 15.3% on most of your net profit (Social Security and Medicare)"),
        (None, "Canada: both the employee and the employer share of your CPP")],
        [W("buckets3", "US"), W("buckets3", "Canada")],
        footnote="Sources: IRS, Self-employment tax, and CRA, CPP contributions for self-employed people. November 2026.", kicker="2. What it covers")
    example = ledger("A freelance web designer", [
        (W("example", "Clients"), "Clients paid her", "$90,000", "plain"),
        (W("example", "spent"), "Running the business", "- $18,000", "minus"),
        (W("example", "profit"), "Profit", "$72,000", "plain"),
        (W("example2", "sets"), "Set aside, at 28%", "$20,160", "total"),
        (W("example2", "month"), "About a month", "$1,680", "note")], kicker="3. A worked example")
    untracked = ledger("What recording her expenses saved", [
        (W("example3", "never"), "Expenses not recorded: 28% of $90,000", "$25,200", "plain"),
        (W("example3", "Keeping") - 0.2, "Expenses recorded: 28% of $72,000", "$20,160", "plain"),
        (W("example3", "lowered"), "Less to set aside", "$5,040", "total")], kicker="3. A worked example")
    countries = ledger("Same profit, two countries", [
        (S("calc3") + 0.1, "United States, federal only: 22.3% of profit", "$16,064 USD", "plain"),
        (W("calc3", "Ontario") + 0.4, "Canada, Ontario: 28.5% of profit", "$20,527 CAD", "plain"),
        (E("calc3") - 1.4, "Plus state income tax in most US states", "", "note")],
        footnote="The calculator's 2026 estimate for a single filer, with the standard deduction or the basic personal amount.", kicker="4. Your own number")
    split = split_card([W("method2", "client"), W("method2", "Tax", 2), W("method2", "yours")])
    bill = bv.reveal("When the bill comes", [("yes", "The money's already sitting there"), ("yes", "Set aside too much? The rest is yours")],
                     [S("method3") + 0.2, W("method3", "little")], kicker="5. Make it automatic")
    recap = bv.reveal("Recap", [
        (1, "Set aside 25 to 30% of your profit, not your income"), (2, "Keep sales tax separate"),
        (3, "Move your share the day each payment lands"), (4, "Check your own number with the calculator, and your accountant")],
        [W("recap", "Set"), W("recap", "Keep"), W("recap", "Move"), W("recap", "check")], kicker="Set aside for taxes")

    calc_top = (1.35, 1600, 700)
    us_result = (2.3, 2131, 690)
    shots = [
        still(0.0, hook),
        still(S("hook2") - 0.1, inside),

        still(S("why") - 0.3, job),

        step("rule", 1, "The rule of thumb", "25 to 30% of your profit"),
        still(W("rule", "Set") - 0.3, rule),
        still(S("rule2") - 0.1, profit),
        still(S("rule3") - 0.1, range_card),

        step("buckets", 2, "What it covers", "Up to three things"),
        still(S("buckets2") - 0.2, three),
        still(W("buckets3", "US") - 0.3, halves),
        still(S("buckets4") - 0.1, three),
        take(W("buckets4", "Argo") - 0.3, "salestax", "owed", -0.3, [("owed", -0.3, 1.2, 1900, 900), ("owed", 6.0, 1.3, 2000, 950)]),

        step("example", 3, "A worked example", "A freelance web designer"),
        still(W("example", "freelance") - 0.4, example),
        still(S("example3") - 0.1, untracked),

        step("calc", 4, "Your own number", "A free calculator for the US and Canada"),
        take(W("calc", "free") - 0.3, "taxcalc", "top", 0.0, [("top", 0.0, 1.0, 1600, 900), ("click_income", -0.3, 1.35, 1600, 700)], until=("click_income", -0.3)),
        take(S("calc2") - 0.1, "taxcalc", "click_income", -0.6,
             [("click_income", -0.6) + calc_top, ("expenses_done", 0.3) + calc_top, ("look_us", 0.0) + us_result, ("look_us", 5.0, 2.4, 2131, 690)],
             until=("click_country", -0.3)),
        take(S("calc3") - 0.1, "taxcalc", "click_country", -0.6,
             [("click_country", -0.6) + calc_top, ("ontario", 0.3) + calc_top, ("look_ca", 0.0) + us_result, ("look_ca", 5.0, 2.4, 2131, 690)]),
        still(E("calc3") - 1.4 - 0.6, countries),

        step("method", 5, "Make it automatic", "A separate account, and a share of every payment"),
        still(S("method2") - 0.1, split),
        still(S("method3") - 0.1, bill),

        step("profit", 6, "Know your real profit", "Record every business cost"),
        take(W("profit", "drop") - 0.3, "receipt", "drop_zone", -0.6, [("drop_zone", -0.6, 1.3, 1600, 960), ("dropped", 0.0, 1.35, 1600, 960)], until=("scanning", 0.6)),
        take(W("profit", "reads") - 0.3, "receipt", "scan_done", 0.0, [("scan_done", 0.0, 1.15, 1600, 930), ("look_total", 0.0, 1.45, 2200, 600)], until=("click_products", -0.4)),
        take(S("profit2") - 0.1, "reports", "statement", -0.3, [("statement", -0.3, 1.5, 1570, 1100), ("statement", 5.0, 1.62, 1570, 1150)], until=("statement", 5.3)),

        still(S("recap") - 0.3, recap),
        still(S("ask") - 0.1, end_card()),
    ]
    shots.sort(key=lambda s: s["at"])
    # A section's title stays up long enough to read, even when the voice moves on quickly.
    for card, following in zip(shots, shots[1:]):
        if card.get("section"):
            following["at"] = max(following["at"], card["at"] + SECTION_HOLD)
    outro = float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(r.OUTRO)],
                                 capture_output=True, text=True, check=True).stdout)
    shots.append({"at": v.length + 1.0, "outro": True})
    shots.append({"at": v.length + 1.0 + outro, "stop": True})
    return shots


def main():
    inv.build_take(TAKES["taxcalc"], "taxcalc")
    v = mac.Voice(PARTS, SECTIONS, WORK)
    if sys.argv[1:2] == ["voice"]:
        for key, (first, last) in v.spans.items():
            print(f"{first:6.1f} {last - first:5.1f} s  {key}")
        print(f"voice {v.length:.1f} s")
        print("heard:", " ".join(w["text"] for w in v.words))
        return
    shots = shot_list(v)
    if sys.argv[1:2] == ["frame"]:
        folder = WORK / "check"
        folder.mkdir(exist_ok=True)
        pages, sprites = qd.Pages(), {}
        for second in map(float, sys.argv[2:]):
            frame = qd.frame_at(shots, second, pages, sprites, load_take)
            if frame is not None:
                frame.save(folder / f"f{second:06.1f}.png")
        return
    picture = WORK / "picture.mp4"
    spoken = qd.render(shots, picture, load_take)
    total = shots[-1]["at"]
    mixed = WORK / "mix.wav"
    mv.write_wav(mixed, mac.mix(v, spoken, total))
    OUT_FILE.parent.mkdir(parents=True, exist_ok=True)
    mv.master(mixed, picture, OUT_FILE)
    mac.write_captions(OUT_FILE.with_suffix(".srt"), v, PARTS)
    meter = subprocess.run([mv.FFMPEG, "-hide_banner", "-nostats", "-i", str(OUT_FILE), "-af", "ebur128=peak=true", "-f", "null", "-"],
                           capture_output=True, text=True).stderr
    summary = meter[meter.rindex("Summary:"):]
    print(f"{OUT_FILE.name}: {total:.1f} s, {summary.split('I:')[1].split('LUFS')[0].strip()} LUFS, "
          f"true peak {summary.split('Peak:')[1].split('dBFS')[0].strip()} dBFS")
    for key, name in CHAPTERS:
        print(f"{mac.clock(0 if key == 'hook' else v.start(key))} - {name}")


if __name__ == "__main__":
    main()
