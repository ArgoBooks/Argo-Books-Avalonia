"""Make the vertical Short "QuickBooks Desktop alternatives": five accounting programs that still run on your computer, the
catch or the price of each, and Argo Books bringing a QuickBooks company across. Narrated by the Azure voice, with big
captions timed to each word.

Usage: python qb_desktop_short.py

It uses the pages and takes of qb_desktop_video.py in work/qbvideo/, so make those first. The finished video goes to
work/videos/.
"""
from pathlib import Path

from PIL import Image, ImageDraw

import catches_short as short
import invoice_short as inv
import make_video as mv
import pc_short as pc
import qb_desktop_video as qd
import quickbooks_video as qv
import roundup as r

HERE = Path(__file__).resolve().parent
WORK = HERE / "work" / "qb_desktop_short"
OUT_FILE = HERE / "work" / "videos" / "short_quickbooks_desktop_alternatives.mp4"
LINES = [
    ("Intuit stopped selling most of QuickBooks Desktop to new customers.", 350),
    ("Here are five that still run on your computer.", 400),
    ("Sage 50. It converts your QuickBooks file, but it's 737 US dollars a year.", 450),
    ("AccountEdge. Mac and Windows, 20 US dollars a month.", 450),
    ("GnuCash. Free, but it only imports Quicken files, not QuickBooks.", 450),
    ("Manager. Free forever, but just one person on one computer.", 450),
    ("And Argo Books, which I make. Its free plan imports your QuickBooks reports.", 300),
    ("And the totals match to the cent.", 450),
    ("The full comparison is linked below.", 0),
]
SCRIPT = " ".join(text for text, _ in LINES)
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 150.0
# The part of the line on GnuCash's features page that says what it imports, which the capture did not name.
BOXES = {"gnucash_features": {"quicken": [1566, 5284, 504, 44]}}
VX, VY, VW, VH = short.VIEW
EDGE = (226, 232, 240)
SOFT_RED = (253, 232, 232)
GREEN = qv.GREEN


class Pages(qd.Pages):
    def get(self, name):
        image, boxes = super().get(name)
        boxes.update(BOXES.get(name, {}))
        return image, boxes


def appear(frame, age, at, draw_it):
    """Draws something that fades in at `at` seconds into the shot."""
    p = r.ease((age - at) / 0.25)
    if p <= 0:
        return
    layer = Image.new("RGBA", frame.size, (0, 0, 0, 0))
    draw_it(ImageDraw.Draw(layer))
    if p < 1:
        layer.putalpha(layer.getchannel("A").point(lambda a: int(a * p)))
    frame.alpha_composite(layer)


def panel(draw, top, bottom):
    draw.rounded_rectangle((70, top, 1010, bottom), radius=32, fill=(255, 255, 255, 255), outline=EDGE + (255,), width=3)


def desktop_card(base, chips):
    """QuickBooks Desktop as a plain window, with "Not sold" stamped on each edition as the voice gets there."""
    editions = ["Pro Plus", "Premier Plus", "Mac Plus"]

    def frame(age):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 110, VY + 830
        panel(draw, top, bottom)
        draw.rounded_rectangle((72, top + 2, 1008, top + 110), radius=30, fill=(241, 243, 247, 255))
        draw.rectangle((72, top + 70, 1008, top + 110), fill=(241, 243, 247, 255))
        draw.line((72, top + 110, 1008, top + 110), fill=EDGE + (255,), width=3)
        draw.text((120, top + 56), "QuickBooks Desktop", font=mv.font("Bold", 46), fill=(77, 86, 107, 255), anchor="lm")
        grey = (138, 147, 166, 255)
        draw.line((800, top + 58, 830, top + 58), fill=grey, width=5)
        draw.rectangle((864, top + 42, 894, top + 72), outline=grey, width=5)
        draw.line((930, top + 42, 958, top + 70), fill=grey, width=5)
        draw.line((958, top + 42, 930, top + 70), fill=grey, width=5)
        draw.text((120, top + 190), "TO NEW US CUSTOMERS", font=mv.font("Bold", 34), fill=(124, 135, 158, 255), anchor="ls")
        chip_font = mv.font("Bold", 44)
        for n, name in enumerate(editions):
            y = top + 290 + n * 150
            draw.text((120, y), name, font=mv.font("Bold", 64), fill=mv.INK + (255,), anchor="lm")
            if n < 2:
                draw.line((120, y + 75, 960, y + 75), fill=(238, 241, 246, 255), width=3)

            def chip(d, y=y):
                w = d.textlength("Not sold", font=chip_font) + 56
                d.rounded_rectangle((960 - w, y - 38, 960, y + 38), radius=38, fill=SOFT_RED + (255,))
                d.text((960 - w / 2, y), "Not sold", font=chip_font, fill=r.RED + (255,), anchor="mm")
            appear(out, age, chips[n], chip)
        draw.text((540, bottom + 60), "Since September 30, 2024. Source: Intuit.", font=mv.font("Regular", 32), fill=mv.GREY + (255,), anchor="mm")
        return out
    return frame


