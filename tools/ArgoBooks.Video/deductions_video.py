"""Make "Small Business Tax Deductions: What You Can Actually Write Off": what a write-off really saves, the test a cost has
to pass, the small costs people miss, the 2026 mileage rates on the free mileage calculator, the home office, the mistakes
to avoid, and keeping the proof. Its files go to work/deductions/, and the video to work/videos/.

Usage:
    python deductions_video.py voice              fetches and measures the voice
    python deductions_video.py frame 12 95        saves single frames to work/deductions/check/
    python deductions_video.py                    renders the video

The calculator take is recorded with `node record_generator.js mileage work/deductions/takes`. The app's takes are video
17's `receipt` and `reports`.
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
import taxes_video as tv

HERE = Path(__file__).resolve().parent
WORK = HERE / "work" / "deductions"
OUT_FILE = HERE / "work" / "videos" / "video_19_deductions.mp4"
TAKES = {"mileage": WORK / "takes", "receipt": HERE / "work" / "books" / "takes", "reports": HERE / "work" / "books" / "takes"}

PARTS = {
    "hook": "People say: it's a write-off, as if that makes the purchase free. It doesn't. But most self-employed people still pay more tax than they need to, because they miss deductions they're actually owed.",
    "hook2": "So in this video: what a write-off really saves you, the test a cost has to pass, the deductions people miss most, the 2026 mileage rates, the home office, and the mistakes to stay away from.",
    "free": "First, what a write-off actually is. A deduction doesn't come off your tax bill. It comes off your profit, the amount your tax is worked out on.",
    "free2": "Say you spend 1,000 dollars on the business, and about 30 percent of each extra dollar of profit would go to tax. The deduction saves you about 300 dollars. The other 700 still comes out of your pocket.",
    "free3": "So never buy something just for the write-off. But anything the business really needs, record it. If you don't, you pay that 300 dollars for nothing.",
    "test": "So what counts? In the US, the IRS says a business cost has to be ordinary and necessary: common in your line of work, and helpful to it. In Canada, the CRA asks whether you spent it to earn business income, and whether the amount is reasonable.",
    "test2": "And if something is partly personal, like your phone or your car, you claim only the business share.",
    "missed": "Now the ones people miss. It's rarely the rent or the big equipment. It's the small, regular costs.",
    "missed2": "Software and subscriptions that renew quietly. Bank fees, and the fee taken out of every card payment. The business share of your phone and internet. Your accountant's fee, which counts too. Business insurance. Courses that keep your skills current. And supplies, bought a few dollars at a time.",
    "missed3": "None of them feel big. Added up over a year, they're often thousands of dollars.",
    "miles": "The biggest one people miss is driving. In the US, you can claim a set rate for every business mile, instead of adding up fuel and repairs. And for 2026, the IRS raised that rate partway through the year: 72 and a half cents a mile until the end of June, and 76 cents from July.",
    "miles2": "There's a free mileage calculator on argorobots.com that handles the change. Say you drove 4,000 business miles in each half of the year. That's a deduction of 5,940 dollars. Add your tax rate, and it shows what that's worth in cash: about 1,780 dollars.",
    "miles3": "In Canada, self-employed people claim the business share of their real car costs instead: fuel, insurance, repairs and the rest, in line with the share of kilometres they drove for the business.",
    "miles4": "Either way, you need a log: the date, where you went, why, and how far. And driving from home to your regular place of work is commuting, which doesn't count.",
    "home": "If you work from home, part of your home costs can count. In the US, the space has to be used regularly, and only for the business. The simple method is 5 dollars a square foot, up to 300 square feet, so up to 1,500 dollars a year.",
    "home2": "In Canada, the space has to be your main place of business, or used only for the business and regularly to meet clients. Then you claim the share of your home costs, like rent, heat and electricity, that matches its size.",
    "traps": "Now the mistakes. Business meals count for only half, in the US and in Canada. Clothes count only if you couldn't wear them anywhere else, like a uniform or safety gear. Personal spending doesn't count, even if it went on the business card. And claiming all of something you also use personally, like your phone, is one of the most common mistakes there is.",
    "proof": "Every deduction needs proof, and the proof has to last. The IRS can generally ask for records for 3 years after you file, and the CRA for 6 years after the tax year ends. In Argo Books, you drop in a photo of a receipt, and it reads the supplier, the date and the amount for you.",
    "proof2": "Every expense goes into a category, and the income statement adds each one up for the year, ready to hand to your accountant.",
    "recap": "So: a write-off saves your tax rate, not the whole cost. It has to be for the business, and only the business share counts. Don't miss the small regular costs, your driving, or your home office. Watch the limits on meals and clothes. And keep every receipt.",
    "ask": "Argo Books is free, and it's the first link in the description, along with the mileage calculator. There's no account to create, and your books stay on your own computer, on Windows, Mac or Linux.",
}
SECTIONS = {"free", "test", "missed", "miles", "home", "traps", "proof", "recap"}
CHAPTERS = [("hook", "Tax write-offs"), ("free", "1. What a write-off saves"), ("test", "2. What counts"),
            ("missed", "3. The ones people miss"), ("miles", "4. Your driving: 2026 rates"), ("home", "5. The home office"),
            ("traps", "6. Mistakes to avoid"), ("proof", "7. Keep the proof"), ("recap", "Recap")]


def load_take(name):
    log = json.loads((TAKES[name] / f"{name}.json").read_text())
    log["path"] = TAKES[name] / f"{name}.mkv"
    return log


def hook_card(times):
    """The phrase everyone says, then the truth about it, then the real problem."""
    def frame_at(t):
        frame = qv.backdrop().convert("RGBA")
        draw = ImageDraw.Draw(frame)
        draw.text((960, 360), "“It's a write-off.”", font=mv.font("Bold", 130), fill=mv.INK, anchor="mm")

        def free(d):
            d.rounded_rectangle((560, 500, 1360, 620), radius=30, fill=tv.SOFT_RED + (255,), outline=r.RED + (255,), width=4)
            d.text((960, 560), "That doesn't make it free", font=mv.font("Bold", 56), fill=r.RED + (255,), anchor="mm")

        def missed(d):
            d.text((960, 760), "But most self-employed people still", font=mv.font("Regular", 50), fill=mv.INK + (255,), anchor="mm")
            d.text((960, 840), "miss deductions they're owed", font=mv.font("Bold", 64), fill=qv.GREEN + (255,), anchor="mm")
        for at, part in zip(times, (free, missed)):
            tv.appear(frame, t, at, part)
        return frame.convert("RGB")
    return frame_at


def saving_card(times):
    """A thousand dollars spent as one bar: the share the deduction gives back in tax, and the rest that is still spent."""
    left, right, top, bottom = 170, 1750, 420, 580
    split = left + round((right - left) * 0.3)

    def frame_at(t):
        frame = qv.backdrop().convert("RGBA")
        draw = ImageDraw.Draw(frame)
        draw.text((170, 150), "1. WHAT A WRITE-OFF SAVES", font=mv.font("Bold", 32), fill=mv.BLUE, anchor="ls")
        draw.text((170, 250), "You spend $1,000 on the business", font=mv.font("Bold", 74), fill=mv.INK, anchor="ls")

        def spent(d):
            d.rounded_rectangle((left, top, right, bottom), radius=24, fill=tv.SOFT_BLUE + (255,), outline=mv.BLUE + (255,), width=4)
            d.text((960, 500), "$1,000", font=mv.font("Bold", 72), fill=mv.INK + (255,), anchor="mm")

        def saved(d):
            d.rounded_rectangle((left, top, right, bottom), radius=24, fill=tv.SOFT_BLUE + (255,), outline=mv.BLUE + (255,), width=4)
            d.rounded_rectangle((left, top, split, bottom), radius=24, fill=tv.SOFT_GREEN + (255,), outline=qv.GREEN + (255,), width=4)
            d.text(((left + split) / 2, 500), "$300", font=mv.font("Bold", 72), fill=qv.GREEN + (255,), anchor="mm")
            d.text((left, 650), "Tax it saves", font=mv.font("Bold", 46), fill=qv.GREEN + (255,), anchor="ls")
            d.text((left, 710), "At about 30% tax on profit", font=mv.font("Regular", 38), fill=mv.GREY + (255,), anchor="ls")

        def spent_still(d):
            d.rounded_rectangle((split + 12, top, right, bottom), radius=24, fill=tv.SOFT_RED + (255,), outline=r.RED + (255,), width=4)
            d.text(((split + right) / 2, 500), "$700", font=mv.font("Bold", 72), fill=r.RED + (255,), anchor="mm")
            d.text((right, 650), "Still out of your pocket", font=mv.font("Bold", 46), fill=r.RED + (255,), anchor="rs")
            d.text((right, 710), "A write-off is never free", font=mv.font("Regular", 38), fill=mv.GREY + (255,), anchor="rs")
        for at, part in zip(times, (spent, saved, spent_still)):
            tv.appear(frame, t, at, part)
        return frame.convert("RGB")
    return frame_at


def missed_card(times):
    """The small regular costs people forget, a tile each as the voice names it, and what they come to together."""
    tiles = ["Software and subscriptions", "Bank and card payment fees", "Phone and internet: the business share",
             "Your accountant's fee", "Business insurance", "Courses and training", "Supplies"]

    def frame_at(t):
        frame = qv.backdrop().convert("RGBA")
        draw = ImageDraw.Draw(frame)
        draw.text((170, 150), "3. THE ONES PEOPLE MISS", font=mv.font("Bold", 32), fill=mv.BLUE, anchor="ls")
        draw.text((170, 250), "Small, regular costs", font=mv.font("Bold", 74), fill=mv.INK, anchor="ls")
        for n, (name, at) in enumerate(zip(tiles + ["Added up over a year: often thousands"], times)):
            x0, y0 = 170 + (n % 4) * 400, 340 + (n // 4) * 300
            x1, y1 = x0 + 370, y0 + 270
            last = n == len(tiles)

            def tile(d, x0=x0, y0=y0, x1=x1, y1=y1, name=name, last=last):
                fill, accent = (tv.SOFT_GREEN, qv.GREEN) if last else ((255, 255, 255), qv.LINE)
                d.rounded_rectangle((x0, y0, x1, y1), radius=24, fill=fill + (255,), outline=accent + (255,), width=4 if last else 3)
                lines = qv.wrapped(d, name, mv.font("Bold", 40), x1 - x0 - 60)
                y = (y0 + y1) / 2 - (len(lines) - 1) * 27
                for line in lines:
                    d.text((x0 + 30, y), line, font=mv.font("Bold", 40), fill=(qv.GREEN if last else mv.INK) + (255,), anchor="lm")
                    y += 54
            tv.appear(frame, t, at, tile)
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
    draw.text((960, 750), "The mileage deduction calculator is linked in the description too.", font=mv.font("Regular", 36), fill=mv.GREY, anchor="mm")
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

    hook = hook_card([W("hook", "It"), W("hook", "most")])
    inside = bv.reveal("In this video", [(1, "What a write-off really saves you"), (2, "The test a cost has to pass"),
                                         (3, "The deductions people miss most"), (4, "The 2026 mileage rates"),
                                         (5, "The home office"), (6, "The mistakes to stay away from")],
                       [W("hook2", "what"), W("hook2", "test"), W("hook2", "deductions"), W("hook2", "mileage"),
                        W("hook2", "home"), W("hook2", "mistakes")], kicker="Tax write-offs")
    profit = bv.reveal("A deduction lowers your profit", [
        ("no", "It doesn't come off your tax bill"), ("yes", "It comes off your profit, the amount your tax is worked out on")],
        [W("free", "bill"), W("free", "profit")], kicker="1. What a write-off saves")
    saving = saving_card([S("free2") + 0.2, W("free2", "saves"), W("free2", "other")])
    never = bv.reveal("So", [("no", "Never buy something just for the write-off"),
                            ("yes", "Record everything the business really needs")],
                      [S("free3") + 0.2, W("free3", "anything")], kicker="1. What a write-off saves")
    test = bv.reveal("What counts as a business cost", [
        (None, "US (IRS): ordinary and necessary. Common in your line of work, and helpful to it"),
        (None, "Canada (CRA): spent to earn business income, and a reasonable amount"),
        ("yes", "Partly personal? Claim only the business share")],
        [W("test", "US"), W("test", "Canada"), S("test2") + 0.2],
        footnote="Sources: IRS, Deducting business expenses, and CRA, Business expenses. November 2026.", kicker="2. What counts")
    missed = missed_card([W("missed2", "Software"), W("missed2", "Bank"), W("missed2", "phone"), W("missed2", "accountant's"),
                          W("missed2", "insurance"), W("missed2", "Courses"), W("missed2", "supplies"), W("missed3", "Added")])
    rates = tv.ledger("The IRS's 2026 business mileage rate", [
        (W("miles", "set"), "A set rate a mile, instead of adding up fuel and repairs", "", "note"),
        (W("miles", "rate", 2), "Miles driven 1 January to 30 June", "72.5¢ a mile", "plain"),
        (W("miles", "July"), "Miles driven 1 July to 31 December", "76¢ a mile", "plain")],
        footnote="Source: IRS, 2026 standard mileage rates, raised from 1 July 2026.", kicker="4. Your driving")
    canada = bv.reveal("In Canada: your real car costs", [
        (None, "Fuel, insurance, repairs, licence and the rest"),
        (None, "Times the share of kilometres you drove for the business")],
        [W("miles3", "fuel"), W("miles3", "drove")],
        footnote="Source: CRA, Motor vehicle expenses for self-employed people. November 2026.", kicker="4. Your driving")
    log = bv.reveal("Keep a log of every trip", [(1, "The date"), (2, "Where you went"), (3, "Why"), (4, "How far"),
                                                 ("no", "Home to your regular place of work is commuting")],
                    [W("miles4", "date"), W("miles4", "where"), W("miles4", "why"), W("miles4", "far"), W("miles4", "driving")],
                    kicker="4. Your driving")
    us_home = tv.ledger("US: the simple method", [
        (W("home", "regularly"), "Used regularly, and only for the business", "", "note"),
        (W("home", "simple"), "Per square foot", "$5", "plain"),
        (W("home", "300"), "Up to", "300 sq ft", "plain"),
        (W("home", "1,500") - 0.2, "The most it gives you in a year", "$1,500", "total")],
        footnote="Source: IRS, Simplified option for home office deduction. November 2026.", kicker="5. The home office")
    ca_home = bv.reveal("Canada: the work space in your home", [
        (None, "Your main place of business, or"), (None, "Used only for the business, and regularly to meet clients"),
        ("yes", "Claim the share of your home costs that matches its size")],
        [W("home2", "main"), W("home2", "only"), W("home2", "claim")],
        footnote="Source: CRA, Business-use-of-home expenses. November 2026.", kicker="5. The home office")
    traps = bv.reveal("Mistakes to avoid", [
        ("no", "Meals: only half counts, in the US and Canada"), ("no", "Clothes: only if you couldn't wear them anywhere else"),
        ("no", "Personal spending, even on the business card"), ("no", "Claiming all of something you also use personally")],
        [W("traps", "meals"), W("traps", "Clothes"), W("traps", "Personal"), W("traps", "claiming")], kicker="6. Mistakes to avoid")
    keep = bv.reveal("How long the proof has to last", [
        (None, "US: generally 3 years after you file (IRS)"), (None, "Canada: 6 years after the tax year ends (CRA)")],
        [W("proof", "IRS"), W("proof", "CRA")],
        footnote="Sources: IRS, How long should I keep records?, and CRA, Keeping records. November 2026.", kicker="7. Keep the proof")
    recap = bv.reveal("Recap", [
        (1, "A write-off saves your tax rate, not the whole cost"), (2, "It has to be for the business: only the business share counts"),
        (3, "Don't miss the small costs, your driving, or your home office"), (4, "Watch the limits on meals and clothes"),
        (5, "Keep every receipt")],
        [W("recap", "saves"), W("recap", "business"), W("recap", "miss"), W("recap", "Watch"), W("recap", "keep")], kicker="Tax write-offs")

    form = (1.75, 1060, 820)
    result = (1.75, 2144, 700)
    shots = [
        still(0.0, hook),
        still(S("hook2") - 0.1, inside),

        step("free", 1, "What a write-off saves", "Not the whole cost"),
        still(W("free", "deduction") - 0.3, profit),
        still(S("free2") - 0.1, saving),
        still(S("free3") - 0.1, never),

        step("test", 2, "What counts", "The test a cost has to pass"),
        still(W("test", "US") - 0.3, test),

        step("missed", 3, "The ones people miss", "Small, regular costs"),
        still(S("missed2") - 0.2, missed),

        step("miles", 4, "Your driving", "The 2026 mileage rates"),
        still(W("miles", "US") - 0.3, rates),
        take(S("miles2") - 0.1, "mileage", "top", 0.0, [("top", 0.0, 1.4, 1600, 800), ("click_first", -0.3, 1.45, 1600, 800)],
             until=("click_first", -0.3)),
        take(W("miles2", "Say") - 0.2, "mileage", "click_first", -0.6,
             [("click_first", -0.6) + form, ("second_done", 0.2) + form, ("look_deduction", 0.0) + result, ("look_deduction", 4.0, 1.8, 2144, 700)],
             until=("click_rate", -0.3)),
        take(W("miles2", "Add") - 0.2, "mileage", "click_rate", -0.6,
             [("click_rate", -0.6, 1.75, 1060, 1000), ("rate_done", 0.2, 1.75, 1060, 1000), ("look_saved", 0.0, 1.75, 2144, 1000),
              ("look_saved", 5.0, 1.8, 2144, 1000)]),
        still(S("miles3") - 0.1, canada),
        still(S("miles4") - 0.1, log),

        step("home", 5, "The home office", "Part of your home costs"),
        still(W("home", "US") - 0.3, us_home),
        still(S("home2") - 0.1, ca_home),

        step("traps", 6, "Mistakes to avoid", "What doesn't count"),
        still(W("traps", "meals") - 0.4, traps),

        step("proof", 7, "Keep the proof", "Every deduction needs it"),
        still(W("proof", "IRS") - 0.4, keep),
        take(W("proof", "drop") - 0.3, "receipt", "drop_zone", -0.6, [("drop_zone", -0.6, 1.3, 1600, 960), ("dropped", 0.0, 1.35, 1600, 960)], until=("scanning", 0.6)),
        take(W("proof", "reads") - 0.3, "receipt", "scan_done", 0.0, [("scan_done", 0.0, 1.15, 1600, 930), ("look_total", 0.0, 1.45, 2200, 600)], until=("click_products", -0.4)),
        take(S("proof2") - 0.1, "reports", "statement", -0.3, [("statement", -0.3, 1.5, 1570, 1100), ("statement", 5.0, 1.62, 1570, 1250)], until=("statement", 5.3)),

        still(S("recap") - 0.3, recap),
        still(S("ask") - 0.1, end_card()),
    ]
    shots.sort(key=lambda s: s["at"])
    # A section's title stays up long enough to read, even when the voice moves on quickly.
    for card, following in zip(shots, shots[1:]):
        if card.get("section"):
            following["at"] = max(following["at"], card["at"] + tv.SECTION_HOLD)
    outro = float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(r.OUTRO)],
                                 capture_output=True, text=True, check=True).stdout)
    shots.append({"at": v.length + 1.0, "outro": True})
    shots.append({"at": v.length + 1.0 + outro, "stop": True})
    return shots


def main():
    inv.build_take(TAKES["mileage"], "mileage")
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
