"""Make the vertical Short "Your receipts are fading": a thermal receipt fading to blank over two years, how long the IRS and
the CRA can ask for it, and the same receipt scanned into Argo Books, where the photo is kept with the expense. Narrated by
the Azure voice, with big captions timed to each word.

Usage: python fading_receipt_short.py

It uses the receipt photo in work/ and the `receipt` take of bookkeeping_video.py in work/books/takes, so record that
first. The finished video goes to work/videos/.
"""
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

import bookkeeping_video as bv
import catches_short as short
import invoice_short as inv
import make_video as mv
import pc_short as pc
import qb_desktop_short as qs
import roundup as r

HERE = Path(__file__).resolve().parent
WORK = HERE / "work" / "fading_receipt_short"
OUT_FILE = HERE / "work" / "videos" / "short_fading_receipt.mp4"
LINES = [
    ("This receipt is disappearing.", 450),
    ("Most receipts are printed on thermal paper, and heat and light fade the print.", 400),
    ("A blank receipt proves nothing, and the tax office can ask for it years later.", 400),
    ("In the US, the IRS generally wants records kept three years after you file.", 300),
    ("In Canada, it's six years after the end of the tax year.", 450),
    ("The good news: a clear photo counts.", 350),
    ("So scan it the day you get it.", 300),
    ("Argo Books reads it, and keeps the photo with the expense, so it never fades.", 450),
    ("The full receipt scanning video is linked below.", 0),
]
SCRIPT = " ".join(text for text, _ in LINES)
HOLD_END = 1.8
# Where the track starts, so this Short does not open on the same bars as the others.
MUSIC_FROM = 15.0
VX, VY, VW, VH = short.VIEW
# The part of the receipt photo shown, the same shape as the view.
CROP = (0, 160, 1080, 1160)
# How faint the print gets: a little of it stays, as it does on a real faded receipt.
FADED = 0.95


class Fading:
    """The receipt photo with its print faded by an amount from 0 (as printed) to 1 (blank). The print fades unevenly, the
    way heat reaches one part of a receipt before another, and the paper yellows a little as it goes."""

    def __init__(self):
        photo = Image.open(mv.WORK / "receipt.jpg").convert("RGB").crop(CROP)
        # The paper without its print: the brightest pixel nearby, which the thin dark letters never are.
        blank = photo.filter(ImageFilter.MaxFilter(11)).filter(ImageFilter.GaussianBlur(3))
        paper = np.asarray(blank.convert("L")) > 190
        mask = Image.fromarray(paper.astype(np.uint8) * 255).filter(ImageFilter.MinFilter(15)).filter(ImageFilter.GaussianBlur(4))
        self.photo = np.asarray(photo, dtype=np.float32)
        self.blank = np.asarray(blank, dtype=np.float32)
        self.mask = np.asarray(mask, dtype=np.float32)[:, :, None] / 255
        rng = np.random.default_rng(7)
        field = Image.fromarray((rng.random((5, 5)) * 255).astype(np.uint8)).resize(photo.size, Image.BICUBIC)
        self.field = np.asarray(field, dtype=np.float32)[:, :, None] / 255
        self.aged = np.array([238, 228, 200], dtype=np.float32)

    def at(self, amount):
        local = np.clip(amount * 1.4 - 0.4 * self.field, 0, 1) * FADED
        out = self.photo + (self.blank - self.photo) * local
        out = out + (self.aged - out) * (0.35 * amount) * (out > 200).all(axis=2, keepdims=True)
        out = self.photo + (out - self.photo) * self.mask
        return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8))


def receipt_shot(base, fading, amount):
    """A still of the receipt, faded by `amount(seconds into the shot)`."""
    def frame(age):
        out = base.copy()
        out.paste(fading.at(amount(age)), (VX, VY))
        return out
    return frame


def rules_card(base, times):
    """How long the US and Canada can ask for records, each country fading in on its second."""
    rules = [("UNITED STATES  ·  IRS", "3 years", "after you file"), ("CANADA  ·  CRA", "6 years", "after the tax year ends")]

    def frame(age):
        out = base.copy()
        draw = ImageDraw.Draw(out)
        top, bottom = VY + 110, VY + 780
        qs.panel(draw, top, bottom)
        draw.line((130, top + 335, 950, top + 335), fill=(238, 241, 246, 255), width=3)
        for n, (who, years, when) in enumerate(rules):
            y = top + 60 + n * 335

            def row(d, who=who, years=years, when=when, y=y):
                d.text((130, y + 30), who, font=mv.font("Bold", 36), fill=(124, 135, 158, 255), anchor="ls")
                d.text((126, y + 140), years, font=mv.font("Bold", 104), fill=mv.INK + (255,), anchor="ls")
                d.text((132, y + 215), when, font=mv.font("Regular", 50), fill=mv.INK + (255,), anchor="ls")
            qs.appear(out, age, times[n], row)
        draw.text((540, bottom + 60), "The general rules. Some records need keeping longer.", font=mv.font("Regular", 30),
                  fill=mv.GREY + (255,), anchor="mm")
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

    fading = Fading()
    begin, end = 0.9, line(2) - 0.4

    def faded_since(at):
        return lambda age: r.ease((at + age - begin) / (end - begin))

    def stage(at, title):
        return {"at": at, "still": receipt_shot(base, fading, faded_since(at)), "title": title}

    # The photo brings the print back, as if the scan had kept it.
    restored = receipt_shot(base, fading, lambda age: 1 - r.ease((age - 0.2) / 0.6))
    keeps = word(7, "keeps") - 0.1
    return [
        stage(0.0, "Today"),
        stage(begin + 0.22 * (end - begin), "6 months later"),
        stage(begin + 0.5 * (end - begin), "1 year later"),
        stage(begin + 0.78 * (end - begin), "2 years later"),
        stage(line(2) - 0.1, "Proves nothing"),
        {"at": line(3) - 0.1, "still": rules_card(base, [0.2, line(4) - line(3)]), "title": "How long to keep them"},
        {"at": line(5) - 0.1, "still": restored, "title": "A clear photo counts"},
        {"at": line(6) - 0.1, "take": "receipt", "from": ("dropped", -1.1), "title": "Scan it the same day",
         "keys": [("dropped", -1.1, 1.2, 1600, 930), ("dropped", 0.0, 1.2, 1600, 930), ("dropped", 1.0, 1.1, 1570, 930)]},
        {"at": line(7) - 0.1, "take": "receipt", "from": ("scan_done", -0.2), "title": "Read for you",
         "keys": [("scan_done", -0.2, 1.15, 1976, 900), ("scan_done", 2.0, 1.2, 1976, 880)]},
        {"at": keeps, "take": "receipt", "from": ("saved", 0.2), "play": 2.0, "title": "Kept with the expense",
         "keys": [("saved", 0.2, 1.45, 1150, 900), ("saved", 4.0, 1.55, 1050, 900)]},
        {"at": line(8) - 0.1, "end": True},
        {"at": total, "stop": True},
    ]


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    voice_path = WORK / "voice.wav"
    r.write_mono(voice_path, mv.voice_line(inv.spoken(LINES)))
    words = short.scripted_words(voice_path, SCRIPT, WORK)
    total = max(w["end"] for w in words) + HOLD_END
    chunks = short.balanced_chunks(words, longest_pause=0.6)
    pc.finish(shot_list(words, total, short.background()), chunks, voice_path, WORK, OUT_FILE, MUSIC_FROM,
              load_take=bv.load_take, end_card=inv.end_card_saying("Full video linked below"))


if __name__ == "__main__":
    main()