def lineup_card(base):
    """The five, numbered, coming in one after another."""
    names = ["Sage 50", "AccountEdge Pro", "GnuCash", "Manager", "Argo Books"]

    def frame(age):
        out = base.copy()
        for n, name in enumerate(names):
            y = VY + 70 + n * 172

            def row(d, n=n, name=name, y=y):
                ours = n == 4
                d.rounded_rectangle((110, y, 970, y + 146), radius=28, fill=(219, 234, 254, 255) if ours else (255, 255, 255, 255),
                                    outline=(mv.BLUE if ours else EDGE) + (255,), width=4 if ours else 3)
                d.ellipse((150, y + 41, 214, y + 105), fill=mv.BLUE + (255,))
                d.text((182, y + 73), str(n + 1), font=mv.font("Bold", 38), fill=(255, 255, 255, 255), anchor="mm")
                d.text((250, y + 73), name, font=mv.font("Bold", 60), fill=mv.INK + (255,), anchor="lm")
            appear(out, age, 0.15 + n * 0.18, row)
        return out
    return frame


def facts_card(base, name, note, rows, footnote):
    """A product's name over rows of (seconds into the shot, "yes" or "no", text), each fading in on its second."""
    def frame(age):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top = VY + 130
        bottom = top + 230 + len(rows) * 150
        panel(draw, top, bottom)
        draw.text((130, top + 130), name, font=mv.font("Bold", 92), fill=mv.INK + (255,), anchor="ls")
        draw.text((132, top + 195), note, font=mv.font("Regular", 38), fill=mv.GREY + (255,), anchor="ls")
        for n, (at, mark, text) in enumerate(rows):
            y = top + 300 + n * 150

            def row(d, mark=mark, text=text, y=y):
                if mark == "yes":
                    qv.tick(d, 130, y - 22, colour=GREEN + (255,), size=46)
                else:
                    qv.cross(d, 134, y - 20, colour=r.RED + (255,), size=40)
                d.text((210, y), text, font=mv.font("Bold", 48), fill=mv.INK + (255,), anchor="lm")
            appear(out, age, at, row)
        draw.text((540, bottom + 60), footnote, font=mv.font("Regular", 30), fill=mv.GREY + (255,), anchor="mm")
        return out
    return frame


def totals_card(base, theirs_at, ours_at):
    """Revenue and expenses in the QuickBooks reports, then the same totals in Argo Books after the import."""
    totals = [("Revenue", "$33,262.00"), ("Expenses", "$6,985.06")]

    def frame(age):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 70, VY + 870
        panel(draw, top, bottom)
        draw.text((130, top + 110), "After the import", font=mv.font("Bold", 64), fill=mv.INK + (255,), anchor="ls")
        label, amount = mv.font("Regular", 44), mv.font("Bold", 50)
        for n, (name, value) in enumerate(totals):
            y = top + 220 + n * 300
            draw.text((130, y), name.upper(), font=mv.font("Bold", 34), fill=(124, 135, 158, 255), anchor="ls")

            def theirs(d, y=y, value=value):
                d.text((130, y + 70), "QuickBooks report", font=label, fill=mv.INK + (255,), anchor="lm")
                d.text((950, y + 70), value, font=amount, fill=mv.INK + (255,), anchor="rm")

            def ours(d, y=y, value=value):
                d.rounded_rectangle((105, y + 120, 975, y + 220), radius=22, fill=(220, 252, 231, 255))
                d.text((130, y + 170), "Argo Books", font=label, fill=mv.INK + (255,), anchor="lm")
                d.text((880, y + 170), value, font=amount, fill=mv.INK + (255,), anchor="rm")
                qv.tick(d, 900, y + 152, colour=GREEN + (255,), size=40)
            appear(out, age, theirs_at, theirs)
            appear(out, age, ours_at, ours)
        draw.text((540, bottom + 60), "An example company's QuickBooks Desktop reports", font=mv.font("Regular", 30), fill=mv.GREY + (255,), anchor="mm")
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
        found = [w["at"] for w in words[starts[k]:last] if w["text"].strip(".,?").lower() == text.lower()]
        return found[nth - 1]

    def page(k, name, title, keys, marks=(), cursor=()):
        return {"at": line(k) - 0.1, "page": name, "title": title, "keys": keys, "marks": list(marks), "cursor": list(cursor)}

    sage = facts_card(base, "Sage 50", "Pro, US edition", [
        (0.3, "yes", "Windows desktop software"),
        (word(2, "converts") - line(2) + 0.1, "yes", "Converts your QuickBooks file"),
        (word(2, "737") - line(2) + 0.1, "no", "$737 USD a year")], "Source: sage.com, November 2026")
    k = 3
    accountedge = page(k, "accountedge_pricing", "2. AccountEdge Pro",
                       [(line(k) - 0.1, 2.6, (2516, 790)), (word(k, "Windows") + 0.3, 2.65, (2516, 790)), (word(k, "20") - 0.1, 2.4, "pro_price"),
                        (line(k + 1), 2.5, "pro_price")],
                       marks=[(word(k, "Mac"), "platforms", r.COBALT), (word(k, "20"), "pro_price", r.RED), (word(k, "US"), "usd", r.COBALT)],
                       cursor=[(word(k, "Mac"), "platforms"), (word(k, "20"), "pro_price")])
    k = 4
    gnucash = page(k, "gnucash_features", "3. GnuCash", [(line(k) - 0.1, 2.3, (1492, 5300)), (line(k + 1), 2.33, (1492, 5300))],
                   marks=[(word(k, "Quicken"), "quicken", r.RED)], cursor=[(word(k, "Quicken"), "quicken")])
    k = 5
    manager = page(k, "manager_download_win", "4. Manager",
                   [(line(k) - 0.1, 2.2, "free_forever"), (word(k, "but") - 0.1, 2.2, "free_forever"), (word(k, "one") + 0.1, 2.4, "single_user"),
                    (line(k + 1), 2.5, "single_user")],
                   marks=[(word(k, "forever"), "free_forever", r.COBALT), (word(k, "one"), "single_user", r.RED)],
                   cursor=[(word(k, "forever"), "free_forever"), (word(k, "one"), "single_user")])
    return [
        {"at": 0.0, "still": desktop_card(base, [word(0, "selling") + n * 0.3 for n in range(3)]), "title": "Leaving QuickBooks Desktop?"},
        {"at": line(1) - 0.1, "still": lineup_card(base), "title": "Still on your computer"},
        {"at": line(2) - 0.1, "still": sage, "title": "1. Sage 50"},
        accountedge,
        gnucash,
        manager,
        {"at": line(6) - 0.1, "take": "quickbooks", "from": ("desktop_steps", 3.6), "title": "5. Argo Books",
         "keys": [("desktop_steps", 3.6, 1.1, 1600, 780),("drag", 0.0, 1.1, 1600, 780), ("dropped", 0.3, 1.15, 1600, 1150)]},
        {"at": line(7) - 0.1, "still": totals_card(base, 0.15, word(7, "match") - line(7)), "title": "Matches to the cent"},
        {"at": line(8) - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    voice_path = WORK / "voice.wav"
    r.write_mono(voice_path, mv.voice_line(inv.spoken(LINES)))
    words = short.scripted_words(voice_path, SCRIPT, WORK)
    total = max(w["end"] for w in words) + HOLD_END
    chunks = short.caption_chunks(words, longest_pause=0.4)
    pc.finish(shot_list(words, total, short.background()), chunks, voice_path, WORK, OUT_FILE, MUSIC_FROM,
              load_take=qd.load_take, pages=Pages(), end_card=inv.end_card_saying("Full comparison linked below"))


if __name__ == "__main__":
    main()
